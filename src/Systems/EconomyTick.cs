using System;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Resolves the business once per in-game hour (~2 real minutes at the
    /// default timescale).
    ///
    ///   income = baseRate[tier] x zone.demand x (loyalty/100) x (1 - heat)
    ///            x (stamina/100) x nightBonus x ownedZoneBonus
    ///            x saturation x traits x vehicle
    /// </summary>
    public class EconomyTick
    {
        private readonly IncidentRoller _incidents;
        private readonly Random _rng = new Random();

        public EconomyTick(IncidentRoller incidents)
        {
            _incidents = incidents;
        }

        public void Update()
        {
            int hour = GTA.Chrono.GameClock.Hour;
            if (hour == GameState.Current.LastEconomyHour) return;

            GameState.Current.LastEconomyHour = hour;
            Resolve(hour);
        }

        private void Resolve(int hour)
        {
            var state = GameState.Current;
            var cfg = Config.Current;
            bool night = hour >= 20 || hour < 5;

            int take = 0;

            foreach (var worker in state.Roster.ToList())
            {
                var zone = Zones.Get(worker.ZoneId);
                bool onTheStreet = worker.ShouldBeOnStreet(hour) && zone != null;

                // Stream two accrues every hour either way — that is what makes
                // the two streams compete for the same worker's time.
                Subscriptions.AccrueHourly(worker, onTheStreet);

                if (!onTheStreet)
                {
                    // Somewhere of their own to rest. Owning any stash is enough —
                    // off-shift workers aren't tied to a region.
                    float recovery = cfg.StaminaRecoverPerHour;
                    if (state.StashHouses.Count > 0) recovery *= cfg.StashStaminaBonus;

                    worker.Stamina += recovery;
                    worker.Clamp();
                    continue;
                }

                float payout = cfg.BaseRateFor(worker.Tier)
                               * zone.Demand
                               * DemandEvents.MultiplierFor(zone.Id)
                               * (worker.Loyalty / 100f)
                               * (1f - state.GetHeat(zone.Id))
                               * (worker.Stamina / 100f)
                               * Saturation(zone, hour, worker)
                               * Traits.StreetMultiplier(worker.TraitSet);

                if (night) payout *= cfg.NightDemandBonus;
                if (state.PlayerOwns(zone.Id)) payout *= cfg.OwnedZoneBonus;
                if (state.ZoneHasVehicle(zone.Id)) payout *= cfg.VehicleDemandBonus;

                int earned = (int)Math.Round(payout);
                if (earned > 0)
                {
                    take += earned;
                    worker.LifetimeEarnings += earned;
                }

                // A car means she is not walking the whole shift.
                float drain = cfg.StaminaDrainPerHour;
                if (state.ZoneHasVehicle(zone.Id)) drain *= cfg.VehicleStaminaRelief;
                worker.Stamina -= drain;

                state.AddHeat(zone.Id,
                    zone.HeatGain * Traits.HeatMultiplier(worker.TraitSet)
                    + DemandEvents.HeatFor(zone.Id));

                // Working someone into the ground is what actually costs you the
                // roster — this is the tension the management loop runs on.
                if (worker.IsExhausted)
                    worker.Loyalty -= cfg.LoyaltyDrainWhenExhausted
                                      * Traits.LoyaltyDrainMultiplier(worker.TraitSet);

                worker.Clamp();
            }

            DecayHeat();

            if (take > 0)
            {
                Game.Player.Money += take;
                state.LifetimeTake += take;
                Notify.Show($"~g~+${take}~s~  Street take ({hour:00}:00)");
            }

            CheckRaids();

            // Rolled after the take so a new event affects the next hour, not the
            // one the player was just paid for.
            string news = DemandEvents.Tick(_rng);
            if (news != null) Notify.Show(news, true);

            // Midnight is payday, interest day, and where the weekly deposit is
            // checked. EconomyTick only fires on an hour change, so hour 0 comes
            // round exactly once per game day.
            if (hour == 0)
            {
                PayWages();
                PaySubscriptions();
                AccrueInterest();
            }

            AwardMilestone();

            // Problems are rolled after the payout so the numbers the player just
            // saw are the ones the roll was based on.
            _incidents.RollHourly();
        }

        /// <summary>
        /// Diminishing returns per corner. Without this, stacking the whole roster
        /// on the highest-demand zone is strictly optimal and territory stops
        /// mattering. Only workers actually out this hour crowd each other.
        /// </summary>
        private float Saturation(ZoneDef zone, int hour, WorkerData self)
        {
            int others = GameState.Current.WorkersIn(zone.Id)
                .Count(w => w.Id != self.Id && w.ShouldBeOnStreet(hour));

            if (others <= 0) return 1f;
            return 1f / (1f + others * Config.Current.ZoneSaturationFalloff);
        }

        private void DecayHeat()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            // Heat bleeds off everywhere, including zones you have pulled out of.
            foreach (var zone in Zones.All)
            {
                float decay = cfg.HeatDecayPerHour;
                if (state.ZoneHasStash(zone.Id)) decay *= cfg.StashHeatDecayBonus;
                if (state.HasUpgrade(UpgradeCatalog.Laundry)) decay *= cfg.LaunderedHeatDecayBonus;

                state.AddHeat(zone.Id, -decay);
            }
        }

        /// <summary>
        /// Heat used to be a soft tax with no ceiling. Past the threshold the zone
        /// gets turned over: everyone off the street, the corner locked for a few
        /// days, and a fine. This is what makes laundering and stash houses read
        /// as insurance rather than nice-to-haves.
        /// </summary>
        private void CheckRaids()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var zone in Zones.All)
            {
                if (state.GetHeat(zone.Id) < cfg.RaidHeatThreshold) continue;
                if (state.IsZoneLocked(zone.Id)) continue;
                if (!state.WorkersIn(zone.Id).Any()) continue;

                state.ClearZone(zone.Id);
                state.ZoneLockedUntilDay[zone.Id] = GameState.AbsoluteDay() + cfg.RaidLockoutDays;
                state.ZoneHeat[zone.Id] = cfg.RaidHeatAfter;

                Charge(cfg.RaidFine, $"Raid on {zone.Display}");

                Notify.Show(
                    $"~r~{zone.Display} got turned over.~s~ Everyone is off the street and " +
                    $"the corner is shut for {cfg.RaidLockoutDays} days.", true);
            }
        }

        private void AwardMilestone()
        {
            var milestone = MilestoneCatalog.CheckNext(GameState.Current);
            if (milestone == null) return;

            if (milestone.Reward > 0)
            {
                Game.Player.Money += milestone.Reward;
                Notify.Show(
                    $"~g~{milestone.Name}~s~ — {milestone.Blurb} ~g~+${milestone.Reward:N0}", true);
            }
            else
            {
                Notify.Show($"~g~{milestone.Name}~s~ — {milestone.Blurb}", true);
            }
        }

        /// <summary>
        /// The weekly deposit. Unlike the street take this cannot fail, generates
        /// no heat and cannot trigger an incident — it is the safe stream, and it
        /// is paid for in worker-hours not spent earning on the street.
        /// </summary>
        private void PaySubscriptions()
        {
            int deposited = Subscriptions.TryPayout();
            if (deposited <= 0) return;

            var state = GameState.Current;
            Game.Player.Money += deposited;
            state.LifetimeTake += deposited;
            state.LifetimeSubscriptionTake += deposited;

            Notify.Show($"~g~+${deposited:N0}~s~  {Subscriptions.Brand} deposits cleared.", true);
        }

        /// <summary>
        /// Enforcers who don't get paid used to simply walk. Now the shortfall
        /// becomes debt — that is the only reason the operation can ever fail.
        /// </summary>
        private void PayWages()
        {
            var state = GameState.Current;
            if (state.Enforcers.Count == 0) return;

            int bill = state.DailyWageBill;
            Charge(bill, $"Wages for {state.Enforcers.Count}");
        }

        private void AccrueInterest()
        {
            var state = GameState.Current;
            var cfg = Config.Current;
            if (state.Debt <= 0) return;

            int interest = (int)Math.Round(state.Debt * cfg.DebtInterestPerDay);
            state.Debt += interest;

            Notify.Show($"~r~Debt is now ${state.Debt:N0}~s~ (+${interest:N0} interest).", true);

            if (state.Debt >= cfg.DebtCollapseThreshold) Collapse();
        }

        /// <summary>
        /// The lose condition. Not a game over — the save survives and property is
        /// kept, because deleting someone's progress outright is a worse outcome
        /// than making them start the street operation again.
        /// </summary>
        private void Collapse()
        {
            var state = GameState.Current;

            int lost = state.Roster.Count;
            state.Roster.Clear();

            foreach (var zone in Zones.All)
                if (state.PlayerOwns(zone.Id)) state.SetOwner(zone.Id, string.Empty);

            state.Enforcers.Clear();
            state.Debt = 0;
            state.TimesCollapsed++;

            Notify.Show(
                "~r~The people you owe came collecting.~s~ " +
                $"{lost} gone, every corner given up. You keep the property.", true);

            Persistence.SaveManager.Log($"COLLAPSE #{state.TimesCollapsed}: lost {lost} workers.");
        }

        /// <summary>
        /// Takes money if there is any, and turns whatever is left into debt.
        /// Every outgoing cost in the mod routes through here.
        /// </summary>
        public static void Charge(int amount, string reason)
        {
            if (amount <= 0) return;
            var state = GameState.Current;

            int paid = Math.Min(Game.Player.Money, amount);
            Game.Player.Money -= paid;

            int shortfall = amount - paid;
            if (shortfall <= 0)
            {
                Notify.Show($"~y~-${paid:N0}~s~  {reason}.");
                return;
            }

            state.Debt += shortfall;
            Notify.Show(
                $"~r~{reason}: ${shortfall:N0} you don't have.~s~ Debt is now ${state.Debt:N0}.", true);
        }
    }
}

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
        private readonly Runtime.SpawnManager _spawner;
        private readonly Random _rng = new Random();

        /// <param name="spawner">
        /// Needed because a crew taking one of yours has to pull her ped as well
        /// as her record — a despawn missed here leaves her standing on a corner
        /// she no longer works for you.
        /// </param>
        public EconomyTick(IncidentRoller incidents, Runtime.SpawnManager spawner)
        {
            _incidents = incidents;
            _spawner = spawner;
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
            int indoorTake = 0;

            foreach (var worker in state.Roster.ToList())
            {
                var zone = Zones.Get(worker.ZoneId);
                bool onTheStreet = worker.ShouldBeOnStreet(hour) && zone != null;

                // She is with a client. Not on a corner, not in a room, not
                // building an audience — the whole point of a booking is that it
                // costs you her hours.
                if (worker.IsOnBooking) continue;

                var house = Houses.Get(worker.HouseId);
                bool indoors = worker.ShouldBeWorkingIndoors(hour)
                               && house != null
                               && state.OwnsHouse(house.Id)
                               && !state.IsHouseLocked(house.Id);

                // Stream two accrues every hour either way — that is what makes
                // the two streams compete for the same worker's time. Working
                // indoors is still working: no followers are built in a room.
                Subscriptions.AccrueHourly(worker, onTheStreet || indoors);

                if (indoors)
                {
                    indoorTake += ResolveIndoor(worker, house, night);
                    continue;
                }

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
                               * Traits.StreetMultiplier(worker.TraitSet)
                               * Crew.PayoutBonus(zone.Id)
                               * Pricing.Payout(zone.Id);

                if (night) payout *= cfg.NightDemandBonus;
                if (state.PlayerOwns(zone.Id)) payout *= cfg.OwnedZoneBonus;
                if (state.ZoneHasVehicle(zone.Id)) payout *= cfg.VehicleDemandBonus;

                // Regulars pay on top of the corner and are not touched by zone
                // demand or saturation — they are hers, not the street's.
                int earned = (int)Math.Round(payout) + ClientBook.RegularIncome(worker);
                if (earned > 0)
                {
                    take += earned;
                    worker.LifetimeEarnings += earned;
                }

                ClientBook.AccrueRegular(worker);

                // An hour on a corner is an hour of experience. Recorded after the
                // payout so a promotion never changes the number just paid out.
                Progression.RecordStreetHour(worker, Crew.MentorBonus(worker, hour));

                // A car means she is not walking the whole shift.
                float drain = cfg.StaminaDrainPerHour;
                if (state.ZoneHasVehicle(zone.Id)) drain *= cfg.VehicleStaminaRelief;
                worker.Stamina -= drain;

                state.AddHeat(zone.Id,
                    zone.HeatGain * Traits.HeatMultiplier(worker.TraitSet)
                                  * Crew.HeatMultiplier(zone.Id)
                                  * Law.HeatMultiplierFor(worker)
                                  * Pricing.Heat(zone.Id)
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

            if (indoorTake > 0)
            {
                Game.Player.Money += indoorTake;
                state.LifetimeTake += indoorTake;
                state.LifetimeHouseTake += indoorTake;
                Notify.Show($"~g~+${indoorTake}~s~  Indoors ({hour:00}:00)");
            }

            CheckRaids();
            CheckHouseRaids();

            // Bookings settle before a new one can be offered, so a worker who
            // just came back is immediately eligible again rather than sitting
            // out an extra hour for no reason the player can see.
            ClientBook.ResolveBookings();
            ClientBook.RollOffer();

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
                PayRent();
                PaySubscriptions();
                ClientBook.DecayRegulars();
                Crew.RollExits();
                Law.TickRetainer();
                Law.RollInformant();
                Law.ReleaseFromCustody();
                Rivals.TickWars();
                Rivals.TickAlliances();
                Rivals.RollPoachAttempt(_spawner);
                AccrueInterest();

                // Flagged rather than started here: this runs at midnight, and the
                // incident roller is what knows whether the player is somewhere an
                // incident can sensibly begin.
                if (Money.ShouldSendCollectors()) state.CollectorsPending = true;
            }

            AwardMilestone();

            // Problems are rolled after the payout so the numbers the player just
            // saw are the ones the roll was based on.
            _incidents.RollHourly();
        }

        /// <summary>
        /// An hour worked indoors.
        ///
        /// No zone demand, no saturation and no night bonus. Rooms are a hard cap,
        /// so crowding is handled by the building rather than by a falloff curve,
        /// and the flat rate around the clock is what makes a house and a corner
        /// worth different things at different times of day.
        /// </summary>
        private int ResolveIndoor(WorkerData worker, HouseDef house, bool night)
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            float payout = cfg.BaseRateFor(worker.Tier)
                           * house.RateMultiplier
                           * (worker.Loyalty / 100f)
                           * (1f - state.GetHouseHeat(house.Id))
                           * (worker.Stamina / 100f)
                           * Traits.StreetMultiplier(worker.TraitSet);

            int earned = (int)Math.Round(payout) + ClientBook.RegularIncome(worker);
            if (earned > 0) worker.LifetimeEarnings += earned;

            ClientBook.AccrueRegular(worker);

            // Indoors is still hours on the job — she is earning the same
            // experience, which is what stops the house being a way to park
            // someone forever without her ever progressing.
            Progression.RecordStreetHour(worker);

            worker.Stamina -= cfg.StaminaDrainPerHour * cfg.HouseStaminaDrain;

            state.AddHouseHeat(house.Id,
                house.HeatGain * Traits.HeatMultiplier(worker.TraitSet));

            if (worker.IsExhausted)
                worker.Loyalty -= cfg.LoyaltyDrainWhenExhausted
                                  * Traits.LoyaltyDrainMultiplier(worker.TraitSet);

            worker.Clamp();
            return earned < 0 ? 0 : earned;
        }

        /// <summary>
        /// Rent is due whether the rooms were used or not. That is what makes a
        /// house something you have to keep busy rather than something you simply
        /// own, and it is the fastest route into debt in the whole mod.
        /// </summary>
        private void PayRent()
        {
            int bill = GameState.Current.DailyRentBill;
            if (bill <= 0) return;

            Charge(bill, "Rent");
        }

        /// <summary>
        /// The house version of a raid. Rarer than a corner's — heat builds an
        /// order of magnitude slower indoors — but it takes the whole operation in
        /// one night rather than costing a few days on one street.
        /// </summary>
        private void CheckHouseRaids()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var house in Houses.All)
            {
                if (!state.OwnsHouse(house.Id)) continue;
                if (state.GetHouseHeat(house.Id) < cfg.HouseRaidHeatThreshold) continue;
                if (state.IsHouseLocked(house.Id)) continue;

                int inside = state.WorkersInHouse(house.Id).Count();
                if (inside == 0) continue;

                foreach (var worker in state.WorkersInHouse(house.Id).ToList())
                {
                    worker.Loyalty -= cfg.HouseRaidLoyaltyHit;
                    worker.Clamp();
                }

                state.ClearHouse(house.Id);
                state.HouseLockedUntilDay[house.Id] =
                    GameState.AbsoluteDay() + cfg.HouseRaidLockoutDays;
                state.HouseHeat[house.Id] = cfg.HouseRaidHeatAfter;

                Charge(cfg.HouseRaidFine, $"Raid on {house.Display}");

                Notify.Show(
                    $"~r~They came through the door at {house.Display}.~s~ " +
                    $"All {inside} of them out, and the place is shut for " +
                    $"{cfg.HouseRaidLockoutDays} days. The rent does not stop.", true);

                Persistence.SaveManager.Log(
                    $"HOUSE RAID: {house.Id}, {inside} workers removed.");
            }
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

            // Pricing bends the crowding curve rather than the yield: a premium
            // corner has fewer clients to go round, so people get in each other's
            // way faster, and a cut-price one absorbs a crowd.
            float falloff = Config.Current.ZoneSaturationFalloff * Pricing.Saturation(zone.Id);
            return 1f / (1f + others * falloff);
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
                if (state.HasUpgrade(UpgradeCatalog.Laundromat)) decay *= cfg.LaunderedHeatDecayBonus;

                state.AddHeat(zone.Id, -decay);
            }

            // Houses cool far more slowly than a corner does. Nothing about an
            // address airs out the way a street does when you pull people off it.
            foreach (var house in Houses.All)
            {
                if (!state.OwnsHouse(house.Id)) continue;

                float decay = cfg.HouseHeatDecayPerHour;
                if (state.HasUpgrade(UpgradeCatalog.Laundromat)) decay *= cfg.LaunderedHeatDecayBonus;

                state.AddHouseHeat(house.Id, -decay);
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

            Law.ClearWarningIfSafe();

            foreach (var zone in Zones.All)
            {
                if (state.GetHeat(zone.Id) < cfg.RaidHeatThreshold) continue;
                if (state.IsZoneLocked(zone.Id)) continue;
                if (!state.WorkersIn(zone.Id).Any()) continue;

                // A man on the payroll turns the first raid into a phone call.
                // If the corner is still hot and still staffed when the notice
                // runs out, it goes ahead anyway — he warns, he does not cancel.
                if (Law.WarnInsteadOfRaid(zone)) continue;

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

            if (!state.HasUpgrade(UpgradeCatalog.Laundromat)) return;

            // Washing the money is what keeps it from leading anywhere. Only the
            // corners you hold benefit — you cannot launder heat off someone
            // else's ground.
            var cfg = Config.Current;
            int washed = (int)Math.Round(deposited * cfg.LaundromatWashShare);

            foreach (var zone in Zones.All.Where(z => state.PlayerOwns(z.Id)))
                state.AddHeat(zone.Id, -cfg.LaundromatHeatWash);

            Notify.Show(
                $"~g~${washed:N0} washed.~s~ Your corners cooled off " +
                $"{cfg.LaundromatHeatWash * 100:0} points.");
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

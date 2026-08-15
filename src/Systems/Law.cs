using System;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// The police, as people rather than as a number.
    ///
    /// Heat was a core system whose only levers were two menu buttons — bribe it
    /// down, launder it away. Both are transactions with nobody on the other end.
    /// Everything here puts a person there: one you pay, one who is selling you
    /// out, and the courthouse that takes your crew away for days at a time.
    /// </summary>
    public static class Law
    {
        private static readonly Random Rng = new Random();

        // --- the man on the payroll ----------------------------------------

        public static bool CopPaid =>
            GameState.AbsoluteDay() < GameState.Current.CopPaidUntilDay;

        public static int CopDaysLeft()
        {
            int left = GameState.Current.CopPaidUntilDay - GameState.AbsoluteDay();
            return left < 0 ? 0 : left;
        }

        public static int RetainerCost()
        {
            var cfg = Config.Current;
            int cost = cfg.CopRetainerDailyCost * cfg.CopRetainerDays;

            // He charges more to come back after you let him go, because he knows
            // what happened last time and so do you.
            if (GameState.Current.CopBurnedCount > 0)
                cost = (int)Math.Round(cost * (1f + 0.35f * GameState.Current.CopBurnedCount));

            return cost;
        }

        public static bool PayRetainer()
        {
            int cost = RetainerCost();

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0} in hand. He does not take an IOU.");
                return false;
            }

            var state = GameState.Current;
            var cfg = Config.Current;

            Game.Player.Money -= cost;

            // Extends rather than replaces, so paying early is never a punishment.
            int from = Math.Max(GameState.AbsoluteDay(), state.CopPaidUntilDay);
            state.CopPaidUntilDay = from + cfg.CopRetainerDays;

            Notify.Show(
                $"~g~He's on the payroll for {CopDaysLeft()} days.~s~ You get a phone call " +
                "before a door goes in, instead of after.");
            return true;
        }

        /// <summary>
        /// Called at midnight. Warns a day out, and settles up if it has lapsed.
        /// </summary>
        public static void TickRetainer()
        {
            var state = GameState.Current;

            if (CopPaid)
            {
                if (CopDaysLeft() == 1)
                    Notify.Show("~y~Your man at the station wants paying again tomorrow.");
                return;
            }

            if (state.CopPaidUntilDay <= 0) return;   // never had one, or already settled

            // It has run out. He is not sentimental about it.
            state.CopPaidUntilDay = 0;
            state.CopBurnedCount++;

            var hottest = Zones.All
                .Where(z => state.PlayerOwns(z.Id) && state.WorkersIn(z.Id).Any())
                // Shared heat, not local. Every other law decision below now
                // reads the same number, and picking the "hottest" corner off a
                // different figure than the one the raid gate uses is how the
                // warning ended up aimed at the wrong block.
                .OrderByDescending(z => BladeWorld.WorldLink.ViceHeat(z.Id))
                .FirstOrDefault();

            if (hottest == null)
            {
                Notify.Show("~y~You stopped paying him.~s~ Nothing to find, this time.");
                return;
            }

            // Straight to the raid threshold: he does not need to build a case, he
            // already has one. This is the entire cost of letting the arrangement
            // lapse, and it is meant to be worse than the retainer was.
            state.ZoneHeat[hottest.Id] = 1f;

            Notify.Show(
                $"~r~You stopped paying him, so he stopped protecting you.~s~ " +
                $"Everything he knows about {hottest.Display} is on somebody's desk.", true);

            Persistence.SaveManager.Log($"Cop retainer lapsed; burned zone {hottest.Id}.");
        }

        /// <summary>
        /// Called instead of a raid while he is being paid. Returns true if the
        /// raid should be held off this hour.
        /// </summary>
        public static bool WarnInsteadOfRaid(ZoneDef zone)
        {
            if (!CopPaid) return false;

            var state = GameState.Current;

            // A warning already given and expired means he did what he could.
            if (state.WarnedZoneId == zone.Id)
            {
                if (GameState.AbsoluteHour() < state.WarningExpiresAtHour) return true;

                state.WarnedZoneId = null;
                state.WarningExpiresAtHour = 0;
                return false;
            }

            if (!string.IsNullOrEmpty(state.WarnedZoneId)) return false;   // one at a time

            state.WarnedZoneId = zone.Id;
            state.WarningExpiresAtHour =
                GameState.AbsoluteHour() + Config.Current.CopWarningHours;

            Notify.Show(
                $"~y~He called.~s~ {zone.Display} is on a list for tonight. " +
                $"You have about {Config.Current.CopWarningHours} hours to get everyone " +
                "off it or cool it down.", true);

            return true;
        }

        /// <summary>Clears a standing warning once the zone is no longer worth raiding.</summary>
        public static void ClearWarningIfSafe()
        {
            var state = GameState.Current;
            if (string.IsNullOrEmpty(state.WarnedZoneId)) return;

            var zone = Zones.Get(state.WarnedZoneId);
            if (zone == null)
            {
                state.WarnedZoneId = null;
                return;
            }

            bool empty = !state.WorkersIn(zone.Id).Any();
            // The raid gate reads shared heat and shared is always >= local,
            // so deciding "cool enough to stand the warning down" on the local
            // figure opened a band in which the warning was cleared and
            // immediately re-armed with a fresh expiry every single hour. Inside
            // it the expiry was never reached, the raid was unreachable, and the
            // player got "nobody came" and "he called" back to back forever.
            // With the other mod running hot that band was a third of the scale
            // wide and permanent.
            bool cool = BladeWorld.WorldLink.ViceHeat(zone.Id) < Config.Current.RaidHeatThreshold;

            if (!empty && !cool) return;

            state.WarnedZoneId = null;
            state.WarningExpiresAtHour = 0;
            Notify.Show($"~g~{zone.Display} came off the list.~s~ Nobody came.");
        }

        // --- somebody talking ----------------------------------------------

        public static WorkerData Informant()
        {
            int id = GameState.Current.InformantWorkerId;
            return id < 0 ? null : GameState.Current.GetWorker(id);
        }

        /// <summary>Heat multiplier for a worker who is quietly helping the police.</summary>
        public static float HeatMultiplierFor(WorkerData worker)
        {
            var state = GameState.Current;
            return worker.Id == state.InformantWorkerId
                ? Config.Current.InformantHeatMultiplier
                : 1f;
        }

        /// <summary>Rolled daily. At most one, and never someone who is loyal.</summary>
        public static void RollInformant()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            if (state.InformantWorkerId >= 0)
            {
                // The person may have left or been released since.
                if (Informant() == null) ClearInformant();
                else HintAtInformant();
                return;
            }

            if (state.Roster.Count < cfg.InformantMinRoster) return;
            if (Rng.NextDouble() >= cfg.InformantChancePerDay) return;

            // Somebody with a reason. A loyal worker is not turning on you, and
            // picking one at random would make loyalty meaningless here.
            var candidates = state.Roster
                .Where(w => w.Loyalty < cfg.InformantMaxLoyalty && !w.IsJailed())
                .ToList();

            if (candidates.Count == 0) return;

            var picked = candidates[Rng.Next(candidates.Count)];
            state.InformantWorkerId = picked.Id;
            state.InformantSinceDay = GameState.AbsoluteDay();

            HintAtInformant();
        }

        /// <summary>
        /// The tell, given the moment it starts. Never names her — that is what
        /// the sweep is for.
        ///
        /// This used to wait a day or two for atmosphere, and simulating it showed
        /// why that was wrong: on Del Perro an informant reaches the raid
        /// threshold in 23 in-game hours and on Vinewood in 14, so on exactly the
        /// corners where it matters most the player lost the ground before being
        /// told there was anything to find. The interesting question was never
        /// whether somebody is talking. It is which one, and that still costs.
        /// </summary>
        private static void HintAtInformant()
        {
            var state = GameState.Current;
            if (state.InformantHinted) return;

            state.InformantHinted = true;

            var worker = Informant();
            var zone = Zones.Get(worker?.ZoneId);

            Notify.Show(
                zone != null
                    ? $"~o~Heat on {zone.Display} is climbing faster than the work explains.~s~ " +
                      "Somebody is talking. The law menu can find out who."
                    : "~o~Somebody is telling the police your business.~s~ " +
                      "The law menu can find out who.", true);
        }

        public static int SweepCost()
        {
            int cost = Config.Current.InformantSweepCost;
            if (GameState.Current.HasUpgrade(UpgradeCatalog.Retainer)) cost /= 2;
            return (int)Math.Round(cost * MuscleBackgrounds.LegalCostMultiplier());
        }

        /// <summary>Pays to find out. Names her, or confirms there is nobody.</summary>
        public static void Sweep()
        {
            int cost = SweepCost();

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0}.");
                return;
            }

            Game.Player.Money -= cost;

            var worker = Informant();
            if (worker == null)
            {
                Notify.Show(
                    "~g~Nobody is talking.~s~ Money well spent, even if it does not feel " +
                    "like it.");
                return;
            }

            GameState.Current.InformantKnown = true;
            Notify.Show(
                $"~r~It's {worker.Name}.~s~ Has been for " +
                $"{GameState.AbsoluteDay() - GameState.Current.InformantSinceDay} days.", true);
        }

        /// <summary>
        /// Throws somebody out for talking. Being right is cheap; being wrong
        /// costs you the worker anyway and everyone else's trust with her.
        /// </summary>
        public static void Accuse(WorkerData worker, Runtime.SpawnManager spawner)
        {
            var state = GameState.Current;
            var cfg = Config.Current;
            bool right = worker.Id == state.InformantWorkerId;

            spawner.Despawn(worker.Id);
            state.ReleaseGuardsFor(worker.Id);
            state.Roster.Remove(worker);

            if (right)
            {
                ClearInformant();

                if (Config.Current.ReputationEnabled) Reputation.Award(cfg.AccusationRightReputation);

                Notify.Show(
                    $"~g~You were right about {worker.Name}.~s~ Whatever she was giving " +
                    "them, it stops tonight.", true);

                Persistence.SaveManager.Log($"Informant {worker.Name} removed correctly.");
                return;
            }

            foreach (var other in state.Roster)
            {
                other.Loyalty -= cfg.AccusationWrongLoyaltyHit;
                other.Clamp();
            }

            if (Config.Current.ReputationEnabled) Reputation.Award(-cfg.AccusationWrongReputation);

            Notify.Show(
                $"~r~{worker.Name} wasn't talking to anybody.~s~ You put her out on the " +
                "word of nothing, and every single one of them watched you do it.", true);

            Persistence.SaveManager.Log($"Wrongly accused {worker.Name}.");
        }

        public static void ClearInformant()
        {
            var state = GameState.Current;
            state.InformantWorkerId = -1;
            state.InformantSinceDay = 0;
            state.InformantHinted = false;
            state.InformantKnown = false;
        }

        // --- custody --------------------------------------------------------

        /// <summary>
        /// Puts her in front of a judge rather than through a turnstile. Days out
        /// scale with how hot the corner was — a bust on a quiet street is an
        /// evening, a bust on a corner vice have been watching is a week.
        /// </summary>
        public static void TakeIntoCustody(WorkerData worker, string zoneId)
        {
            var cfg = Config.Current;
            var state = GameState.Current;

            float heat = BladeWorld.WorldLink.ViceHeat(zoneId);
            int days = cfg.CustodyDaysMin +
                       (int)Math.Round(heat * (cfg.CustodyDaysMax - cfg.CustodyDaysMin));

            if (state.HasUpgrade(UpgradeCatalog.Retainer)) days = Math.Max(1, days - 1);

            worker.JailedUntilDay = GameState.AbsoluteDay() + days;
            worker.ZoneId = null;
            worker.HouseId = null;
            worker.ManagesZoneId = null;
            worker.State = WorkerState.OffDuty;

            int fine = state.HasUpgrade(UpgradeCatalog.Retainer)
                ? cfg.CourtFine / 2
                : cfg.CourtFine;

            fine = (int)Math.Round(fine * MuscleBackgrounds.LegalCostMultiplier());

            Notify.Show(
                $"~r~{worker.Name} is in custody.~s~ {days} day(s) before she is out, " +
                "and a fine on top. A lawyer can shorten it.", true);

            EconomyTick.Charge(fine, $"Fine — {worker.Name}");
        }

        public static int LawyerCost(WorkerData worker)
        {
            int days = worker.JailDaysLeft();
            int cost = Config.Current.LawyerCostPerDay * Math.Max(1, days);

            if (GameState.Current.HasUpgrade(UpgradeCatalog.Retainer)) cost /= 2;
            return (int)Math.Round(cost * MuscleBackgrounds.LegalCostMultiplier());
        }

        /// <summary>Buys the rest of her sentence. All of it, or none.</summary>
        public static bool PayLawyer(WorkerData worker)
        {
            int cost = LawyerCost(worker);

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0} in hand.");
                return false;
            }

            Game.Player.Money -= cost;
            worker.JailedUntilDay = 0;
            worker.Loyalty += Config.Current.LawyerLoyaltyReward;
            worker.Clamp();

            Notify.Show(
                $"~g~{worker.Name} is out.~s~ That cost ${cost:N0}, and she knows who paid it.");
            return true;
        }

        /// <summary>Called at midnight. Lets out anyone who has served their days.</summary>
        public static void ReleaseFromCustody()
        {
            foreach (var worker in GameState.Current.Roster.Where(w => w.JailedUntilDay > 0).ToList())
            {
                if (GameState.AbsoluteDay() < worker.JailedUntilDay) continue;

                worker.JailedUntilDay = 0;

                // Nobody comes out of a week inside pleased with the person who
                // left them there.
                worker.Loyalty -= Config.Current.CustodyLoyaltyHit;
                worker.Clamp();

                Notify.Show(
                    $"~y~{worker.Name} is out.~s~ She served the whole thing. " +
                    $"Loyalty {worker.Loyalty:0}.");
            }
        }
    }
}

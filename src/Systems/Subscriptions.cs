using System;
using System.Linq;
using GTA.Chrono;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// The second revenue stream: a weekly deposit per worker, driven by a
    /// follower count.
    ///
    /// The point of it is that the two streams compete for the same worker-hours.
    /// Street work pays hourly, generates heat and invites incidents. Followers
    /// only build while a worker is <em>off</em> the street, and decay while
    /// they're on it. So the roster stops being a throughput problem and becomes
    /// an allocation one: who earns now and hot, who earns later and safe.
    ///
    /// That deliberately reinforces the existing stamina/loyalty tension rather
    /// than bypassing it — a worker parked off duty to build an audience is also
    /// resting, and a rested worker builds faster.
    /// </summary>
    public static class Subscriptions
    {
        public static bool Unlocked => GameState.Current.HasUpgrade(UpgradeCatalog.Creator);

        public static string Brand => Config.Current.SubscriptionBrand;

        /// <summary>Higher tiers draw a bigger audience for the same hours.</summary>
        public static float TierAppeal(int tier)
        {
            if (tier >= 3) return 1.4f;
            return tier == 2 ? 1.0f : 0.8f;
        }

        /// <summary>
        /// Called once per in-game hour per worker, from the economy tick.
        /// </summary>
        public static void AccrueHourly(WorkerData worker, bool onTheStreet)
        {
            if (!Unlocked) return;
            var cfg = Config.Current;

            if (onTheStreet)
            {
                worker.Followers -= cfg.FollowerDecayPerHourWorking;
            }
            else
            {
                // Scaled by stamina as well as loyalty: someone recovering from
                // being run into the ground is not producing anything either.
                float gain = cfg.FollowerGainPerHourOffDuty
                             * (worker.Stamina / 100f)
                             * (worker.Loyalty / 100f)
                             * TierAppeal(worker.Tier)
                             * Traits.FollowerMultiplier(worker.TraitSet);

                // The ring light is equipment, so it only helps the people who
                // were going to use it.
                if ((worker.TraitSet & WorkerTrait.CameraReady) != 0)
                    gain *= cfg.RingLightCameraReadyBonus;

                // Studio time was bought for this worker specifically. Unlike the
                // ring light it helps whoever you paid for, camera-ready or not —
                // it is the way to make an ordinary earner worth benching.
                if (worker.HasStudio) gain *= cfg.StudioFollowerBonus;

                worker.Followers += gain;
            }

            worker.Clamp();
        }

        /// <summary>What this worker would bring in at the next deposit.</summary>
        public static int WeeklyEstimate(WorkerData worker)
        {
            if (!Unlocked) return 0;
            return (int)Math.Round(
                worker.Followers
                * Config.Current.RevenuePerFollowerWeekly
                * (worker.Loyalty / 100f));
        }

        public static int RosterWeeklyEstimate()
        {
            return GameState.Current.Roster.Sum(WeeklyEstimate);
        }

        /// <summary>Game days remaining until the next deposit, or -1 if not due yet tracked.</summary>
        public static int DaysUntilPayout()
        {
            if (!Unlocked) return -1;

            long elapsed;
            if (!TryDaysSinceAnchor(out elapsed)) return Config.Current.SubscriptionPayoutDays;

            int remaining = Config.Current.SubscriptionPayoutDays - (int)elapsed;
            return remaining < 0 ? 0 : remaining;
        }

        /// <summary>
        /// Pays out if a full period has elapsed. Returns the total deposited, or
        /// zero if nothing was due.
        /// </summary>
        public static int TryPayout()
        {
            var state = GameState.Current;

            if (!Unlocked)
            {
                // Keep the anchor current while locked so buying the upgrade does
                // not immediately trigger a backdated payout.
                AnchorToToday();
                return 0;
            }

            long elapsed;
            if (!TryDaysSinceAnchor(out elapsed))
            {
                AnchorToToday();
                return 0;
            }

            if (elapsed < Config.Current.SubscriptionPayoutDays) return 0;

            int total = 0;
            foreach (var worker in state.Roster)
            {
                int amount = WeeklyEstimate(worker);
                if (amount <= 0) continue;

                worker.LifetimeSubscriptionEarnings += amount;
                total += amount;
            }

            AnchorToToday();
            return total;
        }

        // ------------------------------------------------------------------

        private static bool TryDaysSinceAnchor(out long days)
        {
            days = 0;
            var state = GameState.Current;

            GameClockDate anchor;
            if (!GameClockDate.TryFromOrdinalDate(
                    state.LastPayoutYear, state.LastPayoutDayOfYear, out anchor))
                return false;

            days = GameClock.Today.SignedDurationSince(anchor).WholeDays;

            // Clock moved backwards (a loaded save, or the player set the time).
            // Re-anchor rather than paying out on a negative period.
            if (days < 0) return false;

            return true;
        }

        private static void AnchorToToday()
        {
            var today = GameClock.Today;
            GameState.Current.LastPayoutYear = today.Year;
            GameState.Current.LastPayoutDayOfYear = today.DayOfYear;
        }
    }
}

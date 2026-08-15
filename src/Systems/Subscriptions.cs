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
    /// <summary>
    /// What a worker is doing with her hours this hour, for the purposes of an
    /// audience.
    ///
    /// This replaces a bool called <c>onTheStreet</c> which the caller passed
    /// <c>onTheStreet || indoors</c> — so it never meant what it was named, and
    /// there was no third value to give a woman who is literally producing
    /// content. Passing false for her would have handed her full off-duty gain
    /// on top of her wage; passing true would have decayed her audience while
    /// she made it. A signature change rather than an overload, because an
    /// overload is how the double-count ships.
    /// </summary>
    public enum SubscriptionMode
    {
        /// <summary>Resting. Builds fastest and churns not at all.</summary>
        Idle = 0,

        /// <summary>On a corner or in a room. Bleeds followers.</summary>
        Working = 1,

        /// <summary>Living in a content house. Builds and is spent at once.</summary>
        Producing = 2
    }

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
        public static void AccrueHourly(WorkerData worker, SubscriptionMode mode)
        {
            if (!Unlocked) return;
            var cfg = Config.Current;

            switch (mode)
            {
                case SubscriptionMode.Working:
                    worker.Followers -= cfg.FollowerDecayPerHourWorking;
                    break;

                case SubscriptionMode.Producing:
                    // She is spending the audience as fast as she makes it. The
                    // gain is docked so that benching is still the way to BUILD —
                    // otherwise the follower system collapses into the house and
                    // "bench to build, house to earn" stops being a loop.
                    worker.Followers += OffDutyGain(worker) * cfg.ContentGainShare;
                    break;

                default:
                    worker.Followers += OffDutyGain(worker);
                    break;
            }

            // Churn is charged for every hour she LIVES in a content house, not
            // only the hours she is on shift. Charging it per producing hour made
            // the equilibrium depend on the shift — 283x her gain rate on Always,
            // 521x on Days, 750x on Nights — and on the two shifts anybody
            // actually uses that clamped almost every worker worth housing at the
            // follower cap. Which would have made the audience factor a constant,
            // the banked-audience loop inert, and both studios worth exactly
            // nothing: the precise "upgrade that buys nothing" fault this was
            // supposed to repair. Per residency hour it lands at 304x on Days and
            // 312x on Nights, so the shift barely moves it.
            if (Houses.IsContentHouseId(worker.HouseId))
                worker.Followers -= worker.Followers * cfg.ContentChurnPerHour;

            worker.Clamp();
        }

        /// <summary>
        /// What she builds in an hour she is not working. Extracted so the idle
        /// and producing arms cannot drift apart, and so the content house reaches
        /// every one of these multipliers with no new multiplier code.
        /// </summary>
        public static float OffDutyGain(WorkerData worker)
        {
            if (worker == null) return 0f;
            var cfg = Config.Current;

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

            // The bought studio is a room everybody uses, unlike the per-worker
            // studio time which is hers alone. They stack deliberately.
            if (GameState.Current.HasUpgrade(UpgradeCatalog.Studio))
                gain *= cfg.StudioRosterBonus;

            return gain;
        }

        /// <summary>
        /// Followers she can convert to cash right now, and what it pays.
        ///
        /// The count used to cap and then produce nothing — an audience that had
        /// stopped growing was simply a dead number. Spending it turns the ceiling
        /// into a resource, at the cost of the weekly deposit it was feeding and
        /// some of her goodwill, because she built it.
        /// </summary>
        public static int CashOutValue(WorkerData worker)
        {
            if (!Unlocked) return 0;

            var cfg = Config.Current;
            float spent = worker.Followers * cfg.FollowerCashOutShare;

            return (int)Math.Round(spent * cfg.FollowerCashOutRate);
        }

        public static bool CanCashOut(WorkerData worker, out string reason)
        {
            reason = null;

            if (!Unlocked)
            {
                reason = "Buy the ring light first. There is no audience to sell.";
                return false;
            }

            if (CashOutValue(worker) < 500)
            {
                reason = "Not enough of an audience to be worth anything yet.";
                return false;
            }

            int since = GameState.AbsoluteDay() - worker.LastCashOutDay;
            int wait = Config.Current.CashOutCooldownDays - since;
            if (worker.LastCashOutDay > 0 && wait > 0)
            {
                reason = $"She sold to them {since} day(s) ago. Nobody is buying the same " +
                         $"thing twice in a week — {wait} day(s) yet.";
                return false;
            }

            return true;
        }

        /// <summary>Spends the audience. Returns what it paid, or 0.</summary>
        public static int CashOut(WorkerData worker)
        {
            string reason;
            if (!CanCashOut(worker, out reason))
            {
                Notify.Show($"~r~{reason}");
                return 0;
            }

            var cfg = Config.Current;
            int paid = CashOutValue(worker);

            worker.Followers -= worker.Followers * cfg.FollowerCashOutShare;
            worker.Loyalty -= cfg.FollowerCashOutLoyalty;
            worker.LastCashOutDay = GameState.AbsoluteDay();
            worker.Clamp();

            GTA.Game.Player.Money += paid;
            GameState.Current.LifetimeTake += paid;
            GameState.Current.LifetimeSubscriptionTake += paid;
            worker.LifetimeSubscriptionEarnings += paid;

            Notify.Show(
                $"~g~+${paid:N0}~s~ — {worker.Name} sold the lot to whoever was buying. " +
                $"She is down to {worker.Followers:N0} and she is not thrilled about it.", true);

            return paid;
        }

        /// <summary>What this worker would bring in at the next deposit.</summary>
        public static int WeeklyEstimate(WorkerData worker)
        {
            if (!Unlocked) return 0;

            // Agrees with TryPayout, so the "next deposit" readout never promises
            // money a content resident will not be paid.
            if (Houses.IsContentHouseId(worker.HouseId)) return 0;
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
                // A content resident already monetises this exact stock, every
                // hour, through her audience factor. Paying the weekly deposit on
                // top of it would sell the same followers twice — worth about
                // $15,000 a game-day at the top of the ladder, which is most of
                // the reason the house is supposed to be the worse business.
                if (Houses.IsContentHouseId(worker.HouseId)) continue;

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

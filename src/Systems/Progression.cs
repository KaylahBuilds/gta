using System;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Tier progression and the money you can spend on one specific person.
    ///
    /// Tier is the largest term in the payout formula, and until now it was rolled
    /// once at recruit and never moved again — the player had no way to influence
    /// the single number that mattered most. There are two paths out of that, and
    /// they are deliberately different in kind: hours on the street earn it slowly
    /// and for free, the wardrobe buys it immediately and expensively.
    ///
    /// Everything here attaches to one worker and is lost when she leaves. That is
    /// the point of it. Global upgrades make the operation better; this makes a
    /// person better, which is what turns a roster into a crew you would rather
    /// not lose.
    /// </summary>
    public static class Progression
    {
        public const int MaxTier = 3;

        /// <summary>The tier she is working toward, or 0 if there isn't one.</summary>
        public static int NextTier(WorkerData worker)
        {
            return worker.Tier >= MaxTier ? 0 : worker.Tier + 1;
        }

        public static bool IsMaxTier(WorkerData worker) => worker.Tier >= MaxTier;

        /// <summary>Street hours required for her next promotion. 0 if maxed.</summary>
        public static int HoursNeeded(WorkerData worker)
        {
            int next = NextTier(worker);
            return next == 0 ? 0 : Config.Current.TierUpHoursFor(next);
        }

        /// <summary>0..1 toward the next promotion, ignoring the loyalty gate.</summary>
        public static float HoursProgress(WorkerData worker)
        {
            int needed = HoursNeeded(worker);
            if (needed <= 0) return 1f;

            float p = worker.HoursWorked / (float)needed;
            return p > 1f ? 1f : p;
        }

        /// <summary>
        /// Called once per in-game hour for a worker who is actually on a corner.
        /// Off-duty hours build followers instead, which is the trade.
        /// </summary>
        /// <param name="bonusHours">
        /// Extra experience from working alongside somebody better. Counts toward
        /// promotion but not toward burnout — being taught is not the same as
        /// having done the hours.
        /// </param>
        public static void RecordStreetHour(WorkerData worker, int bonusHours = 0)
        {
            worker.HoursWorked += 1 + (bonusHours < 0 ? 0 : bonusHours);
            worker.LifetimeHours++;
            TryPromoteFromWork(worker);
        }

        /// <summary>
        /// Promotes on hours plus loyalty. The loyalty gate is what stops this from
        /// being a pure idle timer — someone you have run into the ground stops
        /// progressing until you fix that, which is the same lever the rest of the
        /// mod already runs on.
        /// </summary>
        public static bool TryPromoteFromWork(WorkerData worker)
        {
            int next = NextTier(worker);
            if (next == 0) return false;

            var cfg = Config.Current;
            if (worker.HoursWorked < cfg.TierUpHoursFor(next)) return false;
            if (worker.Loyalty < cfg.TierUpLoyalty) return false;

            Promote(worker, next, earned: true);
            return true;
        }

        /// <summary>
        /// Hours are reset on promotion so the next tier is a fresh climb rather
        /// than something already half-paid by the last one.
        /// </summary>
        private static void Promote(WorkerData worker, int tier, bool earned)
        {
            worker.Tier = tier;
            worker.HoursWorked = 0;
            worker.Clamp();

            int open = 0;
            foreach (var zone in Zones.OpenTo(worker.Tier)) open++;

            Notify.Show(
                $"~g~{worker.Name} is tier {tier} now.~s~ " +
                (earned ? "She earned it on the street. " : "") +
                $"{open} of {Zones.All.Count} corners are open to her.", true);

            Persistence.SaveManager.Log(
                $"{worker.Name} promoted to tier {tier} ({(earned ? "worked" : "bought")}).");
        }

        // --- wardrobe: buy the promotion ------------------------------------

        public static bool CanWardrobe(WorkerData worker, out string reason)
        {
            reason = null;

            if (IsMaxTier(worker))
            {
                reason = "She is as high as this goes.";
                return false;
            }

            if (worker.Loyalty < Config.Current.WardrobeMinLoyalty)
            {
                reason = $"She won't take the step up at {worker.Loyalty:0} loyalty. " +
                         $"Get her to {Config.Current.WardrobeMinLoyalty:0} first.";
                return false;
            }

            return true;
        }

        public static bool BuyWardrobe(WorkerData worker)
        {
            string reason;
            if (!CanWardrobe(worker, out reason))
            {
                Notify.Show($"~r~{reason}");
                return false;
            }

            int next = NextTier(worker);
            int cost = Config.Current.WardrobeCostFor(next);
            if (!Take(cost)) return false;

            Promote(worker, next, earned: false);
            return true;
        }

        // --- clinic: stamina and loyalty, immediately ------------------------

        public static bool CanClinic(WorkerData worker, out string reason)
        {
            reason = null;

            if (worker.Stamina >= 99f && worker.Loyalty >= 100f)
            {
                reason = "Nothing to fix. She's fine.";
                return false;
            }

            return true;
        }

        public static bool BuyClinic(WorkerData worker)
        {
            string reason;
            if (!CanClinic(worker, out reason))
            {
                Notify.Show($"~r~{reason}");
                return false;
            }

            if (!Take(Config.Current.ClinicCost)) return false;

            worker.Stamina = 100f;
            worker.Loyalty += Config.Current.ClinicLoyalty;
            worker.Clamp();

            Notify.Show(
                $"~g~{worker.Name}~s~ is rested and looked after. " +
                $"Loyalty {worker.Loyalty:0}.");
            return true;
        }

        // --- studio: permanent follower multiplier ---------------------------

        public static bool CanStudio(WorkerData worker, out string reason)
        {
            reason = null;

            if (!Subscriptions.Unlocked)
            {
                reason = "Buy the ring light first — there is no second stream to boost.";
                return false;
            }

            if (worker.HasStudio)
            {
                reason = "Already paid for.";
                return false;
            }

            return true;
        }

        public static bool BuyStudio(WorkerData worker)
        {
            string reason;
            if (!CanStudio(worker, out reason))
            {
                Notify.Show($"~r~{reason}");
                return false;
            }

            if (!Take(Config.Current.StudioCost)) return false;

            worker.HasStudio = true;

            Notify.Show(
                $"~g~{worker.Name}~s~ has somewhere proper to shoot. " +
                $"Followers build {(Config.Current.StudioFollowerBonus - 1f) * 100:0}% faster " +
                "for her from now on.");
            return true;
        }

        // --- papers: buy the Connected trait ---------------------------------

        public static bool CanPapers(WorkerData worker, out string reason)
        {
            reason = null;

            if ((worker.TraitSet & WorkerTrait.Connected) != 0)
            {
                reason = "She is already Connected. Nothing to buy.";
                return false;
            }

            return true;
        }

        public static bool BuyPapers(WorkerData worker)
        {
            string reason;
            if (!CanPapers(worker, out reason))
            {
                Notify.Show($"~r~{reason}");
                return false;
            }

            if (!Take(Config.Current.PapersCost)) return false;

            worker.TraitSet |= WorkerTrait.Connected;

            Notify.Show(
                $"~g~{worker.Name}~s~ is Connected. She draws far less heat wherever " +
                "you post her.");
            return true;
        }

        // --- shared ----------------------------------------------------------

        /// <summary>
        /// Investment is paid in cash or not at all. Routing it through
        /// EconomyTick.Charge would let the player buy a wardrobe on credit and
        /// then collapse under the interest, which turns a reward into a trap.
        /// </summary>
        private static bool Take(int cost)
        {
            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0} in hand.");
                return false;
            }

            Game.Player.Money -= cost;
            return true;
        }
    }
}

using System;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>Why she is asking to leave.</summary>
    public enum ExitReason
    {
        None = 0,

        /// <summary>She has been doing this long enough.</summary>
        Burnout = 1,

        /// <summary>Something outside the job. Nothing you did.</summary>
        Personal = 2
    }

    /// <summary>
    /// The roster as people rather than slots.
    ///
    /// Three things live here, and they exist because the crew had exactly two
    /// endings — walk off at low loyalty, or get released by the player. Nothing
    /// in between, and nothing that treated a worker as anything other than a row
    /// of numbers that either earns or doesn't.
    ///
    /// A manager gives tier 3 somewhere to go. A mentor makes who you post
    /// together matter. And an exit that is neither a betrayal nor a firing gives
    /// the roster the only arc it was missing.
    /// </summary>
    public static class Crew
    {
        private static readonly Random Rng = new Random();

        // --- managers ------------------------------------------------------

        /// <summary>The worker running a corner, if anyone is.</summary>
        public static WorkerData ManagerOf(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;
            return GameState.Current.Roster.FirstOrDefault(w => w.ManagesZoneId == zoneId);
        }

        /// <summary>
        /// How much better everyone else on this corner does. Scaled by the
        /// manager's own loyalty — somebody who has checked out is not running
        /// anything — so putting your best earner on it is not automatically right.
        /// </summary>
        public static float PayoutBonus(string zoneId)
        {
            var manager = ManagerOf(zoneId);
            if (manager == null) return 1f;

            float strength = manager.Loyalty / 100f;
            return 1f + (Config.Current.ManagerPayoutBonus - 1f) * strength;
        }

        public static float HeatMultiplier(string zoneId)
        {
            var manager = ManagerOf(zoneId);
            if (manager == null) return 1f;

            float strength = manager.Loyalty / 100f;
            return 1f - (1f - Config.Current.ManagerHeatReduction) * strength;
        }

        public static bool CanManage(WorkerData worker, out string reason)
        {
            reason = null;
            var cfg = Config.Current;

            if (worker.Tier < cfg.ManagerMinTier)
            {
                reason = $"Running a corner takes tier {cfg.ManagerMinTier}. She is {worker.Tier}.";
                return false;
            }

            return true;
        }

        // --- mentoring -----------------------------------------------------

        /// <summary>
        /// Extra experience from working alongside somebody better, or under
        /// somebody running the corner. Only ever helps the junior — the senior
        /// gains nothing, which is what stops stacking two tier-3s being optimal.
        /// </summary>
        public static int MentorBonus(WorkerData worker, int hour)
        {
            if (string.IsNullOrEmpty(worker.ZoneId)) return 0;

            var state = GameState.Current;

            bool mentored = state.WorkersIn(worker.ZoneId)
                .Any(other => other.Id != worker.Id
                              && other.Tier > worker.Tier
                              && other.ShouldBeOnStreet(hour));

            if (!mentored)
            {
                var manager = ManagerOf(worker.ZoneId);
                mentored = manager != null && manager.Tier > worker.Tier;
            }

            return mentored ? Config.Current.MentorBonusHours : 0;
        }

        // --- leaving -------------------------------------------------------

        /// <summary>
        /// Rolled once per game day. At most one worker is asking at a time — two
        /// simultaneous resignations is a queue, and the phone is not a to-do list.
        /// </summary>
        public static void RollExits()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            if (state.Roster.Any(w => w.WantsOut))
            {
                CheckOverdue();
                return;
            }

            foreach (var worker in state.Roster.OrderBy(_ => Rng.Next()))
            {
                // Not while she is with a client or sitting in a cell. Neither is
                // a moment she can hand in notice, and a resignation the player
                // cannot act on is just a stuck phone entry.
                if (worker.IsOnBooking || worker.IsJailed()) continue;

                // Burnout first: it is earned, and it should outrank a coin flip.
                if (worker.LifetimeHours >= cfg.BurnoutHours &&
                    Rng.NextDouble() < cfg.BurnoutChancePerDay)
                {
                    Ask(worker, ExitReason.Burnout);
                    return;
                }

                if (Rng.NextDouble() < cfg.PersonalExitChancePerDay)
                {
                    Ask(worker, ExitReason.Personal);
                    return;
                }
            }
        }

        private static void Ask(WorkerData worker, ExitReason reason)
        {
            worker.WantsOutReason = (int)reason;
            worker.WantsOutSinceDay = GameState.AbsoluteDay();

            Notify.Show(reason == ExitReason.Burnout
                ? $"~y~{worker.Name} wants out.~s~ She has been doing this a long time " +
                  "and she is done. Check the phone."
                : $"~y~{worker.Name} wants out.~s~ Nothing to do with you — she has " +
                  "somewhere else to be. Check the phone.", true);
        }

        /// <summary>She goes on her own if left waiting, and it costs you a name.</summary>
        private static void CheckOverdue()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var worker in state.Roster.Where(w => w.WantsOut).ToList())
            {
                if (GameState.AbsoluteDay() - worker.WantsOutSinceDay < cfg.ExitDecisionDays)
                    continue;

                Release(worker, clean: false);
            }
        }

        public static string ReasonBlurb(WorkerData worker)
        {
            return (ExitReason)worker.WantsOutReason == ExitReason.Burnout
                ? "She has done her hours and she is tired in a way money does not fix."
                : "A partner, a family, a straight job — she did not say which and it " +
                  "is not really your business.";
        }

        /// <summary>What it costs to talk her round.</summary>
        public static int RetentionCost(WorkerData worker)
        {
            int cost = Config.Current.RetentionCostPerTier * Math.Max(1, worker.Tier);

            // Burnout is dearer to buy off, because you are asking for something
            // she has already decided she does not want to give.
            if ((ExitReason)worker.WantsOutReason == ExitReason.Burnout)
                cost = (int)Math.Round(cost * 1.5f);

            return cost;
        }

        /// <summary>Pays her to stay. Resets the clock rather than ending it.</summary>
        public static bool Retain(WorkerData worker)
        {
            int cost = RetentionCost(worker);

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0} in hand.");
                return false;
            }

            Game.Player.Money -= cost;

            worker.WantsOutReason = (int)ExitReason.None;
            worker.WantsOutSinceDay = 0;
            worker.Loyalty += Config.Current.RetentionLoyalty;

            // Burnout is about hours, so buying her off has to move the hours or
            // she would simply ask again the next morning and the money would have
            // bought nothing at all.
            if (worker.LifetimeHours >= Config.Current.BurnoutHours)
                worker.LifetimeHours = (int)(Config.Current.BurnoutHours * 0.75f);

            worker.Clamp();

            Notify.Show(
                $"~g~{worker.Name} is staying.~s~ That cost ${cost:N0}, and it will " +
                "not hold forever.");
            return true;
        }

        /// <summary>Refuses. She stays, and she remembers it.</summary>
        public static void Refuse(WorkerData worker)
        {
            worker.WantsOutReason = (int)ExitReason.None;
            worker.WantsOutSinceDay = 0;
            worker.Loyalty -= Config.Current.RefusalLoyaltyHit;
            worker.Clamp();

            Notify.Show(
                $"~o~{worker.Name} is staying because you said so.~s~ " +
                $"Loyalty {worker.Loyalty:0}. She will not forget you made her ask twice.");
        }

        /// <summary>
        /// Lets her go. A clean exit builds your name and often sends somebody
        /// else your way; leaving her waiting until she walks does the opposite.
        /// </summary>
        public static void Release(WorkerData worker, bool clean)
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            state.Roster.Remove(worker);

            if (clean)
            {
                if (Config.Current.ReputationEnabled) Reputation.Award(cfg.ExitGoodReputation);

                bool referred = Rng.NextDouble() < cfg.ReferralChance;
                if (referred) state.PendingReferrals++;

                Notify.Show(
                    $"~g~{worker.Name} got out.~s~ " +
                    (referred
                        ? "She told somebody you were straight with her — your next signing " +
                          "comes in knowing that."
                        : "Word gets around that people leave here in one piece."), true);

                Persistence.SaveManager.Log(
                    $"{worker.Name} released cleanly after {worker.LifetimeHours} lifetime hours.");
            }
            else
            {
                if (Config.Current.ReputationEnabled) Reputation.Award(-cfg.ExitBadReputation);

                Notify.Show(
                    $"~r~{worker.Name} left on her own.~s~ You never gave her an answer, " +
                    "and people heard about it.", true);

                Persistence.SaveManager.Log($"{worker.Name} walked out unanswered.");
            }
        }
    }
}

using System;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Borrowing, and the people who come to collect.
    ///
    /// Debt was one-directional: it only ever happened to you, as the residue of
    /// bills you could not cover, and its only expression was a number climbing
    /// toward a collapse notification. Borrowing on purpose makes it a decision.
    /// Collectors make it a person at the door.
    /// </summary>
    public static class Money
    {
        private static readonly Random Rng = new Random();

        // --- borrowing -------------------------------------------------------

        /// <summary>
        /// Most you can be lent right now. Nobody hands you enough to end the run
        /// in one transaction, so the ceiling shrinks as what you already owe
        /// approaches the collapse line.
        /// </summary>
        public static int MaxLoan()
        {
            var cfg = Config.Current;
            var state = GameState.Current;

            float ceiling = cfg.DebtCollapseThreshold * cfg.LoanDebtCeiling;
            float roomInDebt = (ceiling - state.Debt) / cfg.LoanMultiplier;

            int max = (int)Math.Round(roomInDebt / 1000f) * 1000;
            if (max > cfg.LoanMax) max = cfg.LoanMax;
            if (max < 0) max = 0;

            return max;
        }

        public static bool CanBorrow(int amount, out string reason)
        {
            reason = null;
            var cfg = Config.Current;

            if (amount < cfg.LoanMin)
            {
                reason = $"Nobody gets out of bed for less than ${cfg.LoanMin:N0}.";
                return false;
            }

            if (amount > MaxLoan())
            {
                reason = MaxLoan() <= 0
                    ? "You already owe more than anyone will lend against."
                    : $"The most anyone will give you right now is ${MaxLoan():N0}.";
                return false;
            }

            return true;
        }

        public static int OwedFor(int amount)
        {
            return (int)Math.Round(amount * Config.Current.LoanMultiplier);
        }

        public static bool Borrow(int amount)
        {
            string reason;
            if (!CanBorrow(amount, out reason))
            {
                Notify.Show($"~r~{reason}");
                return false;
            }

            var state = GameState.Current;
            int owed = OwedFor(amount);

            Game.Player.Money += amount;
            state.Debt += owed;
            state.LifetimeBorrowed += amount;

            Notify.Show(
                $"~g~+${amount:N0}~s~ in your hand. ~r~${owed:N0}~s~ on the book, and it " +
                $"compounds at {Config.Current.DebtInterestPerDay * 100:0}% a day like " +
                "everything else you owe.", true);

            Persistence.SaveManager.Log($"Borrowed ${amount}, owing ${owed}.");
            return true;
        }

        // --- collectors -------------------------------------------------------

        public static int CollectorsThreshold()
        {
            var cfg = Config.Current;
            return (int)Math.Round(cfg.DebtCollapseThreshold * cfg.CollectorsDebtShare);
        }

        /// <summary>
        /// Rolled daily once the debt is deep enough. Returns true if somebody is
        /// on their way, so the caller can start the incident.
        /// </summary>
        public static bool ShouldSendCollectors()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            if (state.Debt < CollectorsThreshold()) return false;
            if (GameState.AbsoluteDay() < state.CollectorsGraceUntilDay) return false;

            return Rng.NextDouble() < cfg.CollectorsChancePerDay;
        }

        /// <summary>Seeing them off buys a few days without interest, not forgiveness.</summary>
        public static void CollectorsSeenOff()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            state.CollectorsGraceUntilDay = GameState.AbsoluteDay() + cfg.CollectorsGraceDays;

            if (Config.Current.ReputationEnabled) Reputation.Award(15);

            Notify.Show(
                $"~g~They left with nothing.~s~ You still owe ${state.Debt:N0}, but nobody " +
                $"is coming for it for {cfg.CollectorsGraceDays} days.", true);
        }

        /// <summary>
        /// What happens when you do not turn up. They take somebody and call it
        /// settled — the debt drops, which is the ugliest exchange in the mod and
        /// is meant to be.
        /// </summary>
        public static void CollectorsTakePayment(Runtime.SpawnManager spawner)
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            // The most valuable person they can find, because that is what a
            // collector would do.
            var worker = state.Roster
                .Where(w => !w.IsJailed())
                .OrderByDescending(w => w.Tier)
                .ThenByDescending(w => w.LifetimeEarnings)
                .FirstOrDefault();

            if (worker == null)
            {
                // Nothing to take. They settle it in cash instead.
                int taken = Math.Min(Game.Player.Money, state.Debt);
                Game.Player.Money -= taken;
                state.Debt -= taken;

                Notify.Show(
                    $"~r~They emptied your pockets.~s~ ${taken:N0} gone, ${state.Debt:N0} " +
                    "still owed.", true);
                return;
            }

            int written = (int)Math.Round(state.Debt * cfg.CollectorsTakeShare);
            state.Debt -= written;
            if (state.Debt < 0) state.Debt = 0;

            spawner.Despawn(worker.Id);
            state.Roster.Remove(worker);

            state.CollectorsGraceUntilDay = GameState.AbsoluteDay() + cfg.CollectorsGraceDays;

            if (Config.Current.ReputationEnabled) Reputation.Award(-30);

            Notify.Show(
                $"~r~They took {worker.Name}.~s~ ${written:N0} came off the book for her, " +
                "and everybody on the street knows what you let happen.", true);

            Persistence.SaveManager.Log(
                $"Collectors took {worker.Name}; ${written} written off, ${state.Debt} remains.");
        }
    }
}

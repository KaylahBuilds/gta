namespace OnTheBlade.Core
{
    /// <summary>
    /// Street reputation: the progression axis the mod was missing.
    ///
    /// Money measured how well the business ran but nothing measured how you were
    /// regarded, so a player who had held the map for a month looked identical to
    /// one who signed their first worker an hour ago. Reputation is earned by
    /// turning up — every incident resolved moves it, every one dropped moves it
    /// back — and it gates the things that should be earned rather than bought.
    /// </summary>
    public static class Reputation
    {
        public const int Max = 1000;

        /// <summary>Tier-3 prospects will not sign for a nobody.</summary>
        public const int TierThreeThreshold = 250;

        public static string Rank(int rep)
        {
            if (rep >= 750) return "Untouchable";
            if (rep >= 500) return "Feared";
            if (rep >= 250) return "Respected";
            if (rep >= 100) return "Known";
            return "Nobody";
        }

        /// <summary>Reputation needed for the next rank, or -1 at the top.</summary>
        public static int NextRankAt(int rep)
        {
            if (rep < 100) return 100;
            if (rep < 250) return 250;
            if (rep < 500) return 500;
            if (rep < 750) return 750;
            return -1;
        }

        /// <summary>
        /// Crews charge less for peace when they respect you — protection is a
        /// negotiation, and a nobody has no leverage.
        /// </summary>
        public static float ProtectionDiscount(int rep)
        {
            if (rep >= 750) return 0.60f;
            if (rep >= 500) return 0.75f;
            if (rep >= 250) return 0.88f;
            return 1f;
        }

        /// <summary>New signings trust a known name a little further.</summary>
        public static float LoyaltyBonus(int rep)
        {
            if (rep >= 750) return 15f;
            if (rep >= 500) return 10f;
            if (rep >= 250) return 5f;
            return 0f;
        }

        public static void Award(int amount)
        {
            var state = GameState.Current;
            int before = state.Reputation;

            state.Reputation += amount;
            if (state.Reputation < 0) state.Reputation = 0;
            if (state.Reputation > Max) state.Reputation = Max;

            string wasRank = Rank(before);
            string nowRank = Rank(state.Reputation);

            if (wasRank == nowRank) return;

            Notify.Show(state.Reputation > before
                ? $"~g~You're {nowRank} now.~s~ Word gets around."
                : $"~r~You're back to {nowRank}.~s~ People noticed.", true);
        }
    }
}

namespace OnTheBlade.Core
{
    /// <summary>What you charge on a corner.</summary>
    public enum PriceLevel
    {
        Cut = -1,
        Standard = 0,
        Premium = 1
    }

    /// <summary>
    /// Per-corner pricing.
    ///
    /// One number the player sets, and the only lever in the mod that trades
    /// income now against income later. Cutting the price earns less an hour but
    /// builds a client base twice as fast and lets a corner carry more people;
    /// charging premium earns more an hour, keeps the street quiet, and grows
    /// nothing.
    ///
    /// The saturation term is what stops premium being strictly better. Fewer
    /// clients means fewer to go round, so a premium corner punishes crowding —
    /// premium suits one or two people, cut price suits a full corner.
    /// </summary>
    public static class Pricing
    {
        public static PriceLevel LevelOf(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return PriceLevel.Standard;

            int level;
            if (!GameState.Current.ZonePrices.TryGetValue(zoneId, out level))
                return PriceLevel.Standard;

            if (level < (int)PriceLevel.Cut) return PriceLevel.Cut;
            if (level > (int)PriceLevel.Premium) return PriceLevel.Premium;
            return (PriceLevel)level;
        }

        public static void SetLevel(string zoneId, PriceLevel level)
        {
            if (string.IsNullOrEmpty(zoneId)) return;
            GameState.Current.ZonePrices[zoneId] = (int)level;
        }

        public static string Name(PriceLevel level)
        {
            switch (level)
            {
                case PriceLevel.Cut: return "Cut price";
                case PriceLevel.Premium: return "Premium";
                default: return "Going rate";
            }
        }

        public static string Blurb(PriceLevel level)
        {
            var cfg = Config.Current;

            switch (level)
            {
                case PriceLevel.Cut:
                    return $"{(1f - cfg.CutPricePayout) * 100:0}% less an hour, " +
                           $"{(cfg.CutPriceHeat - 1f) * 100:0}% more heat and more trouble — " +
                           $"but regulars build {cfg.CutPriceRegulars:0.0}x as fast and the " +
                           "corner carries a crowd without choking.";

                case PriceLevel.Premium:
                    return $"{(cfg.PremiumPayout - 1f) * 100:0}% more an hour and " +
                           $"{(1f - cfg.PremiumHeat) * 100:0}% less heat, but regulars barely " +
                           "build and two people on the same corner get in each other's way.";

                default:
                    return "What everyone else charges. Nothing pulls either way.";
            }
        }

        public static float Payout(string zoneId)
        {
            var cfg = Config.Current;
            switch (LevelOf(zoneId))
            {
                case PriceLevel.Cut: return cfg.CutPricePayout;
                case PriceLevel.Premium: return cfg.PremiumPayout;
                default: return 1f;
            }
        }

        public static float Heat(string zoneId)
        {
            var cfg = Config.Current;
            switch (LevelOf(zoneId))
            {
                case PriceLevel.Cut: return cfg.CutPriceHeat;
                case PriceLevel.Premium: return cfg.PremiumHeat;
                default: return 1f;
            }
        }

        public static float Regulars(string zoneId)
        {
            var cfg = Config.Current;
            switch (LevelOf(zoneId))
            {
                case PriceLevel.Cut: return cfg.CutPriceRegulars;
                case PriceLevel.Premium: return cfg.PremiumRegulars;
                default: return 1f;
            }
        }

        /// <summary>Multiplier on the crowding falloff, not on the yield.</summary>
        public static float Saturation(string zoneId)
        {
            var cfg = Config.Current;
            switch (LevelOf(zoneId))
            {
                case PriceLevel.Cut: return cfg.CutPriceSaturation;
                case PriceLevel.Premium: return cfg.PremiumSaturation;
                default: return 1f;
            }
        }

        public static float Trouble(string zoneId)
        {
            var cfg = Config.Current;
            switch (LevelOf(zoneId))
            {
                case PriceLevel.Cut: return cfg.CutPriceTrouble;
                case PriceLevel.Premium: return cfg.PremiumTrouble;
                default: return 1f;
            }
        }
    }
}

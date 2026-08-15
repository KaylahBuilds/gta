namespace OnTheBlade.Core
{
    /// <summary>
    /// The small hours.
    ///
    /// Nights already pay 1.6x from 20:00, which made the shift system a two-way
    /// choice: days for followers, nights for money. This is the third setting —
    /// the window nobody sensible is out in. Better money again, and roughly
    /// double the chance of the night going wrong.
    ///
    /// It is not a shift you assign. Anyone working those hours is in it, so it
    /// falls out of the shift you already picked rather than adding another one.
    /// </summary>
    public static class LateWindow
    {
        /// <summary>Whether an hour falls in the window. Handles wrapping past midnight.</summary>
        public static bool Contains(int hour)
        {
            var cfg = Config.Current;
            if (!cfg.LateWindowEnabled) return false;

            int start = cfg.LateWindowStart;
            int end = cfg.LateWindowEnd;

            if (start == end) return false;

            return start < end
                ? hour >= start && hour < end
                : hour >= start || hour < end;   // wraps midnight
        }

        public static float PayoutMultiplier(int hour) =>
            Contains(hour) ? Config.Current.LateWindowPayout : 1f;

        public static float TroubleMultiplier(int hour) =>
            Contains(hour) ? Config.Current.LateWindowTrouble : 1f;

        public static float HeatMultiplier(int hour) =>
            Contains(hour) ? Config.Current.LateWindowHeat : 1f;

        public static string Label()
        {
            var cfg = Config.Current;
            return $"{cfg.LateWindowStart:00}:00–{cfg.LateWindowEnd:00}:00";
        }
    }
}

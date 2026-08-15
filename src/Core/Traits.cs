using System;
using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Stored as an int bitfield on <see cref="WorkerData"/>. A worker carries
    /// zero to two of these.
    /// </summary>
    [Flags]
    public enum WorkerTrait
    {
        None = 0,
        Hustler = 1 << 0,
        CameraReady = 1 << 1,
        Connected = 1 << 2,
        Fragile = 1 << 3,
        Loyal = 1 << 4,
        Magnetic = 1 << 5
    }

    /// <summary>
    /// What makes one worker different from another.
    ///
    /// Without these the roster is a spreadsheet: everyone sits on the same
    /// continuous axes and the only question is magnitude. Traits give each
    /// worker a shape, which is what makes assignment a puzzle — and what makes
    /// poaching interesting, because you are stealing a specific person rather
    /// than a tier number.
    ///
    /// Every trait is a trade. Nothing here is strictly good.
    /// </summary>
    public static class Traits
    {
        public static readonly WorkerTrait[] All =
        {
            WorkerTrait.Hustler, WorkerTrait.CameraReady, WorkerTrait.Connected,
            WorkerTrait.Fragile, WorkerTrait.Loyal, WorkerTrait.Magnetic
        };

        public static string Name(WorkerTrait t)
        {
            switch (t)
            {
                case WorkerTrait.Hustler: return "Hustler";
                case WorkerTrait.CameraReady: return "Camera-ready";
                case WorkerTrait.Connected: return "Connected";
                case WorkerTrait.Fragile: return "Fragile";
                case WorkerTrait.Loyal: return "Loyal";
                case WorkerTrait.Magnetic: return "Magnetic";
                default: return "—";
            }
        }

        public static string Blurb(WorkerTrait t)
        {
            switch (t)
            {
                case WorkerTrait.Hustler: return "Earns more on the street, builds an audience slowly";
                case WorkerTrait.CameraReady: return "Builds an audience fast, earns less on the street";
                case WorkerTrait.Connected: return "Draws far less heat wherever she works";
                case WorkerTrait.Fragile: return "Loses loyalty faster when run into the ground";
                case WorkerTrait.Loyal: return "Will not walk off, and holds loyalty better";
                case WorkerTrait.Magnetic: return "Pulls more clients — and more of the wrong ones";
                default: return string.Empty;
            }
        }

        public static IEnumerable<WorkerTrait> Split(WorkerTrait set)
        {
            return All.Where(t => (set & t) != 0);
        }

        public static string Describe(WorkerTrait set)
        {
            var parts = Split(set).Select(Name).ToList();
            return parts.Count == 0 ? "Nothing special" : string.Join(", ", parts);
        }

        // --- effects ------------------------------------------------------

        public static float StreetMultiplier(WorkerTrait set)
        {
            float m = 1f;
            if ((set & WorkerTrait.Hustler) != 0) m *= 1.25f;
            if ((set & WorkerTrait.CameraReady) != 0) m *= 0.80f;
            if ((set & WorkerTrait.Magnetic) != 0) m *= 1.15f;
            return m;
        }

        /// <summary>
        /// What she is worth on camera. Deliberately shallow — Camera-ready is
        /// already the strongest single multiplier in the mod (x1.50 on follower
        /// gain, doubled again by the ring light), and she reaches the content
        /// payout through her larger audience anyway. This is a nod, not a
        /// second helping, and it is bounded because the audience factor is.
        ///
        /// Hustler is the mirror of her street bonus: she is good at working a
        /// kerb and that is not this. It gives two more traits a content
        /// identity they have never had.
        /// </summary>
        public static float ContentMultiplier(WorkerTrait set)
        {
            float m = 1f;
            if ((set & WorkerTrait.CameraReady) != 0) m *= 1.15f;
            if ((set & WorkerTrait.Hustler) != 0) m *= 0.90f;
            if ((set & WorkerTrait.Magnetic) != 0) m *= 1.10f;
            return m;
        }

        public static float FollowerMultiplier(WorkerTrait set)
        {
            float m = 1f;
            if ((set & WorkerTrait.CameraReady) != 0) m *= 1.50f;
            if ((set & WorkerTrait.Hustler) != 0) m *= 0.60f;
            return m;
        }

        public static float HeatMultiplier(WorkerTrait set)
        {
            return (set & WorkerTrait.Connected) != 0 ? 0.60f : 1f;
        }

        public static float LoyaltyDrainMultiplier(WorkerTrait set)
        {
            float m = 1f;
            if ((set & WorkerTrait.Fragile) != 0) m *= 1.75f;
            if ((set & WorkerTrait.Loyal) != 0) m *= 0.50f;
            return m;
        }

        /// <summary>Scales bad-client odds.</summary>
        public static float TroubleMultiplier(WorkerTrait set)
        {
            return (set & WorkerTrait.Magnetic) != 0 ? 1.40f : 1f;
        }

        public static bool ImmuneToWalkOff(WorkerTrait set)
        {
            return (set & WorkerTrait.Loyal) != 0;
        }

        // --- rolling ------------------------------------------------------

        /// <summary>
        /// 35% nothing, 45% one trait, 20% two. Conflicting pairs are allowed —
        /// a Hustler who is also Camera-ready is simply mediocre at both, which
        /// is a fine thing to occasionally draw.
        /// </summary>
        public static WorkerTrait Roll(Random rng)
        {
            double roll = rng.NextDouble();
            int count = roll < 0.35 ? 0 : roll < 0.80 ? 1 : 2;

            WorkerTrait set = WorkerTrait.None;
            for (int i = 0; i < count; i++)
                set |= All[rng.Next(All.Length)];

            return set;
        }
    }
}

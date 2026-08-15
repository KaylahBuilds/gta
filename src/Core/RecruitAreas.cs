using System;
using System.Collections.Generic;
using GTA.Math;
using GTA.Native;

namespace OnTheBlade.Core
{
    public enum RecruitArea
    {
        Ordinary = 0,
        Gang = 1,
        Vinewood = 2
    }

    /// <summary>
    /// Where recruiting is easy, and what kind of prospect turns up there.
    ///
    /// Classified by the game's own zone codes rather than coordinates, so the
    /// boundaries follow the map instead of a guessed radius. The trade-off is
    /// that the codes below are from memory and need checking in-game — the
    /// recruit menu prints the code you are currently standing in for exactly
    /// that reason. An unrecognised code simply falls through to
    /// <see cref="RecruitArea.Ordinary"/>, so a wrong entry costs a hotspot, not
    /// a crash.
    /// </summary>
    public static class RecruitAreas
    {
        /// <summary>
        /// South Los Santos and the industrial east — gang turf.
        /// STRAW confirmed in-game (Strawberry reports "Gang turf [STRAW]").
        /// </summary>
        private static readonly HashSet<string> GangCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DAVIS", "STRAW", "CHAMH", "RANCHO", "LMESA", "CYPRE",
            "BANNING", "TEXTI", "SKID", "ELBURRO", "ELYSIAN", "SLAB", "PBOX"
        };

        /// <summary>Vinewood and the money around it.</summary>
        private static readonly HashSet<string> VinewoodCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "VINE", "WVINE", "EAST_V", "ALTA", "HAWICK", "DOWNT",
            "ROCKF", "RICHM", "MORN", "BURTON", "MOVIE", "LEGSQU"
        };

        /// <summary>Raw zone code at a position, e.g. "WVINE". Empty if unavailable.</summary>
        public static string ZoneCode(Vector3 position)
        {
            string code = Function.Call<string>(
                Hash.GET_NAME_OF_ZONE, position.X, position.Y, position.Z);
            return code ?? string.Empty;
        }

        public static RecruitArea At(Vector3 position)
        {
            string code = ZoneCode(position);
            if (GangCodes.Contains(code)) return RecruitArea.Gang;
            if (VinewoodCodes.Contains(code)) return RecruitArea.Vinewood;
            return RecruitArea.Ordinary;
        }

        public static bool IsHotspot(RecruitArea area) => area != RecruitArea.Ordinary;

        public static float SearchRadius(RecruitArea area)
        {
            var cfg = Config.Current;
            float radius = IsHotspot(area) ? cfg.RecruitRadiusHotspot : cfg.RecruitRadius;

            // Somebody from round here knows who is worth talking to. Uses the
            // nearest corner's region rather than the player's exact position,
            // because muscle covers ground and recruiting happens anywhere.
            var near = Zones.NearestTo(GTA.Game.Player.Character.Position, 600f);
            if (near != null)
            {
                var local = GameState.Current.EnforcerInRegion(Regions.ForZone(near.Id)?.Id);
                if (local != null && !local.IsInjured())
                    radius *= local.Profile.RecruitRadiusMultiplier;
            }

            return radius;
        }

        public static string Describe(RecruitArea area)
        {
            switch (area)
            {
                case RecruitArea.Gang: return "Gang turf";
                case RecruitArea.Vinewood: return "Vinewood";
                default: return "Nowhere in particular";
            }
        }

        /// <summary>
        /// Tier weighting by area. Gang turf turns up volume at the lower tiers;
        /// Vinewood turns up fewer people but a better class of them.
        /// </summary>
        public static int RollTier(RecruitArea area, Random rng)
        {
            int tier = RollRawTier(area, rng);

            // A name people have heard of does not turn up nobodies. This is what
            // the franchise is actually for, and what its loyalty penalty pays for.
            if (GameState.Current.HasUpgrade(UpgradeCatalog.Franchise) &&
                tier < Config.Current.FranchiseMinTier)
                tier = Config.Current.FranchiseMinTier;

            // The best prospects will not sign for a nobody. Without this,
            // reputation is a number that never touches anything the player does.
            if (tier >= 3 && Config.Current.ReputationEnabled &&
                GameState.Current.Reputation < Reputation.TierThreeThreshold)
                tier = 2;

            return tier;
        }

        /// <summary>
        /// Skewed well down from the original weighting.
        ///
        /// Vinewood used to hand out tier 3 at 30% and tier 2 at 45%. That was
        /// reasonable when tier was decided once and never moved — it was the only
        /// way to ever have a good worker. Now that tier can be worked for and
        /// bought, starting most of a roster at or near the ceiling left the whole
        /// progression system with nothing to do.
        ///
        /// The weights are config so this can be put back without a rebuild.
        /// </summary>
        private static int RollRawTier(RecruitArea area, Random rng)
        {
            var cfg = Config.Current;
            double roll = rng.NextDouble();

            float three, two;

            switch (area)
            {
                case RecruitArea.Vinewood:
                    three = cfg.TierChanceVinewood3; two = cfg.TierChanceVinewood2;
                    break;

                case RecruitArea.Gang:
                    three = cfg.TierChanceGang3; two = cfg.TierChanceGang2;
                    break;

                default:
                    three = cfg.TierChanceOrdinary3; two = cfg.TierChanceOrdinary2;
                    break;
            }

            if (roll < three) return 3;
            if (roll < three + two) return 2;
            return 1;
        }
    }
}

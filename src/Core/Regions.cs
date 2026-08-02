using System.Collections.Generic;
using System.Linq;
using GTA.Math;

namespace OnTheBlade.Core
{
    /// <summary>
    /// A group of zones with one stash house covering them. Regions exist so that
    /// property and muscle are bought per-area rather than per-corner — buying
    /// six of everything is bookkeeping, not a decision.
    /// </summary>
    public class RegionDef
    {
        public string Id;
        public string Display;

        public string StashName;
        public Vector3 StashPosition;
        public float StashHeading;
        public int StashCost;

        public string[] ZoneIds;
    }

    public static class Regions
    {
        /// <summary>
        /// Stash positions are approximate — verify in-game before release, same
        /// caveat as the zone anchors.
        /// </summary>
        public static readonly List<RegionDef> All = new List<RegionDef>
        {
            new RegionDef
            {
                Id = "west", Display = "West Los Santos",
                StashName = "Vespucci walk-up", StashCost = 45000,
                StashPosition = new Vector3(-1150f, -1520f, 10.6f), StashHeading = 300f,
                ZoneIds = new[] { "vespucci", "delperro" }
            },
            new RegionDef
            {
                Id = "south", Display = "South Los Santos",
                StashName = "Strawberry duplex", StashCost = 35000,
                StashPosition = new Vector3(126f, -1298f, 29.2f), StashHeading = 90f,
                ZoneIds = new[] { "strawberry" }
            },
            new RegionDef
            {
                Id = "east", Display = "East Los Santos",
                StashName = "Mirror Park bungalow", StashCost = 75000,
                StashPosition = new Vector3(1300f, -530f, 70f), StashHeading = 210f,
                ZoneIds = new[] { "mirrorpark", "vinewood" }
            },
            new RegionDef
            {
                Id = "blaine", Display = "Blaine County",
                StashName = "Sandy Shores trailer", StashCost = 25000,
                StashPosition = new Vector3(1961f, 3815f, 32.3f), StashHeading = 30f,
                ZoneIds = new[] { "sandy" }
            }
        };

        public static RegionDef Get(string id) =>
            string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(r => r.Id == id);

        public static RegionDef ForZone(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;
            return All.FirstOrDefault(r => r.ZoneIds.Contains(zoneId));
        }
    }
}

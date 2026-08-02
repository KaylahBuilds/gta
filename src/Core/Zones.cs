using System.Collections.Generic;
using System.Linq;
using GTA.Math;

namespace OnTheBlade.Core
{
    public class ZoneDef
    {
        public string Id;
        public string Display;

        /// <summary>Centre of the working area. Workers post up in a ring around this.</summary>
        public Vector3 Anchor;

        /// <summary>Facing direction for spawned peds, degrees.</summary>
        public float Heading;

        /// <summary>Minimum worker tier allowed here.</summary>
        public int MinTier = 1;

        /// <summary>Payout multiplier.</summary>
        public float Demand = 1f;

        /// <summary>Heat added per worker per in-game hour.</summary>
        public float HeatGain = 0.02f;

        /// <summary>How many workers can be posted here at once.</summary>
        public int Slots = 3;
    }

    /// <summary>
    /// Static zone table. Anchors are approximate — verify each in-game and nudge
    /// them onto a sensible pavement position before release.
    /// </summary>
    public static class Zones
    {
        public static readonly List<ZoneDef> All = new List<ZoneDef>
        {
            new ZoneDef
            {
                Id = "vespucci", Display = "Vespucci Beach",
                Anchor = new Vector3(-1194f, -1394f, 4.6f), Heading = 125f,
                MinTier = 1, Demand = 1.0f, HeatGain = 0.015f, Slots = 4
            },
            new ZoneDef
            {
                Id = "strawberry", Display = "Strawberry",
                Anchor = new Vector3(87f, -1959f, 21.1f), Heading = 320f,
                MinTier = 1, Demand = 1.1f, HeatGain = 0.030f, Slots = 3
            },
            new ZoneDef
            {
                Id = "sandy", Display = "Sandy Shores",
                Anchor = new Vector3(1961f, 3740f, 32.3f), Heading = 210f,
                MinTier = 1, Demand = 0.85f, HeatGain = 0.008f, Slots = 3
            },
            new ZoneDef
            {
                Id = "mirrorpark", Display = "Mirror Park",
                Anchor = new Vector3(1055f, -389f, 68f), Heading = 75f,
                MinTier = 2, Demand = 1.45f, HeatGain = 0.035f, Slots = 3
            },
            new ZoneDef
            {
                Id = "delperro", Display = "Del Perro",
                Anchor = new Vector3(-1367f, -1109f, 5f), Heading = 20f,
                MinTier = 2, Demand = 1.6f, HeatGain = 0.045f, Slots = 3
            },
            new ZoneDef
            {
                Id = "vinewood", Display = "Vinewood Boulevard",
                Anchor = new Vector3(303f, 200f, 104f), Heading = 160f,
                MinTier = 3, Demand = 2.2f, HeatGain = 0.060f, Slots = 2
            }
        };

        public static ZoneDef Get(string id)
        {
            return string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(z => z.Id == id);
        }

        public static IEnumerable<ZoneDef> OpenTo(int tier)
        {
            return All.Where(z => z.MinTier <= tier);
        }

        /// <summary>
        /// Deterministic post position so a worker returns to the same spot every
        /// time they stream back in.
        /// </summary>
        public static Vector3 PostPosition(ZoneDef zone, int slotIndex)
        {
            return PostPosition(zone.Anchor, zone.Heading, zone.Slots, slotIndex);
        }

        /// <summary>
        /// Spread around an explicit anchor.
        ///
        /// The overload exists because the anchor must be snapped to a pavement
        /// <em>once, for the zone</em> — not per worker. Snapping each post
        /// individually collapses every one of them onto the same sidewalk node
        /// and the whole crew stands inside each other.
        /// </summary>
        public static Vector3 PostPosition(Vector3 anchor, float heading, int slots, int slotIndex)
        {
            if (slots <= 1) return anchor;

            const float spacing = 2.6f;
            float offset = (slotIndex - (slots - 1) / 2f) * spacing;

            // Offset along the zone's right-hand vector so workers line the kerb
            // instead of clustering on one point.
            double rad = heading * System.Math.PI / 180.0;
            var right = new Vector3((float)System.Math.Cos(rad), (float)System.Math.Sin(rad), 0f);

            return anchor + right * offset;
        }
    }
}

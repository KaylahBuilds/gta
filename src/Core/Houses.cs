using System.Collections.Generic;
using System.Linq;
using GTA.Math;

namespace OnTheBlade.Core
{
    public class HouseDef
    {
        public string Id;
        public string Display;

        /// <summary>What it is, in the fiction. Shown when deciding whether to buy.</summary>
        public string Blurb;

        public int Cost;

        /// <summary>Hard ceiling on how many people can work here at once.</summary>
        public int Rooms;

        /// <summary>Charged at midnight whether anyone worked or not.</summary>
        public int DailyRent;

        /// <summary>Payout multiplier against the street base rate.</summary>
        public float RateMultiplier;

        /// <summary>
        /// Heat added per worker per hour.
        ///
        /// Tuned against <c>HouseHeatDecayPerHour</c> rather than against a corner,
        /// because those two numbers decide whether the raid exists at all. The
        /// first pass had gains so far under decay that house heat could never
        /// rise and the raid was unreachable — a house was pure profit with no
        /// risk attached. These sit so that a house with every room let goes over
        /// in roughly three and a half to four game days, and each empty room buys
        /// a lot of quiet:
        ///
        ///   walk-up   1 room safe indefinitely, 2 rooms 4.2 days
        ///   parlour   1 safe, 2 rooms 18.7 days, 3 rooms 3.6 days
        ///   house     1-2 safe, 3 rooms 8.3 days, 4 rooms 3.4 days
        ///
        /// That is the decision the house adds: fill it and earn, or hold a room
        /// back and stay off the radar.
        /// </summary>
        public float HeatGain;

        /// <summary>Reputation needed before anyone will rent you the place.</summary>
        public int MinReputation;

        /// <summary>Front door. Used for the map blip and fast travel — nobody is spawned here.</summary>
        public Vector3 Door;
        public float DoorHeading;
    }

    /// <summary>
    /// Indoor operations.
    ///
    /// The mod is named for the street and every system assumed a kerb, which left
    /// it without a second act — late game was simply more corners. A house is the
    /// thing the street pays for: it earns far better, draws almost no heat, and
    /// cannot be scaled past its rooms.
    ///
    /// It is also a much bigger target. A corner that gets turned over costs you a
    /// few days and a fine; a house that gets raided empties in one night.
    ///
    /// Workers posted indoors are deliberately never spawned as peds. They are
    /// inside — there is nothing to see from the street — which sidesteps the
    /// anchor-accuracy problem that every zone has, and means a house needs only a
    /// door position rather than a surveyed pavement.
    /// </summary>
    public static class Houses
    {
        /// <summary>
        /// Door positions are approximate and have not been walked in-game — the
        /// same caveat the zone anchors carry. Nothing spawns at them, so a wrong
        /// coordinate misplaces a blip rather than breaking the house.
        /// </summary>
        public static readonly List<HouseDef> All = new List<HouseDef>
        {
            new HouseDef
            {
                Id = "walkup", Display = "The Strawberry walk-up",
                Blurb = "Two rooms over a shop. Nobody looks twice, and nobody has to " +
                        "stand outside in the rain.",
                Cost = 120000, Rooms = 2, DailyRent = 2000,
                RateMultiplier = 2.0f, HeatGain = 0.0120f, MinReputation = 0,
                Door = new Vector3(78.0f, -1948.0f, 21.1f), DoorHeading = 320f
            },
            new HouseDef
            {
                Id = "parlour", Display = "The Del Perro parlour",
                Blurb = "A massage front with three rooms behind it. The sign out " +
                        "front is doing most of the work.",
                Cost = 260000, Rooms = 3, DailyRent = 3800,
                RateMultiplier = 2.4f, HeatGain = 0.0085f, MinReputation = 250,
                Door = new Vector3(-1290.0f, -1116.0f, 6.9f), DoorHeading = 30f
            },
            new HouseDef
            {
                // Not "vinewood" — that is a zone id, and the two share blip and
                // signature dictionaries keyed by id.
                Id = "vinewood_house", Display = "The Vinewood house",
                Blurb = "Four rooms, a gate and a view. People come to you, and the " +
                        "people who come can afford to.",
                Cost = 520000, Rooms = 4, DailyRent = 6500,
                RateMultiplier = 3.0f, HeatGain = 0.0065f, MinReputation = 500,
                Door = new Vector3(297.0f, 181.0f, 104.5f), DoorHeading = 160f
            }
        };

        public static HouseDef Get(string id)
        {
            return string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(h => h.Id == id);
        }
    }
}

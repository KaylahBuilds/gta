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
        /// risk attached.
        ///
        /// The second pass fixed that arithmetic but did it against 24-hour
        /// occupancy, which nobody actually runs. Decay is a flat 0.015/hr
        /// whatever the house is doing, so a 14-hour day shift cut the gain by
        /// 42% while decay stayed put — and every house at full capacity came
        /// out net negative, i.e. permanently heat-immune. Simulated over 28
        /// game days a full Vinewood house on Days earned $1.13m and was never
        /// once raided, so "hold a room back" was a decision with no upside and
        /// the raid may as well not have existed.
        ///
        /// These are tuned against a 14-hour shift, which is what the numbers
        /// have to survive:
        ///
        ///   walk-up   1 room safe indefinitely, 2 rooms  9.9 days
        ///   parlour   1-2 safe,                 3 rooms 10.1 days
        ///   house     1-3 safe,                 4 rooms 10.2 days
        ///
        /// Ten days is deliberate. It is slow enough that the heat bar is a
        /// warning rather than an ambush, and fast enough that a full house is a
        /// position you manage — pull somebody for a day, buy the laundromat —
        /// rather than one you set and forget. A full house on Nights (10 hours)
        /// still sits under decay, so the graveyard shift remains a real
        /// strategy: about 82% of the income for none of the risk.
        ///
        /// That is the decision the house adds: fill it and manage it, hold a
        /// room back and forget it, or run nights and take less.
        /// </summary>
        public float HeatGain;

        /// <summary>Reputation needed before anyone will rent you the place.</summary>
        public int MinReputation;

        /// <summary>
        /// A content house. Residents produce for the camera and see no clients
        /// at all: it flips ResolveIndoor to ResolveContent and changes how heat
        /// accrues. Nothing else in the indoor stack knows the difference, which
        /// is the whole reason this is a bool on an existing type rather than a
        /// fourth kind of posting.
        ///
        /// Note that <see cref="HeatGain"/> changes units when this is set: per
        /// RESIDENT per hour, not per working worker per hour. A content house
        /// heats because people live in it, so the shift cannot be used as a
        /// free safety toggle — which is exactly the fault the brothel ladder
        /// shipped with.
        /// </summary>
        public bool IsContentHouse;

        /// <summary>Extra roster slots for people who live where they work. 0 for a brothel.</summary>
        public int RosterSlots;

        /// <summary>Id of the rung below. Null means no prerequisite.</summary>
        public string RequiresId;

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
                RateMultiplier = 2.0f, HeatGain = 0.0161f, MinReputation = 0,
                Door = new Vector3(78.0f, -1948.0f, 21.1f), DoorHeading = 320f
            },
            new HouseDef
            {
                Id = "parlour", Display = "The Del Perro parlour",
                Blurb = "A massage front with three rooms behind it. The sign out " +
                        "front is doing most of the work.",
                Cost = 260000, Rooms = 3, DailyRent = 3800,
                RateMultiplier = 2.4f, HeatGain = 0.0107f, MinReputation = 250,
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
                RateMultiplier = 3.0f, HeatGain = 0.0080f, MinReputation = 500,
                Door = new Vector3(297.0f, 181.0f, 104.5f), DoorHeading = 160f
            },

            // --- content houses ---------------------------------------------
            //
            // A different business in the same building type. Nobody here sees a
            // client: they are paid a flat hourly rate multiplied by the audience
            // they walked in with, so the ladder sorts the roster rather than
            // scaling it. A tier-1 is worth 0.60 here against 0.22 of a tier-3 on
            // a corner, and a tier-3 gives up her regulars to stand in one — the
            // content house is uniformly the worse business, and that is what
            // stops anybody moving a good earner indoors while her corner is cold.
            //
            // HeatGain is per RESIDENT per hour here, not per working worker per
            // hour, and every value is solved from one rule: decay exactly cancels
            // seventy per cent of capacity.
            //
            //   HeatGain = ContentHeatDecayPerHour / (0.70 x Rooms)
            //
            // So the quiet line is 70% of the rooms at every rung, the decision is
            // always "is this person worth what she costs to keep quiet", and the
            // answer is a flat figure per resident per day that does not care how
            // big the house is. Because it is per resident rather than per
            // producing hour, the shift selector cannot be used to duck heat —
            // which is precisely how the brothel ladder above got away with being
            // heat-immune at full capacity for two versions.
            //
            //   Chamberlain  4 rooms, quiet up to 2.8, full drift +0.00086/hr
            //   Mirror Park  7 rooms, quiet up to 4.9, full drift +0.00086/hr
            //   Rockford    11 rooms, quiet up to 7.7, full drift +0.00086/hr
            //   Vinewood    15 rooms, quiet up to 10.5, full drift +0.00086/hr
            //
            // Identical at every rung by construction: about 15 game days from
            // cold to a raid if you fill one and never touch it, 44 with
            // discretion bought, 88 with a houseful of Connected residents on
            // top of that — the floor in ContentHeatReductionFloor is what stops
            // that last case reaching never.
            //
            // Prices and rents were solved together with ContentBaseRatePerHour
            // against the marginal cost of each rung, charging the cumulative
            // rent the sequential prerequisite actually forces rather than each
            // rung's own. Run full with the heat bill paid, the ladder repays in
            // about 5 / 17 / 33 / 53 game days — escalating, so every rung is a
            // bigger commitment than the last. Total rent of $16,600 a day also
            // has to stay above the roughly $10,600 a day that seven extra
            // benched workers would produce in follower deposits, or buying the
            // ladder empty purely for the roster slots would pay for itself.

            new HouseDef
            {
                Id = "content_chamberlain", Display = "The Chamberlain place",
                Blurb = "A shell on a corner lot with the windows boarded from the " +
                        "inside. Four rooms, a decent router and nobody asking.",
                Cost = 60000, Rooms = 4, DailyRent = 700,
                RateMultiplier = 0.90f, HeatGain = 0.002143f, MinReputation = 0,
                IsContentHouse = true, RosterSlots = 1, RequiresId = null,
                Door = new Vector3(-14.0f, -1440.0f, 30.6f), DoorHeading = 90f
            },
            new HouseDef
            {
                Id = "content_flat", Display = "The Mirror Park flat",
                Blurb = "Seven rooms over two floors, and light that does not need " +
                        "apologising for. The first address you would give out.",
                Cost = 210000, Rooms = 7, DailyRent = 1900,
                RateMultiplier = 1.05f, HeatGain = 0.001224f, MinReputation = 200,
                IsContentHouse = true, RosterSlots = 1, RequiresId = "content_chamberlain",
                Door = new Vector3(1236.0f, -415.0f, 68.2f), DoorHeading = 250f
            },
            new HouseDef
            {
                Id = "content_rockford", Display = "The Rockford Hills place",
                Blurb = "Eleven rooms behind a gate, in the postcode people put in " +
                        "the caption. This is where it stops looking like a hustle.",
                Cost = 560000, Rooms = 11, DailyRent = 5200,
                RateMultiplier = 1.20f, HeatGain = 0.000779f, MinReputation = 450,
                IsContentHouse = true, RosterSlots = 2, RequiresId = "content_flat",
                Door = new Vector3(-1120.0f, 380.0f, 78.0f), DoorHeading = 200f
            },
            new HouseDef
            {
                Id = "content_vinewood", Display = "The Vinewood Hills house",
                Blurb = "Fifteen rooms, a pool nobody swims in and a view that does " +
                        "half the work. Everyone you have can live here.",
                Cost = 950000, Rooms = 15, DailyRent = 8800,
                RateMultiplier = 1.35f, HeatGain = 0.000571f, MinReputation = 700,
                IsContentHouse = true, RosterSlots = 3, RequiresId = "content_rockford",
                Door = new Vector3(-174.0f, 502.0f, 137.4f), DoorHeading = 200f
            }
        };

        /// <summary>True if this id names a content house. Safe on null.</summary>
        public static bool IsContentHouseId(string id)
        {
            var house = Get(id);
            return house != null && house.IsContentHouse;
        }

        /// <summary>Every content house, in ladder order.</summary>
        public static System.Collections.Generic.IEnumerable<HouseDef> Content
        {
            get { return All.Where(h => h.IsContentHouse); }
        }

        /// <summary>Every brothel, in ladder order.</summary>
        public static System.Collections.Generic.IEnumerable<HouseDef> Brothels
        {
            get { return All.Where(h => !h.IsContentHouse); }
        }

        public static HouseDef Get(string id)
        {
            return string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(h => h.Id == id);
        }
    }
}

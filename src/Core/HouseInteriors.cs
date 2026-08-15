using System.Collections.Generic;
using GTA.Math;
using GTA.Native;

namespace OnTheBlade.Core
{
    /// <summary>
    /// The inside of every house. A walk-up you cannot walk up into is a
    /// menu entry with a price on it — these make each one a PLACE, with the
    /// girls who work it standing in the room.
    ///
    /// Every coordinate is validated against the engine before anyone is
    /// teleported (GET_INTERIOR_AT_COORDS returns 0 where no interior
    /// exists), and anything that fails degrades to the Strawberry house,
    /// which is vanilla, always streamed, and proven in the sister mod. A
    /// wrong coordinate costs a different room, never a fall through the map.
    /// </summary>
    public static class HouseInteriors
    {
        public class Room
        {
            public Vector3 At;
            public float Heading;
            public string Flavour;
        }

        /// <summary>Vanilla, always present, and the fallback for everything.</summary>
        public static readonly Room Fallback = new Room
        {
            At = new Vector3(-9.96f, -1438.54f, 31.10f), Heading = 130f,
            Flavour = "two rooms over a shop, curtains shut",
        };

        private static readonly Dictionary<string, Room> ByHouse =
            new Dictionary<string, Room>
            {
                // The Strawberry walk-up — the house this interior IS.
                { "walkup", new Room {
                    At = new Vector3(-9.96f, -1438.54f, 31.10f), Heading = 130f,
                    Flavour = "two rooms over a shop, curtains shut" } },

                // A low apartment behind a front — the parlour's back rooms.
                { "parlour", new Room {
                    At = new Vector3(-1150.6f, -1520.5f, 10.63f), Heading = 100f,
                    Flavour = "a back room behind the massage sign" } },

                // Money. A house people are driven to.
                { "vinewood_house", new Room {
                    At = new Vector3(-802.3f, 175.0f, 72.84f), Heading = 110f,
                    Flavour = "a house with a view and a gate on the drive" } },

                { "content_chamberlain", new Room {
                    At = new Vector3(-1150.6f, -1520.5f, 10.63f), Heading = 100f,
                    Flavour = "a flat full of ring lights and tripods" } },

                { "content_rockford", new Room {
                    At = new Vector3(-802.3f, 175.0f, 72.84f), Heading = 110f,
                    Flavour = "a place with better light than most studios" } },
            };

        /// <summary>The room for a house, already validated. Never null.</summary>
        public static Room For(HouseDef house)
        {
            if (house == null) return Fallback;

            Room room;
            if (!ByHouse.TryGetValue(house.Id, out room)) room = Fallback;

            return Exists(room.At) ? room : Fallback;
        }

        /// <summary>Whether the engine actually has an interior at a point.</summary>
        public static bool Exists(Vector3 at)
        {
            int handle = Function.Call<int>(
                Hash.GET_INTERIOR_AT_COORDS, at.X, at.Y, at.Z);

            return handle != 0
                   && Function.Call<bool>(Hash.IS_VALID_INTERIOR, handle);
        }

        /// <summary>Where the girls stand inside, off the room's anchor.
        /// Hand-placed and trusted: SafeGround rejects interiors by design.</summary>
        public static Vector3[] Stands(Vector3 anchor, float heading)
        {
            double rad = heading * System.Math.PI / 180.0;
            var forward = new Vector3(
                (float)-System.Math.Sin(rad), (float)System.Math.Cos(rad), 0f);
            var right = new Vector3(forward.Y, -forward.X, 0f);

            return new[]
            {
                anchor + forward * 2.0f + right * 1.2f,
                anchor + forward * 2.4f - right * 1.2f,
                anchor - right * 2.1f + forward * 0.5f,
                anchor + right * 2.4f + forward * 2.8f,
            };
        }
    }
}

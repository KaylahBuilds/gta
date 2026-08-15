using System.Collections.Generic;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// Draws the territory board on the map.
    ///
    /// Until this existed the mod had blips for workers, prospects and incidents
    /// and nothing at all for the thing it is actually about — you could own a
    /// corner, lose it, and fight over it without the map ever showing where it
    /// was.
    ///
    /// Two blips per zone: a shaded BLOCK over the ground somebody holds, and a
    /// small pin carrying the name and state. Ground nobody holds gets only the
    /// pin — shading unclaimed turf is noise, and in a heavily modded install
    /// the map is already crowded.
    /// </summary>
    public class ZoneBlips
    {
        /// <summary>
        /// The blocking, matched to the product side deliberately: both
        /// businesses paint the same city on the same pause map, and turf that
        /// changed shape or shade between them would read as two games.
        /// Same square, same pale alpha, same grid alignment as
        /// TheTrapStar's CornerBlips.
        /// </summary>
        private const float TerritorySide = 170f;
        private const int TerritoryAlpha = 55;

        private readonly Dictionary<string, Blip> _area = new Dictionary<string, Blip>();
        private readonly Dictionary<string, Blip> _pin = new Dictionary<string, Blip>();

        /// <summary>Last drawn state per zone, so blips are only rebuilt when something changed.</summary>
        private readonly Dictionary<string, string> _drawn = new Dictionary<string, string>();

        private int _nextScan;

        public void Update()
        {
            if (!Config.Current.ShowZoneBlips)
            {
                if (_pin.Count > 0 || _area.Count > 0) Clear();
                return;
            }

            if (Game.GameTime < _nextScan) return;
            _nextScan = Game.GameTime + Config.Current.ZoneBlipRefreshMs;

            foreach (var zone in Zones.All)
            {
                string signature = Signature(zone);

                string previous;
                if (_drawn.TryGetValue(zone.Id, out previous) && previous == signature) continue;

                _drawn[zone.Id] = signature;
                Redraw(zone);
            }

            DrawHouses();
        }

        /// <summary>
        /// Houses you own, pinned at their door.
        ///
        /// Keys are prefixed because a house id and a zone id share these
        /// dictionaries and are not guaranteed to be distinct.
        /// </summary>
        private void DrawHouses()
        {
            var state = GameState.Current;

            foreach (var house in Houses.All)
            {
                string key = "house:" + house.Id;

                if (!state.OwnsHouse(house.Id))
                {
                    if (_pin.ContainsKey(key)) { Remove(key); _drawn.Remove(key); }
                    continue;
                }

                if (!Config.Current.ShowHouseBlips)
                {
                    if (_pin.ContainsKey(key)) { Remove(key); _drawn.Remove(key); }
                    continue;
                }

                // A content house shuts for condition rather than for a raid,
                // and condition is not in the lock state — without both here the
                // blip renders a shut house as open and never refreshes when the
                // place wears down.
                bool raided = state.IsHouseLocked(house.Id);
                bool derelict = house.IsContentHouse && state.IsContentShut(house.Id);
                bool shut = raided || derelict;

                int used = state.WorkersInHouse(house.Id).Count();

                string signature = string.Join("|",
                    raided ? "raid" + state.HouseLockDaysLeft(house.Id)
                           : derelict ? "derelict" : "open",
                    used.ToString(),
                    house.IsContentHouse
                        ? ((int)(state.ContentCondition(house.Id) * 10f)).ToString()
                        : "-");

                string previous;
                if (_drawn.TryGetValue(key, out previous) && previous == signature) continue;
                _drawn[key] = signature;

                Remove(key);

                Blip pin = World.CreateBlip(house.Door);
                if (pin == null || !pin.Exists()) continue;

                pin.Sprite = BlipSprite.Health;
                pin.Color = shut
                    ? Config.Current.ZoneRaided
                    : house.IsContentHouse
                        ? Config.Current.ContentBlipColour
                        : Config.Current.HouseBlipColour;
                pin.Scale = Config.Current.ZoneBlipScale;
                pin.IsShortRange = false;
                pin.Name = raided
                    ? $"{house.Display} — shut, {state.HouseLockDaysLeft(house.Id)}d"
                    : derelict
                        ? $"{house.Display} — not fit to work in"
                        : house.IsContentHouse
                            ? $"{house.Display} — {used}/{house.Rooms} living there, " +
                              $"{state.ContentCondition(house.Id) * 100:0}%"
                            : $"{house.Display} — {used}/{house.Rooms} working";

                _pin[key] = pin;
            }
        }

        /// <summary>Everything the blip's appearance depends on, as one comparable string.</summary>
        private static string Signature(ZoneDef zone)
        {
            var state = GameState.Current;

            // The set's flag is part of the signature: naming or repainting it
            // on the product side must redraw this mod's board too, and the
            // world-file read behind these is cached, not a file hit per zone.
            return string.Join("|",
                state.OwnerOf(zone.Id) ?? "neutral",
                state.IsZoneLocked(zone.Id) ? "raid" + state.LockoutDaysLeft(zone.Id) : "open",
                state.WorkersIn(zone.Id).Count().ToString(),
                BladeWorld.WorldLink.SetName() ?? "-",
                (BladeWorld.WorldLink.SetColour() ?? 0).ToString());
        }

        private void Redraw(ZoneDef zone)
        {
            Remove(zone.Id);

            var state = GameState.Current;
            bool mine = state.PlayerOwns(zone.Id);
            bool contested = state.IsContested(zone.Id);
            bool raided = state.IsZoneLocked(zone.Id);

            BlipColor colour;
            BlipSprite sprite;
            string status;

            var cfg = Config.Current;

            if (raided)
            {
                colour = cfg.ZoneRaided;
                sprite = BlipSprite.PoliceArea;
                status = $"raided, {state.LockoutDaysLeft(zone.Id)}d";
            }
            else if (mine)
            {
                // Your ground flies the set's flag once the product side has
                // named one — same colour, same name, both businesses.
                colour = BladeWorld.WorldLink.SetColour() ?? cfg.ZoneMine;
                string set = BladeWorld.WorldLink.SetName();

                sprite = BlipSprite.DollarSignCircled;
                status = set != null
                    ? $"{set} turf, {state.WorkersIn(zone.Id).Count()}/{zone.Slots} posted"
                    : $"yours, {state.WorkersIn(zone.Id).Count()}/{zone.Slots} posted";
            }
            else if (contested)
            {
                // The girl, in pink. This is the escort side of the city, so
                // its ground is marked with a woman and not with a pill — the
                // product side owns that icon and the two boards are read at
                // the same time on the same map.
                colour = cfg.ZoneRival;
                sprite = BlipSprite.Hooker;
                status = state.OwnerName(zone.Id);
            }
            else
            {
                colour = cfg.ZoneNeutral;
                sprite = BlipSprite.Hooker;
                status = "neutral";
            }

            // THE BLOCKING — exactly what the product side does for a corner
            // somebody holds: a SQUARE of pale colour over the ground itself
            // (ADD_BLIP_FOR_AREA), axis-aligned to the map grid, so held turf
            // reads as blocks rather than as rings drawn over the streets.
            //
            // This replaces the BigCircleOutline pin that used to mark rival
            // ground: a map-scale circle swallowed several blocks and
            // everything inside them, and said less than the shading does.
            //
            // Shaded whenever ANYBODY holds it — yours, theirs, or raided.
            // Only genuinely unclaimed ground stays bare.
            if (mine || raided || contested)
            {
                int handle = GTA.Native.Function.Call<int>(
                    GTA.Native.Hash.ADD_BLIP_FOR_AREA,
                    zone.Anchor.X, zone.Anchor.Y, zone.Anchor.Z,
                    TerritorySide, TerritorySide);

                if (handle != 0)
                {
                    Blip area = new Blip(handle);
                    if (area.Exists())
                    {
                        area.Color = colour;
                        area.Alpha = TerritoryAlpha;
                        area.Rotation = 0;
                        area.Name = $"{zone.Display} — {status}";
                        _area[zone.Id] = area;
                    }
                }
            }

            Blip pin = World.CreateBlip(zone.Anchor);
            if (pin == null || !pin.Exists()) return;

            pin.Sprite = sprite;
            pin.Color = colour;
            pin.Scale = Config.Current.ZoneBlipScale;
            pin.IsShortRange = false;   // territory should be visible from anywhere
            pin.Name = $"{zone.Display} — {status}";
            _pin[zone.Id] = pin;
        }

        /// <param name="zoneId">A zone id, or a "house:" prefixed house id.</param>
        private void Remove(string zoneId)
        {
            Blip b;
            if (_area.TryGetValue(zoneId, out b))
            {
                if (b != null && b.Exists()) b.Delete();
                _area.Remove(zoneId);
            }
            if (_pin.TryGetValue(zoneId, out b))
            {
                if (b != null && b.Exists()) b.Delete();
                _pin.Remove(zoneId);
            }
        }

        /// <summary>Safe to call twice, and must run on abort — these outlive the script otherwise.</summary>
        public void Clear()
        {
            foreach (var b in _area.Values) if (b != null && b.Exists()) b.Delete();
            foreach (var b in _pin.Values) if (b != null && b.Exists()) b.Delete();

            _area.Clear();
            _pin.Clear();
            _drawn.Clear();
        }
    }
}

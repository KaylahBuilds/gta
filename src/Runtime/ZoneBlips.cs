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
    /// Two blips per zone: a coloured radius for the ground itself, and a small
    /// pin carrying the name and state. Neutral corners get only the pin, because
    /// filling the map with circles for ground nobody holds is noise — and in a
    /// heavily modded install the map is already crowded.
    /// </summary>
    public class ZoneBlips
    {
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
        }

        /// <summary>Everything the blip's appearance depends on, as one comparable string.</summary>
        private static string Signature(ZoneDef zone)
        {
            var state = GameState.Current;
            return string.Join("|",
                state.OwnerOf(zone.Id) ?? "neutral",
                state.IsZoneLocked(zone.Id) ? "raid" + state.LockoutDaysLeft(zone.Id) : "open",
                state.WorkersIn(zone.Id).Count().ToString());
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

            if (raided)
            {
                colour = BlipColor.Yellow;
                sprite = BlipSprite.PoliceArea;
                status = $"raided, {state.LockoutDaysLeft(zone.Id)}d";
            }
            else if (mine)
            {
                colour = BlipColor.Green;
                sprite = BlipSprite.DollarSignCircled;
                status = $"yours, {state.WorkersIn(zone.Id).Count()}/{zone.Slots} posted";
            }
            else if (contested)
            {
                colour = BlipColor.Red;
                sprite = BlipSprite.BigCircle;
                status = state.OwnerName(zone.Id);
            }
            else
            {
                colour = BlipColor.Grey;
                sprite = BlipSprite.BigCircleOutline;
                status = "neutral";
            }

            // Ground is only shaded when asked for, and only for turf somebody
            // actually holds. The pin carries the same information without
            // covering the map.
            if (Config.Current.ShowZoneAreaCircles && (mine || contested || raided))
            {
                Blip area = World.CreateBlip(zone.Anchor, zone.Radius);
                if (area != null && area.Exists())
                {
                    area.Color = colour;
                    area.Alpha = Config.Current.ZoneBlipAlpha;
                    area.Name = $"{zone.Display} — {status}";
                    _area[zone.Id] = area;
                }
            }

            Blip pin = World.CreateBlip(zone.Anchor);
            if (pin == null || !pin.Exists()) return;

            pin.Sprite = sprite;
            pin.Color = colour;
            pin.Scale = 0.8f;
            pin.IsShortRange = false;   // territory should be visible from anywhere
            pin.Name = $"{zone.Display} — {status}";
            _pin[zone.Id] = pin;
        }

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

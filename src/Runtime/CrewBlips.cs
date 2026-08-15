using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// The girls, on the map — the author's ask, verbatim: territory belongs to
    /// the zones, but "the girls need their own blips".
    ///
    /// One pin per roster member with somewhere to be: pinned to her live ped
    /// when the streaming layer has her spawned (the blip moves with her), at
    /// her post when it does not. Off-duty and jailed women carry no pin — the
    /// roster and the law menu already answer where they are, and a map pin for
    /// somebody in a cell points at nothing.
    ///
    /// Pink is hers; red means trouble; yellow means she is with a client. The
    /// pin name carries the rest, so the pause-map legend reads as a crew sheet.
    /// </summary>
    public class CrewBlips
    {
        private readonly SpawnManager _spawner;

        private readonly Dictionary<int, Blip> _pins = new Dictionary<int, Blip>();
        private readonly Dictionary<int, string> _drawn = new Dictionary<int, string>();

        private int _nextScan;

        public CrewBlips(SpawnManager spawner)
        {
            _spawner = spawner;
        }

        public void Update()
        {
            if (!Config.Current.ShowCrewBlips)
            {
                if (_pins.Count > 0) Clear();
                return;
            }

            if (Game.GameTime < _nextScan) return;
            _nextScan = Game.GameTime + Config.Current.ZoneBlipRefreshMs;

            var state = GameState.Current;
            var seen = new HashSet<int>();

            foreach (var worker in state.Roster)
            {
                Vector3 at;
                BlipColor colour;
                string signature;
                string name = Describe(worker, out at, out colour, out signature);

                if (name == null) continue;   // nowhere to be — no pin
                seen.Add(worker.Id);

                string previous;
                if (_drawn.TryGetValue(worker.Id, out previous) && previous == signature)
                {
                    // Same state — keep the pin unless the game ate it.
                    Blip existing;
                    if (_pins.TryGetValue(worker.Id, out existing)
                        && existing != null && existing.Exists())
                        continue;
                }

                _drawn[worker.Id] = signature;
                Remove(worker.Id);

                Blip pin = World.CreateBlip(at);
                if (pin == null || !pin.Exists()) continue;

                pin.Sprite = BlipSprite.Standard;
                pin.Scale = Config.Current.CrewBlipScale;
                pin.Color = colour;
                pin.IsShortRange = true;
                pin.Name = name;

                _pins[worker.Id] = pin;
            }

            // Anyone who left the books, went inside, or went off duty.
            foreach (var id in _pins.Keys.ToList())
            {
                if (seen.Contains(id)) continue;

                Remove(id);
                _drawn.Remove(id);
            }
        }

        private string Describe(WorkerData worker, out Vector3 at,
                                out BlipColor colour, out string signature)
        {
            at = Vector3.Zero;
            colour = BlipColor.Pink;
            signature = null;

            if (worker.IsJailed()) return null;
            if (worker.IsOnBooking) return null;   // she is away — no post to pin

            // A SPAWNED worker already carries the streaming layer's own blip
            // on her ped — a second pin here doubled every spawned woman on
            // the legend. This class covers the ones the world cannot show.
            if (_spawner.Live.TryGetValue(worker.Id, out var runtime) && runtime.IsValid)
                return null;

            string place;

            if (!string.IsNullOrEmpty(worker.ZoneId))
            {
                // Off shift or resting is off duty, whatever the record says —
                // a "working" pin on a woman asleep at noon is a map that lies.
                if (!worker.ShouldBeOnStreet(GTA.Chrono.GameClock.Hour)
                    && worker.State != WorkerState.InTrouble) return null;

                var zone = Zones.Get(worker.ZoneId);
                if (zone == null) return null;

                // The pavement-snapped anchor the crew actually stands on, so
                // the pin does not jump when she streams in.
                at = _spawner.AnchorFor(zone);
                place = zone.Display;
            }
            else if (worker.IsIndoors)
            {
                var house = Houses.Get(worker.HouseId);
                if (house == null) return null;

                at = house.Door;
                place = house.Display;
            }
            else
            {
                return null;   // off duty — the roster answers this one
            }

            bool trouble = worker.State == WorkerState.InTrouble;

            colour = trouble ? BlipColor.Red : BlipColor.Pink;
            string doing = trouble ? "in trouble" : "working";

            signature = string.Join("|",
                worker.ZoneId ?? "-",
                worker.HouseId ?? "-",
                doing);

            return $"{worker.Name} — {doing}, {place}";
        }

        private void Remove(int workerId)
        {
            Blip pin;
            if (!_pins.TryGetValue(workerId, out pin)) return;

            if (pin != null && pin.Exists()) pin.Delete();
            _pins.Remove(workerId);
        }

        public void Clear()
        {
            foreach (var pin in _pins.Values)
                if (pin != null && pin.Exists()) pin.Delete();

            _pins.Clear();
            _drawn.Clear();
        }
    }
}

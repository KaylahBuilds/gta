using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Houses are bought AT THE SITE and walked into, not purchased off a
    /// phone from the other side of the city. A walk-up you cannot walk up
    /// into is a menu row with a price on it.
    ///
    /// Unowned doors carry a for-sale pin so they can be found; owned doors
    /// let you inside, where the girls working the place are standing.
    /// </summary>
    public class HouseDoors
    {
        private const float DoorRange = 2.5f;
        private const float BlipRange = 500f;
        private const int TickMs = 900;

        private readonly Dictionary<string, Blip> _blips = new Dictionary<string, Blip>();
        private readonly List<Ped> _inside = new List<Ped>();

        private int _nextTickAt;
        private bool _in;
        private Vector3 _back;

        public bool IsInside => _in;

        public void Update()
        {
            if (_in)
            {
                GTA.UI.Screen.ShowHelpTextThisFrame(
                    $"Press ~INPUT_CONTEXT~ or {Config.Current.TalkKeyName} to step back outside.");
                return;
            }

            if (Game.GameTime < _nextTickAt) return;
            _nextTickAt = Game.GameTime + TickMs;

            var state = GameState.Current;
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            foreach (var house in Houses.All)
            {
                float d = player.Position.DistanceTo(house.Door);
                bool owned = state.OwnsHouse(house.Id);

                if (owned) { DropBlip(house.Id); }
                else if (d <= BlipRange) { EnsureBlip(house); }
                else { DropBlip(house.Id); continue; }

                if (d > DoorRange || player.IsInVehicle()) continue;

                if (owned)
                {
                    int working = state.WorkersInHouse(house.Id).Count();
                    GTA.UI.Screen.ShowHelpTextThisFrame(
                        $"Press ~b~{Config.Current.TalkKeyName}~s~ to go inside " +
                        $"~b~{house.Display}~s~ — {working} working.");
                    continue;
                }

                string refusal = Refusal(house, state);

                GTA.UI.Screen.ShowHelpTextThisFrame(refusal ??
                    $"Press ~b~{Config.Current.TalkKeyName}~s~ to take " +
                    $"~b~{house.Display}~s~ — ~g~${house.Cost:N0}~s~, " +
                    $"{house.Rooms} rooms.");
            }
        }

        /// <summary>Why you cannot have it yet, or null.</summary>
        private static string Refusal(HouseDef house, GameState state)
        {
            var needs = Houses.Get(house.RequiresId);

            if (needs != null && !state.OwnsHouse(needs.Id))
                return $"~b~{house.Display}~s~ — {needs.Display} comes first.";

            if (state.Reputation < house.MinReputation)
                return $"~b~{house.Display}~s~ — nobody hands you these keys at " +
                       $"{state.Reputation} reputation. You need {house.MinReputation}.";

            if (Game.Player.Money < house.Cost)
                return $"~b~{house.Display}~s~ — ~r~${house.Cost:N0}~s~, and you're " +
                       $"short ~r~${house.Cost - Game.Player.Money:N0}~s~.";

            return null;
        }

        /// <summary>The talk key at a door: buy it, or walk in. Returns true
        /// when it consumed the press.</summary>
        public bool TryUse()
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return false;

            if (_in) { Leave(); return true; }
            if (player.IsInVehicle()) return false;

            var state = GameState.Current;

            foreach (var house in Houses.All)
            {
                if (player.Position.DistanceTo(house.Door) > DoorRange) continue;

                if (state.OwnsHouse(house.Id)) { Enter(house); return true; }

                string refusal = Refusal(house, state);
                if (refusal != null) { Notify.Show("~o~" + refusal); return true; }

                Game.Player.Money -= house.Cost;
                state.OwnedHouses.Add(house.Id);
                DropBlip(house.Id);

                Notify.Show(
                    $"~g~{house.Display} is yours.~s~ {house.Rooms} rooms, rent of " +
                    $"${house.DailyRent:N0} a day from tonight. Post somebody before " +
                    "it starts costing you — and go and look at it.", true);

                Persistence.SaveManager.Log(
                    $"Bought house {house.Id} at the door for ${house.Cost}.");
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------

        private void Enter(HouseDef house)
        {
            var player = Game.Player.Character;
            var room = HouseInteriors.For(house);

            _back = player.Position;
            _in = true;

            GTA.UI.Screen.FadeOut(300);
            Script.Wait(350);

            player.Position = room.At;
            player.Heading = room.Heading;

            GTA.Native.Function.Call(GTA.Native.Hash.REQUEST_COLLISION_AT_COORD,
                room.At.X, room.At.Y, room.At.Z);
            Script.Wait(250);

            SpawnInside(house, room);

            GTA.UI.Screen.FadeIn(300);

            Notify.Show($"~g~Inside.~s~ {house.Display} — {room.Flavour}.");
        }

        /// <summary>The girls who work the place, in the room. Their own
        /// models, so the roster you built is the roster you see.</summary>
        private void SpawnInside(HouseDef house, HouseInteriors.Room room)
        {
            ReleaseInside();

            var state = GameState.Current;
            var stands = HouseInteriors.Stands(room.At, room.Heading);
            var working = state.WorkersInHouse(house.Id).Take(stands.Length).ToList();

            for (int i = 0; i < working.Count; i++)
            {
                var w = working[i];
                if (string.IsNullOrEmpty(w.ModelName)) continue;

                var model = new Model(w.ModelName);
                if (!model.IsInCdImage || !model.IsPed) continue;

                model.Request(500);
                if (!model.IsLoaded) { model.MarkAsNoLongerNeeded(); continue; }

                // Hand-placed and trusted: SafeGround rejects interiors by
                // design, so running these through it would refuse every one.
                var ped = SafeGround.CreatePed(model, stands[i]);
                model.MarkAsNoLongerNeeded();

                if (ped == null || !ped.Exists()) continue;

                ped.IsPersistent = true;
                ped.BlockPermanentEvents = true;

                Vector3 to = room.At - ped.Position;
                if (to.Length() > 0.3f) ped.Heading = to.ToHeading();

                ped.Task.StartScenarioInPlace(
                    i % 2 == 0 ? "WORLD_HUMAN_STAND_MOBILE" : "WORLD_HUMAN_SMOKING",
                    0, true);

                _inside.Add(ped);
            }
        }

        private void ReleaseInside()
        {
            foreach (var ped in _inside)
            {
                if (ped == null || !ped.Exists()) continue;

                ped.BlockPermanentEvents = false;
                ped.IsPersistent = false;
                ped.MarkAsNoLongerNeeded();
            }

            _inside.Clear();
        }

        private void Leave()
        {
            var player = Game.Player.Character;

            if (player != null && player.Exists())
            {
                GTA.UI.Screen.FadeOut(300);
                Script.Wait(350);
                player.Position = _back;
                Script.Wait(250);
                GTA.UI.Screen.FadeIn(300);
            }

            ReleaseInside();
            _in = false;
        }

        private void EnsureBlip(HouseDef house)
        {
            Blip blip;
            if (_blips.TryGetValue(house.Id, out blip) && blip != null && blip.Exists()) return;

            blip = World.CreateBlip(house.Door);
            if (blip == null || !blip.Exists()) return;

            blip.Sprite = BlipSprite.Garage;
            blip.Color = BlipColor.White;
            blip.Scale = 0.8f;
            blip.IsShortRange = true;
            blip.Name = $"For sale — {house.Display} (${house.Cost:N0})";

            _blips[house.Id] = blip;
        }

        private void DropBlip(string id)
        {
            Blip blip;
            if (!_blips.TryGetValue(id, out blip)) return;

            if (blip != null && blip.Exists()) blip.Delete();
            _blips.Remove(id);
        }

        /// <summary>Abort safety: never strand the player inside.</summary>
        public void Cleanup()
        {
            ReleaseInside();

            if (_in)
            {
                var player = Game.Player.Character;
                if (player != null && player.Exists()) player.Position = _back;
                _in = false;
            }

            foreach (var blip in _blips.Values)
                if (blip != null && blip.Exists()) blip.Delete();

            _blips.Clear();
        }
    }
}

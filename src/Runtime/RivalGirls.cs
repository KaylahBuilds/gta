using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// Rival territory, staffed. A zone a rival crew HOLDS shows its girls
    /// working the block — two of theirs on the pavement and one of the
    /// crew's men watching from a remove — the same way the product side
    /// shows a gang's dealer standing a corner it owns. Territory is bodies,
    /// not map paint: drive a rival block and you SEE whose it is.
    ///
    /// Pure scene, no economy: their money is not modelled, only their
    /// presence. Contained like everything else — nobody spawns while you
    /// are wanted or fighting, and the scene releases the moment you leave.
    /// </summary>
    public class RivalGirls
    {
        private const float SpawnRange = 110f;
        private const float DropRange = 160f;
        private const int TickMs = 2000;

        private static readonly string[] GirlModels =
            { "s_f_y_hooker_01", "s_f_y_hooker_02", "s_f_y_hooker_03" };

        private readonly List<Ped> _peds = new List<Ped>();
        private readonly System.Random _rng = new System.Random();

        private string _zoneId;
        private int _nextTickAt;

        public void Update()
        {
            if (Game.GameTime < _nextTickAt) return;
            _nextTickAt = Game.GameTime + TickMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            var state = GameState.Current;

            if (Game.Player.WantedLevel > 0 || player.IsInCombat)
            {
                Release();
                return;
            }

            // A live scene holds until you leave it or the ground changes hands.
            if (_zoneId != null)
            {
                var held = Zones.All.FirstOrDefault(z => z.Id == _zoneId);

                bool stillTheirs = held != null
                    && state.OwnerOf(held.Id) != null
                    && !state.PlayerOwns(held.Id)
                    && !state.IsZoneLocked(held.Id);

                if (!stillTheirs
                    || player.Position.DistanceTo(held.Anchor) > DropRange
                    || _peds.All(p => p == null || !p.Exists() || p.IsDead))
                {
                    Release();
                }

                return;
            }

            // The nearest rival-held, unlocked zone in range gets its people.
            foreach (var zone in Zones.All)
            {
                string owner = state.OwnerOf(zone.Id);
                if (owner == null || state.PlayerOwns(zone.Id)) continue;
                if (state.IsZoneLocked(zone.Id)) continue;
                if (player.Position.DistanceTo(zone.Anchor) > SpawnRange) continue;

                SpawnScene(zone, owner);
                return;
            }
        }

        private void SpawnScene(ZoneDef zone, string ownerId)
        {
            _zoneId = zone.Id;

            var crew = GameState.Current.Rivals.FirstOrDefault(r => r.Id == ownerId);

            // Two of their girls on the pavement...
            for (int i = 0; i < 2; i++)
            {
                Vector3 at = Stand(zone.Anchor, 4f + i * 6f);
                var girl = Spawn(GirlModels[_rng.Next(GirlModels.Length)], at);
                if (girl == null) continue;

                girl.Task.StartScenarioInPlace("WORLD_HUMAN_PROSTITUTE", 0, true);
                _peds.Add(girl);
            }

            // ...and one of the crew's men watching from a remove. His model
            // IS the crew's colours — territory readable at a glance.
            if (crew != null && !string.IsNullOrEmpty(crew.PedModel))
            {
                var watcher = Spawn(crew.PedModel, Stand(zone.Anchor, 12f));
                if (watcher != null)
                {
                    watcher.Weapons.Give(WeaponHash.Pistol, 30, false, false);
                    watcher.Task.StartScenarioInPlace("WORLD_HUMAN_GUARD_STAND", 0, true);
                    _peds.Add(watcher);
                }
            }
        }

        /// <summary>The pavement near the anchor, never a raw offset — a bad
        /// spot costs the scene a body, never puts one on a roof.</summary>
        private Vector3 Stand(Vector3 anchor, float outFrom)
        {
            Vector3 rough = anchor.Around(outFrom);
            Vector3 walk = World.GetNextPositionOnSidewalk(rough);

            if (walk == Vector3.Zero || walk.DistanceTo(anchor) > 35f)
                walk = World.GetNextPositionOnSidewalk(anchor);

            return walk != Vector3.Zero ? walk : anchor;
        }

        private Ped Spawn(string modelName, Vector3 at)
        {
            var model = new Model(modelName);
            if (!model.IsInCdImage || !model.IsPed) return null;

            model.Request(250);
            if (!model.IsLoaded) { model.MarkAsNoLongerNeeded(); return null; }

            var ped = SafeGround.CreatePed(model, at);
            model.MarkAsNoLongerNeeded();

            if (ped == null || !ped.Exists()) return null;

            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
            return ped;
        }

        private void Release()
        {
            foreach (var ped in _peds)
            {
                if (ped == null || !ped.Exists()) continue;

                ped.IsPersistent = false;
                ped.MarkAsNoLongerNeeded();
            }

            _peds.Clear();
            _zoneId = null;
        }

        /// <summary>Must run on abort — these are persistent peds.</summary>
        public void Clear() => Release();
    }
}

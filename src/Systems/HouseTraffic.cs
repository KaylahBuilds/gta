using System;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// A working house LOOKS worked. Stand near one you own with girls posted
    /// and you see the trade: a client walks up to the door and goes in, a
    /// girl steps out for air and goes back, somebody leaves and doesn't look
    /// at you. The house stops being a door with a blip on it.
    ///
    /// Exactly ONE ped is alive at a time, on purpose. The ped budget in a
    /// heavily modded install is the scarcest thing this mod spends, and
    /// ambience is the first thing that should yield.
    /// </summary>
    public class HouseTraffic
    {
        private const float NearRange = 60f;
        private const int TickMs = 1200;

        private static readonly Random Rng = new Random();

        private static readonly string[] ClientModels =
        {
            "a_m_y_business_01", "a_m_m_business_01", "a_m_y_hipster_02",
            "a_m_m_eastsa_02", "a_m_y_genstreet_02",
        };

        private Ped _ped;
        private Vector3 _target;
        private bool _leaving;
        private int _nextTickAt;
        private int _nextSpawnAt;
        private int _givesUpAt;

        public void Update()
        {
            if (Game.GameTime < _nextTickAt) return;
            _nextTickAt = Game.GameTime + TickMs;

            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            if (_ped != null) { Walk(player); return; }

            if (Game.GameTime < _nextSpawnAt) return;
            if (Game.Player.WantedLevel > 0 || player.IsInCombat) return;

            var state = GameState.Current;

            // A house you own, with somebody working it, that you can see.
            var house = Houses.All.FirstOrDefault(h =>
                state.OwnsHouse(h.Id)
                && state.WorkersInHouse(h.Id).Any()
                && player.Position.DistanceTo(h.Door) <= NearRange);

            if (house == null) return;

            // Busier houses see more feet.
            int rooms = Math.Max(1, house.Rooms);
            _nextSpawnAt = Game.GameTime + 20000 + Rng.Next(40000 / rooms);

            Spawn(house, state);
        }

        private void Spawn(HouseDef house, GameState state)
        {
            // Half the time somebody LEAVES instead of arriving — a house that
            // only ever swallows people reads as a queue, not a business.
            _leaving = Rng.NextDouble() < 0.45;

            string modelName;
            if (_leaving && Rng.NextDouble() < 0.5)
            {
                // One of your own, stepping out for air.
                var girl = state.WorkersInHouse(house.Id)
                    .FirstOrDefault(w => !string.IsNullOrEmpty(w.ModelName));
                modelName = girl != null ? girl.ModelName
                    : ClientModels[Rng.Next(ClientModels.Length)];
            }
            else
            {
                modelName = ClientModels[Rng.Next(ClientModels.Length)];
            }

            var model = new Model(modelName);
            if (!model.IsInCdImage || !model.IsPed) return;

            model.Request(500);
            if (!model.IsLoaded) { model.MarkAsNoLongerNeeded(); return; }

            // Arrivals start out on the pavement and walk in; leavers start at
            // the door and walk away.
            Vector3 street = SafeGround.Fix(house.Door.Around(14f));

            Vector3 from = _leaving ? SafeGround.Fix(house.Door) : street;
            _target = _leaving ? street : house.Door;

            _ped = SafeGround.CreatePed(model, from);
            model.MarkAsNoLongerNeeded();

            if (_ped == null || !_ped.Exists()) return;

            _ped.IsPersistent = true;
            _ped.BlockPermanentEvents = true;
            _ped.Task.FollowNavMeshTo(_target);

            _givesUpAt = Game.GameTime + 40000;
        }

        private void Walk(Ped player)
        {
            if (!_ped.Exists() || _ped.IsDead) { Release(); return; }

            // Arrived, or gave up, or the player left the scene entirely.
            bool arrived = _ped.Position.DistanceTo(_target) < 2.5f;

            if (arrived || Game.GameTime > _givesUpAt
                || player.Position.DistanceTo(_target) > NearRange + 40f)
            {
                Release();
                return;
            }

            // Keep him moving: a single navmesh task can be dropped when the
            // engine reshuffles, and a client frozen mid-pavement is worse
            // than no client at all.
            if (!_ped.IsWalking && !_ped.IsRunning)
                _ped.Task.FollowNavMeshTo(_target);
        }

        private void Release()
        {
            if (_ped != null && _ped.Exists())
            {
                _ped.BlockPermanentEvents = false;
                _ped.IsPersistent = false;
                _ped.MarkAsNoLongerNeeded();
            }

            _ped = null;
        }

        public void Cleanup() => Release();
    }
}

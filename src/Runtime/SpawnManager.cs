using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// Streams worker peds in and out by distance.
    ///
    /// This is the load-bearing piece of the whole mod: holding 20+ live peds
    /// permanently tanks framerate and loses a constant fight with the engine's
    /// population culling. Workers are numbers; peds are a temporary view.
    /// </summary>
    public class SpawnManager
    {
        private readonly Dictionary<int, WorkerRuntime> _live = new Dictionary<int, WorkerRuntime>();

        /// <summary>
        /// Zone id -> its anchor snapped to the nearest pavement, resolved once.
        ///
        /// Snapping is done here rather than per worker because
        /// GetNextPositionOnSidewalk returns the nearest *node*, so four posts
        /// 2.6m apart all resolve to the same node and the crew stands in a heap.
        /// Snap the zone once, then spread from the snapped point.
        /// </summary>
        private readonly Dictionary<string, Vector3> _snappedAnchors = new Dictionary<string, Vector3>();

        private RelationshipGroup _crew;
        private bool _crewReady;
        private int _nextScan;

        public IReadOnlyDictionary<int, WorkerRuntime> Live => _live;

        /// <summary>Relationship group owned by the player's crew. Incidents use
        /// this to make antagonists hostile to workers.</summary>
        public RelationshipGroup Crew
        {
            get
            {
                EnsureCrewGroup();
                return _crew;
            }
        }

        /// <summary>
        /// The zone's anchor moved onto the nearest pavement, cached. Falls back
        /// to the raw anchor if the game finds nothing walkable nearby — a bad
        /// coordinate should place someone awkwardly, not nowhere.
        /// </summary>
        private Vector3 SnappedAnchor(ZoneDef zone)
        {
            Vector3 cached;
            if (_snappedAnchors.TryGetValue(zone.Id, out cached)) return cached;

            Vector3 onFoot = World.GetNextPositionOnSidewalk(zone.Anchor);
            Vector3 result = onFoot == Vector3.Zero ? zone.Anchor : onFoot;

            _snappedAnchors[zone.Id] = result;

            if (Config.Current.LogSpawnDiagnostics)
            {
                Persistence.SaveManager.Log(
                    $"ANCHOR-SNAP {zone.Id} raw=({zone.Anchor.X:0.0},{zone.Anchor.Y:0.0},{zone.Anchor.Z:0.0}) " +
                    $"snapped=({result.X:0.0},{result.Y:0.0},{result.Z:0.0}) " +
                    $"moved={zone.Anchor.DistanceTo(result):0.0}m");
            }

            return result;
        }

        /// <summary>Where this worker stands, spread from the zone's snapped anchor.</summary>
        public Vector3 PostFor(WorkerData worker, ZoneDef zone)
        {
            return Zones.PostPosition(
                SnappedAnchor(zone), zone.Heading, zone.Slots,
                GameState.Current.SlotIndexOf(worker));
        }

        public bool TryGetRuntime(int workerId, out WorkerRuntime runtime)
        {
            if (_live.TryGetValue(workerId, out runtime) && runtime.IsValid) return true;
            runtime = null;
            return false;
        }

        public void Update()
        {
            if (Game.GameTime < _nextScan) return;
            _nextScan = Game.GameTime + Config.Current.StreamScanIntervalMs;

            EnsureCrewGroup();
            PruneDead();

            Vector3 player = Game.Player.Character.Position;
            float spawnR = Config.Current.SpawnRadius;
            float despawnR = Config.Current.DespawnRadius;

            foreach (var worker in GameState.Current.Roster.ToList())
            {
                var zone = Zones.Get(worker.ZoneId);

                // InTrouble workers must stay streamable — an active incident
                // needs their ped to exist once the player arrives. Otherwise it
                // is the shift that decides, not just the assignment.
                bool onPost = worker.State == WorkerState.InTrouble ||
                              worker.ShouldBeOnStreet(GTA.Chrono.GameClock.Hour);

                if (!onPost || zone == null)
                {
                    Despawn(worker.Id);
                    continue;
                }

                Vector3 post = PostFor(worker, zone);
                float distance = player.DistanceTo(post);

                if (distance <= spawnR)
                {
                    if (!_live.ContainsKey(worker.Id))
                    {
                        var runtime = WorkerRuntime.Create(worker, post, zone.Heading, _crew);
                        if (runtime != null) _live[worker.Id] = runtime;
                        // If null, the model was not resident yet — retry next scan.
                    }
                }
                else if (distance >= despawnR)
                {
                    Despawn(worker.Id);
                }
            }
        }

        /// <summary>Position of a worker's ped if streamed, otherwise their post.</summary>
        public Vector3? PositionOf(WorkerData worker)
        {
            WorkerRuntime runtime;
            if (_live.TryGetValue(worker.Id, out runtime) && runtime.IsValid)
                return runtime.Ped.Position;

            var zone = Zones.Get(worker.ZoneId);
            if (zone == null) return null;

            return PostFor(worker, zone);
        }

        private void EnsureCrewGroup()
        {
            if (_crewReady) return;

            _crew = World.AddRelationshipGroup("OTB_CREW");
            var player = Game.Player.Character.RelationshipGroup;

            _crew.SetRelationshipBetweenGroups(player, Relationship.Companion, true);
            player.SetRelationshipBetweenGroups(_crew, Relationship.Companion, true);

            _crewReady = true;
        }

        /// <summary>Drop handles for peds the engine or the player has destroyed.</summary>
        private void PruneDead()
        {
            var stale = _live.Where(kv => !kv.Value.IsValid).Select(kv => kv.Key).ToList();
            foreach (int id in stale)
            {
                _live[id].Destroy();
                _live.Remove(id);
            }
        }

        public void Despawn(int workerId)
        {
            WorkerRuntime runtime;
            if (!_live.TryGetValue(workerId, out runtime)) return;

            runtime.Destroy();
            _live.Remove(workerId);
        }

        public void DespawnAll()
        {
            foreach (var runtime in _live.Values) runtime.Destroy();
            _live.Clear();
        }
    }
}

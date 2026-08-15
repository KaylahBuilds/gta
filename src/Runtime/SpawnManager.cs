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
        private readonly Dictionary<int, EnforcerRuntime> _muscle = new Dictionary<int, EnforcerRuntime>();

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

        /// <summary>
        /// Peds this mod currently owns. The old ceiling was purely emergent —
        /// a property of six anchors being far apart — and a future zone within
        /// 150m of another would have raised it silently. Enforced now, with a
        /// priority order when a spawn would cross it: incident peds are never
        /// skipped (combat correctness — that is the scene the player was
        /// summoned to), then workers of the in-range zone, then guarding
        /// muscle, then region-station muscle, which is pure flavour and yields
        /// first. A skipped spawn retries on the next scan.
        /// </summary>
        public int OwnedPeds => _live.Count + _muscle.Count;

        public IReadOnlyDictionary<int, WorkerRuntime> Live => _live;
        public IReadOnlyDictionary<int, EnforcerRuntime> LiveMuscle => _muscle;

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
        /// <summary>
        /// The zone's working anchor — snapped to pavement and cached. Public so
        /// incidents spawn and blip against the same corrected point workers stand
        /// on, rather than the raw hand-written coordinate.
        /// </summary>
        public Vector3 AnchorFor(ZoneDef zone) => zone == null ? Vector3.Zero : SnappedAnchor(zone);

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
                    if (!_live.ContainsKey(worker.Id)
                        && OwnedPeds < Config.Current.MaxOwnedPeds)
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

            StreamMuscle(player, spawnR, despawnR);
        }

        /// <summary>
        /// Puts the muscle on the street.
        ///
        /// They stand where the work is: next to the woman they are minding if
        /// they have been given one, otherwise on the busiest corner you hold in
        /// their region. That is the whole reason to know where they are — a
        /// glance tells you which of your corners is actually covered, which the
        /// menu could only ever tell you in a list.
        /// </summary>
        private void StreamMuscle(Vector3 player, float spawnR, float despawnR)
        {
            var state = GameState.Current;

            foreach (var enforcer in state.Enforcers.ToList())
            {
                // A follower is measured against the player, not his post.
                EnforcerRuntime followed;
                if (_muscle.TryGetValue(enforcer.Id, out followed) && followed.Following)
                {
                    if (!followed.IsValid || player.DistanceTo(followed.Ped.Position) >= despawnR)
                        DespawnMuscle(enforcer.Id);
                    continue;
                }

                Vector3? post = PostForEnforcer(enforcer);

                if (post == null)
                {
                    DespawnMuscle(enforcer.Id);
                    continue;
                }

                float distance = player.DistanceTo(post.Value);

                if (distance <= spawnR)
                {
                    if (_muscle.ContainsKey(enforcer.Id)) continue;

                    // Muscle yield to workers when the budget is tight; a
                    // guarding man is attached to a visible worker and outranks
                    // a region station, which is pure flavour.
                    int reserve = enforcer.IsGuarding ? 0 : 2;
                    if (OwnedPeds + reserve >= Config.Current.MaxOwnedPeds) continue;

                    var runtime = EnforcerRuntime.Create(enforcer, post.Value, 0f, _crew);
                    if (runtime != null) _muscle[enforcer.Id] = runtime;
                }
                else if (distance >= despawnR)
                {
                    DespawnMuscle(enforcer.Id);
                }
            }
        }

        /// <summary>Where this man is standing, or null if there is nowhere sensible.</summary>
        public Vector3? PostForEnforcer(EnforcerData enforcer)
        {
            var state = GameState.Current;

            // Minding somebody: he is next to her, wherever she is. Spread by
            // his position among the guards in that zone rather than a constant
            // offset — two guards at a constant +(1.6,1.6) stood inside each
            // other, which breaks the focus system's identity guarantee before
            // it exists (coincident peds cannot be told apart by walking up).
            if (enforcer.IsGuarding)
            {
                var guarded = state.GetWorker(enforcer.GuardingWorkerId);
                var guardedZone = guarded == null ? null : Zones.Get(guarded.ZoneId);

                if (guardedZone != null)
                {
                    var guards = state.Enforcers
                        .Where(e => e.IsGuarding)
                        .Where(e =>
                        {
                            var w = state.GetWorker(e.GuardingWorkerId);
                            return w != null && w.ZoneId == guarded.ZoneId;
                        })
                        .OrderBy(e => e.Id)
                        .ToList();

                    int index = guards.FindIndex(e => e.Id == enforcer.Id);
                    if (index < 0) index = 0;

                    return Zones.PostPosition(
                               SnappedAnchor(guardedZone) + new Vector3(0f, 2.4f, 0f),
                               guardedZone.Heading, System.Math.Max(2, guards.Count), index);
                }
            }

            var region = Regions.Get(enforcer.RegionId);
            if (region == null || region.ZoneIds == null) return null;

            // Otherwise the busiest corner you hold in his region.
            ZoneDef best = null;
            int mostWorkers = -1;

            foreach (string zoneId in region.ZoneIds)
            {
                var zone = Zones.Get(zoneId);
                if (zone == null || !state.PlayerOwns(zoneId)) continue;

                int count = state.WorkersIn(zoneId).Count();
                if (count <= mostWorkers) continue;

                mostWorkers = count;
                best = zone;
            }

            // Nothing held in his region — he is still on the payroll, so he is
            // still somewhere. The first corner of his patch will do.
            if (best == null && region.ZoneIds.Length > 0) best = Zones.Get(region.ZoneIds[0]);
            if (best == null) return null;

            // Off to one side of the working posts, so he is not standing in the
            // middle of the crew.
            return SnappedAnchor(best) + new Vector3(-2.2f, 2.2f, 0f);
        }

        /// <summary>
        /// Set once at construction so incidents can reach the stream without
        /// plumbing a reference through every constructor in the mod.
        /// </summary>
        public static SpawnManager Instance { get; private set; }

        public void RegisterInstance() => Instance = this;

        public void DespawnMuscleFor(int enforcerId) => DespawnMuscle(enforcerId);

        private void DespawnMuscle(int enforcerId)
        {
            EnforcerRuntime runtime;
            if (!_muscle.TryGetValue(enforcerId, out runtime)) return;

            runtime.Despawn();
            _muscle.Remove(enforcerId);
        }

        public bool TryGetMuscle(int enforcerId, out EnforcerRuntime runtime)
        {
            if (_muscle.TryGetValue(enforcerId, out runtime) && runtime.IsValid) return true;
            runtime = null;
            return false;
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

            // Same for muscle: a man the player shot, or the engine deleted, must
            // leave the dictionary or he can never be spawned again — the
            // ContainsKey check upstream would keep finding a dead handle.
            var goneMuscle = _muscle.Where(kv => !kv.Value.IsValid).Select(kv => kv.Key).ToList();
            foreach (int id in goneMuscle)
            {
                _muscle[id].Despawn();
                _muscle.Remove(id);
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

            // Muscle too. Every ped here is IsPersistent, which means the engine
            // will not cull it — so anything this method forgets is leaked into
            // the world on every script reload and every quit, permanently, until
            // the player restarts the game.
            foreach (var runtime in _muscle.Values) runtime.Despawn();
            _muscle.Clear();
        }
    }
}

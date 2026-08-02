using GTA;
using GTA.Math;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems.Incidents
{
    /// <summary>
    /// One live problem. Owns a route blip, a countdown and whatever peds it
    /// spawned, and is responsible for cleaning all of it up regardless of how
    /// it ends.
    ///
    /// Incidents come in two shapes. Most are scoped to a worker and target their
    /// post; turf battles are scoped to a zone and have no worker at all. That is
    /// what <see cref="NoWorker"/> and the overridable <see cref="TargetPosition"/>
    /// exist for.
    ///
    /// An incident may start while the player is across the map. That state is
    /// valid: the blip and countdown run, and antagonist peds are only created
    /// once the area is actually streamed in.
    /// </summary>
    public abstract class Incident
    {
        public const int NoWorker = -1;

        protected readonly SpawnManager Spawner;
        private readonly int _durationMs;

        private Blip _blip;

        // Counted down from elapsed time rather than compared against an absolute
        // deadline. An absolute deadline keeps running while Main skips ticks —
        // during a death, an arrest or a cutscene — so the player regained control
        // to an already-expired incident. Draining per tick, capped, means lost
        // time simply does not count against them.
        private int _remainingMs;
        private int _lastTickAt;

        private const int MaxDrainPerTickMs = 250;

        public int WorkerId { get; }
        public bool Finished { get; private set; }
        public bool Succeeded { get; private set; }

        protected Incident(int workerId, SpawnManager spawner, int durationMs)
        {
            WorkerId = workerId;
            Spawner = spawner;
            _durationMs = durationMs;
        }

        /// <summary>Null for zone-scoped incidents, or if the worker has since left.</summary>
        public WorkerData Worker =>
            WorkerId == NoWorker ? null : GameState.Current.GetWorker(WorkerId);

        protected bool IsWorkerScoped => WorkerId != NoWorker;

        public abstract string Title { get; }
        protected abstract string Objective { get; }

        /// <summary>How close the player must get for the incident to resolve.</summary>
        protected virtual float ResolveDistance => 14f;

        protected virtual BlipSprite Sprite => BlipSprite.Standard;
        protected virtual BlipColor Colour => BlipColor.Red;

        /// <summary>
        /// Reputation moved by the outcome. Turning up is what builds a name;
        /// letting things happen without you is what costs one.
        /// </summary>
        protected virtual int ReputationReward => 8;
        protected virtual int ReputationPenalty => 10;

        /// <summary>
        /// Where the blip sits and what "arrived" is measured against. Defaults to
        /// the worker's post; zone-scoped incidents override it.
        /// </summary>
        protected virtual Vector3? TargetPosition
        {
            get
            {
                var worker = Worker;
                return worker == null ? (Vector3?)null : Spawner.PositionOf(worker);
            }
        }

        // ------------------------------------------------------------------

        public void Start()
        {
            var worker = Worker;

            // A worker-scoped incident whose worker vanished between the roll and
            // here has nothing to act on.
            if (IsWorkerScoped && worker == null)
            {
                Finished = true;
                return;
            }

            if (worker != null) worker.State = WorkerState.InTrouble;

            // The burner network is a head start: word reaches you sooner, so the
            // clock you are racing is longer.
            _remainingMs = _durationMs;
            if (GameState.Current.HasUpgrade(UpgradeCatalog.Burner))
                _remainingMs += Config.Current.BurnerExtraSeconds * 1000;

            _lastTickAt = Game.GameTime;

            Vector3? target = TargetPosition;
            if (target.HasValue)
            {
                _blip = World.CreateBlip(target.Value);
                _blip.Sprite = Sprite;
                _blip.Color = Colour;
                _blip.Name = BlipLabel;
                _blip.ShowRoute = true;
            }

            Notify.Show($"~r~{Title}~s~ — {StartMessage}", true);
            OnStart(worker);
        }

        protected virtual string BlipLabel => Worker == null ? Title : $"{Title}: {Worker.Name}";

        protected virtual string StartMessage =>
            Worker == null ? "get over there." : $"{Worker.Name}. Get over there.";

        public void Update()
        {
            if (Finished) return;

            var worker = Worker;
            if (IsWorkerScoped && worker == null)
            {
                Finish(false);
                return;
            }

            WorkerRuntime runtime = null;
            bool live = IsWorkerScoped && Spawner.TryGetRuntime(WorkerId, out runtime);

            if (_blip != null && _blip.Exists())
            {
                if (live) _blip.Position = runtime.Ped.Position;
            }

            int now = Game.GameTime;
            int elapsed = now - _lastTickAt;
            _lastTickAt = now;
            if (elapsed > 0)
                _remainingMs -= elapsed > MaxDrainPerTickMs ? MaxDrainPerTickMs : elapsed;

            DrawObjective(_remainingMs);

            OnUpdate(worker, live ? runtime : null);
            if (Finished) return;

            if (_remainingMs <= 0) Fail(worker);
        }

        private void DrawObjective(int remainingMs)
        {
            int seconds = remainingMs / 1000;
            if (seconds < 0) seconds = 0;
            GTA.UI.Screen.ShowSubtitle($"{Objective}  ~y~{seconds}s", 200);
        }

        // ------------------------------------------------------------------

        protected float PlayerDistance()
        {
            Vector3? target = TargetPosition;
            if (!target.HasValue) return float.MaxValue;
            return Game.Player.Character.Position.DistanceTo(target.Value);
        }

        protected bool PlayerHasArrived() => PlayerDistance() <= ResolveDistance;

        protected void Succeed(WorkerData worker, string message)
        {
            OnSucceed(worker);
            worker?.Clamp();
            Notify.Show($"~g~{message}");
            Finish(true);
        }

        protected void Fail(WorkerData worker, string message = null)
        {
            OnFail(worker);
            worker?.Clamp();
            Notify.Show($"~r~{message ?? Title + " went bad."}");
            Finish(false);
        }

        private void Finish(bool succeeded)
        {
            Succeeded = succeeded;
            Finished = true;

            if (Config.Current.ReputationEnabled)
                Reputation.Award(succeeded ? ReputationReward : -ReputationPenalty);

            var worker = Worker;
            if (worker != null && worker.State == WorkerState.InTrouble)
            {
                // Subclasses that pull someone off the street set ZoneId to null.
                worker.State = string.IsNullOrEmpty(worker.ZoneId)
                    ? WorkerState.OffDuty
                    : WorkerState.Working;
            }

            Cleanup();
        }

        /// <summary>
        /// Must be safe to call twice, and must run on script abort as well as on
        /// normal completion.
        /// </summary>
        public virtual void Cleanup()
        {
            if (_blip != null && _blip.Exists())
            {
                _blip.Delete();
                _blip = null;
            }
        }

        // ------------------------------------------------------------------

        protected abstract void OnStart(WorkerData worker);

        /// <param name="worker">Null for zone-scoped incidents.</param>
        /// <param name="runtime">Null while the worker is not streamed in.</param>
        protected abstract void OnUpdate(WorkerData worker, WorkerRuntime runtime);

        protected abstract void OnSucceed(WorkerData worker);
        protected abstract void OnFail(WorkerData worker);

        // ------------------------------------------------------------------

        /// <summary>Shared helper for antagonist peds so cleanup is uniform.</summary>
        protected static Ped SpawnAntagonist(string modelName, Vector3 near, float heading,
                                             float scatter = 3.5f)
        {
            var model = new Model(modelName);
            if (!model.IsInCdImage || !model.IsValid) return null;

            model.Request(0);
            if (!model.IsLoaded) return null;

            Ped ped = World.CreatePed(model, SafeSpot(near.Around(scatter)), heading);
            model.MarkAsNoLongerNeeded();

            if (ped == null || !ped.Exists()) return null;

            ped.IsPersistent = true;
            ped.BlockPermanentEvents = true;
            return ped;
        }

        /// <summary>
        /// Whether this antagonist is still contesting the ground.
        ///
        /// Not the same question as "is it dead". A ped that fled, fell through
        /// the map, spawned somewhere unreachable or is down but not technically
        /// dead is no longer fighting for the corner — and testing IsDead alone
        /// meant one of those could block a win forever while the player stood on
        /// an empty street waiting to be told they had lost.
        /// </summary>
        protected static bool StillContesting(Ped ped, Vector3 ground, float radius)
        {
            if (ped == null || !ped.Exists()) return false;
            if (ped.IsDead || !ped.IsAlive) return false;

            // Ragdolled, downed or otherwise out of the fight.
            if (ped.Health <= 5) return false;

            // Ran off. Whatever they are doing, they are not holding this corner.
            return ped.Position.DistanceTo(ground) <= radius;
        }

        /// <summary>
        /// Nudges a spawn point onto ground a ped can actually stand on.
        ///
        /// Zone anchors are hand-written coordinates and most have never been
        /// checked in-game, so spawning at one raw can put an enforcer on a roof,
        /// inside geometry or in the sea — where they are alive, unreachable, and
        /// able to keep a turf battle unwinnable forever. Asking the game for a
        /// safe position makes every corner behave regardless of how good the
        /// coordinate behind it is.
        /// </summary>
        protected static Vector3 SafeSpot(Vector3 wanted)
        {
            Vector3 safe;
            if (World.GetSafePositionForPed(wanted, out safe,
                    GetSafePositionFlags.NotWater | GetSafePositionFlags.NotInterior) &&
                safe != Vector3.Zero)
                return safe;

            // Nothing safe nearby — fall back to the requested point rather than
            // silently refusing to spawn.
            return wanted;
        }

        protected static void ReleasePed(ref Ped ped)
        {
            if (ped != null && ped.Exists())
            {
                ped.MarkAsNoLongerNeeded();
                ped.Delete();
            }
            ped = null;
        }
    }
}

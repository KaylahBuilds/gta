using System.Linq;
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

            // Dispatch stacks on top of the burners rather than replacing them —
            // it needs the network to answer the phones for.
            if (GameState.Current.HasUpgrade(UpgradeCatalog.Dispatch))
                _remainingMs += Config.Current.DispatchExtraSeconds * 1000;

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

            UpdateMuscle();

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
                // A house posting counts as a post too, or anyone moved indoors
                // mid-incident would come out of it benched.
                worker.State = string.IsNullOrEmpty(worker.ZoneId) && !worker.IsIndoors
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
            ResolveMuscle();

            if (_blip != null && _blip.Exists())
            {
                _blip.Delete();
                _blip = null;
            }
        }

        // ------------------------------------------------------------------
        // Hired muscle
        //
        // An armed enforcer covering this ground turns up and fights. He is
        // deliberately spawned by the same deferral the antagonists use, so he
        // arrives with them rather than standing on an empty street waiting.
        // ------------------------------------------------------------------

        private Ped _muscle;
        private Blip _muscleBlip;
        private int _muscleEnforcerId = -1;
        private int _lastFightOrderAt;

        /// <summary>The armed, fit enforcer covering a zone, or null.</summary>
        protected static EnforcerData AvailableMuscle(string zoneId)
        {
            if (!Config.Current.MuscleTurnsUp) return null;

            var enforcer = GameState.Current.EnforcerFor(zoneId);
            if (enforcer == null) return null;
            if (!enforcer.IsArmed || enforcer.IsInjured()) return null;

            return enforcer;
        }

        /// <summary>
        /// Brings in whoever covers this ground. Safe to call every tick — it
        /// returns immediately once he is out, or if there is nobody to send.
        /// </summary>
        protected void TrySpawnMuscle(string zoneId, Vector3 near, float heading)
        {
            if (_muscle != null) return;

            var enforcer = AvailableMuscle(zoneId);
            if (enforcer == null) return;

            // One owner at a time. If the region stream has this man standing
            // on the contested corner, drop that ped BEFORE the incident spawns
            // its own from the same model — otherwise the same enforcer is
            // visibly present twice, and the incident's cleanup only knows
            // about one of them.
            SpawnManager.Instance?.DespawnMuscleFor(enforcer.Id);

            var loadout = enforcer.Loadout;
            if (loadout == null) return;

            Ped ped = SpawnAntagonist(enforcer.ModelName, near, heading, 6f);
            if (ped == null) return;

            ped.RelationshipGroup = Factions.Allied(Spawner.Crew);
            ped.Armor = loadout.Armour;
            ped.Accuracy = loadout.Accuracy;
            ped.Weapons.Give(loadout.Weapon, loadout.Ammo, true, true);

            // He is here to fight, not to be dragged off by whatever the ambient
            // world throws at him.
            ped.CanSwitchWeapons = true;
            ped.CanWrithe = false;

            _muscle = ped;
            _muscleEnforcerId = enforcer.Id;

            if (Config.Current.ShowMuscleBlip)
            {
                _muscleBlip = ped.AddBlip();
                if (_muscleBlip != null && _muscleBlip.Exists())
                {
                    _muscleBlip.Color = Config.Current.MuscleBlipColour;
                    _muscleBlip.Scale = 0.7f;
                    _muscleBlip.Name = enforcer.Name;
                }
            }

            OrderFight();

            Notify.Show(
                $"~b~{enforcer.Name} is here.~s~ {Armoury.NameOf(enforcer.WeaponId)}, " +
                "and he is not waiting for you.");
        }

        /// <summary>
        /// Re-issues the fight order periodically. A ped given one combat task
        /// goes idle the moment its target dies, which left muscle standing in the
        /// open halfway through a turf battle.
        /// </summary>
        private void UpdateMuscle()
        {
            if (_muscle == null || !_muscle.Exists()) return;
            if (_muscle.IsDead || !_muscle.IsAlive) return;

            int now = Game.GameTime;
            if (now - _lastFightOrderAt < 3000) return;

            if (!_muscle.IsInCombat) OrderFight();
            _lastFightOrderAt = now;
        }

        private void OrderFight()
        {
            if (_muscle == null || !_muscle.Exists()) return;
            _muscle.Task.CombatHatedTargetsAroundPed(
                Config.Current.MuscleFightRadius, TaskCombatFlags.None);
            _lastFightOrderAt = Game.GameTime;
        }

        /// <summary>
        /// Settles what the fight cost him, then releases the ped. Losing muscle
        /// does not end the contract — he is laid up and still drawing his wage,
        /// which is the whole reason arming him properly is worth the money.
        /// </summary>
        private void ResolveMuscle()
        {
            if (_muscleBlip != null && _muscleBlip.Exists())
            {
                _muscleBlip.Delete();
                _muscleBlip = null;
            }

            if (_muscle == null) return;

            // Deliberately not "the ped is gone". A handle that no longer resolves
            // means the engine cleaned it up, which is not the same as him being
            // carried out — and charging the player two days of wages for a
            // streaming quirk is the kind of thing that reads as a broken mod.
            bool exists = _muscle.Exists();
            bool wentDown = exists && (_muscle.IsDead || !_muscle.IsAlive);

            var enforcer = _muscleEnforcerId >= 0
                ? GameState.Current.Enforcers.FirstOrDefault(e => e.Id == _muscleEnforcerId)
                : null;

            if (wentDown)
            {
                if (enforcer != null)
                {
                    int days = Config.Current.MuscleInjuryDays;
                    enforcer.InjuredUntilDay = GameState.AbsoluteDay() + days;
                    enforcer.TimesHurt++;
                    enforcer.Clamp();

                    Notify.Show(
                        $"~r~{enforcer.Name} got carried out of that.~s~ " +
                        $"Off the corner for {days} day(s), still on the wage.", true);

                    Persistence.SaveManager.Log(
                        $"{enforcer.Name} injured in an incident; out until day {enforcer.InjuredUntilDay}.");
                }

                // Leave the body; deleting it mid-scene looks wrong. Releasing
                // ownership lets the engine clear it normally.
                _muscle.MarkAsNoLongerNeeded();
            }
            else
            {
                // He walked away from it. Turning out is the job.
                if (exists && enforcer != null) enforcer.Handled++;

                if (exists)
                {
                    Ped alive = _muscle;
                    ReleasePed(ref alive);
                }
            }

            _muscle = null;
            _muscleEnforcerId = -1;
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
            if (!model.IsLoaded) { model.MarkAsNoLongerNeeded(); return null; }

            Ped ped = SafeGround.CreatePed(model, SafeSpot(near.Around(scatter)), heading);
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
            // The old fallback returned the RAW point when nothing validated —
            // which, beside a venue with interior meshes loaded, spawned the
            // antagonists INSIDE the building the player was meant to defend.
            // SafeGround prefers the sidewalk over the void, and rejects
            // interior floors and low roofs by layer, not just height.
            return SafeGround.Fix(wanted);
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

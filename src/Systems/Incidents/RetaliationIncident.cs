using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems.Incidents
{
    /// <summary>
    /// The crew you took her from turns up.
    ///
    /// Anchored to where the recruitment happened rather than to her post: a
    /// worker signed seconds ago has no post yet, and "they came to find you"
    /// lands harder than a blip across the map anyway. Worker-scoped all the
    /// same, because losing costs you her.
    /// </summary>
    public class RetaliationIncident : Incident
    {
        private const float SpawnRange = 120f;
        private const int DurationMs = 120000;
        private const float HoldRadius = 90f;

        private readonly Vector3 _anchor;
        private readonly string _rivalId;
        private readonly List<Ped> _crew = new List<Ped>();
        private bool _spawned;

        public RetaliationIncident(Vector3 anchor, string rivalId, int workerId, SpawnManager spawner)
            : base(workerId, spawner, DurationMs)
        {
            _anchor = anchor;
            _rivalId = rivalId;
        }

        private RivalCrew Rival => GameState.Current.GetRival(_rivalId);

        public override string Title => "They came for her";
        protected override string Objective =>
            $"See off {Rival?.Name} before they take {Worker?.Name} back";
        protected override float ResolveDistance => 60f;
        protected override BlipSprite Sprite => BlipSprite.Deathmatch;
        protected override BlipColor Colour => BlipColor.Red;

        protected override Vector3? TargetPosition => _anchor;

        protected override int ReputationReward => 18;
        protected override int ReputationPenalty => 22;

        protected override string BlipLabel => $"{Title}: {Rival?.Name}";

        protected override string StartMessage =>
            $"{Rival?.Name} want {Worker?.Name} back.";

        // ------------------------------------------------------------------

        protected override void OnStart(WorkerData worker) { }

        protected override void OnUpdate(WorkerData worker, WorkerRuntime runtime)
        {
            var rival = Rival;
            if (rival == null)
            {
                Fail(worker, "That went quiet.");
                return;
            }

            if (!_spawned)
            {
                if (PlayerDistance() > SpawnRange) return;
                Spawn(rival);
                return;
            }

            if (!_crew.Any(p => StillContesting(p, _anchor, HoldRadius)))
                Succeed(worker, $"{rival.Name} got the message. {worker?.Name} saw you handle it.");
        }

        private void Spawn(RivalCrew rival)
        {
            var hostile = Factions.Hostile(Spawner.Crew);
            var player = Game.Player.Character;

            // Smaller than a turf battle — this is a message, not a war.
            int count = 2 + (int)(rival.Strength / 40f);
            if (count > 4) count = 4;

            for (int i = 0; i < count; i++)
            {
                Ped ped = SpawnAntagonist(rival.PedModel, _anchor, 0f, 7f);
                if (ped == null) continue;

                ped.RelationshipGroup = hostile;
                ped.Armor = 15;
                ped.Accuracy = 25;
                ped.Weapons.Give(WeaponHash.Pistol, 150, true, true);
                ped.Task.Combat(player);

                _crew.Add(ped);
            }

            if (_crew.Count == 0) return;   // models not resident; retry next tick

            _spawned = true;
            Notify.Show($"~r~{rival.Name}~s~ — {_crew.Count} of them, and they are not talking.");
        }

        // ------------------------------------------------------------------

        protected override void OnSucceed(WorkerData worker)
        {
            var rival = Rival;
            if (rival != null)
            {
                rival.Strength -= 10f;
                rival.Aggression -= 0.05f;   // they think twice
                rival.Clamp();
            }

            if (worker != null) worker.Loyalty += 15f;
        }

        protected override void OnFail(WorkerData worker)
        {
            var rival = Rival;
            if (rival != null)
            {
                rival.Strength += 8f;
                rival.Clamp();
            }

            if (worker == null) return;

            Spawner.Despawn(worker.Id);
            GameState.Current.Roster.Remove(worker);
            Notify.Show($"~r~They took {worker.Name} back.~s~ You weren't there.");
        }

        public override void Cleanup()
        {
            foreach (Ped ped in _crew)
            {
                if (ped == null || !ped.Exists()) continue;

                if (ped.IsDead)
                {
                    ped.MarkAsNoLongerNeeded();
                }
                else
                {
                    Ped alive = ped;
                    ReleasePed(ref alive);
                }
            }
            _crew.Clear();

            base.Cleanup();
        }
    }
}

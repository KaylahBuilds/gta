using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems.Incidents
{
    /// <summary>
    /// The people you owe, at the door.
    ///
    /// Debt used to end in a notification — the operation collapsed and the player
    /// read about it. This is the step before that: they come for it in person,
    /// repeatedly, while the number is still climbing.
    ///
    /// Anchored where the player is standing rather than at a corner. Everything
    /// else in the mod happens somewhere you have to drive to; this happens to
    /// you, which is the point of it.
    /// </summary>
    public class CollectorsIncident : Incident
    {
        private const string CollectorModel = "g_m_m_armboss_01";
        private const string BackupModel = "g_m_y_mexgoon_02";
        private const int DurationMs = 100000;
        private const float HoldRadius = 70f;

        private readonly Vector3 _anchor;
        private readonly List<Ped> _crew = new List<Ped>();
        private bool _spawned;

        public CollectorsIncident(Vector3 anchor, SpawnManager spawner)
            : base(NoWorker, spawner, DurationMs)
        {
            _anchor = anchor;
        }

        public override string Title => "They came for it";

        protected override string Objective =>
            $"See them off — ${GameState.Current.Debt:N0} owed";

        protected override float ResolveDistance => 60f;
        protected override BlipSprite Sprite => BlipSprite.Deathmatch;
        protected override BlipColor Colour => BlipColor.Red;

        protected override Vector3? TargetPosition => _anchor;

        protected override int ReputationReward => 15;
        protected override int ReputationPenalty => 30;

        protected override string BlipLabel => "Collectors";

        protected override string StartMessage =>
            $"The people you owe ${GameState.Current.Debt:N0} are here, and they are not asking.";

        // ------------------------------------------------------------------

        protected override void OnStart(WorkerData worker) { }

        protected override void OnUpdate(WorkerData worker, WorkerRuntime runtime)
        {
            if (!_spawned)
            {
                // They are already on top of the player, so there is no distance
                // check to wait on — spawn immediately.
                Spawn();
                return;
            }

            if (!_crew.Any(p => StillContesting(p, _anchor, HoldRadius)))
                Succeed(null, "They left with nothing.");
        }

        private void Spawn()
        {
            var hostile = Factions.Hostile(Spawner.Crew);
            var player = Game.Player.Character;
            Vector3 near = player.Position;

            // Scales with the hole you are in.
            var state = GameState.Current;
            float depth = state.Debt / (float)Config.Current.DebtCollapseThreshold;

            int count = 2 + (int)(depth * 3f);
            if (count > 5) count = 5;

            for (int i = 0; i < count; i++)
            {
                Ped ped = SpawnAntagonist(
                    i == 0 ? CollectorModel : BackupModel, near, player.Heading + 180f, 8f);

                if (ped == null) continue;

                ped.RelationshipGroup = hostile;
                ped.Armor = 40;
                ped.Accuracy = 35;
                ped.Weapons.Give(WeaponHash.Pistol, 250, true, true);
                ped.Task.Combat(player);

                _crew.Add(ped);
            }

            if (_crew.Count == 0) return;   // models not resident; retry next tick

            _spawned = true;
            Notify.Show($"~r~{_crew.Count} of them, and they know what you look like.");
        }

        // ------------------------------------------------------------------

        protected override void OnSucceed(WorkerData worker)
        {
            Money.CollectorsSeenOff();
        }

        protected override void OnFail(WorkerData worker)
        {
            Money.CollectorsTakePayment(Spawner);
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

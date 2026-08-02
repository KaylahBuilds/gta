using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems.Incidents
{
    /// <summary>
    /// A fight over a corner. Zone-scoped rather than worker-scoped — there may
    /// be nobody of yours posted there at all.
    ///
    /// The same class covers both directions. Attacking and defending differ only
    /// in what happens at the end, so splitting them into two classes would
    /// duplicate the spawn and combat handling for no benefit.
    /// </summary>
    public class TurfBattleIncident : Incident
    {
        private const float SpawnRange = 130f;
        private const int AttackDurationMs = 240000;
        private const int DefendDurationMs = 180000;

        private readonly string _zoneId;
        private readonly string _rivalId;
        private readonly bool _attacking;

        private readonly List<Ped> _enforcers = new List<Ped>();
        private bool _spawned;

        public TurfBattleIncident(string zoneId, string rivalId, bool attacking, SpawnManager spawner)
            : base(NoWorker, spawner, attacking ? AttackDurationMs : DefendDurationMs)
        {
            _zoneId = zoneId;
            _rivalId = rivalId;
            _attacking = attacking;
        }

        private ZoneDef Zone => Zones.Get(_zoneId);
        private RivalCrew Rival => GameState.Current.GetRival(_rivalId);

        public override string Title => _attacking ? "Taking the corner" : "Turf under attack";

        // Named, because the subtitle is often the only thing the player reads —
        // the blip is easy to lose among other mods' notifications, and "defend
        // your corner" does not tell you which one or whether it is worth the
        // drive.
        protected override string Objective => _attacking
            ? $"Take {Zone?.Display} off {Rival?.Name}"
            : $"Defend {Zone?.Display}";

        protected override float ResolveDistance => 60f;
        protected override BlipSprite Sprite => BlipSprite.Deathmatch;
        protected override BlipColor Colour => _attacking ? BlipColor.Red : BlipColor.Yellow;

        protected override Vector3? TargetPosition => Zone?.Anchor;

        protected override int ReputationReward => _attacking ? 30 : 22;
        protected override int ReputationPenalty => _attacking ? 8 : 28;

        protected override string BlipLabel => $"{Title}: {Zone?.Display}";

        protected override string StartMessage => _attacking
            ? $"{Rival?.Name} hold {Zone?.Display}. Go take it."
            : $"{Rival?.Name} are moving on {Zone?.Display}.";

        // ------------------------------------------------------------------

        protected override void OnStart(WorkerData worker)
        {
            // Enforcers are deferred until the player is close enough that the
            // area is actually streamed in.
        }

        protected override void OnUpdate(WorkerData worker, WorkerRuntime runtime)
        {
            var zone = Zone;
            var rival = Rival;
            if (zone == null || rival == null)
            {
                Fail(null, "That contest fell apart.");
                return;
            }

            if (!_spawned)
            {
                if (PlayerDistance() > SpawnRange) return;
                SpawnEnforcers(zone, rival);
                return;
            }

            bool cleared = _enforcers.All(p => p == null || !p.Exists() || p.IsDead);
            if (cleared)
            {
                Succeed(null, _attacking
                    ? $"{zone.Display} is yours."
                    : $"You held {zone.Display}.");
            }
        }

        private void SpawnEnforcers(ZoneDef zone, RivalCrew rival)
        {
            var hostile = Factions.Hostile(Spawner.Crew);
            var player = Game.Player.Character;

            for (int i = 0; i < rival.EnforcerCount; i++)
            {
                Ped ped = SpawnAntagonist(rival.PedModel, zone.Anchor, zone.Heading, 9f);
                if (ped == null) continue;

                ped.RelationshipGroup = hostile;
                ped.Armor = 25;
                ped.Accuracy = 30;
                ped.Weapons.Give(WeaponHash.Pistol, 250, true, true);
                ped.Task.Combat(player);

                _enforcers.Add(ped);
            }

            // Every model request failed — retry on the next tick rather than
            // handing the player a free win.
            if (_enforcers.Count == 0) return;

            _spawned = true;
            Notify.Show($"~r~{rival.Name}~s~ — {_enforcers.Count} of them.");
        }

        // ------------------------------------------------------------------

        protected override void OnSucceed(WorkerData worker)
        {
            var rival = Rival;
            var state = GameState.Current;

            if (_attacking)
            {
                state.SetOwner(_zoneId, Ownership.Player);
                if (rival != null) rival.Strength -= 25f;
            }
            else
            {
                // Zone stays yours; they just lose people.
                if (rival != null) rival.Strength -= 15f;
            }

            rival?.Clamp();

            if (rival != null && rival.IsBroken)
                Notify.Show($"~g~{rival.Name} are finished.~s~ They won't be back.");
        }

        protected override void OnFail(WorkerData worker)
        {
            var rival = Rival;
            var state = GameState.Current;

            if (_attacking)
            {
                // Nothing lost but the attempt — they dig in a little.
                if (rival != null) rival.Strength += 5f;
                Notify.Show($"~r~{Zone?.Display} stays with {rival?.Name}.");
            }
            else
            {
                state.SetOwner(_zoneId, _rivalId);
                state.ClearZone(_zoneId);
                if (rival != null) rival.Strength += 10f;
                Notify.Show(
                    $"~r~You lost {Zone?.Display}.~s~ Everyone posted there is off the street.");
            }

            rival?.Clamp();
        }

        public override void Cleanup()
        {
            foreach (Ped ped in _enforcers)
            {
                if (ped == null || !ped.Exists()) continue;

                if (ped.IsDead)
                {
                    // Leave the bodies — deleting them mid-firefight looks wrong.
                    // Releasing ownership lets the engine clear them normally.
                    ped.MarkAsNoLongerNeeded();
                }
                else
                {
                    Ped alive = ped;
                    ReleasePed(ref alive);
                }
            }
            _enforcers.Clear();

            base.Cleanup();
        }
    }
}

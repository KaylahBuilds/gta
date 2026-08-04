using GTA;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems.Incidents
{
    /// <summary>
    /// A client turns on one of your crew. Get there and deal with him before the
    /// timer runs out.
    ///
    /// This is the bread-and-butter incident: frequent, low stakes, and the main
    /// reason showing up matters at all.
    /// </summary>
    public class BadClientIncident : Incident
    {
        private const string ClientModel = "g_m_y_lost_01";
        private const int DurationMs = 90000;

        private Ped _client;
        private bool _engaged;

        public BadClientIncident(int workerId, SpawnManager spawner)
            : base(workerId, spawner, DurationMs) { }

        public override string Title => "Client trouble";
        protected override string Objective => $"Get to {Worker?.Name} and handle the client";
        protected override BlipSprite Sprite => BlipSprite.Deathmatch;

        protected override void OnStart(WorkerData worker)
        {
            // Antagonist creation is deferred until the worker streams in — the
            // player may be across the map when this fires.
        }

        protected override void OnUpdate(WorkerData worker, WorkerRuntime runtime)
        {
            if (runtime == null) return;

            if (_client == null)
            {
                _client = SpawnAntagonist(ClientModel, runtime.Ped.Position, runtime.Ped.Heading + 180f);
                if (_client == null) return;

                _client.RelationshipGroup = Factions.Hostile(Spawner.Crew);
                _client.Task.Combat(runtime.Ped);
                _engaged = true;

                // Muscle covering this corner deals with him whether you make it
                // or not — this is the errand they were hired to take off you.
                TrySpawnMuscle(worker.ZoneId, runtime.Ped.Position, runtime.Ped.Heading);
                return;
            }

            if (!_client.Exists()) return;

            // Worker down: nothing left to save.
            if (!runtime.IsValid)
            {
                Fail(worker, $"{worker.Name} was hurt badly. You were too slow.");
                return;
            }

            bool clientDown = _client.IsDead || !_client.IsAlive;
            bool clientFled = _client.Position.DistanceTo(runtime.Ped.Position) > 45f;

            if (_engaged && (clientDown || clientFled) && PlayerHasArrived())
                Succeed(worker, $"{worker.Name} is fine. Word gets around that you show up.");
        }

        protected override void OnSucceed(WorkerData worker)
        {
            worker.Loyalty += 12f;

            int compensation = 150 * worker.Tier;
            Game.Player.Money += compensation;
            worker.LifetimeEarnings += compensation;
            GameState.Current.LifetimeTake += compensation;
        }

        protected override void OnFail(WorkerData worker)
        {
            worker.Loyalty -= 22f;
            worker.Stamina -= 35f;
            worker.ZoneId = null;   // pulled off the street to recover
        }

        public override void Cleanup()
        {
            ReleasePed(ref _client);
            base.Cleanup();
        }
    }
}

using GTA;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems.Incidents
{
    /// <summary>
    /// Vice has someone working the corner. No combat solution — you either get
    /// there and pull your crew member off the post in time, or you pay bail.
    ///
    /// Probability scales with zone heat, which makes heat management the real
    /// mechanic rather than a decorative number.
    /// </summary>
    public class ViceStingIncident : Incident
    {
        private const string UndercoverModel = "a_m_y_business_01";
        private const int DurationMs = 75000;
        private const int BailCost = 1500;

        private Ped _undercover;

        public ViceStingIncident(int workerId, SpawnManager spawner)
            : base(workerId, spawner, DurationMs) { }

        public override string Title => "Vice sting";
        protected override string Objective => $"Get {Worker?.Name} off the post before the bust";
        protected override float ResolveDistance => 18f;
        protected override BlipSprite Sprite => BlipSprite.PoliceStation;
        protected override BlipColor Colour => BlipColor.Blue;
        protected override int ReputationReward => 12;
        protected override int ReputationPenalty => 14;

        protected override void OnStart(WorkerData worker) { }

        protected override void OnUpdate(WorkerData worker, WorkerRuntime runtime)
        {
            if (runtime != null && _undercover == null)
            {
                _undercover = SpawnAntagonist(
                    UndercoverModel, runtime.Ped.Position, runtime.Ped.Heading + 180f);

                // Deliberately not hostile: he is meant to read as a customer
                // right up until the timer expires.
                _undercover?.Task.StartScenarioInPlace("WORLD_HUMAN_STAND_MOBILE", 0, true);
            }

            if (PlayerHasArrived())
                Succeed(worker, $"{worker.Name} is clear. That was close.");
        }

        protected override void OnSucceed(WorkerData worker)
        {
            GameState.Current.AddHeat(worker.ZoneId, -0.12f);
            worker.Loyalty += 8f;
            worker.ZoneId = null;   // off the post; reassign when heat settles

            // Force a re-stream so the ped is not left standing next to a cop.
            Spawner.Despawn(WorkerId);
        }

        protected override void OnFail(WorkerData worker)
        {
            var state = GameState.Current;
            string zoneId = worker.ZoneId;
            bool bailFund = state.HasUpgrade(UpgradeCatalog.BailFund);

            worker.Loyalty -= 15f;
            state.AddHeat(zoneId, 0.25f);

            // A bail fund buys her out the same night, so she keeps her post
            // instead of the corner standing empty until you reassign her.
            if (!bailFund) worker.ZoneId = null;

            if (bailFund)
            {
                Notify.Show(
                    $"~o~{worker.Name} got picked up.~s~ The fund covered it — she's back " +
                    "on the corner tonight.");
            }
            else
            {
                int owed = state.HasUpgrade(UpgradeCatalog.Retainer) ? BailCost / 2 : BailCost;

                // Routed through Charge so a bail you cannot afford becomes debt
                // rather than quietly costing nothing.
                Notify.Show($"~r~{worker.Name} got picked up.~s~ The corner is hot.");
                EconomyTick.Charge(owed, "Bail");
            }

            Spawner.Despawn(WorkerId);
        }

        public override void Cleanup()
        {
            ReleasePed(ref _undercover);
            base.Cleanup();
        }
    }
}

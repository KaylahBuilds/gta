using GTA;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems.Incidents
{
    /// <summary>
    /// A worker whose loyalty has bottomed out starts walking. Reach them with
    /// the retention money in hand or they are off the roster permanently.
    ///
    /// This is the consequence that makes the stamina/loyalty economy bite: it
    /// only ever fires because of how the player has been running the operation.
    /// </summary>
    public class WalkOffIncident : Incident
    {
        private const int DurationMs = 120000;

        private bool _walking;

        public WalkOffIncident(int workerId, SpawnManager spawner)
            : base(workerId, spawner, DurationMs) { }

        public override string Title => "Walking off";
        protected override string Objective => $"Reach {Worker?.Name} and settle it";
        protected override float ResolveDistance => 8f;
        protected override BlipSprite Sprite => BlipSprite.Friend;
        protected override BlipColor Colour => BlipColor.Yellow;

        private int RetentionCost => 400 * (Worker?.Tier ?? 1);

        protected override void OnStart(WorkerData worker)
        {
            Notify.Show(
                $"~o~{worker.Name} is done.~s~ Settling it costs ~g~${RetentionCost}~s~.");
        }

        protected override void OnUpdate(WorkerData worker, WorkerRuntime runtime)
        {
            // Once streamed in, they stop working and start leaving.
            if (runtime != null && !_walking)
            {
                runtime.Ped.Task.ClearAll();
                // Drifting within a radius of the post, rather than the parameterless
                // WanderAround() which SHVDN deprecated.
                runtime.Ped.Task.WanderAround(runtime.Ped.Position, 50f);
                _walking = true;
            }

            if (!PlayerHasArrived()) return;

            if (Game.Player.Money < RetentionCost)
            {
                GTA.UI.Screen.ShowSubtitle(
                    $"~r~You need ${RetentionCost} to keep {worker.Name}", 200);
                return;
            }

            Succeed(worker, $"{worker.Name} is staying. For now.");
        }

        protected override void OnSucceed(WorkerData worker)
        {
            Game.Player.Money -= RetentionCost;
            worker.Loyalty += 30f;
            worker.Stamina += 20f;

            // Re-stream so they drop the wander task and go back on post.
            Spawner.Despawn(WorkerId);
        }

        protected override void OnFail(WorkerData worker)
        {
            Spawner.Despawn(WorkerId);
            GameState.Current.Roster.Remove(worker);
            Notify.Show($"~r~{worker.Name} is gone.~s~ That one is on you.");
        }
    }
}

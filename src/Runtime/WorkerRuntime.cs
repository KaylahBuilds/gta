using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// The in-world representation of a <see cref="WorkerData"/>. Created when the
    /// player comes near, destroyed when they leave. Holds no state that matters —
    /// everything meaningful lives on the WorkerData record.
    /// </summary>
    public class WorkerRuntime
    {
        public int WorkerId { get; }
        public Ped Ped { get; private set; }
        public Blip Blip { get; private set; }

        private WorkerRuntime(int workerId, Ped ped, Blip blip)
        {
            WorkerId = workerId;
            Ped = ped;
            Blip = blip;
        }

        public bool IsValid => Ped != null && Ped.Exists() && !Ped.IsDead;

        /// <summary>
        /// Returns null if the model is not resident yet; the caller simply retries
        /// on the next scan rather than blocking the tick on a load.
        /// </summary>
        public static WorkerRuntime Create(WorkerData data, Vector3 position, float heading,
                                           RelationshipGroup crew)
        {
            // Recruited from arbitrary world peds, so the model is only ever known
            // by hash.
            var model = new Model(data.ModelHash);
            if (!model.IsInCdImage || !model.IsValid) return null;

            model.Request(0);
            if (!model.IsLoaded) { model.MarkAsNoLongerNeeded(); return null; }

            // No sidewalk NODE snap here on purpose (four posts would heap on
            // one node) — but every post is still LAYER-validated: a spread
            // that pushes a girl through a venue wall gets corrected to the
            // nearest reachable ground instead of standing her inside the
            // building where nobody can defend her.
            position = SafeGround.Fix(position);

            if (Config.Current.LogSpawnDiagnostics)
            {
                Persistence.SaveManager.Log(
                    $"SPAWN {data.Name} zone={data.ZoneId} " +
                    $"at=({position.X:0.0},{position.Y:0.0},{position.Z:0.0})");
            }

            Ped ped = SafeGround.CreatePed(model, position, heading);
            model.MarkAsNoLongerNeeded();

            if (ped == null || !ped.Exists()) return null;

            // Owned by the script: exempt from population culling.
            ped.IsPersistent = true;
            // Stop them reacting to traffic, gunfire and pedestrians by wandering off.
            ped.BlockPermanentEvents = true;
            ped.CanBeTargetted = true;
            ped.RelationshipGroup = crew;

            ped.Task.StartScenarioInPlace(ScenarioFor(data), 0, true);

            Blip blip = ped.AddBlip();
            blip.Sprite = BlipSprite.Friend;
            blip.Color = Config.Current.WorkerBlipColour;
            blip.Scale = 0.7f;
            blip.IsShortRange = true;
            blip.Name = $"{data.Name} ({Zones.Get(data.ZoneId)?.Display ?? "Unassigned"})";

            return new WorkerRuntime(data.Id, ped, blip);
        }

        private static string ScenarioFor(WorkerData data)
        {
            return data.Tier >= 2
                ? "WORLD_HUMAN_PROSTITUTE_HIGH_CLASS"
                : "WORLD_HUMAN_PROSTITUTE_LOW_CLASS";
        }

        public void Destroy()
        {
            if (Blip != null && Blip.Exists())
            {
                Blip.Delete();
                Blip = null;
            }

            if (Ped != null && Ped.Exists())
            {
                Ped.MarkAsNoLongerNeeded();
                Ped.Delete();
            }
            Ped = null;
        }
    }
}

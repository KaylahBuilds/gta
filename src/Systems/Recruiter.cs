using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using OnTheBlade.Core;
using OnTheBlade.Runtime;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Turns a nearby world ped into a roster record.
    ///
    /// Eligibility is a curated model list (<see cref="Config.RecruitModels"/>),
    /// matched by hash so no hash-to-name lookup is ever needed. Keeping the pool
    /// deliberate is what makes the hotspots in <see cref="RecruitAreas"/> matter:
    /// eligible peds are uncommon enough that where you look is a real decision.
    ///
    /// Recruitment is a straight contract offer. Tier is rolled from the area;
    /// loyalty then rises or falls purely on how the business is run.
    /// </summary>
    /// <summary>What a recruitment turned up, so the caller can react to a claim.</summary>
    public class RecruitResult
    {
        public WorkerData Worker;

        /// <summary>Crew she already worked for, or null if she was unattached.</summary>
        public RivalCrew ClaimedFrom;

        /// <summary>True if her old crew is on the way right now.</summary>
        public bool Retaliation;

        /// <summary>Where the deal was struck — the retaliation anchors here.</summary>
        public Vector3 Where;
    }

    public static class Recruiter
    {
        private static readonly string[] Names =
        {
            "Rae", "Nadia", "Cricket", "Simone", "Dez", "Marisol", "Kiki", "Jolene",
            "Priya", "Tam", "Odette", "Bex", "Sunny", "Coral", "Vera", "Mika",
            "Lark", "Noor", "Trish", "Esme", "Junie", "Wren", "Calla", "Shay"
        };

        private static readonly Random Rng = new Random();

        /// <summary>Model hashes from the configured list, rebuilt if the config changes.</summary>
        private static HashSet<int> _candidateHashes;
        private static string[] _candidateSource;

        private static HashSet<int> CandidateHashes()
        {
            string[] models = Config.Current.RecruitModels;
            if (_candidateHashes != null && ReferenceEquals(_candidateSource, models))
                return _candidateHashes;

            _candidateSource = models;
            _candidateHashes = new HashSet<int>(models.Select(m => new Model(m).Hash));
            return _candidateHashes;
        }

        /// <summary>
        /// The model list is the eligibility filter. The remaining checks are
        /// practical rather than editorial: peds in vehicles (deleting one strands
        /// the car), peds another script or mission already owns, and crew already
        /// on the books.
        /// </summary>
        public static bool IsEligible(Ped ped, HashSet<int> ownHandles)
        {
            if (ped == null || !ped.Exists()) return false;
            if (ped.IsPlayer || !ped.IsAlive || !ped.IsHuman) return false;
            if (!CandidateHashes().Contains(ped.Model.Hash)) return false;
            if (ped.IsInVehicle()) return false;
            if (ped.IsPersistent) return false;
            if (ownHandles != null && ownHandles.Contains(ped.Handle)) return false;
            return true;
        }

        private static HashSet<int> OwnHandles(SpawnManager spawner)
        {
            var handles = new HashSet<int>();
            if (spawner == null) return handles;

            foreach (var runtime in spawner.Live.Values)
                if (runtime.IsValid) handles.Add(runtime.Ped.Handle);

            return handles;
        }

        /// <summary>Nearest eligible prospects, closest first. The radius widens in a hotspot.</summary>
        public static List<Ped> FindProspects(SpawnManager spawner, int max)
        {
            var player = Game.Player.Character;
            if (player == null || !player.Exists()) return new List<Ped>();

            RecruitArea area = RecruitAreas.At(player.Position);
            float radius = RecruitAreas.SearchRadius(area);
            var own = OwnHandles(spawner);

            return World.GetNearbyPeds(player, radius)
                .Where(p => IsEligible(p, own))
                .OrderBy(p => p.Position.DistanceTo(player.Position))
                .Take(max)
                .ToList();
        }

        public static int CountProspects(SpawnManager spawner)
        {
            return FindProspects(spawner, 64).Count;
        }

        /// <summary>
        /// Returns the new record, or null if nobody suitable was nearby or the
        /// book is full. The source ped is removed — SpawnManager re-creates them
        /// at their post from the stored model hash.
        /// </summary>
        public static RecruitResult TryRecruitNearest(SpawnManager spawner)
        {
            if (GameState.Current.RosterFull) return null;

            Ped prospect = FindProspects(spawner, 1).FirstOrDefault();
            if (prospect == null) return null;

            Vector3 where = Game.Player.Character.Position;
            RecruitArea area = RecruitAreas.At(where);
            int hash = prospect.Model.Hash;

            var worker = GameState.Current.AddWorker(
                PickName(), hash, RecruitAreas.RollTier(area, Rng));

            // Recoverable now that the pool is a known list — useful in the log
            // and when hand-editing a save. The hash stays authoritative.
            worker.ModelName = Config.Current.RecruitModels
                .FirstOrDefault(m => new Model(m).Hash == hash);

            worker.TraitSet = Traits.Roll(Rng);
            worker.Loyalty = 45f + (float)Rng.NextDouble() * 25f;
            worker.Clamp();

            prospect.MarkAsNoLongerNeeded();
            prospect.Delete();

            // Was she already working for somebody?
            var owner = Poaching.RollClaim(area, Rng);
            if (owner != null) Poaching.ApplyTension(worker, owner);

            return new RecruitResult
            {
                Worker = worker,
                ClaimedFrom = owner,
                Retaliation = owner != null && Poaching.RollRetaliation(owner, Rng),
                Where = where
            };
        }

        private static string PickName()
        {
            var taken = new HashSet<string>(GameState.Current.Roster.Select(w => w.Name));
            var free = Names.Where(n => !taken.Contains(n)).ToList();

            if (free.Count > 0) return free[Rng.Next(free.Count)];
            return Names[Rng.Next(Names.Length)] + " " + GameState.Current.NextWorkerId;
        }
    }
}

using System;
using System.Runtime.Serialization;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Hired muscle covering one region. Deliberately abstract — no ped, no
    /// pathing, no AI. An enforcer is a probability that a routine problem never
    /// reaches the player, which is the entire point of them: late game should
    /// stop being a queue of errands.
    ///
    /// They never cover the incidents that matter (stings, walk-offs, turf). If
    /// muscle could handle those, there would be no game left to play.
    /// </summary>
    [DataContract]
    public class EnforcerData
    {
        [DataMember] public int Id;
        [DataMember] public string Name;
        [DataMember] public string RegionId;

        /// <summary>0-100. Chance of resolving a covered incident without the player.</summary>
        [DataMember] public float Skill = 55f;

        [DataMember] public int DailyWage;
        [DataMember] public int Handled;

        public void Clamp()
        {
            if (Skill < 0f) Skill = 0f;
            if (Skill > 100f) Skill = 100f;
        }
    }

    public static class EnforcerCatalog
    {
        private static readonly string[] Names =
        {
            "Tavo", "Bishop", "Marlene", "Duke", "Cyrus", "Nita", "Ox", "Renata"
        };

        private static readonly Random Rng = new Random();

        public static EnforcerData Create(int id, string regionId, string[] takenNames)
        {
            string name = null;
            foreach (string candidate in Names)
            {
                if (Array.IndexOf(takenNames, candidate) >= 0) continue;
                name = candidate;
                break;
            }

            return new EnforcerData
            {
                Id = id,
                Name = name ?? $"Muscle {id}",
                RegionId = regionId,
                Skill = 45f + (float)Rng.NextDouble() * 30f,
                DailyWage = Config.Current.EnforcerDailyWage
            };
        }
    }
}

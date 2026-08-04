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

        /// <summary>Id from <see cref="Armoury"/>. Empty means bare hands.</summary>
        [DataMember] public string WeaponId = Armoury.Unarmed;

        /// <summary>
        /// Ped model he turns up as. Fixed at hire so the same man arrives every
        /// time rather than a different face at each incident.
        /// </summary>
        [DataMember] public string ModelName;

        /// <summary>
        /// Absolute day he is fit again. Being carried off a corner does not end
        /// the contract — he is still on the payroll while he is laid up, which is
        /// precisely what makes losing a fight sting.
        /// </summary>
        [DataMember] public int InjuredUntilDay;

        /// <summary>Fights he has been carried out of.</summary>
        [DataMember] public int TimesHurt;

        [IgnoreDataMember]
        public bool IsArmed => !string.IsNullOrEmpty(WeaponId) && Armoury.Get(WeaponId) != null;

        [IgnoreDataMember]
        public LoadoutDef Loadout => Armoury.Get(WeaponId);

        public bool IsInjured() => GameState.AbsoluteDay() < InjuredUntilDay;

        public int InjuryDaysLeft()
        {
            int left = InjuredUntilDay - GameState.AbsoluteDay();
            return left < 0 ? 0 : left;
        }

        /// <summary>
        /// Skill as the rolls actually see it: what he is plus what he is holding,
        /// and nothing at all while he is laid up.
        /// </summary>
        [IgnoreDataMember]
        public float EffectiveSkill
        {
            get
            {
                if (IsInjured()) return 0f;

                float s = Skill + Armoury.MuscleOf(WeaponId);
                return s > 100f ? 100f : s;
            }
        }

        public void Clamp()
        {
            if (Skill < 0f) Skill = 0f;
            if (Skill > 100f) Skill = 100f;
            if (InjuredUntilDay < 0) InjuredUntilDay = 0;
            if (TimesHurt < 0) TimesHurt = 0;

            // A loadout id from a config that has since been edited, or a save
            // written by a newer build, must not leave him holding nothing while
            // the menu still shows he is armed.
            if (!string.IsNullOrEmpty(WeaponId) && Armoury.Get(WeaponId) == null)
                WeaponId = Armoury.Unarmed;
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

            var models = Config.Current.EnforcerModels;

            return new EnforcerData
            {
                Id = id,
                Name = name ?? $"Muscle {id}",
                RegionId = regionId,
                Skill = 45f + (float)Rng.NextDouble() * 30f,
                DailyWage = Config.Current.EnforcerDailyWage,
                WeaponId = Armoury.Unarmed,
                ModelName = models != null && models.Length > 0
                    ? models[Rng.Next(models.Length)]
                    : "g_m_y_famca_01"
            };
        }
    }
}

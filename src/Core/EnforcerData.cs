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

        /// <summary>See <see cref="MuscleBackground"/>. 0 is the neutral one.</summary>
        [DataMember] public int Background;

        /// <summary>
        /// Worker he is minding instead of a region. -1 for none.
        ///
        /// Mutually exclusive with covering ground: a man standing behind one
        /// person is not watching a neighbourhood, and that is the trade.
        /// </summary>
        [DataMember] public int GuardingWorkerId = -1;

        [IgnoreDataMember]
        public bool IsGuarding => GuardingWorkerId >= 0;

        [IgnoreDataMember]
        public BackgroundDef Profile => MuscleBackgrounds.Get(Background);

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

        /// <summary>
        /// Wage for what he actually is. Flat pay meant a good roll was strictly
        /// better than a bad one at identical cost, so hiring cheap and training
        /// him up was never a strategy — it was just worse.
        /// </summary>
        public int WageFor()
        {
            var cfg = Config.Current;
            float wage = cfg.EnforcerDailyWage
                         * (cfg.EnforcerWageSkillFloor + Skill / 100f)
                         * Profile.WageMultiplier;

            int rounded = (int)System.Math.Round(wage / 10f) * 10;
            return rounded < 50 ? 50 : rounded;
        }

        /// <summary>
        /// Records a problem dealt with, and improves him for it.
        ///
        /// <see cref="Handled"/> has been incrementing since the first build and
        /// feeding nothing at all — a counter shown in the menu that meant the man
        /// was busy, not that he was getting better.
        /// </summary>
        public void RecordHandled()
        {
            Handled++;

            var profile = Profile;
            if (profile.GrowthPerHandled <= 0f) return;
            if (Skill >= profile.SkillCap) return;

            Skill += profile.GrowthPerHandled;
            if (Skill > profile.SkillCap) Skill = profile.SkillCap;

            DailyWage = WageFor();
            Clamp();
        }

        public void Clamp()
        {
            if (Skill < 0f) Skill = 0f;
            if (Skill > 100f) Skill = 100f;
            if (InjuredUntilDay < 0) InjuredUntilDay = 0;
            if (TimesHurt < 0) TimesHurt = 0;
            if (Background < 0 || Background > (int)MuscleBackground.Veteran) Background = 0;
            if (GuardingWorkerId < -1) GuardingWorkerId = -1;

            // The sweep for "minding somebody who has left" used to live here.
            // It could not: Clamp runs from EnsureCollections, which runs from
            // [OnDeserialized] — before GameState.Current is assigned — so it
            // read the throwaway instance from the field initialiser, whose
            // Roster is empty. GetWorker returned null for every id, every guard
            // cleared unconditionally on every load, and the autosave wrote the
            // damage back two minutes later. It now lives in
            // GameState.ReleaseDanglingGuards, which runs against `this`.

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

        public static EnforcerData Create(int id, string regionId, string[] takenNames,
                                          MuscleBackground background = MuscleBackground.Journeyman)
        {
            string name = null;
            foreach (string candidate in Names)
            {
                if (Array.IndexOf(takenNames, candidate) >= 0) continue;
                name = candidate;
                break;
            }

            var models = Config.Current.EnforcerModels;
            var profile = MuscleBackgrounds.Get(background);

            var enforcer = new EnforcerData
            {
                Id = id,
                Name = name ?? $"Muscle {id}",
                RegionId = regionId,
                Background = (int)background,

                // Spread around where his background starts him, then held under
                // his own ceiling — a veteran cannot roll low and a local cannot
                // roll into being worth his keep.
                Skill = profile.StartSkill + (float)(Rng.NextDouble() * 12.0 - 6.0),

                WeaponId = Armoury.Unarmed,
                GuardingWorkerId = -1,
                ModelName = models != null && models.Length > 0
                    ? models[Rng.Next(models.Length)]
                    : "g_m_y_famca_01"
            };

            if (enforcer.Skill > profile.SkillCap) enforcer.Skill = profile.SkillCap;
            enforcer.DailyWage = enforcer.WageFor();
            enforcer.Clamp();

            return enforcer;
        }

        /// <summary>What it costs up front to take somebody of this background on.</summary>
        public static int HireCost(MuscleBackground background)
        {
            var profile = MuscleBackgrounds.Get(background);
            int cost = (int)System.Math.Round(
                Config.Current.EnforcerHireCost * profile.WageMultiplier / 500f) * 500;

            return cost < 2000 ? 2000 : cost;
        }
    }
}

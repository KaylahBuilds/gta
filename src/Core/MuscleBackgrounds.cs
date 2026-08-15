using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Where an enforcer came from before you.
    ///
    /// <see cref="Journeyman"/> is deliberately 0 so that everyone hired before
    /// backgrounds existed deserialises to the neutral one rather than silently
    /// acquiring somebody else's strengths and ceilings.
    /// </summary>
    public enum MuscleBackground
    {
        Journeyman = 0,
        ExCop = 1,
        LegBreaker = 2,
        Fixer = 3,
        Local = 4,
        Veteran = 5
    }

    public class BackgroundDef
    {
        public MuscleBackground Kind;
        public string Name;
        public string Blurb;

        /// <summary>Skill he is hired at, before the random spread.</summary>
        public float StartSkill;

        /// <summary>Highest skill he will ever reach through work.</summary>
        public float SkillCap;

        /// <summary>Skill gained per problem handled. Zero means he never improves.</summary>
        public float GrowthPerHandled;

        /// <summary>Multiplier on his daily wage.</summary>
        public float WageMultiplier;

        /// <summary>Multiplier on turning rivals away from turf he covers.</summary>
        public float DeterrenceMultiplier;

        /// <summary>Heat decay multiplier in his region. Above 1 means he cools the ground.</summary>
        public float HeatDecayMultiplier;

        /// <summary>Heat added each time he handles something himself.</summary>
        public float HeatPerHandled;

        /// <summary>Multiplier on bail, lawyer and sweep costs while he is on the payroll.</summary>
        public float LegalCostMultiplier;

        /// <summary>Multiplier on the recruit search radius in his region.</summary>
        public float RecruitRadiusMultiplier;
    }

    /// <summary>
    /// Muscle used to have one axis where workers have six traits, which made
    /// hiring a lottery you could neither influence nor recover from. A background
    /// gives each man a shape and, crucially, a ceiling — so a cheap bad roll is a
    /// project rather than a mistake, and an expensive one is not automatically
    /// the right answer.
    /// </summary>
    public static class MuscleBackgrounds
    {
        public static readonly List<BackgroundDef> All = new List<BackgroundDef>
        {
            new BackgroundDef
            {
                Kind = MuscleBackground.Journeyman, Name = "Journeyman",
                Blurb = "Nothing remarkable either way. Turns up, does the job, gets better at it.",
                StartSkill = 50f, SkillCap = 85f, GrowthPerHandled = 0.9f,
                WageMultiplier = 1.0f, DeterrenceMultiplier = 1.0f,
                HeatDecayMultiplier = 1.0f, HeatPerHandled = 0f,
                LegalCostMultiplier = 1.0f, RecruitRadiusMultiplier = 1.0f
            },
            new BackgroundDef
            {
                Kind = MuscleBackground.ExCop, Name = "Ex-job",
                Blurb = "He knows how a case gets built and how it falls apart. The ground he " +
                        "covers cools far faster — but he will not put hands on anybody.",
                StartSkill = 48f, SkillCap = 82f, GrowthPerHandled = 0.8f,
                WageMultiplier = 1.15f, DeterrenceMultiplier = 0.65f,
                HeatDecayMultiplier = 1.60f, HeatPerHandled = 0f,
                LegalCostMultiplier = 0.85f, RecruitRadiusMultiplier = 1.0f
            },
            new BackgroundDef
            {
                Kind = MuscleBackground.LegBreaker, Name = "Leg-breaker",
                Blurb = "Nobody argues with him twice. Nobody forgets him either, and the " +
                        "street gets warmer every time he settles something.",
                StartSkill = 58f, SkillCap = 92f, GrowthPerHandled = 1.1f,
                WageMultiplier = 1.25f, DeterrenceMultiplier = 1.40f,
                HeatDecayMultiplier = 0.85f, HeatPerHandled = 0.020f,
                LegalCostMultiplier = 1.0f, RecruitRadiusMultiplier = 1.0f
            },
            new BackgroundDef
            {
                Kind = MuscleBackground.Fixer, Name = "Fixer",
                Blurb = "Useless in a fight and worth every penny anyway. Bail, lawyers and " +
                        "having people checked all cost you far less while he is around.",
                StartSkill = 45f, SkillCap = 78f, GrowthPerHandled = 0.7f,
                WageMultiplier = 1.10f, DeterrenceMultiplier = 0.60f,
                HeatDecayMultiplier = 1.15f, HeatPerHandled = 0f,
                LegalCostMultiplier = 0.50f, RecruitRadiusMultiplier = 1.0f
            },
            new BackgroundDef
            {
                Kind = MuscleBackground.Local, Name = "Local",
                Blurb = "From these streets. Cheap, knows everybody worth knowing round here, " +
                        "and is never going to be more than useful.",
                StartSkill = 42f, SkillCap = 66f, GrowthPerHandled = 1.0f,
                WageMultiplier = 0.65f, DeterrenceMultiplier = 1.0f,
                HeatDecayMultiplier = 1.0f, HeatPerHandled = 0f,
                LegalCostMultiplier = 1.0f, RecruitRadiusMultiplier = 1.45f
            },
            new BackgroundDef
            {
                Kind = MuscleBackground.Veteran, Name = "Veteran",
                Blurb = "Already as good as he is going to get, which is very good. He costs " +
                        "what that is worth and he will not improve a single point.",
                StartSkill = 80f, SkillCap = 84f, GrowthPerHandled = 0.15f,
                WageMultiplier = 1.90f, DeterrenceMultiplier = 1.10f,
                HeatDecayMultiplier = 1.0f, HeatPerHandled = 0f,
                LegalCostMultiplier = 1.0f, RecruitRadiusMultiplier = 1.0f
            }
        };

        public static BackgroundDef Get(MuscleBackground kind)
        {
            return All.FirstOrDefault(b => b.Kind == kind) ?? All[0];
        }

        public static BackgroundDef Get(int kind) => Get((MuscleBackground)kind);

        /// <summary>
        /// Cheapest possible legal multiplier across everyone on the payroll. A
        /// fixer is a fixer whichever region he happens to cover — the discount is
        /// him making calls, not him standing somewhere.
        /// </summary>
        public static float LegalCostMultiplier()
        {
            float best = 1f;

            foreach (var e in GameState.Current.Enforcers)
            {
                if (e.IsInjured()) continue;

                float m = Get(e.Background).LegalCostMultiplier;
                if (m < best) best = m;
            }

            return best;
        }
    }
}

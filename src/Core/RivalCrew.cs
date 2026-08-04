using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OnTheBlade.Core
{
    /// <summary>Owner ids used in <see cref="GameState.ZoneOwners"/>.</summary>
    public static class Ownership
    {
        public const string Player = "player";

        /// <summary>Neutral is represented by a missing or empty entry.</summary>
        public static bool IsNeutral(string owner) => string.IsNullOrEmpty(owner);
    }

    /// <summary>
    /// A rival crew. Deliberately thin: strength and aggression are the only two
    /// numbers, because a rival that needs more state than that is a rival the
    /// player cannot reason about.
    /// </summary>
    [DataContract]
    public class RivalCrew
    {
        [DataMember] public string Id;
        [DataMember] public string Name;

        /// <summary>0-100. Drops when beaten; at zero the crew stops contesting.</summary>
        [DataMember] public float Strength = 60f;

        /// <summary>0-1. Scales how often they come after your corners.</summary>
        [DataMember] public float Aggression = 0.5f;

        [DataMember] public string PedModel;

        /// <summary>
        /// Absolute day open war started. 0 means no war.
        ///
        /// War is what aggression was always building toward and never reached —
        /// it topped out at 1 and simply meant "contests a bit more often". A crew
        /// at war comes constantly, fields more people, and will not sell you
        /// peace at the ordinary price.
        /// </summary>
        [DataMember] public int WarSinceDay;

        [IgnoreDataMember]
        public bool IsAtWar => WarSinceDay > 0 && !IsBroken;

        /// <summary>Absolute day a paid alliance runs out.</summary>
        [DataMember] public int AlliedUntilDay;

        [IgnoreDataMember]
        public bool IsBroken => Strength <= 0f;

        public bool IsAllied() =>
            !IsBroken && AlliedUntilDay > 0 && GameState.AbsoluteDay() < AlliedUntilDay;

        public int AllyDaysLeft()
        {
            int left = AlliedUntilDay - GameState.AbsoluteDay();
            return left < 0 ? 0 : left;
        }

        /// <summary>Enforcers they field in a turf battle: 2 at the floor, 5 at full strength.</summary>
        [IgnoreDataMember]
        public int EnforcerCount
        {
            get
            {
                int n = 2 + (int)(Strength / 30f);
                if (IsAtWar) n += Config.Current.WarExtraEnforcers;
                return n > 7 ? 7 : n;
            }
        }

        public void Clamp()
        {
            if (Strength < 0f) Strength = 0f;
            if (Strength > 100f) Strength = 100f;
            if (Aggression < 0f) Aggression = 0f;
            if (Aggression > 1f) Aggression = 1f;
            if (WarSinceDay < 0) WarSinceDay = 0;
            if (AlliedUntilDay < 0) AlliedUntilDay = 0;

            // A crew that has been beaten to nothing is not fighting a war and is
            // not anybody's ally either.
            if (IsBroken)
            {
                WarSinceDay = 0;
                AlliedUntilDay = 0;
            }
        }
    }

    public static class RivalCatalog
    {
        public static List<RivalCrew> Defaults() => new List<RivalCrew>
        {
            new RivalCrew
            {
                Id = "chamberlain", Name = "Chamberlain Set",
                Strength = 45f, Aggression = 0.35f, PedModel = "g_m_y_famdnf_01"
            },
            new RivalCrew
            {
                Id = "bahia", Name = "Bahia Vagos",
                Strength = 60f, Aggression = 0.50f, PedModel = "g_m_y_mexgoon_01"
            },
            new RivalCrew
            {
                Id = "outfit", Name = "The Del Perro Outfit",
                Strength = 70f, Aggression = 0.45f, PedModel = "g_m_y_pologoon_01"
            },
            new RivalCrew
            {
                Id = "lost", Name = "Lost MC",
                Strength = 85f, Aggression = 0.70f, PedModel = "g_m_y_lost_01"
            }
        };

        /// <summary>
        /// Opening board. The two tier-1 coastal zones start neutral so there is
        /// somewhere to operate before the player can fight anyone; everything
        /// above that has to be taken.
        /// </summary>
        public static Dictionary<string, string> DefaultOwnership() =>
            new Dictionary<string, string>
            {
                { "vespucci",   "" },
                { "sandy",      "" },
                { "strawberry", "chamberlain" },
                { "mirrorpark", "bahia" },
                { "delperro",   "outfit" },
                { "vinewood",   "lost" }
            };
    }
}

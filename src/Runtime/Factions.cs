using GTA;

namespace OnTheBlade.Runtime
{
    /// <summary>
    /// Relationship groups shared across the mod. Created once and reused —
    /// AddRelationshipGroup returns the existing group if the name is already
    /// registered, but the wiring only needs doing once per session.
    /// </summary>
    public static class Factions
    {
        private static RelationshipGroup _hostile;
        private static RelationshipGroup _allied;
        private static bool _ready;

        /// <summary>Antagonists: hostile to the crew, the player and his muscle.</summary>
        public static RelationshipGroup Hostile(RelationshipGroup crew)
        {
            Ensure(crew);
            return _hostile;
        }

        /// <summary>
        /// Hired muscle: on the player's side and a target for antagonists.
        ///
        /// A separate group from the crew because the two want opposite things
        /// from the AI — workers must never be dragged into a firefight, muscle
        /// exists to be in one.
        /// </summary>
        public static RelationshipGroup Allied(RelationshipGroup crew)
        {
            Ensure(crew);
            return _allied;
        }

        /// <summary>
        /// Both groups are wired together in one pass. Setting them up separately
        /// behind their own guards meant whichever was asked for first marked the
        /// work done and the other never got its relationships at all.
        /// </summary>
        private static void Ensure(RelationshipGroup crew)
        {
            if (_ready) return;

            _hostile = World.AddRelationshipGroup("OTB_HOSTILE");
            _allied = World.AddRelationshipGroup("OTB_ALLIED");

            var player = Game.Player.Character.RelationshipGroup;

            // Antagonists against everyone of yours.
            _hostile.SetRelationshipBetweenGroups(crew, Relationship.Hate, true);
            crew.SetRelationshipBetweenGroups(_hostile, Relationship.Hate, true);

            _hostile.SetRelationshipBetweenGroups(player, Relationship.Hate, true);
            player.SetRelationshipBetweenGroups(_hostile, Relationship.Hate, true);

            _hostile.SetRelationshipBetweenGroups(_allied, Relationship.Hate, true);
            _allied.SetRelationshipBetweenGroups(_hostile, Relationship.Hate, true);

            // Muscle stands with you and with the crew. Respect rather than Like
            // on the player side: Like makes allies crowd and follow, and a man
            // who trails you into every menu is worse than no man at all.
            _allied.SetRelationshipBetweenGroups(player, Relationship.Respect, true);
            player.SetRelationshipBetweenGroups(_allied, Relationship.Respect, true);

            _allied.SetRelationshipBetweenGroups(crew, Relationship.Respect, true);
            crew.SetRelationshipBetweenGroups(_allied, Relationship.Respect, true);

            _ready = true;
        }

        /// <summary>Called on script reload so groups are rewired on the next use.</summary>
        public static void Reset() => _ready = false;
    }
}

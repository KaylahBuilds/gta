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
        private static bool _ready;

        /// <summary>Antagonists: hostile to the crew and to the player.</summary>
        public static RelationshipGroup Hostile(RelationshipGroup crew)
        {
            if (_ready) return _hostile;

            _hostile = World.AddRelationshipGroup("OTB_HOSTILE");
            var player = Game.Player.Character.RelationshipGroup;

            _hostile.SetRelationshipBetweenGroups(crew, Relationship.Hate, true);
            crew.SetRelationshipBetweenGroups(_hostile, Relationship.Hate, true);

            _hostile.SetRelationshipBetweenGroups(player, Relationship.Hate, true);
            player.SetRelationshipBetweenGroups(_hostile, Relationship.Hate, true);

            _ready = true;
            return _hostile;
        }

        /// <summary>Called on script reload so groups are rewired on the next use.</summary>
        public static void Reset() => _ready = false;
    }
}

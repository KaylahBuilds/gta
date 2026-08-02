using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    public class UpgradeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public int Cost;

        /// <summary>Must be owned before this one can be bought. Null for none.</summary>
        public string Requires;
    }

    /// <summary>
    /// One-time global purchases. Effects are read by querying
    /// <see cref="GameState.HasUpgrade"/> at the point of use rather than being
    /// baked into stored numbers — that way a save never carries a stale bonus.
    /// </summary>
    public static class UpgradeCatalog
    {
        public const string RosterA = "roster_a";
        public const string RosterB = "roster_b";
        public const string Retainer = "retainer";
        public const string Laundry = "laundry";
        public const string Creator = "creator";

        public static readonly List<UpgradeDef> All = new List<UpgradeDef>
        {
            new UpgradeDef
            {
                Id = Creator, Name = "Ring light and a laptop", Cost = 8000,
                Description = "Opens the second revenue stream. Followers build " +
                              "while the crew are off the street and deposits land weekly."
            },
            new UpgradeDef
            {
                Id = RosterA, Name = "A bigger book", Cost = 15000,
                Description = "Two more people on the roster."
            },
            new UpgradeDef
            {
                Id = RosterB, Name = "A full book", Cost = 40000, Requires = RosterA,
                Description = "Two more again. Needs a bigger book first."
            },
            new UpgradeDef
            {
                Id = Retainer, Name = "Legal retainer", Cost = 25000,
                Description = "Bail costs half, and vice show up a quarter less often."
            },
            new UpgradeDef
            {
                Id = Laundry, Name = "Laundered take", Cost = 30000,
                Description = "Heat bleeds off half again as fast everywhere."
            }
        };

        public static UpgradeDef Get(string id) => All.FirstOrDefault(u => u.Id == id);
    }
}

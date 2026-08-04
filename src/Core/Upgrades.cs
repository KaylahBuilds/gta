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

        /// <summary>Id kept as "laundry" so existing saves keep the upgrade they bought.</summary>
        public const string Laundromat = "laundry";

        public const string Creator = "creator";
        public const string Burner = "burner";
        public const string BailFund = "bail_fund";
        public const string Screening = "screening";

        public static readonly List<UpgradeDef> All = new List<UpgradeDef>
        {
            new UpgradeDef
            {
                Id = Creator, Name = "Ring light", Cost = 8000,
                Description = "Opens the second revenue stream. Followers build while the crew " +
                              "are off the street, and Camera-ready workers build 1.5x faster."
            },
            new UpgradeDef
            {
                Id = Burner, Name = "Burner network", Cost = 8500,
                Description = "Incidents blip 30 seconds earlier. You get across the map or you don't."
            },
            new UpgradeDef
            {
                Id = BailFund, Name = "Bail fund", Cost = 6000,
                Description = "Anyone picked up comes back the same night instead of costing you two."
            },
            new UpgradeDef
            {
                Id = Screening, Name = "Somebody who checks", Cost = 11000,
                Description = "A name run before anyone is booked. You see how bad a " +
                              "client is likely to be before you say yes, instead of after."
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
                Id = Laundromat, Name = "Laundromat", Cost = 22000,
                Description = "Washes 40% of the weekly deposit. Heat bleeds off half again as " +
                              "fast everywhere, and payday cools the corners you hold."
            }
        };

        public static UpgradeDef Get(string id) => All.FirstOrDefault(u => u.Id == id);
    }
}

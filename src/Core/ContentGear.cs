using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    public class ContentGearDef
    {
        public string Id;
        public string Name;
        public string Description;
        public int Cost;
    }

    /// <summary>
    /// Equipment bought for one content house.
    ///
    /// Shaped on <see cref="ZoneUpgrades"/> deliberately: a
    /// Dictionary&lt;string, List&lt;string&gt;&gt; is the only collection shape in
    /// this codebase already proven to round-trip through
    /// DataContractJsonSerializer. Costs live here rather than in Config, the
    /// same way <see cref="UpgradeCatalog"/> does it.
    ///
    /// Kept in its own dictionary rather than folded into ZoneUpgrades because a
    /// house cannot be taken off you — this survives a raid, and zone upgrades
    /// deliberately do not.
    /// </summary>
    public static class ContentGear
    {
        public const string Lights = "content_lights";
        public const string Set = "content_set";
        public const string Editor = "content_editor";
        public const string Discreet = "content_discreet";

        public static readonly List<ContentGearDef> All = new List<ContentGearDef>
        {
            new ContentGearDef
            {
                Id = Lights, Name = "Lights and sound", Cost = 55000,
                Description = "Somewhere that looks like it was meant for this. " +
                              "Everything shot here is worth 20% more — and the rig " +
                              "runs hot, so the place wears out a third faster."
            },
            new ContentGearDef
            {
                // Repriced from $38,000. At that price it could not repay inside a
                // fortnight at any rung on any shift — best case was 32 game days
                // at the top and 446 at the bottom. Wear is charged per producing
                // worker-hour, so what this is worth is strictly proportional to
                // how busy the house is, and the price has to sit where that is
                // true rather than where it sounds impressive.
                Id = Set, Name = "A proper set", Cost = 14000,
                Description = "Built once, properly, instead of rearranged nightly. " +
                              "The place wears 40% slower, and an empty house stops " +
                              "rotting altogether. Does almost nothing below about " +
                              "eight people producing."
            },
            new ContentGearDef
            {
                Id = Editor, Name = "An editor on retainer", Cost = 30000,
                Description = "Somebody cutting the back catalogue while everyone " +
                              "sleeps. Residents who are off shift still earn 6% of " +
                              "their rate. Worth nothing in a house nobody rests in."
            },
            new ContentGearDef
            {
                // Repriced from $70,000 alongside the multiplier change. At 0.60 it
                // made a house permanently cold at full capacity, which deleted the
                // raid; at 0.80 it buys headroom instead of immunity, and the price
                // sits where that headroom is worth roughly a fortnight rather than
                // eight days.
                Id = Discreet, Name = "The place is discreet", Cost = 85000,
                Description = "Deliveries round the back, no signage, neighbours who " +
                              "have been spoken to. This address draws 20% less " +
                              "attention per resident. It does not make it invisible."
            }
        };

        public static ContentGearDef Get(string id)
        {
            return All.FirstOrDefault(g => g.Id == id);
        }

        public static bool Owns(string houseId, string gearId)
        {
            if (string.IsNullOrEmpty(houseId)) return false;

            List<string> owned;
            if (!GameState.Current.ContentGear.TryGetValue(houseId, out owned)) return false;

            return owned != null && owned.Contains(gearId);
        }

        public static void Buy(string houseId, string gearId)
        {
            if (string.IsNullOrEmpty(houseId)) return;

            List<string> owned;
            if (!GameState.Current.ContentGear.TryGetValue(houseId, out owned) || owned == null)
            {
                owned = new List<string>();
                GameState.Current.ContentGear[houseId] = owned;
            }

            if (!owned.Contains(gearId)) owned.Add(gearId);
        }

        public static int CountOn(string houseId)
        {
            List<string> owned;
            return !string.IsNullOrEmpty(houseId)
                   && GameState.Current.ContentGear.TryGetValue(houseId, out owned)
                   && owned != null
                ? owned.Count
                : 0;
        }

        // --- effects ---------------------------------------------------------
        // Every one reads Config at the point of use, so a save never carries a
        // stale bonus and retuning a key takes effect on load.

        /// <summary>Payout multiplier from the lighting rig.</summary>
        public static float RateMultiplier(string houseId)
        {
            return Owns(houseId, Lights) ? Config.Current.ContentGearRateBonus : 1f;
        }

        /// <summary>
        /// Wear multiplier. The rig makes it worse and the set makes it better,
        /// which is the whole reason the set exists as a second purchase.
        /// </summary>
        public static float WearMultiplier(string houseId)
        {
            float m = 1f;
            if (Owns(houseId, Lights)) m *= Config.Current.ContentGearWearPenalty;
            if (Owns(houseId, Set)) m *= Config.Current.ContentGearWearBonus;
            return m;
        }

        /// <summary>Heat multiplier from discretion. Floored with traits at the point of use.</summary>
        public static float HeatMultiplier(string houseId)
        {
            return Owns(houseId, Discreet) ? Config.Current.ContentGearHeatBonus : 1f;
        }

        /// <summary>An empty house with a proper set in it does not rot.</summary>
        public static bool StopsRot(string houseId)
        {
            return Owns(houseId, Set);
        }

        /// <summary>Share of her rate an off-shift resident earns from the back catalogue.</summary>
        public static float CatalogueShare(string houseId)
        {
            return Owns(houseId, Editor) ? Config.Current.ContentCatalogueShare : 0f;
        }
    }
}

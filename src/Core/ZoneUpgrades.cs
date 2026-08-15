using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    public class ZoneUpgradeDef
    {
        public string Id;
        public string Name;
        public string Description;
        public int Cost;
    }

    /// <summary>
    /// Money spent on one corner.
    ///
    /// Everything else purchasable in the mod is global, which meant holding
    /// ground paid a flat ownership bonus and nothing else — territory was a
    /// scoreboard rather than somewhere you had put anything. These attach to a
    /// single zone and are lost with it if a crew takes it off you, which is what
    /// makes defending a corner about more than the corner.
    /// </summary>
    public static class ZoneUpgrades
    {
        public const string Lookout = "lookout";
        public const string Patrol = "patrol";
        public const string Room = "room";
        public const string Regulars = "corner_regulars";

        public static readonly List<ZoneUpgradeDef> All = new List<ZoneUpgradeDef>
        {
            new ZoneUpgradeDef
            {
                Id = Lookout, Name = "A kid on the corner", Cost = 6000,
                Description = "Somebody watching the road who has nothing else to do. " +
                              "This corner runs a third cooler."
            },
            new ZoneUpgradeDef
            {
                Id = Patrol, Name = "The patrol sorted", Cost = 12000,
                Description = "The car that comes past here belongs to somebody who has " +
                              "been spoken to. Vice work this corner half as often."
            },
            new ZoneUpgradeDef
            {
                Id = Room, Name = "The room upstairs", Cost = 18000,
                Description = "Somewhere to sit down between. Nobody working here burns " +
                              "out at anything like the usual rate."
            },
            new ZoneUpgradeDef
            {
                Id = Regulars, Name = "Known corner", Cost = 10000,
                Description = "People know to come here. Demand on this corner is up for " +
                              "good, whoever is standing on it."
            }
        };

        public static ZoneUpgradeDef Get(string id)
        {
            return string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(u => u.Id == id);
        }

        public static bool Owns(string zoneId, string upgradeId)
        {
            if (string.IsNullOrEmpty(zoneId)) return false;

            List<string> owned;
            if (!GameState.Current.ZoneUpgrades.TryGetValue(zoneId, out owned)) return false;

            return owned != null && owned.Contains(upgradeId);
        }

        public static void Buy(string zoneId, string upgradeId)
        {
            if (string.IsNullOrEmpty(zoneId)) return;

            List<string> owned;
            if (!GameState.Current.ZoneUpgrades.TryGetValue(zoneId, out owned) || owned == null)
            {
                owned = new List<string>();
                GameState.Current.ZoneUpgrades[zoneId] = owned;
            }

            if (!owned.Contains(upgradeId)) owned.Add(upgradeId);
        }

        /// <summary>
        /// Everything on a corner is lost when it changes hands. Called when a
        /// crew takes it — you paid for improvements to ground you no longer hold.
        /// </summary>
        public static void ClearZone(string zoneId)
        {
            if (!string.IsNullOrEmpty(zoneId)) GameState.Current.ZoneUpgrades.Remove(zoneId);
        }

        public static int CountOn(string zoneId)
        {
            List<string> owned;
            return GameState.Current.ZoneUpgrades.TryGetValue(zoneId, out owned) && owned != null
                ? owned.Count
                : 0;
        }

        // --- effects --------------------------------------------------------

        public static float HeatMultiplier(string zoneId) =>
            Owns(zoneId, Lookout) ? Config.Current.LookoutHeatMultiplier : 1f;

        public static float StingMultiplier(string zoneId) =>
            Owns(zoneId, Patrol) ? Config.Current.PatrolStingMultiplier : 1f;

        public static float StaminaMultiplier(string zoneId) =>
            Owns(zoneId, Room) ? Config.Current.RoomStaminaMultiplier : 1f;

        public static float DemandMultiplier(string zoneId) =>
            Owns(zoneId, Regulars) ? Config.Current.KnownCornerDemand : 1f;
    }
}

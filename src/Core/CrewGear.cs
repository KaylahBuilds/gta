using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    public class ArmourDef
    {
        public int Level;
        public string Name;
        public string Blurb;
        public int Cost;

        /// <summary>Multiplier on the odds somebody is carried out of a fight.</summary>
        public float CasualtyMultiplier;

        /// <summary>Flat addition to the crew rating. Small — this is not a weapon.</summary>
        public float Rating;
    }

    public class CrewVehicleDef
    {
        public int Level;
        public string Name;
        public string Blurb;
        public int Cost;

        /// <summary>Flat addition to the crew rating in this region.</summary>
        public float Rating;

        public float CasualtyMultiplier;

        /// <summary>Chance a bad night writes it off entirely.</summary>
        public float LossChance;
    }

    /// <summary>
    /// What the crew are wearing and what they turn up in.
    ///
    /// Both exist because the thing that makes a standing order sustainable is
    /// not winning — it is **not losing people**. A crew that holds every corner
    /// and spends half its life in hospital is still on the wage, still deterring
    /// nobody, and still costing you the fights it is not there for.
    ///
    /// Bought per region, like the stash house and the car, because that is the
    /// unit muscle is already organised into.
    /// </summary>
    public static class CrewGear
    {
        public static readonly List<ArmourDef> Armour = new List<ArmourDef>
        {
            new ArmourDef
            {
                Level = 0, Name = "Whatever they own", Cost = 0,
                CasualtyMultiplier = 1f, Rating = 0f,
                Blurb = "Nothing. Somebody goes down most times it gets serious."
            },
            new ArmourDef
            {
                Level = 1, Name = "Vests", Cost = 9000,
                CasualtyMultiplier = 0.5f, Rating = 3f,
                Blurb = "Second-hand and heavy, and they halve the odds anybody " +
                        "is carried out of it."
            },
            new ArmourDef
            {
                Level = 2, Name = "Plates", Cost = 26000,
                CasualtyMultiplier = 0.2f, Rating = 8f,
                Blurb = "Proper protection. People still get hurt, but rarely, and " +
                        "they fight like men who know it."
            }
        };

        public static readonly List<CrewVehicleDef> Vehicles = new List<CrewVehicleDef>
        {
            new CrewVehicleDef
            {
                Level = 0, Name = "Their own cars", Cost = 0,
                Rating = 0f, CasualtyMultiplier = 1f, LossChance = 0f,
                Blurb = "They get there when they get there, one at a time."
            },
            new CrewVehicleDef
            {
                Level = 1, Name = "A van", Cost = 45000,
                Rating = 15f, CasualtyMultiplier = 0.85f, LossChance = 0.10f,
                Blurb = "They arrive together and they arrive at once. Worth more " +
                        "than the number suggests when somebody is already on the corner."
            },
            new CrewVehicleDef
            {
                Level = 2, Name = "An armoured van", Cost = 120000,
                Rating = 35f, CasualtyMultiplier = 0.55f, LossChance = 0.05f,
                Blurb = "Plated, and it does not stop when somebody shoots at it. " +
                        "This is what turns a marginal corner into one you simply hold."
            }
        };

        public static ArmourDef ArmourAt(int level)
        {
            return Armour.FirstOrDefault(a => a.Level == level) ?? Armour[0];
        }

        public static CrewVehicleDef VehicleAt(int level)
        {
            return Vehicles.FirstOrDefault(v => v.Level == level) ?? Vehicles[0];
        }

        public static ArmourDef ArmourIn(string regionId)
        {
            return ArmourAt(GameState.Current.ArmourLevel(regionId));
        }

        public static CrewVehicleDef VehicleIn(string regionId)
        {
            return VehicleAt(GameState.Current.CrewVehicleLevel(regionId));
        }
    }

    /// <summary>Whether the crew hold ground for you, and how far they will go.</summary>
    public enum StandingOrder
    {
        /// <summary>Contests fire as an incident. You drive out or you lose it.</summary>
        Off = 0,

        /// <summary>They hold what you already have.</summary>
        Defend = 1,

        /// <summary>They will also go and take a corner off somebody.</summary>
        DefendAndTake = 2
    }
}

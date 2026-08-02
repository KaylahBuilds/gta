using System;
using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    public class DemandEventDef
    {
        public string Kind;

        /// <summary>Headline shown when it starts, with {0} replaced by the zone.</summary>
        public string Headline;

        public float Multiplier;

        /// <summary>Extra heat per hour while it runs. Negative eases off.</summary>
        public float HeatPerHour;

        public int MinHours;
        public int MaxHours;

        /// <summary>Zones this can land on. Null means anywhere.</summary>
        public string[] ZoneIds;
    }

    /// <summary>
    /// Zone demand used to be a constant, so the optimal roster arrangement was
    /// computed once and never revisited. Events make demand move: somewhere is
    /// worth being tonight, somewhere else is worth leaving alone, and neither is
    /// where it was yesterday.
    ///
    /// One event at a time, deliberately. Two competing pulls is not a decision,
    /// it is arithmetic.
    /// </summary>
    public static class DemandEvents
    {
        public static readonly List<DemandEventDef> All = new List<DemandEventDef>
        {
            new DemandEventDef
            {
                Kind = "concert", Headline = "There's a show letting out around {0}.",
                Multiplier = 1.9f, HeatPerHour = 0.01f, MinHours = 5, MaxHours = 9,
                ZoneIds = new[] { "vinewood", "delperro", "mirrorpark" }
            },
            new DemandEventDef
            {
                Kind = "convention", Headline = "Out-of-towners all over {0} this week.",
                Multiplier = 1.6f, HeatPerHour = 0f, MinHours = 8, MaxHours = 14,
                ZoneIds = null
            },
            new DemandEventDef
            {
                Kind = "crackdown", Headline = "Police are running {0} hard tonight.",
                Multiplier = 0.35f, HeatPerHour = 0.05f, MinHours = 4, MaxHours = 8,
                ZoneIds = null
            },
            new DemandEventDef
            {
                Kind = "payday", Headline = "Payday. {0} is busy and everyone's holding.",
                Multiplier = 1.5f, HeatPerHour = 0f, MinHours = 6, MaxHours = 10,
                ZoneIds = null
            },
            new DemandEventDef
            {
                Kind = "quiet", Headline = "{0} has gone quiet. Nobody's about.",
                Multiplier = 0.55f, HeatPerHour = -0.02f, MinHours = 5, MaxHours = 10,
                ZoneIds = null
            }
        };

        public static DemandEventDef Get(string kind) =>
            string.IsNullOrEmpty(kind) ? null : All.FirstOrDefault(e => e.Kind == kind);

        public static bool IsActive(GameState state) =>
            !string.IsNullOrEmpty(state.EventKind) &&
            GameState.AbsoluteHour() < state.EventEndsAtHour;

        /// <summary>Demand multiplier for a zone right now.</summary>
        public static float MultiplierFor(string zoneId)
        {
            var state = GameState.Current;
            if (!IsActive(state)) return 1f;
            return state.EventZoneId == zoneId ? state.EventMultiplier : 1f;
        }

        public static float HeatFor(string zoneId)
        {
            var state = GameState.Current;
            if (!IsActive(state) || state.EventZoneId != zoneId) return 0f;

            var def = Get(state.EventKind);
            return def?.HeatPerHour ?? 0f;
        }

        public static string Describe(GameState state)
        {
            if (!IsActive(state)) return null;

            var zone = Zones.Get(state.EventZoneId);
            var def = Get(state.EventKind);
            if (zone == null || def == null) return null;

            int hoursLeft = state.EventEndsAtHour - GameState.AbsoluteHour();
            return string.Format(def.Headline, zone.Display) + $" ({hoursLeft}h left)";
        }

        /// <summary>Ends an expired event, or rolls a new one. Returns a message to show, if any.</summary>
        public static string Tick(Random rng)
        {
            var state = GameState.Current;

            if (IsActive(state)) return null;

            // Clear a finished one before considering the next.
            if (!string.IsNullOrEmpty(state.EventKind))
            {
                var over = Zones.Get(state.EventZoneId);
                state.EventKind = null;
                state.EventZoneId = null;
                state.EventMultiplier = 1f;
                return over == null ? null : $"~s~Things are back to normal around {over.Display}.";
            }

            if (rng.NextDouble() >= Config.Current.DemandEventChance) return null;

            var def = All[rng.Next(All.Count)];

            var candidates = def.ZoneIds == null
                ? Zones.All
                : Zones.All.Where(z => def.ZoneIds.Contains(z.Id)).ToList();

            if (candidates.Count == 0) return null;

            var zone = candidates[rng.Next(candidates.Count)];
            int hours = rng.Next(def.MinHours, def.MaxHours + 1);

            state.EventKind = def.Kind;
            state.EventZoneId = zone.Id;
            state.EventMultiplier = def.Multiplier;
            state.EventEndsAtHour = GameState.AbsoluteHour() + hours;

            string arrow = def.Multiplier >= 1f ? "~g~" : "~o~";
            return $"{arrow}{string.Format(def.Headline, zone.Display)}~s~ ({hours}h)";
        }
    }
}

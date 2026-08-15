using System;
using System.Collections.Generic;
using System.Linq;

namespace OnTheBlade.Core
{
    public class MilestoneDef
    {
        public string Id;
        public string Name;
        public string Blurb;
        public int Reward;
        public Func<GameState, bool> Met;
    }

    /// <summary>
    /// The progression spine.
    ///
    /// Every system in the mod generates numbers, but nothing told the player
    /// what to aim at. These name the arc — first hire, first corner, a full
    /// book, a clean operation, the last rival standing — and pay out once each.
    /// Checked on the hourly tick, which is cheap because the predicates are all
    /// counts against state already in memory.
    /// </summary>
    public static class MilestoneCatalog
    {
        public static readonly List<MilestoneDef> All = new List<MilestoneDef>
        {
            new MilestoneDef
            {
                Id = "first_hire", Name = "Someone to look after", Reward = 0,
                Blurb = "Sign your first worker.",
                Met = s => s.Roster.Count >= 1
            },
            new MilestoneDef
            {
                Id = "first_corner", Name = "A corner of your own", Reward = 2500,
                Blurb = "Hold a zone.",
                Met = s => Zones.All.Any(z => s.PlayerOwns(z.Id))
            },
            new MilestoneDef
            {
                Id = "three_corners", Name = "Getting a name", Reward = 10000,
                Blurb = "Hold three zones at once.",
                Met = s => Zones.All.Count(z => s.PlayerOwns(z.Id)) >= 3
            },
            new MilestoneDef
            {
                Id = "full_book", Name = "A full book", Reward = 15000,
                Blurb = "Carry eight workers.",
                Met = s => s.Roster.Count >= 8
            },
            new MilestoneDef
            {
                Id = "two_streams", Name = "Diversified", Reward = 5000,
                Blurb = "Take a subscription deposit.",
                Met = s => s.LifetimeSubscriptionTake > 0
            },
            new MilestoneDef
            {
                // Its own counter rather than the subscription one, so the
                // milestone above keeps meaning "your first weekly deposit
                // landed" instead of silently firing on the first content hour.
                Id = "on_camera", Name = "On camera", Reward = 8000,
                Blurb = "Take an hour's pay from a content house.",
                Met = s => s.LifetimeContentTake > 0
            },
            new MilestoneDef
            {
                Id = "quiet_streets", Name = "Nothing to see", Reward = 12000,
                Blurb = "Hold three zones with every one of them under 20% heat.",
                Met = s =>
                {
                    var held = Zones.All.Where(z => s.PlayerOwns(z.Id)).ToList();
                    return held.Count >= 3 && held.All(z => s.GetHeat(z.Id) < 0.2f);
                }
            },
            new MilestoneDef
            {
                Id = "first_blood", Name = "First crew broken", Reward = 20000,
                Blurb = "Break a rival crew.",
                Met = s => s.Rivals.Any(r => r.IsBroken)
            },
            new MilestoneDef
            {
                Id = "kingpin", Name = "Nobody left to fight", Reward = 100000,
                Blurb = "Break every rival crew.",
                Met = s => s.Rivals.Count > 0 && s.Rivals.All(r => r.IsBroken)
            },
            new MilestoneDef
            {
                Id = "whole_map", Name = "The whole map", Reward = 75000,
                Blurb = "Hold every zone at once.",
                Met = s => Zones.All.All(z => s.PlayerOwns(z.Id))
            },
            new MilestoneDef
            {
                Id = "millionaire", Name = "Seven figures", Reward = 0,
                Blurb = "Take a million across both streams.",
                Met = s => s.LifetimeTake >= 1000000
            }
        };

        public static MilestoneDef Get(string id) => All.FirstOrDefault(m => m.Id == id);

        /// <summary>Returns any milestone newly met this tick, or null.</summary>
        public static MilestoneDef CheckNext(GameState state)
        {
            foreach (var m in All)
            {
                if (state.Milestones.Contains(m.Id)) continue;
                if (!m.Met(state)) continue;

                state.Milestones.Add(m.Id);
                return m;
            }
            return null;
        }
    }
}

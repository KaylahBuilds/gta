using System.Linq;
using OnTheBlade.Core;

namespace OnTheBlade.BladeWorld
{
    /// <summary>
    /// On The Blade's half of the shared city.
    ///
    /// This mod is the **authority**: it owns territory, the crews, the man at
    /// the station and the informant, because it already has the turf battle,
    /// poaching, war and collapse systems. Duplicating any of that in the other
    /// mod would mean two scripts disagreeing about who holds Strawberry.
    ///
    /// Everything here is one-way except heat and reputation. We publish what we
    /// own and read only the two fields The Trap Star owns — its half of the
    /// heat, and its half of the name.
    ///
    /// If the other mod is not installed nothing here does anything: the file
    /// simply has no trap contribution in it, and every sum comes out the same as
    /// it did before Phase 4 existed.
    /// </summary>
    public static class WorldLink
    {
        /// <summary>
        /// Publish everything we are the authority for. Called once per in-game
        /// hour — often enough that the other mod is never more than a couple of
        /// minutes stale, rarely enough that it is not writing a file on a tick.
        /// </summary>
        public static void Publish()
        {
            var state = GameState.Current;

            WorldFile.Update(data =>
            {
                data.Rep.Blade = state.Reputation;
                // (the trap writes its own half; Rep.Total is the street name)

                // --- territory ---
                data.Zones.Clear();
                foreach (var zone in Zones.All)
                {
                    int until;
                    state.ZoneLockedUntilDay.TryGetValue(zone.Id, out until);

                    data.Zones.Add(new ZoneEntry
                    {
                        Id = zone.Id,
                        Owner = state.OwnerOf(zone.Id) ?? string.Empty,
                        LockedUntilDay = until
                    });
                }

                // --- crews ---
                data.Crews.Clear();
                foreach (var rival in state.Rivals)
                {
                    data.Crews.Add(new CrewEntry
                    {
                        Id = rival.Id,
                        Name = rival.Name,
                        Strength = rival.Strength,
                        Aggression = rival.Aggression,
                        AtWar = rival.IsAtWar,
                        TruceUntilDay = state.TruceUntilDay.ContainsKey(rival.Id)
                            ? state.TruceUntilDay[rival.Id]
                            : 0
                    });
                }

                // --- pillow talk and the wash ---
                PublishTips(data, state);

                // Venues wash money for the other business: capacity scales
                // with how much legitimate front the operation actually has.
                data.WashCapacityPerDay = state.Roster.Count > 0
                    ? 8000 + state.Roster.Count * 2000
                    : 0;

                // --- the law ---
                data.Police.RetainerUntilDay = state.CopPaidUntilDay;
                data.Police.WarnedZone = state.WarnedZoneId;
                data.Police.InformantActive = state.InformantWorkerId >= 0;

                // --- our half of the heat, and only ours ---
                foreach (var zone in Zones.All)
                {
                    var entry = data.HeatFor(zone.Id, create: true);

                    // Everything this mod generates is vice attention. Girls on a
                    // corner are not what a narcotics warrant is about — but a
                    // corner under any kind of scrutiny is a corner being watched,
                    // which is what the small narco bleed represents.
                    entry.ViceBlade = state.GetHeat(zone.Id);

                    // Deliberately zero. This used to publish a narco bleed of
                    // our own vice heat, which the reader on the other side then
                    // multiplied by HeatSpillover — so the cross term was carried
                    // twice and came to 0.50 against a configured 0.35, a number
                    // that appeared in neither config file and could not be tuned
                    // without moving the self term with it. Worse, our OWN reader
                    // picked our bleed back up and inflated our own heat by 5.25%
                    // with the other mod not even installed, so a threshold of
                    // 0.95 actually fired at 0.9026.
                    //
                    // The field stays on the DTO so the wire format does not
                    // change and an older build on the other side keeps parsing.
                    entry.NarcoBlade = 0f;
                }
            });
        }

        /// <summary>
        /// What the girls hear, told to the other business. At most one tip a
        /// day, only while somebody is actually working — clients talk to
        /// workers, not to empty rosters. The list is pruned by age and kept
        /// short; the reader tracks what it has already shown.
        /// </summary>
        private static void PublishTips(WorldData data, GameState state)
        {
            int today = GameState.AbsoluteDay();

            data.Tips.RemoveAll(t => t == null || today - t.Day > 2);

            if (state.Roster.Count == 0) return;
            if (state.LastTipDay == today) return;
            if (data.Tips.Count >= 4) return;

            var rng = new System.Random(unchecked(today * 397));
            if (rng.NextDouble() > 0.45) return;

            state.LastTipDay = today;

            string id = $"tip-{today}-{rng.Next(9999)}";
            double kind = rng.NextDouble();

            if (kind < 0.4)
            {
                // A zone under the wrong kind of attention.
                var hot = Zones.All
                    .OrderByDescending(z => state.GetHeat(z.Id))
                    .FirstOrDefault(z => state.GetHeat(z.Id) > 0.35f);
                if (hot == null) return;

                data.Tips.Add(new TipEntry
                {
                    Id = id, Kind = "raid", ZoneId = hot.Id, Day = today
                });
            }
            else if (kind < 0.7)
            {
                // A client bragged about a package. Somewhere near a zone.
                var zone = Zones.All.ElementAtOrDefault(rng.Next(Zones.All.Count));
                if (zone == null) return;

                var at = zone.Anchor + new GTA.Math.Vector3(
                    rng.Next(-60, 60), rng.Next(-60, 60), 0f);

                data.Tips.Add(new TipEntry
                {
                    Id = id, Kind = "stash", ZoneId = zone.Id, Day = today,
                    X = at.X, Y = at.Y, Z = at.Z,
                    Grams = 40 + rng.Next(50)
                });
            }
            else
            {
                // A whale: somebody with money wants weight, quietly.
                data.Tips.Add(new TipEntry
                {
                    Id = id, Kind = "whale", Day = today,
                    Grams = 50 + rng.Next(100),
                    PerGram = 0   // the reader prices it at its own premium
                });
            }
        }

        // --- what we read back --------------------------------------------------

        /// <summary>
        /// Effective vice heat on a corner: everything either business has put on
        /// it, plus a share of the narcotics attention.
        ///
        /// This is what makes running both on one block cost more than running
        /// either alone, and it is arithmetic rather than a scripted crossover.
        /// </summary>
        public static float ViceHeat(string zoneId)
        {
            float mine = GameState.Current.GetHeat(zoneId);

            var world = WorldFile.Read();
            var entry = world?.HeatFor(zoneId, create: false);
            if (entry == null) return mine;

            float total = entry.Vice + entry.Narco * Config.Current.HeatSpillover;

            // Our own figure is authoritative and may be fresher than the file.
            if (total < mine) total = mine;

            return total > 1f ? 1f : total;
        }

        /// <summary>One street name. Ours plus theirs.</summary>
        public static int Reputation()
        {
            var world = WorldFile.Read();
            int theirs = world?.Rep?.Trap ?? 0;

            return GameState.Current.Reputation + theirs;
        }

        /// <summary>Whether the other mod is in this world at all.</summary>
        public static bool Linked => WorldFile.Read() != null;

        /// <summary>
        /// The set's name, claimed on the product side and flown here too —
        /// one crew, two businesses. Null while no name has been claimed, and
        /// null for every schema-1 world file, which is the correct fallback.
        /// </summary>
        public static string SetName()
        {
            var set = WorldFile.Read()?.Set;
            return string.IsNullOrEmpty(set?.Name) ? null : set.Name;
        }

        /// <summary>The set's map colour, or null to keep this mod's own
        /// configured colour. Zero is the writer's "never chosen" sentinel.</summary>
        public static GTA.BlipColor? SetColour()
        {
            var set = WorldFile.Read()?.Set;
            if (set == null || string.IsNullOrEmpty(set.Name) || set.ColourId == 0)
                return null;

            return (GTA.BlipColor)set.ColourId;
        }

        public static string StatusLine()
        {
            var world = WorldFile.Read();

            if (world == null)
                return "~o~Standalone.~s~ The Trap Star isn't in this world.";

            int theirs = world.Rep?.Trap ?? 0;

            float worstNarco = Zones.All
                .Select(z => world.HeatFor(z.Id, false)?.NarcoTrap ?? 0f)
                .DefaultIfEmpty(0f)
                .Max();

            string set = SetName();

            return $"~g~Linked.~s~ One name at {Reputation()} " +
                   $"({GameState.Current.Reputation} yours, {theirs} from the product side). " +
                   $"Worst narcotics heat on your ground: {worstNarco * 100:0}%." +
                   (set != null ? $" Flying the {set} flag." : string.Empty);
        }
    }
}

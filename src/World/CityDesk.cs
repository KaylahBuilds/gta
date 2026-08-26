using System;
using System.Linq;
using OnTheBlade.Core;

namespace OnTheBlade.BladeWorld
{
    /// <summary>
    /// BLADE'S HALF OF THE CITY TICKER.
    ///
    /// This mod has been a silent partner in its own city. It publishes
    /// territory, crews, police and — critically — the WASH CAPACITY that both
    /// other pillars spend, and it has never once said anything, nor heard
    /// anything back. Other mods consume it. It does not participate.
    ///
    /// That is the wrong way round for the oldest and largest of the three, and
    /// it is why a city ticker with one writer is a log rather than a city.
    ///
    /// Two directions, both small:
    ///
    ///   OUT — the nights that actually happened here reach the other mods'
    ///   feeds. A venue having a night, a sting, a crew moving on a zone. The
    ///   drug side then has a city that is visibly running without it.
    ///
    ///   IN — what the other pillars did reaches this mod as a text, in the
    ///   voice it already uses for street word. Rate-limited hard, because
    ///   Blade texts the player a great deal already and news from another
    ///   business should read as background, never as an inbox.
    /// </summary>
    public static class CityDesk
    {
        private static readonly Random Rng = new Random();

        /// <summary>How far through the shared ticker we have already read.</summary>
        private static int _seen;
        private static int _lastReadDay;

        /// <summary>
        /// One line onto the wire, pruning only entries WE wrote.
        ///
        /// Never touches another mod's news — the same single-writer discipline
        /// the heat ledger and the zone list already follow, because a shared
        /// file with no locking survives only while every writer stays inside
        /// its own lane.
        /// </summary>
        public static void Publish(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // A blank state must never speak. Same rule as Publish(): a session
            // running after an unreadable save would otherwise announce a city
            // that does not exist.
            if (Persistence.SaveManager.SaveBlocked) return;

            WorldFile.Update(data =>
            {
                if (data.Ticker == null) return;

                data.Ticker.RemoveAll(t =>
                    t == null
                    || (t.Source == WorldFile.Me
                        && GameState.AbsoluteDay() - t.Day > 2));

                data.Ticker.Add(new TickerEntry
                {
                    Source = WorldFile.Me,
                    Text = text,
                    Day = GameState.AbsoluteDay(),
                });
            });
        }

        /// <summary>
        /// What the rest of the city has been doing, at most once a day and
        /// only one line of it.
        ///
        /// Deliberately stingy. This mod already texts the player about
        /// bookings, incidents, rivals and the law; another business's news
        /// earning the same volume would drown the things that need answering.
        /// </summary>
        public static void ReadIn()
        {
            int today = GameState.AbsoluteDay();
            if (today == _lastReadDay) return;

            var world = WorldFile.Read();
            if (world == null || world.Ticker == null) { _lastReadDay = today; return; }

            var fresh = world.Ticker
                .Skip(_seen)
                .Where(t => t != null
                            && !string.IsNullOrEmpty(t.Text)
                            && t.Source != WorldFile.Me)
                .ToList();

            _seen = world.Ticker.Count;
            _lastReadDay = today;

            if (fresh.Count == 0) return;

            // One, chosen at random rather than the newest — the newest is
            // whatever happened to be written last, not what is worth hearing.
            var pick = fresh[Rng.Next(fresh.Count)];

            Notify.Show("~s~Word from the other side of the business: " + pick.Text);
        }
    }
}

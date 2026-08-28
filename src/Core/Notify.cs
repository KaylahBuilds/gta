using System.Collections.Generic;
using GTA;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Single point of contact with the game's notification feed.
    ///
    /// SHVDN deprecated <c>Notification.Show</c> in favour of
    /// <c>Notification.PostTicker</c>, and this mod posts from a dozen places.
    /// Routing them all through here means the next rename is a one-line change
    /// rather than another forty-site sweep.
    ///
    /// IT IS A QUEUE NOW.
    ///
    /// Everything here used to post the instant it was called, which is fine for
    /// one message and wrong for a burst. This mod deals in bursts: the nightly
    /// resolve settles every venue, every worker and every zone in one frame,
    /// and an incident can fire on top of it. Run alongside the other two
    /// pillars and the feed becomes unreadable — a live screenshot had six lines
    /// drawing through each other and through an open menu.
    ///
    /// Calls enqueue; one line goes out every NotifySpacingMs.
    ///
    ///   IMPORTANT JUMPS. A worker in trouble queued behind five payout lines is
    ///   a warning that lands after the thing it warned about.
    ///
    ///   IT GOES STALE. An ordinary line about a zone you left a minute and a
    ///   half ago is noise. Important ones never expire.
    ///
    ///   IT IS BOUNDED, dropping the OLDEST ordinary line past the cap, because
    ///   in a burst the newest is the one still true.
    /// </summary>
    public static class Notify
    {
        private sealed class Pending
        {
            public string Text;
            public bool Blinking;
            public int ExpiresAt;
        }

        private static readonly Queue<Pending> Loud = new Queue<Pending>();
        private static readonly Queue<Pending> Ordinary = new Queue<Pending>();

        private static int _nextPostAt;

        private const int StaleMs = 25000;
        private const int MaxOrdinary = 8;

        public static void Show(string message, bool blinking = false)
        {
            if (string.IsNullOrEmpty(message)) return;

            var p = new Pending
            {
                Text = message,
                Blinking = blinking,
                ExpiresAt = Game.GameTime + StaleMs,
            };

            if (blinking) { Loud.Enqueue(p); return; }

            Ordinary.Enqueue(p);
            while (Ordinary.Count > MaxOrdinary) Ordinary.Dequeue();
        }

        /// <summary>
        /// One line out, when one is due. Called every tick from Main.
        ///
        /// Spacing of 0 posts immediately and restores the old behaviour.
        /// </summary>
        public static void Pump()
        {
            int spacing = Config.Current.NotifySpacingMs;

            if (spacing <= 0)
            {
                while (Loud.Count > 0) Post(Loud.Dequeue());
                while (Ordinary.Count > 0) Post(Ordinary.Dequeue());
                return;
            }

            if (Game.GameTime < _nextPostAt) return;

            while (Ordinary.Count > 0 && Game.GameTime > Ordinary.Peek().ExpiresAt)
                Ordinary.Dequeue();

            Pending next = Loud.Count > 0 ? Loud.Dequeue()
                         : Ordinary.Count > 0 ? Ordinary.Dequeue()
                         : null;

            if (next == null) return;

            Post(next);
            _nextPostAt = Game.GameTime + spacing;
        }

        private static void Post(Pending p) =>
            GTA.UI.Notification.PostTicker(p.Text, p.Blinking, true);

        /// <summary>Drops everything waiting — for a shutdown, where posting a
        /// backlog into a loading screen helps nobody.</summary>
        public static void Clear()
        {
            Loud.Clear();
            Ordinary.Clear();
        }
    }
}

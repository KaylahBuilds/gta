namespace OnTheBlade.Core
{
    /// <summary>
    /// Single point of contact with the game's notification feed.
    ///
    /// SHVDN deprecated <c>Notification.Show</c> in favour of
    /// <c>Notification.PostTicker</c>, and this mod posts from a dozen places.
    /// Routing them all through here means the next rename is a one-line change
    /// rather than another forty-site sweep.
    /// </summary>
    public static class Notify
    {
        public static void Show(string message, bool blinking = false)
        {
            GTA.UI.Notification.PostTicker(message, blinking, true);
        }
    }
}

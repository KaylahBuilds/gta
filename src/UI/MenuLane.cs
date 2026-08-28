using System.Drawing;
using LemonUI;
using LemonUI.Menus;

namespace OnTheBlade.UI
{
    /// <summary>
    /// MOVING THE MENUS OUT OF THE TICKER'S LANE.
    ///
    /// GTA draws its notifications down the top-left of the screen, and LemonUI
    /// draws menus in exactly the same place. One mod gets away with it because
    /// you rarely have a menu open while a notification lands. Three mods
    /// running together do not: the corners pay out, a worker walks off, a rig
    /// fills, and all of it prints straight through whatever menu is open.
    ///
    /// It got worse as the mods got better. Every system added is another thing
    /// that posts, so the busier the city gets the less of it you can read.
    ///
    /// This shifts every menu in the pool sideways, once, from one place — the
    /// alternative was setting Offset at seventy construction sites across three
    /// repositories and remembering to do it at the seventy-first.
    ///
    /// WHY SIDEWAYS AND NOT DOWN. Down runs into the minimap. Right runs into
    /// the stats HUD on a wide screen. The gap between the ticker and the middle
    /// of the screen is the only lane that is empty at every aspect ratio, which
    /// is why the default is a horizontal nudge and the vertical one is zero.
    ///
    /// Both are config keys because this is resolution-dependent and the author
    /// plays ultrawide. Nudging a number in a json beats rebuilding a mod.
    /// </summary>
    public static class MenuLane
    {
        /// <summary>
        /// Applies the offset to every menu the pool is holding.
        ///
        /// Called once, at the end of construction, after every menu has been
        /// added. Anything added later — a submenu built on demand — inherits
        /// nothing, so build menus up front, which all three mods already do.
        /// </summary>
        public static void Apply(ObjectPool pool)
        {
            var cfg = OnTheBlade.Core.Config.Current;

            float x = cfg.MenuOffsetX;
            float y = cfg.MenuOffsetY;

            if (x == 0f && y == 0f) return;

            var offset = new PointF(x, y);

            foreach (var item in pool)
            {
                var menu = item as NativeMenu;
                if (menu != null) menu.Offset = offset;
            }
        }
    }
}

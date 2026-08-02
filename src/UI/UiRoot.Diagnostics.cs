using System.Linq;
using GTA;
using GTA.Math;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Persistence;

namespace OnTheBlade.UI
{
    /// <summary>
    /// Tools for verifying the guessed coordinates in <see cref="Zones"/> and
    /// <see cref="Regions"/>.
    ///
    /// Every anchor in this mod was written from memory and none has been checked
    /// in-game. Driving to six of them and eyeballing whether a worker is on the
    /// kerb is slow and subjective; jumping straight to each one and reading back
    /// a corrected coordinate is neither.
    ///
    /// Off unless <see cref="Config.ShowDiagnosticsMenu"/> is set.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _diagnostics;
        private NativeItem _whereAmI;

        private void BuildDiagnosticsMenu()
        {
            if (!Config.Current.ShowDiagnosticsMenu) return;

            _diagnostics = new NativeMenu("On The Blade", "DIAGNOSTICS");
            _pool.Add(_diagnostics);
            _main.AddSubMenu(_diagnostics);
            _diagnostics.Shown += (s, e) => RebuildDiagnostics();
        }

        private void RebuildDiagnostics()
        {
            _diagnostics.Clear();

            var player = Game.Player.Character;
            Vector3 p = player.Position;
            string code = RecruitAreas.ZoneCode(p);

            _whereAmI = new NativeItem("Where am I",
                $"Zone code [{code}] — {RecruitAreas.Describe(RecruitAreas.At(p))}. " +
                $"Heading {player.Heading:0}.")
            {
                AltTitle = $"{p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}",
                Enabled = false
            };
            _diagnostics.Add(_whereAmI);

            var capture = new NativeItem("Log this spot as an anchor",
                "Writes a paste-ready Vector3 and heading to OnTheBlade.log. " +
                "Stand exactly where a worker should stand, then activate.");
            capture.Activated += (s, e) => CaptureAnchor();
            _diagnostics.Add(capture);

            foreach (var zone in Zones.All)
            {
                var def = zone;
                float distance = p.DistanceTo(zone.Anchor);

                var jump = new NativeItem($"Jump to {zone.Display}",
                    $"Anchor ({zone.Anchor.X:0.0}, {zone.Anchor.Y:0.0}, {zone.Anchor.Z:0.0}) " +
                    $"heading {zone.Heading:0}. Check whether that is a sensible kerb.")
                {
                    AltTitle = distance > 5000f ? "far" : $"{distance:0}m"
                };
                jump.Activated += (s, e) => JumpTo(def.Anchor, def.Heading);
                _diagnostics.Add(jump);
            }

            foreach (var region in Regions.All)
            {
                var def = region;
                var jump = new NativeItem($"Jump to {region.StashName}",
                    $"Stash anchor ({region.StashPosition.X:0.0}, {region.StashPosition.Y:0.0}, " +
                    $"{region.StashPosition.Z:0.0}). Should be somewhere you can arrive by car.");
                jump.Activated += (s, e) => JumpTo(def.StashPosition, def.StashHeading);
                _diagnostics.Add(jump);
            }
        }

        /// <summary>
        /// Writes the player's exact position in the form the source expects, so a
        /// corrected anchor is a copy-paste rather than a transcription.
        /// </summary>
        private void CaptureAnchor()
        {
            var player = Game.Player.Character;
            Vector3 p = player.Position;
            string code = RecruitAreas.ZoneCode(p);

            SaveManager.Log(
                $"ANCHOR [{code}]  new Vector3({p.X:0.0}f, {p.Y:0.0}f, {p.Z:0.0}f), " +
                $"Heading = {player.Heading:0}f");

            Notify.Show($"~g~Logged.~s~ [{code}] {p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}");
        }

        private void JumpTo(Vector3 target, float heading)
        {
            if (_missions.Busy)
            {
                Notify.Show("~r~Not while something is going on.");
                return;
            }

            var player = Game.Player.Character;

            GTA.UI.Screen.FadeOut(500);
            Script.Wait(600);

            if (player.IsInVehicle())
            {
                Vehicle vehicle = player.CurrentVehicle;
                vehicle.Position = target;
                vehicle.Heading = heading;
            }
            else
            {
                player.Position = target;
                player.Heading = heading;
            }

            Script.Wait(300);
            GTA.UI.Screen.FadeIn(500);

            _diagnostics.Visible = false;
            _main.Visible = false;
        }
    }
}

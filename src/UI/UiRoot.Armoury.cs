using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// One enforcer: what he is carrying, what it cost, and what it has cost him.
    /// Split from the main UiRoot file purely to keep both readable — it is one
    /// class.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _armoury;
        private int _boundEnforcerId = -1;

        private void BuildArmouryMenu()
        {
            _armoury = new NativeMenu("On The Blade", "MUSCLE");
            _pool.Add(_armoury);

            // Parented rather than added as a row: it is reached by picking a man,
            // so an "armoury >>>" entry in the muscle list would open it with
            // nobody bound. Parent still gives Back the right destination.
            _armoury.Parent = _muscle;
            _armoury.Shown += (s, e) => RebuildArmoury();
        }

        private void OpenArmoury(int enforcerId)
        {
            _boundEnforcerId = enforcerId;

            // Hide the parent first. Opening a menu without closing the one behind
            // it leaves both drawing at the same coordinates, which reads as
            // garbled overlapping text rather than as two menus.
            _muscle.Visible = false;
            _armoury.Visible = true;
        }

        private void RebuildArmoury()
        {
            _armoury.Clear();

            var enforcer = GameState.Current.Enforcers
                .FirstOrDefault(e => e.Id == _boundEnforcerId);

            if (enforcer == null)
            {
                _armoury.Add(new NativeItem("(nobody selected)",
                    "Pick someone off the payroll first."));
                return;
            }

            _armoury.Name = enforcer.Name.ToUpperInvariant();

            var cfg = Config.Current;
            var region = Regions.Get(enforcer.RegionId);
            int rank = Armoury.RankOf(enforcer.WeaponId);

            // --- what he is carrying now ---
            _armoury.Add(new NativeItem("Carrying",
                enforcer.IsArmed
                    ? $"{enforcer.Loadout.Description} Adds {enforcer.Loadout.Muscle} to his skill."
                    : "Bare hands. He can talk people down, but he will not turn up to a " +
                      "fight and he adds nothing to your odds of holding a corner.")
            {
                AltTitle = enforcer.IsArmed
                    ? $"~g~{Armoury.NameOf(enforcer.WeaponId)}"
                    : "~o~Bare hands",
                Enabled = false
            });

            // --- condition ---
            if (enforcer.IsInjured())
            {
                _armoury.Add(new NativeItem("Laid up",
                    "He is off the corner but still on the wage. He deters nobody and " +
                    "will not turn out while he is like this.")
                {
                    AltTitle = $"~r~{enforcer.InjuryDaysLeft()} day(s)",
                    Enabled = false
                });
            }

            int deter = (int)Math.Round(enforcer.EffectiveSkill * cfg.MuscleTurfDeterrence);
            _armoury.Add(new NativeItem("Skill",
                $"{enforcer.Skill:0} of his own plus {Armoury.MuscleOf(enforcer.WeaponId)} for what " +
                $"he is holding. Turns away about {deter}% of moves on your corners in " +
                $"{region?.Display ?? enforcer.RegionId}." +
                (enforcer.IsInjured() ? " ~r~Nothing at all while he is laid up.~s~" : string.Empty))
            {
                AltTitle = $"{enforcer.EffectiveSkill:0}",
                Enabled = false
            });

            _armoury.Add(new NativeItem("Record",
                $"Problems he has dealt with, and fights he has been carried out of.")
            {
                AltTitle = enforcer.TimesHurt > 0
                    ? $"{enforcer.Handled} handled, ~r~{enforcer.TimesHurt} hurt"
                    : $"{enforcer.Handled} handled",
                Enabled = false
            });

            // --- the armoury ---
            foreach (var loadout in Armoury.All)
            {
                bool owned = enforcer.WeaponId == loadout.Id;
                bool downgrade = Armoury.RankOf(loadout.Id) < rank;
                bool afford = Game.Player.Money >= loadout.Cost;

                string description = loadout.Description;
                if (downgrade) description = "He already carries better than this.";
                else if (!owned && !afford) description += $"  |  ~r~You need ${loadout.Cost:N0}.";

                var item = new NativeItem(loadout.Name, description)
                {
                    AltTitle = owned ? "~g~Carrying"
                             : downgrade ? "~r~—"
                             : $"${loadout.Cost:N0}",
                    Enabled = !owned && !downgrade && afford
                };

                if (!owned && !downgrade && afford)
                {
                    var def = loadout;
                    int id = enforcer.Id;
                    item.Activated += (s, e) => BuyLoadout(id, def);
                }

                _armoury.Add(item);
            }

            // --- let him go ---
            var dismiss = new NativeItem("Let him go",
                $"Ends the arrangement. ${enforcer.DailyWage}/day back in your pocket, and " +
                (enforcer.IsArmed
                    ? "whatever you bought him goes with him."
                    : "nothing lost but the cover."));

            int dismissId = enforcer.Id;
            dismiss.Activated += (s, e) =>
            {
                Dismiss(dismissId);
                _armoury.Visible = false;
                _muscle.Visible = true;
            };
            _armoury.Add(dismiss);
        }

        private void BuyLoadout(int enforcerId, LoadoutDef loadout)
        {
            var enforcer = GameState.Current.Enforcers.FirstOrDefault(e => e.Id == enforcerId);
            if (enforcer == null) return;

            if (Game.Player.Money < loadout.Cost)
            {
                Notify.Show($"~r~You need ${loadout.Cost:N0} in hand.");
                return;
            }

            Game.Player.Money -= loadout.Cost;
            enforcer.WeaponId = loadout.Id;
            enforcer.Clamp();

            Notify.Show(
                $"~g~{enforcer.Name} is carrying {loadout.Name.ToLowerInvariant()}.~s~ " +
                $"He turns out to trouble in {Regions.Get(enforcer.RegionId)?.Display ?? "his patch"} now.");

            Persistence.SaveManager.Log(
                $"{enforcer.Name} armed with {loadout.Id} for ${loadout.Cost}.");

            RebuildArmoury();
        }
    }
}

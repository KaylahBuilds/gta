using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// The police: the one you pay, the one selling you out, and whoever is
    /// currently inside. Split from the main UiRoot file purely to keep both
    /// readable — it is one class.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _law;
        private NativeMenu _accuse;

        private void BuildLawMenu()
        {
            _law = new NativeMenu("On The Blade", "THE LAW");
            _accuse = new NativeMenu("On The Blade", "WHO'S TALKING");

            _pool.Add(_law);
            _pool.Add(_accuse);

            _streets.AddSubMenu(_law);
            _law.AddSubMenu(_accuse);

            _law.Shown += (s, e) => RebuildLaw();
            _accuse.Shown += (s, e) => RebuildAccuse();
        }

        private void RebuildLaw()
        {
            _law.Clear();
            _law.AddSubMenu(_accuse);

            var state = GameState.Current;
            var cfg = Config.Current;

            // --- the man on the payroll ---
            int cost = Law.RetainerCost();
            bool paid = Law.CopPaid;

            var retainer = new NativeItem(
                paid ? "Your man at the station" : "Somebody at the station",
                paid
                    ? $"Paid up for {Law.CopDaysLeft()} more day(s). A corner about to be " +
                      $"raided gets you {cfg.CopWarningHours} hours' notice instead. " +
                      $"Activate to pay for another {cfg.CopRetainerDays}."
                    : $"${cfg.CopRetainerDailyCost:N0} a day, bought {cfg.CopRetainerDays} at a " +
                      "time. You get a phone call before a door goes in. " +
                      (state.CopBurnedCount > 0
                          ? "~o~He remembers you letting him go, and he has put his price up.~s~"
                          : "Let it lapse and he stops being on your side."))
            {
                AltTitle = paid ? $"~g~{Law.CopDaysLeft()}d" : $"${cost:N0}",
                Enabled = Game.Player.Money >= cost
            };

            retainer.Activated += (s, e) => { Law.PayRetainer(); RebuildLaw(); };
            _law.Add(retainer);

            if (!string.IsNullOrEmpty(state.WarnedZoneId))
            {
                var warned = Zones.Get(state.WarnedZoneId);
                int hoursLeft = state.WarningExpiresAtHour - GameState.AbsoluteHour();
                if (hoursLeft < 0) hoursLeft = 0;

                _law.Add(new NativeItem("On a list tonight",
                    $"{warned?.Display} is going to get hit. Pull everyone off it or get " +
                    "the heat down before the notice runs out.")
                {
                    AltTitle = $"~r~{warned?.Display} {hoursLeft}h",
                    Enabled = false
                });
            }

            // --- somebody talking ---
            int sweep = Law.SweepCost();
            var informant = Law.Informant();

            if (state.InformantKnown && informant != null)
            {
                _law.Add(new NativeItem("Somebody is talking",
                    $"{informant.Name}. Every hour she works generates " +
                    $"{cfg.InformantHeatMultiplier:0.0}x the heat it should. " +
                    "Put her out through the menu below.")
                {
                    AltTitle = $"~r~{informant.Name}",
                    Enabled = false
                });
            }
            else
            {
                var sweepItem = new NativeItem("Have everyone checked",
                    state.InformantHinted
                        ? "You already know somebody is. This finds out who, and it is " +
                          "cheaper than throwing out the wrong one."
                        : "Runs your whole book past somebody who would know. Tells you " +
                          "whether anyone is talking to the police, and who.")
                {
                    AltTitle = $"${sweep:N0}",
                    Enabled = Game.Player.Money >= sweep
                };

                sweepItem.Activated += (s, e) => { Law.Sweep(); RebuildLaw(); };
                _law.Add(sweepItem);
            }

            // --- custody ---
            var inside = state.Roster.Where(w => w.IsJailed()).ToList();

            if (inside.Count == 0)
            {
                _law.Add(new NativeItem("Nobody inside", "Everyone is where you left them.")
                {
                    AltTitle = "~g~clear",
                    Enabled = false
                });
            }

            foreach (var worker in inside)
            {
                int lawyer = Law.LawyerCost(worker);

                var item = new NativeItem(worker.Name,
                    $"{worker.JailDaysLeft()} day(s) left. She earns nothing and cannot be " +
                    $"posted. ${lawyer:N0} buys the rest of it, and she knows who paid." +
                    (state.HasUpgrade(UpgradeCatalog.Retainer)
                        ? " The retainer is already halving this."
                        : string.Empty))
                {
                    AltTitle = $"~r~{worker.JailDaysLeft()}d",
                    Enabled = Game.Player.Money >= lawyer
                };

                // Sentences expire at midnight whether the menu is open or not —
                // re-resolved so the lawyer is never paid for a release that
                // already happened on its own.
                int jailedId = worker.Id;
                item.Activated += (s, e) =>
                {
                    var w = GameState.Current.GetWorker(jailedId);

                    if (w == null || !w.IsJailed())
                    {
                        Notify.Show("~o~She's already out.");
                        RebuildLaw();
                        return;
                    }

                    Law.PayLawyer(w);
                    RebuildLaw();
                };
                _law.Add(item);
            }
        }

        /// <summary>
        /// Throwing somebody out on suspicion. Deliberately its own submenu — this
        /// is not a thing to do by accident from a list of other business.
        /// </summary>
        private void RebuildAccuse()
        {
            _accuse.Clear();
            var state = GameState.Current;
            var cfg = Config.Current;

            _accuse.Add(new NativeItem("Before you do this",
                state.InformantKnown
                    ? "You had them checked, so you already know. This is just the part " +
                      "where you put her out."
                    : $"You have not had anyone checked. Get it wrong and you lose her " +
                      $"anyway, everyone else loses {cfg.AccusationWrongLoyaltyHit:0} loyalty, " +
                      "and your name takes it.")
            {
                AltTitle = state.InformantKnown ? "~g~confirmed" : "~r~guessing",
                Enabled = false
            });

            if (state.Roster.Count == 0)
            {
                _accuse.Add(new NativeItem("(nobody on the books)", "Nothing to do here."));
                return;
            }

            foreach (var worker in state.Roster.OrderBy(w => w.Id))
            {
                bool known = state.InformantKnown && worker.Id == state.InformantWorkerId;

                var item = new NativeItem(worker.Name,
                    $"Tier {worker.Tier}, loyalty {worker.Loyalty:0}, " +
                    $"{worker.LifetimeHours} hours in. " +
                    (known
                        ? "~r~This is her.~s~"
                        : "Put her out for talking to the police."))
                {
                    AltTitle = known ? "~r~it's her" : $"{worker.Loyalty:0}"
                };

                // Re-resolved at the press: the low-loyalty women this menu lists
                // are exactly the ones walk-offs and midnight poaches remove
                // while it sits open — accusing a ghost would still dock the
                // whole roster's loyalty and your name for removing nobody.
                int accusedId = worker.Id;
                item.Activated += (s, e) =>
                {
                    var w = GameState.Current.GetWorker(accusedId);

                    if (w == null)
                    {
                        Notify.Show("~o~She's already gone — nothing to put out.");
                        _accuse.Visible = false;
                        _law.Visible = true;
                        return;
                    }

                    Law.Accuse(w, _spawner);
                    _accuse.Visible = false;
                    _law.Visible = true;
                };

                _accuse.Add(item);
            }
        }
    }
}

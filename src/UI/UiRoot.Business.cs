using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// Upgrades, property and muscle. Split from the main UiRoot file purely to
    /// keep both readable — it is one class.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _upgrades;
        private NativeMenu _property;
        private NativeMenu _muscle;
        private NativeMenu _hire;
        private NativeMenu _phone;
        private NativeMenu _goals;

        private void BuildBusinessMenus()
        {
            _upgrades = new NativeMenu("On The Blade", "UPGRADES");
            _property = new NativeMenu("On The Blade", "PROPERTY");
            _muscle = new NativeMenu("On The Blade", "MUSCLE");
            _hire = new NativeMenu("On The Blade", "HIRE");
            _phone = new NativeMenu("On The Blade", "CALL");
            _goals = new NativeMenu("On The Blade", "WHERE THIS IS GOING");

            _pool.Add(_upgrades);
            _pool.Add(_property);
            _pool.Add(_muscle);
            _pool.Add(_hire);
            _pool.Add(_phone);
            _pool.Add(_goals);

            _main.AddSubMenu(_upgrades);
            _main.AddSubMenu(_property);
            _main.AddSubMenu(_muscle);
            _muscle.AddSubMenu(_hire);

            _upgrades.Shown += (s, e) => RebuildUpgrades();
            _property.Shown += (s, e) => RebuildProperty();
            _muscle.Shown += (s, e) => RebuildMuscle();
            _hire.Shown += (s, e) => RebuildHire();
            _phone.Shown += (s, e) => RebuildPhone();
            _goals.Shown += (s, e) => RebuildGoals();
        }

        private void RebuildGoals()
        {
            _goals.Clear();
            var state = GameState.Current;

            foreach (var m in MilestoneCatalog.All)
            {
                bool done = state.Milestones.Contains(m.Id);
                _goals.Add(new NativeItem(m.Name, m.Blurb)
                {
                    AltTitle = done
                        ? "~g~Done"
                        : m.Reward > 0 ? $"${m.Reward:N0}" : "—",
                    Enabled = false
                });
            }

            int complete = state.Milestones.Count;
            _goals.Add(new NativeItem("Progress", "How much of the arc is behind you.")
            {
                AltTitle = $"{complete}/{MilestoneCatalog.All.Count}",
                Enabled = false
            });

            if (state.TimesCollapsed > 0)
            {
                _goals.Add(new NativeItem("Times you folded",
                    "The operation collapsed under debt this many times.")
                {
                    AltTitle = $"~r~{state.TimesCollapsed}",
                    Enabled = false
                });
            }
        }

        /// <summary>Opened by the phone keybind and by the in-game phone contact.</summary>
        public void TogglePhone() => _phone.Visible = !_phone.Visible;

        // ------------------------------------------------------------------

        private void RebuildUpgrades()
        {
            _upgrades.Clear();
            var state = GameState.Current;

            foreach (var upgrade in UpgradeCatalog.All)
            {
                bool owned = state.HasUpgrade(upgrade.Id);
                bool locked = !string.IsNullOrEmpty(upgrade.Requires) &&
                              !state.HasUpgrade(upgrade.Requires);

                var item = new NativeItem(upgrade.Name, upgrade.Description)
                {
                    AltTitle = owned ? "~g~Owned"
                             : locked ? "~r~Locked"
                             : $"${upgrade.Cost:N0}",
                    Enabled = !owned && !locked
                };

                if (!owned && !locked)
                {
                    var def = upgrade;
                    item.Activated += (s, e) => BuyUpgrade(def);
                }

                _upgrades.Add(item);
            }

            _upgrades.Add(new NativeItem("Roster capacity", "How many people you can carry.")
            {
                AltTitle = $"{state.Roster.Count}/{state.RosterCap}",
                Enabled = false
            });

            if (state.Debt > 0)
            {
                int payable = Math.Min(Game.Player.Money, state.Debt);
                var settle = new NativeItem("Pay down what you owe",
                    $"Debt grows {Config.Current.DebtInterestPerDay * 100:0}% a day and the " +
                    $"operation folds at ${Config.Current.DebtCollapseThreshold:N0}. " +
                    (payable > 0 ? $"Activate to pay ${payable:N0} now." : "You have nothing to pay with."))
                {
                    AltTitle = $"~r~${state.Debt:N0}",
                    Enabled = payable > 0
                };
                settle.Activated += (s, e) => PayDebt();
                _upgrades.Add(settle);
            }
        }

        private void PayDebt()
        {
            var state = GameState.Current;
            int paid = Math.Min(Game.Player.Money, state.Debt);
            if (paid <= 0) return;

            Game.Player.Money -= paid;
            state.Debt -= paid;

            Notify.Show(state.Debt > 0
                ? $"~y~-${paid:N0}~s~ off the debt. ~r~${state.Debt:N0}~s~ still owed."
                : $"~g~Paid off.~s~ You owe nobody anything.");

            RebuildUpgrades();
        }

        private void BuyUpgrade(UpgradeDef upgrade)
        {
            if (Game.Player.Money < upgrade.Cost)
            {
                Notify.Show($"~r~You need ${upgrade.Cost:N0}.");
                return;
            }

            Game.Player.Money -= upgrade.Cost;
            GameState.Current.Upgrades.Add(upgrade.Id);
            Notify.Show($"~g~{upgrade.Name}~s~ sorted.");

            RebuildUpgrades();
        }

        // ------------------------------------------------------------------

        private void RebuildProperty()
        {
            _property.Clear();
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var region in Regions.All)
            {
                bool owned = state.OwnsStash(region.Id);
                string zoneList = string.Join(", ",
                    region.ZoneIds.Select(z => Zones.Get(z)?.Display ?? z));

                var item = new NativeItem(region.StashName,
                    owned
                        ? $"Covers {zoneList}. Activate to travel there."
                        : $"Covers {zoneList}. Heat bleeds off twice as fast there, " +
                          "and everyone off duty rests better.")
                {
                    AltTitle = owned ? "~g~Owned" : $"${region.StashCost:N0}"
                };

                var def = region;
                item.Activated += (s, e) =>
                {
                    if (state.OwnsStash(def.Id)) TravelTo(def);
                    else BuyStash(def);
                };

                _property.Add(item);
            }

            // --- vehicles, one per region ---
            foreach (var region in Regions.All)
            {
                bool owned = state.OwnsVehicle(region.Id);

                var car = new NativeItem($"Car — {region.Display}",
                    owned
                        ? "Running. Demand is up and shifts cost less stamina here."
                        : $"${cfg.VehicleCost:N0}. Demand +{(cfg.VehicleDemandBonus - 1f) * 100:0}% " +
                          $"and {(1f - cfg.VehicleStaminaRelief) * 100:0}% less stamina drain " +
                          "in this region. They stop walking the whole shift.")
                {
                    AltTitle = owned ? "~g~Running" : $"${cfg.VehicleCost:N0}",
                    Enabled = !owned
                };

                if (!owned)
                {
                    var def = region;
                    car.Activated += (s, e) => BuyVehicle(def);
                }

                _property.Add(car);
            }
        }

        private void BuyVehicle(RegionDef region)
        {
            int cost = Config.Current.VehicleCost;
            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0}.");
                return;
            }

            Game.Player.Money -= cost;
            GameState.Current.Vehicles.Add(region.Id);
            Notify.Show($"~g~Car sorted for {region.Display}.");

            RebuildProperty();
        }

        private void BuyStash(RegionDef region)
        {
            if (Game.Player.Money < region.StashCost)
            {
                Notify.Show($"~r~You need ${region.StashCost:N0}.");
                return;
            }

            Game.Player.Money -= region.StashCost;
            GameState.Current.StashHouses.Add(region.Id);
            Notify.Show($"~g~{region.StashName}~s~ is yours.");

            RebuildProperty();
        }

        private void TravelTo(RegionDef region)
        {
            // Refused rather than silently teleporting the player out of a fight.
            if (_missions.Busy)
            {
                Notify.Show("~r~Not while something is going on.");
                return;
            }

            var player = Game.Player.Character;

            GTA.UI.Screen.FadeOut(600);
            Script.Wait(700);

            // Move the vehicle rather than the player when driving, otherwise they
            // arrive on foot and the car is left across the map.
            if (player.IsInVehicle())
            {
                Vehicle vehicle = player.CurrentVehicle;
                vehicle.Position = region.StashPosition;
                vehicle.Heading = region.StashHeading;
            }
            else
            {
                player.Position = region.StashPosition;
                player.Heading = region.StashHeading;
            }

            Script.Wait(300);
            GTA.UI.Screen.FadeIn(600);

            _property.Visible = false;
            _main.Visible = false;
        }

        // ------------------------------------------------------------------

        private void RebuildMuscle()
        {
            _muscle.Clear();
            _muscle.AddSubMenu(_hire);

            var state = GameState.Current;

            if (state.Enforcers.Count == 0)
            {
                _muscle.Add(new NativeItem("(nobody on the payroll)",
                    "Muscle handles routine client trouble so you don't have to drive out."));
            }

            foreach (var enforcer in state.Enforcers.OrderBy(e => e.Id))
            {
                var region = Regions.Get(enforcer.RegionId);

                int deter = (int)Math.Round(enforcer.Skill * Config.Current.MuscleTurfDeterrence);

                var item = new NativeItem(enforcer.Name,
                    $"Covers {region?.Display ?? enforcer.RegionId}. Skill {enforcer.Skill:0} — " +
                    $"turns away about {deter}% of moves on your corners here, and handles " +
                    $"client trouble. Dealt with {enforcer.Handled}. " +
                    $"${enforcer.DailyWage}/day. Activate to let them go.")
                {
                    AltTitle = $"{enforcer.Skill:0}"
                };

                int id = enforcer.Id;
                item.Activated += (s, e) => Dismiss(id);

                _muscle.Add(item);
            }

            _muscle.Add(new NativeItem("Daily wages", "Charged at midnight. Miss it and they walk.")
            {
                AltTitle = $"${state.DailyWageBill:N0}",
                Enabled = false
            });
        }

        private void RebuildHire()
        {
            _hire.Clear();
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var region in Regions.All)
            {
                bool taken = state.Enforcers.Any(e => e.RegionId == region.Id);

                var item = new NativeItem(region.Display,
                    taken
                        ? "Already covered."
                        : $"${cfg.EnforcerHireCost:N0} up front, then ${cfg.EnforcerDailyWage}/day. " +
                          "Turns rivals away from your corners here, and handles routine " +
                          "client trouble. Never covers stings or walk-offs.")
                {
                    AltTitle = taken ? "~g~Covered" : $"${cfg.EnforcerHireCost:N0}",
                    Enabled = !taken
                };

                if (!taken)
                {
                    var def = region;
                    item.Activated += (s, e) => Hire(def);
                }

                _hire.Add(item);
            }
        }

        private void Hire(RegionDef region)
        {
            var state = GameState.Current;
            int cost = Config.Current.EnforcerHireCost;

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0}.");
                return;
            }

            Game.Player.Money -= cost;

            var taken = state.Enforcers.Select(e => e.Name).ToArray();
            var enforcer = EnforcerCatalog.Create(state.NextEnforcerId++, region.Id, taken);
            state.Enforcers.Add(enforcer);

            Notify.Show(
                $"~g~{enforcer.Name}~s~ is covering {region.Display}. Skill {enforcer.Skill:0}.");

            RebuildHire();
        }

        private void Dismiss(int enforcerId)
        {
            var state = GameState.Current;
            var enforcer = state.Enforcers.FirstOrDefault(e => e.Id == enforcerId);
            if (enforcer == null) return;

            state.Enforcers.Remove(enforcer);
            Notify.Show($"~y~{enforcer.Name}~s~ is off the payroll.");

            RebuildMuscle();
        }

        // ------------------------------------------------------------------

        private void RebuildPhone()
        {
            _phone.Clear();
            var state = GameState.Current;

            var report = new NativeItem("Where are we at?",
                "A quick read on the operation.");
            report.Activated += (s, e) => StatusReport();
            _phone.Add(report);

            var recall = new NativeItem("Everyone in",
                "Pulls the whole roster off the street. Posts are kept.")
            {
                Enabled = state.Roster.Any(w => w.State == WorkerState.Working)
            };
            recall.Activated += (s, e) => RecallAll();
            _phone.Add(recall);

            _phone.AddSubMenu(_goals);

            foreach (var region in Regions.All.Where(r => state.OwnsStash(r.Id)))
            {
                var travel = new NativeItem($"Head to the {region.StashName}",
                    "Fast travel.");
                var def = region;
                travel.Activated += (s, e) =>
                {
                    _phone.Visible = false;
                    TravelTo(def);
                };
                _phone.Add(travel);
            }
        }

        private void StatusReport()
        {
            var state = GameState.Current;

            int working = state.Roster.Count(w => w.State == WorkerState.Working);
            int held = Zones.All.Count(z => state.PlayerOwns(z.Id));
            float worstHeat = Zones.All.Select(z => state.GetHeat(z.Id)).DefaultIfEmpty(0f).Max();

            string owed = state.Debt > 0 ? $"~n~~r~You owe ${state.Debt:N0}.~s~" : string.Empty;

            string streams = Subscriptions.Unlocked
                ? $"~n~Next {Subscriptions.Brand} deposit ~g~${Subscriptions.RosterWeeklyEstimate():N0}~s~ " +
                  $"in {Subscriptions.DaysUntilPayout()}d."
                : string.Empty;

            // ~n~ is the game's newline token; \n does not render in notifications.
            Notify.Show(
                $"~b~On The Blade~s~~n~" +
                $"{working}/{state.Roster.Count} out, {held} corners held.~n~" +
                $"Worst heat {worstHeat * 100:0}%. Lifetime ${state.LifetimeTake:N0}." +
                streams + owed, true);
        }

        private void RecallAll()
        {
            var state = GameState.Current;
            int pulled = 0;

            foreach (var worker in state.Roster.Where(w => w.State == WorkerState.Working).ToList())
            {
                worker.ZoneId = null;
                worker.State = WorkerState.OffDuty;
                _spawner.Despawn(worker.Id);
                pulled++;
            }

            Notify.Show($"~y~{pulled}~s~ off the street. Corners stay yours.");
            _phone.Visible = false;
        }
    }
}

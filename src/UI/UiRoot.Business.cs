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

            _bizGroup.AddSubMenu(_upgrades);
            _bizGroup.AddSubMenu(_property);
            _streets.AddSubMenu(_muscle);
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

            // The shared city, visible rather than magic — the one place this
            // mod renders the link, the combined name, and the set's flag.
            _goals.Add(new NativeItem("The other business",
                BladeWorld.WorldLink.StatusLine())
            {
                Enabled = false
            });
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

            // --- borrowing ---
            {
                int max = Money.MaxLoan();
                var cfg = Config.Current;

                // Every amount here has to sit under the ceiling at zero debt, or
                // the menu offers sums that are always refused.
                var borrow = new NativeListItem<string>("Borrow it",
                    "10,000", "20,000", "35,000", "60,000")
                {
                    Description =
                        max <= 0
                            ? "~r~You owe too much already. Nobody is lending you anything.~s~"
                            : $"Cash now, ${cfg.LoanMultiplier:0.00} on the book for every dollar " +
                              $"of it, compounding at {cfg.DebtInterestPerDay * 100:0}% a day like " +
                              $"everything else you owe. Most anyone will give you right now is " +
                              $"${max:N0}. Scroll to the amount, then activate."
                };

                if (max > 0)
                {
                    borrow.Activated += (s, e) =>
                    {
                        int[] amounts = { 10000, 20000, 35000, 60000 };
                        int wanted = amounts[borrow.SelectedIndex];

                        if (Money.Borrow(wanted)) RebuildUpgrades();
                    };
                }
                else
                {
                    borrow.Enabled = false;
                }

                _upgrades.Add(borrow);
            }

            if (state.Debt > 0)
            {
                int payable = Math.Min(Game.Player.Money, state.Debt);
                int collectorsAt = Money.CollectorsThreshold();

                if (state.Debt >= collectorsAt)
                {
                    _upgrades.Add(new NativeItem("They're looking for you",
                        "Past this much owed, people start turning up in person for it. " +
                        "Seeing them off buys days, not forgiveness.")
                    {
                        AltTitle = "~r~collectors",
                        Enabled = false
                    });
                }
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

            // Re-added every rebuild because Clear() above removes them. Adding
            // these in BuildOrdersMenu only worked until the first time this
            // menu was opened.
            if (_orders != null) _muscle.AddSubMenu(_orders);
            if (_gear != null) _muscle.AddSubMenu(_gear);

            var state = GameState.Current;

            if (state.Enforcers.Count == 0)
            {
                _muscle.Add(new NativeItem("(nobody on the payroll)",
                    "Muscle handles routine client trouble so you don't have to drive out."));
            }

            foreach (var enforcer in state.Enforcers.OrderBy(e => e.Id))
            {
                var region = Regions.Get(enforcer.RegionId);

                int deter = (int)Math.Round(enforcer.EffectiveSkill * Config.Current.MuscleTurfDeterrence);

                string carrying = enforcer.IsArmed
                    ? $"Carrying {Armoury.NameOf(enforcer.WeaponId).ToLowerInvariant()}, so he turns " +
                      "out in person when there is trouble here."
                    : "Bare hands — he handles clients over the phone but never turns out.";

                var item = new NativeItem(enforcer.Name,
                    $"Covers {region?.Display ?? enforcer.RegionId}. {carrying} " +
                    $"Turns away about {deter}% of moves on your corners here. " +
                    $"${enforcer.DailyWage}/day. Activate to arm him or let him go.")
                {
                    AltTitle = enforcer.IsInjured()
                        ? $"~r~Hurt {enforcer.InjuryDaysLeft()}d"
                        : enforcer.IsArmed
                            ? $"~g~{enforcer.EffectiveSkill:0}"
                            : $"{enforcer.EffectiveSkill:0}"
                };

                int id = enforcer.Id;
                item.Activated += (s, e) => OpenArmoury(id);

                _muscle.Add(item);
            }

            _muscle.Add(new NativeItem("Daily wages", "Charged at midnight. Miss it and they walk.")
            {
                AltTitle = $"${state.DailyWageBill:N0}",
                Enabled = false
            });
        }

        /// <summary>
        /// Hiring is now a two-part choice: where, and what kind of man. The
        /// region is picked with a list item so the backgrounds are not multiplied
        /// by four regions into a wall of twenty entries.
        /// </summary>
        private void RebuildHire()
        {
            _hire.Clear();
            var state = GameState.Current;

            var open = Regions.All
                .Where(r => !state.Enforcers.Any(e => e.RegionId == r.Id))
                .ToList();

            if (open.Count == 0)
            {
                _hire.Add(new NativeItem("(every region is covered)",
                    "Let somebody go before taking anyone else on.")
                {
                    Enabled = false
                });
                return;
            }

            var names = open.Select(r => r.Display).ToArray();
            var where = new NativeListItem<string>("Where", names)
            {
                Description = "Which ground he covers. One man per region."
            };
            _hire.Add(where);

            foreach (var background in MuscleBackgrounds.All)
            {
                int cost = EnforcerCatalog.HireCost(background.Kind);
                var profile = background;

                float wage = Config.Current.EnforcerDailyWage
                             * (Config.Current.EnforcerWageSkillFloor + profile.StartSkill / 100f)
                             * profile.WageMultiplier;

                var item = new NativeItem(profile.Name,
                    $"{profile.Blurb} Starts around {profile.StartSkill:0} skill and tops out " +
                    $"at {profile.SkillCap:0}. About ${(int)wage:N0} a day to keep.")
                {
                    AltTitle = $"${cost:N0}",
                    Enabled = Game.Player.Money >= cost
                };

                item.Activated += (s, e) =>
                {
                    var region = open[where.SelectedIndex];
                    Hire(region, profile.Kind);
                };

                _hire.Add(item);
            }
        }

        private void Hire(RegionDef region, MuscleBackground background)
        {
            var state = GameState.Current;
            int cost = EnforcerCatalog.HireCost(background);

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0}.");
                return;
            }

            if (state.Enforcers.Any(e => e.RegionId == region.Id))
            {
                Notify.Show($"~r~{region.Display} is already covered.");
                return;
            }

            Game.Player.Money -= cost;

            var taken = state.Enforcers.Select(e => e.Name).ToArray();
            var enforcer = EnforcerCatalog.Create(
                state.NextEnforcerId++, region.Id, taken, background);

            state.Enforcers.Add(enforcer);

            Notify.Show(
                $"~g~{enforcer.Name}~s~ — {enforcer.Profile.Name.ToLowerInvariant()}, covering " +
                $"{region.Display}. Skill {enforcer.Skill:0}, ${enforcer.DailyWage}/day.", true);

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

            // Both of these expire, so they go above everything else — burying a
            // timed decision under the status report is how a player misses one.
            AddExitItems();
            if (state.HasOffer) AddOfferItems();

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

        /// <summary>
        /// Somebody asking to leave. Three answers, all of them costing something —
        /// there is deliberately no option that keeps her, keeps the money and
        /// keeps her loyalty.
        /// </summary>
        private void AddExitItems()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            var worker = state.Roster.FirstOrDefault(w => w.WantsOut);
            if (worker == null) return;

            int daysLeft = cfg.ExitDecisionDays -
                           (GameState.AbsoluteDay() - worker.WantsOutSinceDay);
            if (daysLeft < 0) daysLeft = 0;

            _phone.Add(new NativeItem($"{worker.Name} wants out",
                $"{Crew.ReasonBlurb(worker)} She has been with you " +
                $"{worker.LifetimeHours} hours. Answer within {daysLeft} day(s) or she " +
                "goes on her own and people hear about it.")
            {
                AltTitle = $"~o~{daysLeft}d",
                Enabled = false
            });

            // Every row re-resolves her at the moment it is pressed. The menu can
            // sit open across midnight, and midnight is exactly when overdue
            // exits and poaches remove her — acting on the snapshot would charge
            // retention money, or award send-off reputation, for a woman who is
            // already gone.
            int exitId = worker.Id;

            var letGo = new NativeItem("Let her go",
                $"She leaves on good terms. Builds your name, and there is a fair " +
                "chance she sends somebody your way.");
            letGo.Activated += (s, e) =>
            {
                var her = StillAsking(exitId);
                if (her == null) return;

                _spawner.Despawn(her.Id);
                Crew.Release(her, clean: true);
                RebuildPhone();
            };
            _phone.Add(letGo);

            int cost = Crew.RetentionCost(worker);
            var keep = new NativeItem("Talk her round",
                $"${cost:N0} and +{cfg.RetentionLoyalty:0} loyalty. It buys time, not a " +
                "change of mind — she will ask again.")
            {
                AltTitle = $"${cost:N0}",
                Enabled = Game.Player.Money >= cost
            };
            keep.Activated += (s, e) =>
            {
                var her = StillAsking(exitId);
                if (her == null) return;

                if (Game.Player.Money < Crew.RetentionCost(her))
                {
                    Notify.Show("~r~You can't cover it right now.");
                    return;
                }

                Crew.Retain(her);
                RebuildPhone();
            };
            _phone.Add(keep);

            var refuse = new NativeItem("Tell her no",
                $"Costs nothing now. She stays, loses {cfg.RefusalLoyaltyHit:0} loyalty, " +
                "and below 25 she starts looking for the door on her own terms.");
            refuse.Activated += (s, e) =>
            {
                var her = StillAsking(exitId);
                if (her == null) return;

                Crew.Refuse(her);
                RebuildPhone();
            };
            _phone.Add(refuse);
        }

        /// <summary>The exit conversation's live re-resolution: still on the
        /// books, still asking. Null (with the reason shown) otherwise.</summary>
        private WorkerData StillAsking(int workerId)
        {
            var her = GameState.Current.GetWorker(workerId);

            if (her == null)
            {
                Notify.Show("~o~That resolved itself — she's already gone.");
                RebuildPhone();
                return null;
            }

            if (!her.WantsOut)
            {
                Notify.Show("~o~She's not asking any more.");
                RebuildPhone();
                return null;
            }

            return her;
        }

        /// <summary>
        /// The standing client offer. Cash and favour are separate rows rather than
        /// a toggle, because they are different decisions and the player should be
        /// able to read both prices at once.
        /// </summary>
        private void AddOfferItems()
        {
            var state = GameState.Current;
            var worker = state.GetWorker(state.OfferWorkerId);
            if (worker == null) return;

            int hoursLeft = state.OfferExpiresAtHour - GameState.AbsoluteHour();
            if (hoursLeft < 0) hoursLeft = 0;

            string risk = Clients.RiskLabel(state.OfferRisk)
                          + (Clients.CanScreen ? $" ({state.OfferRisk * 100:0}%)" : string.Empty);

            var connected = Clients.GetConnected((LeverageKind)state.OfferLeverage);

            _phone.Add(new NativeItem($"{state.OfferClientName} is asking",
                $"He wants {worker.Name} for {state.OfferHours} hours. She earns nothing " +
                $"else in that time. Risk: {risk}~s~. " +
                (Clients.CanScreen
                    ? string.Empty
                    : "Buy somebody who checks, under Upgrades, and you'd know how bad. ") +
                $"He waits about {hoursLeft} more hour(s).")
            {
                AltTitle = connected != null ? "~b~connected" : $"~g~${state.OfferPayout:N0}",
                Enabled = false
            });

            // Cash
            var cash = new NativeItem("Take the money",
                $"${state.OfferPayout:N0} when she is back, and one more client who " +
                "comes looking for her.");
            cash.Activated += (s, e) =>
            {
                ClientBook.Accept(takeFavour: false);
                RebuildPhone();
            };
            _phone.Add(cash);

            // Favour, when he is somebody
            if (connected != null)
            {
                var favour = new NativeItem($"Take the favour — {connected.Title}",
                    connected.Offer + " You get nothing in cash.");
                favour.Activated += (s, e) =>
                {
                    ClientBook.Accept(takeFavour: true);
                    RebuildPhone();
                };
                _phone.Add(favour);
            }

            var pass = new NativeItem("Pass",
                "Tell him she isn't available. Costs you nothing but the money.");
            pass.Activated += (s, e) =>
            {
                ClientBook.Decline();
                RebuildPhone();
            };
            _phone.Add(pass);
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
                // All three posting fields, the same set Law custody clears.
                // Nulling only ZoneId left a recalled manager attached:
                // Crew.ManagerOf matches on ManagesZoneId and never looks at
                // State, so she carried on applying her payout and heat
                // multipliers to a corner she had been pulled off. An indoor
                // worker kept IsIndoors true for the same reason, which is what
                // ClientBook.DecayRegulars tests to decide who is posted — so
                // she stopped earning but never bled a regular either.
                worker.ZoneId = null;
                worker.HouseId = null;
                worker.ManagesZoneId = null;
                worker.State = WorkerState.OffDuty;
                _spawner.Despawn(worker.Id);
                pulled++;
            }

            Notify.Show($"~y~{pulled}~s~ off the street. Corners stay yours.");
            _phone.Visible = false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using LemonUI;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Persistence;
using OnTheBlade.Runtime;
using OnTheBlade.Systems;
using OnTheBlade.Systems.Incidents;

namespace OnTheBlade.UI
{
    public partial class UiRoot
    {
        private const string OffDuty = "Off duty";
        private const int BonusCost = 500;

        private readonly ObjectPool _pool;
        private readonly SpawnManager _spawner;
        private readonly MissionController _missions;
        private readonly ProspectSpotter _spotter;

        private NativeItem _status;
        private NativeItem _recruit;

        private readonly NativeMenu _main;
        private readonly NativeMenu _roster;
        private readonly NativeMenu _detail;
        private readonly NativeMenu _territory;

        private int _boundWorkerId = -1;
        private bool _binding;

        public UiRoot(ObjectPool pool, SpawnManager spawner, MissionController missions,
                      ProspectSpotter spotter)
        {
            _pool = pool;
            _spawner = spawner;
            _missions = missions;
            _spotter = spotter;

            _main = new NativeMenu("On The Blade", "OPERATIONS");
            _roster = new NativeMenu("On The Blade", "ROSTER");
            _detail = new NativeMenu("On The Blade", "CREW MEMBER");
            _territory = new NativeMenu("On The Blade", "TERRITORY");

            _pool.Add(_main);
            _pool.Add(_roster);
            _pool.Add(_detail);
            _pool.Add(_territory);

            BuildMain();
            BuildBusinessMenus();
            BuildRivalsMenu();
            BuildDiagnosticsMenu();

            _main.Shown += (s, e) => RefreshStatus();
            _roster.Shown += (s, e) => RebuildRoster();
            _territory.Shown += (s, e) => RebuildTerritory();
        }

        public void Toggle() => _main.Visible = !_main.Visible;

        // ------------------------------------------------------------------

        private void BuildMain()
        {
            _status = new NativeItem("Situation", "Anything currently going wrong.")
            {
                AltTitle = "Quiet",
                Enabled = false
            };
            _main.Add(_status);

            _recruit = new NativeItem(
                "Recruit nearest prospect",
                "Offer a contract to the nearest eligible woman.");

            _recruit.Activated += (s, e) =>
            {
                if (GameState.Current.RosterFull)
                {
                    Notify.Show(
                        $"~r~Your book is full~s~ ({GameState.Current.RosterCap}). " +
                        "Buy a bigger one under Upgrades.");
                    return;
                }

                var result = Recruiter.TryRecruitNearest(_spawner);
                if (result == null)
                {
                    Notify.Show("~r~Nobody nearby is interested.");
                    return;
                }

                var worker = result.Worker;
                Notify.Show(
                    $"~g~{worker.Name}~s~ signed on (tier {worker.Tier}). Assign them a post.");

                if (result.ClaimedFrom != null)
                {
                    Notify.Show(
                        $"~o~She was working for {result.ClaimedFrom.Name}.~s~ " +
                        "Experienced, but she doesn't trust you yet — and word travels.");

                    if (result.Retaliation)
                    {
                        _main.Visible = false;   // no time to be in a menu
                        _missions.TryStart(new RetaliationIncident(
                            result.Where, result.ClaimedFrom.Id, worker.Id, _spawner));
                    }
                }

                RefreshStatus();
            };

            _main.Add(_recruit);
            _main.AddSubMenu(_roster);
            _main.AddSubMenu(_territory);

            var save = new NativeItem("Save now", "Write the current operation to disk.");
            save.Activated += (s, e) =>
            {
                SaveManager.Save(GameState.Current);
                Notify.Show("~g~On The Blade~s~ saved.");
            };
            _main.Add(save);

            // Parented rather than added as an item: the detail menu is reached by
            // picking a worker, so a "detail >>>" row in the roster would open it
            // with nobody bound. Parent still gives Back the right destination.
            _detail.Parent = _roster;
        }

        private void RefreshStatus()
        {
            if (_status != null)
            {
                // A running demand event outranks "quiet" — it is the thing most
                // likely to change what the player does next.
                string live = DemandEvents.Describe(GameState.Current);

                _status.AltTitle = _missions.Busy
                    ? $"~r~{_missions.ActiveTitle}"
                    : live != null ? "~y~something's on" : "~g~Quiet";

                _status.Description = live
                    ?? "Anything currently going wrong. Reputation: "
                       + $"{Reputation.Rank(GameState.Current.Reputation)} "
                       + $"({GameState.Current.Reputation}).";
            }

            if (_recruit == null) return;

            var area = _spotter.CurrentArea;
            bool hotspot = RecruitAreas.IsHotspot(area);
            var scan = Recruiter.Scan(_spawner);

            // The raw zone code is printed on purpose: the hotspot lists in
            // RecruitAreas are unverified, and this is how you check one in-game.
            string code = RecruitAreas.ZoneCode(Game.Player.Character.Position);

            _recruit.AltTitle = scan.Eligible > 0
                ? (hotspot ? $"~g~{scan.Eligible} nearby" : $"{scan.Eligible} nearby")
                : "~r~nobody";

            // When there is nobody, say which filter rejected them. "Nobody
            // nearby" on a crowded street tells the player nothing they can act on.
            string why = scan.Explain();

            _recruit.Description =
                $"{RecruitAreas.Describe(area)} [{code}] — searching " +
                $"{RecruitAreas.SearchRadius(area):0}m. " +
                (why ?? (hotspot
                    ? "Prospects are blipped while you are here."
                    : "Gang turf and Vinewood turn up more people."));
        }

        // ------------------------------------------------------------------

        private void RebuildRoster()
        {
            _roster.Clear();

            var state = GameState.Current;
            if (state.Roster.Count == 0)
            {
                _roster.Add(new NativeItem("(nobody on the books)",
                    "Recruit from the operations menu first."));
                return;
            }

            foreach (var worker in state.Roster.OrderBy(w => w.Id))
            {
                var zone = Zones.Get(worker.ZoneId);
                string earnings = Subscriptions.Unlocked
                    ? $"Street ${worker.LifetimeEarnings:N0}  |  " +
                      $"{Subscriptions.Brand} ${worker.LifetimeSubscriptionEarnings:N0}"
                    : $"Lifetime ${worker.LifetimeEarnings:N0}";

                var item = new NativeItem(
                    worker.Name,
                    $"Tier {worker.Tier}  |  Loyalty {worker.Loyalty:0}  |  " +
                    $"Stamina {worker.Stamina:0}  |  {earnings}")
                {
                    AltTitle = worker.State == WorkerState.InTrouble
                        ? "~r~In trouble"
                        : worker.WasPoached
                            ? $"~o~{zone?.Display ?? OffDuty}"
                            : zone?.Display ?? OffDuty
                };

                int id = worker.Id;
                item.Activated += (s, e) =>
                {
                    BindWorker(id);

                    // Hide the roster first. Opening a menu without closing its
                    // parent leaves both drawing at the same coordinates, which
                    // reads as garbled overlapping text rather than as two menus.
                    _roster.Visible = false;
                    _detail.Visible = true;
                };

                _roster.Add(item);
            }
        }

        // ------------------------------------------------------------------

        private void BindWorker(int workerId)
        {
            var worker = GameState.Current.GetWorker(workerId);
            if (worker == null) return;

            _boundWorkerId = workerId;
            _binding = true;
            _detail.Clear();
            _detail.Name = worker.Name.ToUpperInvariant();

            // --- post assignment ---
            var options = new List<string> { OffDuty };
            options.AddRange(Zones.OpenTo(worker.Tier).Select(z => z.Display));

            var post = new NativeListItem<string>("Post", options.ToArray())
            {
                Description = $"Tier {worker.Tier} unlocks " +
                              $"{Zones.OpenTo(worker.Tier).Count()} of {Zones.All.Count} zones."
            };

            var current = Zones.Get(worker.ZoneId);
            post.SelectedIndex = current == null ? 0 : Math.Max(0, options.IndexOf(current.Display));
            post.ItemChanged += (s, e) =>
            {
                if (_binding) return;
                AssignPost(workerId, e.Object);
            };
            _detail.Add(post);

            // --- shift ---
            // Assignment is persistent; the shift decides which hours it is
            // actually worked. Nights pay 1.6x, days build followers.
            var shiftNames = new[] { "Always", "Days (06-20)", "Nights (20-06)", "Not working" };
            var shift = new NativeListItem<string>("Shift", shiftNames)
            {
                Description = "Nights pay more. Off-shift hours build followers and stamina.",
                SelectedIndex = (int)worker.Shift
            };
            shift.ItemChanged += (s, e) =>
            {
                if (_binding) return;
                SetShift(workerId, (WorkerShift)e.Index);
            };
            _detail.Add(shift);

            // --- traits ---
            _detail.Add(new NativeItem("Traits", TraitDescription(worker))
            {
                AltTitle = Traits.Describe(worker.TraitSet),
                Enabled = false
            });

            // --- read-only status ---
            _detail.Add(new NativeItem("Loyalty", "Scales payout. Below 25 they start looking for the door.")
            {
                AltTitle = $"{worker.Loyalty:0}/100",
                Enabled = false
            });
            _detail.Add(new NativeItem("Stamina", "Drains on shift, recovers off duty.")
            {
                AltTitle = $"{worker.Stamina:0}/100",
                Enabled = false
            });

            if (worker.WasPoached)
            {
                var former = GameState.Current.GetRival(worker.ClaimedFrom);
                _detail.Add(new NativeItem("Taken from",
                    "She came off someone else's corner. They have not forgotten.")
                {
                    AltTitle = $"~o~{former?.Name ?? worker.ClaimedFrom}",
                    Enabled = false
                });
            }

            if (Subscriptions.Unlocked)
            {
                int days = Subscriptions.DaysUntilPayout();

                _detail.Add(new NativeItem($"{Subscriptions.Brand} followers",
                    "Builds while they are off the street, decays while they are on it.")
                {
                    AltTitle = $"{worker.Followers:N0}",
                    Enabled = false
                });

                _detail.Add(new NativeItem("Next deposit",
                    $"Their share of the weekly deposit, due in {days} game day(s).")
                {
                    AltTitle = $"${Subscriptions.WeeklyEstimate(worker):N0}",
                    Enabled = false
                });
            }

            // --- actions ---
            var bonus = new NativeItem($"Pay a bonus (${BonusCost})",
                "A straight cash bonus. Cheapest way to buy loyalty back.");
            bonus.Activated += (s, e) => PayBonus(workerId);
            _detail.Add(bonus);

            var release = new NativeItem("Release from crew",
                "Ends the contract. They leave the roster permanently.");
            release.Activated += (s, e) =>
            {
                Release(workerId);
                _detail.Visible = false;
            };
            _detail.Add(release);

            _binding = false;
        }

        private static string TraitDescription(WorkerData worker)
        {
            var parts = Traits.Split(worker.TraitSet)
                .Select(t => $"{Traits.Name(t)}: {Traits.Blurb(t)}")
                .ToList();

            return parts.Count == 0
                ? "No strong tendencies either way."
                : string.Join("  |  ", parts);
        }

        private void SetShift(int workerId, WorkerShift shift)
        {
            var worker = GameState.Current.GetWorker(workerId);
            if (worker == null) return;

            worker.Shift = shift;

            // Force a re-stream so she leaves or takes up her post immediately
            // rather than at the next scan boundary.
            _spawner.Despawn(workerId);

            Notify.Show($"~g~{worker.Name}~s~ — {worker.ShiftLabel.ToLowerInvariant()}.");
        }

        private void AssignPost(int workerId, string displayName)
        {
            var worker = GameState.Current.GetWorker(workerId);
            if (worker == null) return;

            // Reassigning mid-incident would strand the mission's blip and peds.
            if (worker.State == WorkerState.InTrouble)
            {
                Notify.Show(
                    $"~r~{worker.Name} is in trouble right now.~s~ Deal with that first.");
                return;
            }

            if (displayName == OffDuty)
            {
                worker.ZoneId = null;
                worker.State = WorkerState.OffDuty;
                _spawner.Despawn(workerId);
                Notify.Show($"~y~{worker.Name}~s~ is off duty.");
                return;
            }

            var zone = Zones.All.FirstOrDefault(z => z.Display == displayName);
            if (zone == null) return;

            if (GameState.Current.IsZoneLocked(zone.Id))
            {
                Notify.Show(
                    $"~r~{zone.Display} is shut after the raid.~s~ " +
                    $"{GameState.Current.LockoutDaysLeft(zone.Id)} day(s) left.");
                return;
            }

            if (GameState.Current.IsContested(zone.Id))
            {
                Notify.Show(
                    $"~r~{GameState.Current.OwnerName(zone.Id)} hold {zone.Display}.~s~ " +
                    "Take it first — see Territory.");
                return;
            }

            if (GameState.Current.WorkersIn(zone.Id).Count(w => w.Id != workerId) >= zone.Slots)
            {
                Notify.Show($"~r~{zone.Display}~s~ is full ({zone.Slots} slots).");
                return;
            }

            worker.ZoneId = zone.Id;
            worker.State = WorkerState.Working;
            // Force a re-stream so the ped moves to the new post immediately.
            _spawner.Despawn(workerId);

            Notify.Show($"~g~{worker.Name}~s~ posted to {zone.Display}.");

            // Working a neutral corner claims it. This is the only way to own turf
            // without a fight, and it is what turns on the ownership bonus and
            // rival pressure early — otherwise the starter zones stay inert.
            if (Ownership.IsNeutral(GameState.Current.OwnerOf(zone.Id)))
            {
                GameState.Current.SetOwner(zone.Id, Ownership.Player);
                Notify.Show($"~g~{zone.Display}~s~ is yours now. Expect company.");
            }
        }

        private void PayBonus(int workerId)
        {
            var worker = GameState.Current.GetWorker(workerId);
            if (worker == null) return;

            if (Game.Player.Money < BonusCost)
            {
                Notify.Show("~r~Not enough cash.");
                return;
            }

            Game.Player.Money -= BonusCost;
            worker.Loyalty += 12f;
            worker.Clamp();

            Notify.Show($"~g~{worker.Name}~s~ loyalty now {worker.Loyalty:0}.");
            BindWorker(workerId);
        }

        private void Release(int workerId)
        {
            var worker = GameState.Current.GetWorker(workerId);
            if (worker == null) return;

            _spawner.Despawn(workerId);
            GameState.Current.Roster.Remove(worker);
            Notify.Show($"~y~{worker.Name}~s~ left the crew.");
        }

        // ------------------------------------------------------------------

        private void RebuildTerritory()
        {
            _territory.Clear();
            var state = GameState.Current;

            foreach (var zone in Zones.All)
            {
                int staffed = state.WorkersIn(zone.Id).Count();
                float heat = state.GetHeat(zone.Id);
                string heatColour = heat > 0.6f ? "r" : heat > 0.3f ? "o" : "g";

                bool contested = state.IsContested(zone.Id);
                var holder = contested ? state.GetRival(state.OwnerOf(zone.Id)) : null;

                string description =
                    $"{state.OwnerName(zone.Id)}  |  Tier {zone.MinTier}+  |  " +
                    $"Demand x{zone.Demand:0.00}  |  Heat ~{heatColour}~{heat * 100:0}%";

                if (contested)
                {
                    // Rival-held corners are the only actionable entries here.
                    var take = new NativeItem(zone.Display,
                        description +
                        $"  |  Strength {holder?.Strength:0}, aggression {holder?.Aggression * 100:0}%. " +
                        "Activate to go take it.")
                    {
                        AltTitle = $"~r~{state.OwnerName(zone.Id)}"
                    };

                    string zoneId = zone.Id;
                    string rivalId = state.OwnerOf(zone.Id);
                    take.Activated += (s, e) => StartTakeover(zoneId, rivalId);

                    _territory.Add(take);
                }
                else if (state.IsZoneLocked(zone.Id))
                {
                    _territory.Add(new NativeItem(zone.Display,
                        description + "  |  Shut after a raid. Nobody can be posted here yet.")
                    {
                        AltTitle = $"~r~Raided {state.LockoutDaysLeft(zone.Id)}d",
                        Enabled = false
                    });
                }
                else
                {
                    // Owned or neutral corners are where a bribe is worth paying.
                    int bribe = BribeCost(zone.Id);
                    var item = new NativeItem(zone.Display,
                        description +
                        (bribe > 0
                            ? $"  |  Activate to pay ${bribe:N0} and lose " +
                              $"{Config.Current.BribeHeatCleared * 100:0} points of heat."
                            : "  |  Nothing to bribe away."))
                    {
                        AltTitle = state.PlayerOwns(zone.Id)
                            ? $"~g~Yours {staffed}/{zone.Slots}"
                            : $"Neutral {staffed}/{zone.Slots}",
                        Enabled = bribe > 0
                    };

                    if (bribe > 0)
                    {
                        string zoneId = zone.Id;
                        item.Activated += (s, e) => PayBribe(zoneId);
                    }

                    _territory.Add(item);
                }
            }

            _territory.Add(new NativeItem("Street take", "Lifetime, from working the corners.")
            {
                AltTitle = $"${state.LifetimeStreetTake:N0}",
                Enabled = false
            });

            if (Subscriptions.Unlocked)
            {
                _territory.Add(new NativeItem($"{Subscriptions.Brand} take",
                    $"Lifetime, from weekly deposits. Next one is worth about " +
                    $"${Subscriptions.RosterWeeklyEstimate():N0}.")
                {
                    AltTitle = $"${state.LifetimeSubscriptionTake:N0}",
                    Enabled = false
                });
            }

            _territory.Add(new NativeItem("Lifetime total", "Both streams combined.")
            {
                AltTitle = $"${state.LifetimeTake:N0}",
                Enabled = false
            });

            if (state.PoachedCount > 0)
            {
                float worst = state.Rivals.Where(r => !r.IsBroken)
                    .Select(r => r.Aggression)
                    .DefaultIfEmpty(0f)
                    .Max();

                _territory.Add(new NativeItem("Taken off other crews",
                    "Every one of these raised the temperature with all of them. " +
                    $"Angriest crew is at {worst * 100:0}%.")
                {
                    AltTitle = $"~o~{state.PoachedCount}",
                    Enabled = false
                });
            }
        }

        /// <summary>
        /// Scales with how hot the corner actually is, so a bribe is cheap early
        /// and expensive exactly when you need it. Zero if there is no heat.
        /// </summary>
        private static int BribeCost(string zoneId)
        {
            float heat = GameState.Current.GetHeat(zoneId);
            if (heat <= 0.02f) return 0;

            float cleared = Math.Min(heat, Config.Current.BribeHeatCleared);
            return (int)Math.Round(cleared * Config.Current.BribeCostPerHeat);
        }

        private void PayBribe(string zoneId)
        {
            int cost = BribeCost(zoneId);
            if (cost <= 0) return;

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0} in hand.~s~ Nobody takes an IOU.");
                return;
            }

            Game.Player.Money -= cost;
            GameState.Current.AddHeat(zoneId, -Config.Current.BribeHeatCleared);

            var zone = Zones.Get(zoneId);
            Notify.Show($"~g~{zone?.Display}~s~ cooled off. That cost ${cost:N0}.");
            RebuildTerritory();
        }

        private void StartTakeover(string zoneId, string rivalId)
        {
            if (!_missions.Ready)
            {
                Notify.Show("~r~Something else needs handling first.");
                return;
            }

            if (!_missions.TryStart(new TurfBattleIncident(zoneId, rivalId, true, _spawner))) return;

            // Close the whole stack, not just this submenu — the blip is up and
            // the player needs to be driving, not reading a menu.
            _territory.Visible = false;
            _main.Visible = false;
        }
    }
}

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
        private readonly NativeMenu _bizGroup;
        private readonly NativeMenu _streets;

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

            // The groups — every feature kept, one level deeper: the business
            // (what you own) and the streets (who you deal with).
            _bizGroup = new NativeMenu("On The Blade", "THE BUSINESS");
            _streets = new NativeMenu("On The Blade", "THE STREETS");

            _pool.Add(_main);
            _pool.Add(_roster);
            _pool.Add(_detail);
            _pool.Add(_territory);
            _pool.Add(_bizGroup);
            _pool.Add(_streets);

            BuildMain();
            BuildBusinessMenus();
            BuildHousesMenu();
            BuildContentHouseMenu();  // after BuildHousesMenu — it parents to _houses
            BuildLawMenu();
            BuildArmouryMenu();     // after BuildBusinessMenus — it parents to _muscle
            BuildOrdersMenu();      // likewise
            BuildInvestMenu();
            BuildRivalsMenu();
            BuildDiagnosticsMenu();
            BuildStreetMenu();

            _main.Shown += (s, e) => RefreshStatus();
            _roster.Shown += (s, e) => RebuildRoster();
            _territory.Shown += (s, e) => RebuildTerritory();

            // Re-bind on show so backing out of Invest does not leave a stale tier,
            // stamina or loyalty on the row behind it.
            _detail.Shown += (s, e) =>
            {
                if (_boundWorkerId >= 0) BindWorker(_boundWorkerId);
            };
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

            // Recruiting left this menu. Signing somebody happens face to
            // face now — walk up to an eligible woman and press the talk key.
            // The row survives as the scan readout, because "why is nobody
            // eligible here" is still the question this menu can answer best.
            _recruit = new NativeItem(
                "Prospects",
                "Walk up to somebody and press the talk key to offer a contract.")
            {
                Enabled = false
            };

            _main.Add(_recruit);
            _main.AddSubMenu(_roster);
            _main.AddSubMenu(_territory);
            _main.AddSubMenu(_bizGroup);
            _main.AddSubMenu(_streets);

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

                // The COMBINED street name — the drug game counts here too.
                int streetName = BladeWorld.WorldLink.Reputation();

                _status.Description = live
                    ?? "Anything currently going wrong. Reputation: "
                       + $"{Reputation.Rank(streetName)} "
                       + $"({streetName}, both businesses).";
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
                    // If she left while the roster sat open, BindWorker would
                    // no-op and the detail menu would open still bound to the
                    // PREVIOUS woman — bonus and release would hit the wrong one.
                    if (GameState.Current.GetWorker(id) == null)
                    {
                        Notify.Show("~o~She's no longer on the books.");
                        RebuildRoster();
                        return;
                    }

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
            // Corners and houses share one list: a worker is in exactly one place,
            // and two separate selectors would let you set both and then have to
            // guess which the player meant.
            var options = new List<string> { OffDuty };
            options.AddRange(Zones.OpenTo(worker.Tier).Select(z => z.Display));

            var openHouses = Houses.All
                .Where(h => GameState.Current.OwnsHouse(h.Id))
                .ToList();
            options.AddRange(openHouses.Select(h => h.Display));

            // Running a corner is a posting like any other, so it belongs in the
            // same selector. Only offered on ground you hold and only to somebody
            // senior enough, which keeps the list short.
            string managerReason;
            var manageable = Crew.CanManage(worker, out managerReason)
                ? Zones.All.Where(z => GameState.Current.PlayerOwns(z.Id)).ToList()
                : new List<ZoneDef>();

            options.AddRange(manageable.Select(z => ManageLabel(z)));

            var post = new NativeListItem<string>("Post", options.ToArray())
            {
                Description = $"Tier {worker.Tier} unlocks " +
                              $"{Zones.OpenTo(worker.Tier).Count()} of {Zones.All.Count} corners." +
                              (openHouses.Count > 0
                                  ? " Indoors pays more and draws almost no heat, but the rooms run out."
                                  : string.Empty) +
                              (manageable.Count > 0
                                  ? " Running a corner earns her nothing and everyone else more."
                                  : string.Empty)
            };

            var current = Zones.Get(worker.ZoneId);
            var currentHouse = Houses.Get(worker.HouseId);
            var managed = Zones.Get(worker.ManagesZoneId);

            post.SelectedIndex =
                managed != null ? Math.Max(0, options.IndexOf(ManageLabel(managed)))
                : currentHouse != null ? Math.Max(0, options.IndexOf(currentHouse.Display))
                : current == null ? 0
                : Math.Max(0, options.IndexOf(current.Display));
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
                Description = "Nights pay more. Off-shift hours build followers and stamina." +
                              (Config.Current.LateWindowEnabled
                                  ? $" {LateWindow.Label()} pays more again and roughly doubles " +
                                    "the chance of trouble — anyone out then is in it."
                                  : string.Empty),
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

            // --- experience toward the next tier ---
            // Shown even when it cannot currently progress, because "why is she
            // stuck" is exactly the question this row exists to answer.
            if (Progression.IsMaxTier(worker))
            {
                _detail.Add(new NativeItem("Experience",
                    "Tier 3 is as far as this goes. Every corner is open to her.")
                {
                    AltTitle = "~g~Tier 3",
                    Enabled = false
                });
            }
            else
            {
                int needed = Progression.HoursNeeded(worker);
                bool loyalStuck = worker.Loyalty < Config.Current.TierUpLoyalty;

                _detail.Add(new NativeItem("Experience",
                    $"Street hours toward tier {Progression.NextTier(worker)}. Off-duty " +
                    "hours build followers instead, so they do not count here. " +
                    (loyalStuck
                        ? $"~o~She will not step up below {Config.Current.TierUpLoyalty:0} loyalty.~s~"
                        : "Or pay for the wardrobe under Invest."))
                {
                    AltTitle = $"{worker.HoursWorked}/{needed} hrs",
                    Enabled = false
                });
            }

            // --- inside ---
            if (worker.IsJailed())
            {
                _detail.Add(new NativeItem("In custody",
                    $"{worker.JailDaysLeft()} day(s) left. She earns nothing and cannot be " +
                    "posted. A lawyer can buy the rest of it — see The law.")
                {
                    AltTitle = $"~r~{worker.JailDaysLeft()}d",
                    Enabled = false
                });
            }

            // --- running a corner ---
            if (worker.IsManager)
            {
                var run = Zones.Get(worker.ManagesZoneId);
                _detail.Add(new NativeItem("Running",
                    $"She works nobody and earns nothing herself. Everyone on " +
                    $"{run?.Display} earns {(Crew.PayoutBonus(worker.ManagesZoneId) - 1f) * 100:0}% " +
                    $"more and draws {(1f - Crew.HeatMultiplier(worker.ManagesZoneId)) * 100:0}% " +
                    "less heat, scaled by how she is doing.")
                {
                    AltTitle = $"~b~{run?.Display}",
                    Enabled = false
                });
            }

            // --- how long she has been at this ---
            {
                var cfg = Config.Current;
                bool tired = worker.LifetimeHours >= cfg.BurnoutHours;

                _detail.Add(new NativeItem("Time in",
                    tired
                        ? "~o~She has done more than enough hours. She could ask to get " +
                          "out any day now.~s~"
                        : "Hours across her whole time with you. Past " +
                          $"{cfg.BurnoutHours} she starts thinking about getting out.")
                {
                    AltTitle = tired
                        ? $"~o~{worker.LifetimeHours} hrs"
                        : $"{worker.LifetimeHours}/{cfg.BurnoutHours} hrs",
                    Enabled = false
                });
            }

            // --- clients ---
            if (worker.IsOnBooking)
            {
                int left = worker.BookingEndsAtHour - GameState.AbsoluteHour();
                if (left < 0) left = 0;

                _detail.Add(new NativeItem("With a client",
                    $"Out with {worker.BookingClientName}. She earns nothing else until " +
                    "she is back, and you find out how it went when she is.")
                {
                    AltTitle = $"~b~{left}h left",
                    Enabled = false
                });
            }

            _detail.Add(new NativeItem("Regulars",
                worker.Regulars > 0
                    ? $"Clients who come back for her. Pays about " +
                      $"${ClientBook.RegularIncome(worker):N0} on top of every hour she works, " +
                      "and drops off if you bench her."
                    : "Nobody asks for her yet. They build while she works, faster at " +
                      "higher tiers.")
            {
                AltTitle = worker.Regulars > 0
                    ? $"~g~{worker.Regulars}/{Config.Current.MaxRegulars}"
                    : $"0/{Config.Current.MaxRegulars}",
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
                    "Builds while they are off the street, decays while they are on it." +
                    (worker.HasStudio ? " ~g~Studio time is paid for.~s~" : string.Empty))
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

                // Followers cap and then produce nothing. Spending them turns the
                // ceiling into something you can actually use.
                string cashReason;
                bool canCash = Subscriptions.CanCashOut(worker, out cashReason);
                int cashValue = Subscriptions.CashOutValue(worker);

                var cashOut = new NativeItem("Sell the audience",
                    canCash
                        ? $"Cashes in {Config.Current.FollowerCashOutShare * 100:0}% of her " +
                          $"followers for ${cashValue:N0} now. Costs her " +
                          $"{Config.Current.FollowerCashOutLoyalty:0} loyalty — she built it."
                        : cashReason)
                {
                    AltTitle = canCash ? $"~g~${cashValue:N0}" : "~r~—",
                    Enabled = canCash
                };

                cashOut.Activated += (s, e) =>
                {
                    // Re-resolved like the bonus and release rows beside it — the
                    // cash-out must not pay real money for the audience of a woman
                    // who left the books while this menu was open.
                    var her = GameState.Current.GetWorker(workerId);

                    if (her == null)
                    {
                        Notify.Show("~o~She's no longer on the books.");
                        return;
                    }

                    if (Subscriptions.CashOut(her) > 0) BindWorker(workerId);
                };
                _detail.Add(cashOut);
            }

            // --- on camera ---
            // Three numbers that are invisible without a readout, and all three
            // read as bugs if the player cannot see them: what she makes here,
            // where her audience settles rather than where it is now, and the
            // regulars she keeps but earns nothing from.
            var contentHouse = Houses.Get(worker.HouseId);
            if (contentHouse != null && contentHouse.IsContentHouse)
            {
                _detail.Add(new NativeItem("On camera",
                    $"Producing at {contentHouse.Display}. She sees no clients at all " +
                    "while she is in there, and nobody can book her.")
                {
                    AltTitle = $"~g~${ContentHouses.ProjectedHourly(worker, contentHouse):N0}/hr",
                    Enabled = false
                });

                float settling = ContentHouses.ProjectedStock(worker);

                _detail.Add(new NativeItem("Audience",
                    $"Settling near {settling:N0} at this rate — she spends it as fast as " +
                    "she makes it in there. Every follower is worth more here than in the " +
                    "weekly deposit, which she does not get while she is producing.")
                {
                    AltTitle = $"{worker.Followers:N0} now",
                    Enabled = false
                });

                if (worker.Regulars > 0)
                {
                    int forgone = ClientBook.RegularIncome(worker);

                    _detail.Add(new NativeItem("Not seeing clients",
                        $"She keeps the {worker.Regulars} regulars she has — they do not " +
                        $"drift while she is posted — but she earns nothing from them in " +
                        $"there. That is about ${forgone:N0} an hour she is not making, and " +
                        "it is why the good earners belong on a corner.")
                    {
                        AltTitle = $"~o~-${forgone:N0}/hr",
                        Enabled = false
                    });
                }
            }

            // --- actions ---
            // Re-added on every bind because the detail menu is cleared and rebuilt
            // per worker; the submenu itself reads _boundWorkerId when it opens.
            var invest = _detail.AddSubMenu(_invest);
            invest.Title = "Invest in her";
            invest.Description =
                "Money spent on this one person: the step up a tier, a doctor, " +
                "studio time, papers. None of it survives her leaving the crew.";

            var bonus = new NativeItem($"Send a bonus with somebody (${BonusCost})",
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

            // Posting somebody who is inside would silently do nothing — every
            // working check already excludes her — and look like a broken menu.
            if (worker.IsJailed())
            {
                Notify.Show(
                    $"~r~{worker.Name} is in custody.~s~ {worker.JailDaysLeft()} day(s), " +
                    "or pay a lawyer under The law.");
                return;
            }

            if (displayName == OffDuty)
            {
                worker.ZoneId = null;
                worker.HouseId = null;
                worker.ManagesZoneId = null;
                worker.State = WorkerState.OffDuty;
                _spawner.Despawn(workerId);
                Notify.Show($"~y~{worker.Name}~s~ is off duty.");
                return;
            }

            var managedZone = Zones.All.FirstOrDefault(z => ManageLabel(z) == displayName);
            if (managedZone != null)
            {
                AssignManager(worker, managedZone);
                return;
            }

            var house = Houses.All.FirstOrDefault(h => h.Display == displayName);
            if (house != null)
            {
                AssignHouse(worker, house);
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
            worker.HouseId = null;      // a corner and a room are mutually exclusive
            worker.ManagesZoneId = null;
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

        /// <summary>Label for a manage-this-corner entry in the post selector.</summary>
        private static string ManageLabel(ZoneDef zone) => $"Run {zone.Display}";

        /// <summary>
        /// Puts her in charge of a corner instead of on it. One manager per zone —
        /// a second would stack the bonus and make the answer "promote everybody".
        /// </summary>
        private void AssignManager(WorkerData worker, ZoneDef zone)
        {
            var state = GameState.Current;

            string reason;
            if (!Crew.CanManage(worker, out reason))
            {
                Notify.Show($"~r~{reason}");
                return;
            }

            if (!state.PlayerOwns(zone.Id))
            {
                Notify.Show($"~r~You don't hold {zone.Display}.");
                return;
            }

            var existing = Crew.ManagerOf(zone.Id);
            if (existing != null && existing.Id != worker.Id)
            {
                Notify.Show($"~r~{existing.Name} already runs {zone.Display}.");
                return;
            }

            worker.ZoneId = null;
            worker.HouseId = null;
            worker.ManagesZoneId = zone.Id;
            worker.State = WorkerState.Working;
            _spawner.Despawn(worker.Id);

            var cfg = Config.Current;
            Notify.Show(
                $"~g~{worker.Name} runs {zone.Display} now.~s~ She earns nothing herself — " +
                $"everyone working it earns up to {(cfg.ManagerPayoutBonus - 1f) * 100:0}% more " +
                $"and draws {(1f - cfg.ManagerHeatReduction) * 100:0}% less heat.", true);
        }

        /// <summary>
        /// Moves a worker indoors. Rooms are a hard cap rather than a falloff, so
        /// unlike a corner this can simply be refused.
        /// </summary>
        private void AssignHouse(WorkerData worker, HouseDef house)
        {
            var state = GameState.Current;

            if (!state.OwnsHouse(house.Id))
            {
                Notify.Show($"~r~You don't own {house.Display}.");
                return;
            }

            if (state.IsHouseLocked(house.Id))
            {
                Notify.Show(
                    $"~r~{house.Display} is shut after the raid.~s~ " +
                    $"{state.HouseLockDaysLeft(house.Id)} day(s) left.");
                return;
            }

            // A content house below the condition line is not workable either.
            // Without this she could be posted in, earn exactly nothing, and
            // still count as a resident for heat — and because the eject only
            // fires on the transition into shut, she would never be turned back
            // out. Fourteen game days of that ends in a raid, a fine and a
            // five-day lockout for zero income throughout.
            if (house.IsContentHouse && state.IsContentShut(house.Id))
            {
                Notify.Show(
                    $"~r~{house.Display} is not fit to work in.~s~ " +
                    $"Putting it right costs ${ContentHouses.RepairCost(house):N0}.");
                return;
            }

            if (state.WorkersInHouse(house.Id).Any(w => w.Id == worker.Id))
                return;   // already there; nothing to say

            if (state.RoomsFree(house) <= 0)
            {
                Notify.Show($"~r~{house.Display} is full~s~ ({house.Rooms} rooms).");
                return;
            }

            worker.ZoneId = null;
            worker.ManagesZoneId = null;
            worker.HouseId = house.Id;
            worker.State = WorkerState.Working;

            // She is inside now — pull the street ped rather than leaving one
            // standing on a corner she no longer works.
            _spawner.Despawn(worker.Id);

            Notify.Show(
                $"~g~{worker.Name}~s~ is working {house.Display}. " +
                $"{state.RoomsFree(house)} room(s) left.");
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

            // The remote rate. Handed over in person it is worth twice this —
            // that differential, not availability, is the reason to drive out.
            worker.Loyalty += Config.Current.RemoteBonusLoyalty;
            worker.Clamp();

            Notify.Show($"~g~{worker.Name}~s~ loyalty now {worker.Loyalty:0}.");
            BindWorker(workerId);
        }

        private void Release(int workerId)
        {
            var worker = GameState.Current.GetWorker(workerId);
            if (worker == null) return;

            _spawner.Despawn(workerId);
            GameState.Current.ReleaseGuardsFor(worker.Id);
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
                    // Pricing is only offered on ground you actually hold — you do
                    // not set the rate on somebody else's corner.
                    if (state.PlayerOwns(zone.Id))
                    {
                        var levels = new[] { "Cut price", "Going rate", "Premium" };
                        var price = new NativeListItem<string>($"  {zone.Display} rate", levels)
                        {
                            Description = Pricing.Blurb(Pricing.LevelOf(zone.Id)),
                            SelectedIndex = (int)Pricing.LevelOf(zone.Id) + 1
                        };

                        string priceZone = zone.Id;
                        price.ItemChanged += (s, e) =>
                        {
                            Pricing.SetLevel(priceZone, (PriceLevel)(e.Index - 1));
                            var set = Pricing.LevelOf(priceZone);
                            Notify.Show(
                                $"~g~{Zones.Get(priceZone)?.Display}~s~ — {Pricing.Name(set).ToLowerInvariant()}.");
                            RebuildTerritory();
                        };

                        _territory.Add(price);

                        // Money spent on this specific corner. Only on ground you
                        // hold, and lost with it if a crew takes it.
                        foreach (var up in ZoneUpgrades.All)
                        {
                            bool owned = ZoneUpgrades.Owns(zone.Id, up.Id);
                            bool afford = Game.Player.Money >= up.Cost;

                            var row = new NativeItem($"    {up.Name}",
                                owned
                                    ? up.Description + "  Bought for this corner."
                                    : up.Description +
                                      (afford
                                          ? "  Lost if you lose the corner."
                                          : $"  ~r~You need ${up.Cost:N0}."))
                            {
                                AltTitle = owned ? "~g~Done" : $"${up.Cost:N0}",
                                Enabled = !owned && afford
                            };

                            if (!owned && afford)
                            {
                                string uz = zone.Id;
                                var ud = up;
                                row.Activated += (s, e) => BuyZoneUpgrade(uz, ud);
                            }

                            _territory.Add(row);
                        }
                    }

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

        private void BuyZoneUpgrade(string zoneId, ZoneUpgradeDef upgrade)
        {
            if (Game.Player.Money < upgrade.Cost)
            {
                Notify.Show($"~r~You need ${upgrade.Cost:N0}.");
                return;
            }

            if (!GameState.Current.PlayerOwns(zoneId))
            {
                Notify.Show("~r~That corner isn't yours to spend on.");
                return;
            }

            Game.Player.Money -= upgrade.Cost;
            ZoneUpgrades.Buy(zoneId, upgrade.Id);

            Notify.Show(
                $"~g~{Zones.Get(zoneId)?.Display} — {upgrade.Name.ToLowerInvariant()}.~s~ " +
                "Sorted, for as long as you hold it.");

            RebuildTerritory();
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

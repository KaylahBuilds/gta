using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// Standing orders and what the crew are equipped with. Split from the main
    /// UiRoot file purely to keep both readable — it is one class.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _orders;
        private NativeMenu _gear;

        private void BuildOrdersMenu()
        {
            _orders = new NativeMenu("On The Blade", "HOLDING THE BLOCK");
            _gear = new NativeMenu("On The Blade", "KITTING THEM OUT");

            _pool.Add(_orders);
            _pool.Add(_gear);

            // Parent, not AddSubMenu. RebuildMuscle calls _muscle.Clear() on
            // every Shown, which destroyed the two rows added here — both menus
            // were unreachable in the shipped build. The rows are added by
            // RebuildMuscle instead, which is the only place that survives the
            // clear; Parent is still set here so Back has somewhere to go.
            _orders.Parent = _muscle;
            _gear.Parent = _muscle;

            _orders.Shown += (s, e) => RebuildOrders();
            _gear.Shown += (s, e) => RebuildGear();
        }

        // ------------------------------------------------------------------

        private void RebuildOrders()
        {
            _orders.Clear();
            var state = GameState.Current;
            var cfg = Config.Current;

            _orders.Add(new NativeItem("What this is",
                "Pay the crew to fight for a corner instead of you driving out. " +
                "They do not do it for free and they do not do it for the credit — " +
                "a corner held while you were elsewhere is worth about a third the " +
                "reputation of one you held yourself.")
            {
                Enabled = false
            });

            foreach (var zone in Zones.All)
            {
                if (!state.PlayerOwns(zone.Id))
                {
                    _orders.Add(new NativeItem(zone.Display,
                        "Not yours. Nothing to hold.")
                    {
                        AltTitle = "~o~not yours",
                        Enabled = false
                    });
                    continue;
                }

                var region = Regions.ForZone(zone.Id);
                var enforcer = StandingOrders.Holder(zone.Id);

                // The worst crew that might come, so the readout is a promise
                // rather than an average.
                var worst = state.Rivals
                    .Where(r => !r.IsBroken && !r.IsAllied() && !state.HasTruce(r.Id))
                    .OrderByDescending(r => StandingOrders.RivalRating(r))
                    .FirstOrDefault();

                var order = StandingOrders.OrderFor(zone.Id);
                var names = new[] { "Off", "Hold it", "Hold it and take ground" };
                // Level two is double the retainer, and buys the chance that a
                // night that went one-sided ends with you owning a corner of
                // theirs — and with them angrier about it.

                var item = new NativeListItem<string>(zone.Display, names)
                {
                    SelectedIndex = (int)order
                };

                if (enforcer == null)
                {
                    item.Description = $"Nobody free covers {region?.Display}. Hire somebody " +
                                       "there — a man minding one woman is standing next to " +
                                       "her, not on this corner. Nothing is charged for an " +
                                       "order with no one to carry it out.";

                    // Disabled rather than switchable: an order here buys
                    // nothing, and leaving it green and clickable was how a
                    // player ended up paying a retainer on a corner that had
                    // never had anybody on it.
                    item.Enabled = false;
                    item.AltTitle = "~r~nobody covers it";
                    _orders.Add(item);
                    continue;
                }

                if (enforcer.IsInjured())
                {
                    item.Description = $"{enforcer.Name} is laid up for " +
                                       $"{enforcer.InjuryDaysLeft()} more day(s). The order " +
                                       "stands, and nothing is charged while he is out.";
                    item.AltTitle = $"~r~hurt {enforcer.InjuryDaysLeft()}d";
                }
                else if (worst == null)
                {
                    item.Description = $"{enforcer.Name} covers this. Nobody is coming for it " +
                                       "right now — every crew is broken, paid off or with you.";
                }
                else
                {
                    float ratio = StandingOrders.Ratio(zone.Id, worst);
                    var verdict = StandingOrders.Verdict(zone.Id, worst);

                    // With the order off, Verdict short-circuits — show what it
                    // *would* say, so the player can decide before committing.
                    if (order == StandingOrder.Off)
                        verdict = PreviewVerdict(ratio);

                    item.Description =
                        $"{enforcer.Name} vs {worst.Name} at their worst: " +
                        $"{StandingOrders.CrewRating(zone.Id):0} against " +
                        $"{StandingOrders.RivalRating(worst):0} — " +
                        $"{StandingOrders.VerdictLabel(verdict)}~s~ (x{ratio:0.00}). " +
                        $"${cfg.OrderRetainerPerDay:N0}/day, about " +
                        $"${cfg.OrderBattleFee + (int)(worst.Strength * cfg.OrderFeePerStrength):N0} " +
                        "each time they fight.";
                }

                // Only when somebody is actually standing there. The injured
                // branch above sets its own AltTitle and this overwrote it, so the
                // row read a green "holding $600/d" while RetainerBill charged
                // nothing and the row's own description said nothing was charged.
                if (!enforcer.IsInjured())
                {
                    item.AltTitle = order == StandingOrder.Off
                        ? "~o~off"
                        : order == StandingOrder.Defend
                            ? $"~g~holding ~s~${cfg.OrderRetainerPerDay:N0}/d"
                            : $"~g~holding + taking ~s~${cfg.OrderRetainerPerDay * 2:N0}/d";
                }

                string zid = zone.Id;
                item.ItemChanged += (s, e) =>
                {
                    StandingOrders.SetOrder(zid, (StandingOrder)e.Index);

                    Notify.Show((StandingOrder)e.Index == StandingOrder.Off
                        ? $"~y~{Zones.Get(zid)?.Display}~s~ — they stand down. It's on you again."
                        : $"~g~{Zones.Get(zid)?.Display}~s~ — they hold it now.");

                    RebuildOrders();
                };

                _orders.Add(item);
            }

            _orders.Add(new NativeItem("Retainer",
                "Charged at midnight for every corner on an order, on top of wages. " +
                "Can't cover it and it becomes debt like everything else.")
            {
                AltTitle = $"~y~${StandingOrders.RetainerBill():N0}/day",
                Enabled = false
            });
        }

        /// <summary>What the verdict would be if you switched the order on.</summary>
        private static OrderVerdict PreviewVerdict(float ratio)
        {
            var cfg = Config.Current;

            if (ratio >= cfg.OrderGuaranteeRatio) return OrderVerdict.Guaranteed;
            if (ratio >= cfg.OrderHoldRatio) return OrderVerdict.HeldWithCost;
            if (ratio >= cfg.OrderCoinFlipRatio) return OrderVerdict.CoinFlip;
            return OrderVerdict.WillLose;
        }

        // ------------------------------------------------------------------

        private void RebuildGear()
        {
            _gear.Clear();
            var state = GameState.Current;

            _gear.Add(new NativeItem("Why bother",
                "Winning is not the problem. Not losing people is — a crew that " +
                "holds every corner and spends half its life in hospital is still " +
                "on the wage and still deterring nobody. Kit shifts the odds a " +
                "little and the casualties a lot; who you hired decides the verdict.")
            {
                Enabled = false
            });

            foreach (var region in Regions.All)
            {
                var enforcer = state.EnforcerInRegion(region.Id);

                _gear.Add(new NativeItem($"— {region.Display} —",
                    enforcer == null
                        ? "Nobody covers this region yet."
                        : $"{enforcer.Name}, {enforcer.Profile.Name.ToLowerInvariant()}, " +
                          $"carrying {Armoury.NameOf(enforcer.WeaponId).ToLowerInvariant()}.")
                {
                    AltTitle = enforcer == null
                        ? "~r~empty"
                        : $"rating {StandingOrders.CrewRating(RegionZone(region)):0}",
                    Enabled = false
                });

                AddGearRow(region, armour: true);
                AddGearRow(region, armour: false);
            }
        }

        /// <summary>Any zone in the region, for a rating readout.</summary>
        private static string RegionZone(RegionDef region)
        {
            return region.ZoneIds != null && region.ZoneIds.Length > 0 ? region.ZoneIds[0] : null;
        }

        private void AddGearRow(RegionDef region, bool armour)
        {
            var state = GameState.Current;

            int level = armour ? state.ArmourLevel(region.Id) : state.CrewVehicleLevel(region.Id);
            int nextLevel = level + 1;

            string ownedName = armour
                ? CrewGear.ArmourAt(level).Name
                : CrewGear.VehicleAt(level).Name;

            var next = armour
                ? (object)CrewGear.Armour.FirstOrDefault(a => a.Level == nextLevel)
                : CrewGear.Vehicles.FirstOrDefault(v => v.Level == nextLevel);

            if (next == null)
            {
                _gear.Add(new NativeItem(armour ? "  Protection" : "  Transport",
                    $"{ownedName}. Nothing better to buy.")
                {
                    AltTitle = "~g~best",
                    Enabled = false
                });
                return;
            }

            int cost = armour ? ((ArmourDef)next).Cost : ((CrewVehicleDef)next).Cost;
            string name = armour ? ((ArmourDef)next).Name : ((CrewVehicleDef)next).Name;
            string blurb = armour ? ((ArmourDef)next).Blurb : ((CrewVehicleDef)next).Blurb;

            bool afford = Game.Player.Money >= cost;

            var item = new NativeItem(armour ? $"  Protection — {name}" : $"  Transport — {name}",
                $"{blurb} Currently: {ownedName}." +
                (armour ? string.Empty : " ~o~A bad night can write it off.~s~") +
                (afford ? string.Empty : $" ~r~You need ${cost:N0}."))
            {
                AltTitle = $"${cost:N0}",
                Enabled = afford
            };

            string rid = region.Id;
            bool isArmour = armour;
            item.Activated += (s, e) => BuyGear(rid, isArmour, nextLevel, cost, name);

            _gear.Add(item);
        }

        private void BuyGear(string regionId, bool armour, int level, int cost, string name)
        {
            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0}.");
                return;
            }

            Game.Player.Money -= cost;

            if (armour) GameState.Current.CrewArmour[regionId] = level;
            else GameState.Current.CrewVehicles[regionId] = level;

            Notify.Show(
                $"~g~{Regions.Get(regionId)?.Display}~s~ — {name.ToLowerInvariant()} sorted.", true);

            Persistence.SaveManager.Log(
                $"Bought {(armour ? "armour" : "vehicle")} level {level} for {regionId}.");

            RebuildGear();
        }
    }
}

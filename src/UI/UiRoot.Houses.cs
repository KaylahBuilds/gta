using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// Indoor operations. Split from the main UiRoot file purely to keep both
    /// readable — it is one class.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _houses;

        private void BuildHousesMenu()
        {
            _houses = new NativeMenu("On The Blade", "INDOORS");
            _pool.Add(_houses);
            _bizGroup.AddSubMenu(_houses);
            _houses.Shown += (s, e) => RebuildHouses();
        }

        private void RebuildHouses()
        {
            _houses.Clear();
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var house in Houses.All)
            {
                bool owned = state.OwnsHouse(house.Id);

                if (!owned)
                {
                    bool repOk = state.Reputation >= house.MinReputation;
                    bool afford = Game.Player.Money >= house.Cost;

                    // The ladder is sequential, so this reason comes first —
                    // there is no point telling somebody they are short of cash
                    // for a building they cannot buy at any price yet.
                    var needs = Houses.Get(house.RequiresId);
                    bool prereqOk = needs == null || state.OwnsHouse(needs.Id);

                    string why = !prereqOk
                        ? $"{needs.Display} comes first."
                        : !repOk
                            ? $"Nobody will hand you the keys at {state.Reputation} reputation. " +
                              $"You need {house.MinReputation}."
                            : !afford
                                ? $"~r~You need ${house.Cost:N0} in hand."
                                : house.IsContentHouse
                                    ? $"{house.Rooms} rooms at {house.RateMultiplier:0.00}x the " +
                                      $"content rate. Quiet up to " +
                                      $"{ContentHouses.QuietLine(house):0.0} of them; every one " +
                                      $"past that costs money to keep quiet. " +
                                      (house.RosterSlots > 0
                                          ? $"+{house.RosterSlots} on the roster. "
                                          : string.Empty) +
                                      $"Rent ${house.DailyRent:N0} a day whether anyone works or not."
                                    : $"{house.Rooms} rooms at {house.RateMultiplier:0.0}x the street " +
                                      $"rate, and almost no heat. Rent is ${house.DailyRent:N0} a day " +
                                      "whether anyone works or not.";

                    // Houses are taken AT THE SITE now, in person. This row is
                    // the listing, not the sale — it marks the door and you go
                    // and stand there.
                    var listing = new NativeItem(house.Display,
                        $"{house.Blurb}  |  {why}  |  " +
                        "Activate to mark the door — you take it standing there.")
                    {
                        AltTitle = !prereqOk
                            ? "~o~locked"
                            : repOk ? $"${house.Cost:N0}" : $"~r~Rep {house.MinReputation}"
                    };

                    var def = house;
                    listing.Activated += (s, e) =>
                    {
                        GTA.Native.Function.Call(GTA.Native.Hash.SET_NEW_WAYPOINT,
                            def.Door.X, def.Door.Y);

                        Notify.Show($"~y~{def.Display} is marked.~s~ Go to the door " +
                                    $"and press ~b~{Config.Current.TalkKeyName}~s~.");
                    };

                    _houses.Add(listing);
                    continue;
                }

                // --- owned ---
                int used = state.WorkersInHouse(house.Id).Count();
                float heat = state.GetHouseHeat(house.Id);
                string heatColour = heat > 0.7f ? "r" : heat > 0.4f ? "o" : "g";

                if (state.IsHouseLocked(house.Id))
                {
                    _houses.Add(new NativeItem(house.Display,
                        $"Shut after the raid. Nobody can be posted here yet, and the " +
                        $"${house.DailyRent:N0} a day is still going out.")
                    {
                        AltTitle = $"~r~Shut {state.HouseLockDaysLeft(house.Id)}d",
                        Enabled = false
                    });
                    continue;
                }

                if (house.IsContentHouse)
                {
                    float condition = state.ContentCondition(house.Id);
                    bool shut = state.IsContentShut(house.Id);

                    var content = new NativeItem(house.Display,
                        shut
                            ? $"~r~Not fit to work in.~s~ Everyone is out and the " +
                              $"${house.DailyRent:N0} a day is still going out. Open it up to " +
                              "see what putting it right costs."
                            : $"{used}/{house.Rooms} rooms lived in, quiet up to " +
                              $"{ContentHouses.QuietLine(house):0.0}. Heat ~{heatColour}~" +
                              $"{heat * 100:0}%~s~. Condition {condition * 100:0}%. " +
                              $"Rent ${house.DailyRent:N0}/day. Activate to open it up.")
                    {
                        AltTitle = shut ? "~r~shut" : $"~g~{used}/{house.Rooms}"
                    };

                    var open = house;
                    content.Activated += (s, e) => OpenContentHouse(open);
                    _houses.Add(content);
                    continue;
                }

                var item = new NativeItem(house.Display,
                    $"{used}/{house.Rooms} rooms working. {house.RateMultiplier:0.0}x rate, " +
                    $"no night bonus. Heat ~{heatColour}~{heat * 100:0}%~s~ — raided at " +
                    $"{cfg.HouseRaidHeatThreshold * 100:0}%. Rent ${house.DailyRent:N0}/day. " +
                    "Activate to travel there.")
                {
                    AltTitle = $"~g~{used}/{house.Rooms}"
                };

                var travel = house;
                item.Activated += (s, e) => TravelToHouse(travel);
                _houses.Add(item);
            }

            // --- totals ---
            if (state.OwnedHouses.Count > 0)
            {
                foreach (var owned in Houses.All.Where(h => state.OwnsHouse(h.Id)).ToList())
                {
                    var drop = new NativeItem($"Give up {owned.Display}",
                        $"Hand the keys back. Anyone inside goes off duty, everything you " +
                        $"bought for it is gone, and the ${owned.DailyRent:N0} a day stops. " +
                        "There is nothing back on the deposit.")
                    {
                        AltTitle = $"~o~-${owned.DailyRent:N0}/day"
                    };

                    var def = owned;
                    drop.Activated += (s, e) => GiveUpLease(def);
                    _houses.Add(drop);
                }

                _houses.Add(new NativeItem("Rent", "Charged at midnight across every house you hold.")
                {
                    AltTitle = $"~y~${state.DailyRentBill:N0}/day",
                    Enabled = false
                });

                _houses.Add(new NativeItem("Earned indoors", "Lifetime, from the brothels.")
                {
                    AltTitle = $"${state.LifetimeHouseTake:N0}",
                    Enabled = false
                });

                // Its own row rather than folded into the one above: the two
                // ladders are two businesses, and a player with half a million in
                // content income should not see it reported as zero in the menu
                // that sold them the house.
                if (state.LifetimeContentTake > 0 || Houses.Content.Any(h => state.OwnsHouse(h.Id)))
                {
                    _houses.Add(new NativeItem("Earned on camera", "Lifetime, from the content houses.")
                    {
                        AltTitle = $"${state.LifetimeContentTake:N0}",
                        Enabled = false
                    });
                }
            }
        }

        /// <summary>
        /// Hands a property back.
        ///
        /// Rent is charged unconditionally and this is the only way to stop it.
        /// Without it a house was the one irreversible recurring cost in either
        /// mod: it turned a bad run into an unrecoverable one, and it is why
        /// Collapse used to hand you an empty roster and a rent bill it could
        /// never pay.
        /// </summary>
        private void GiveUpLease(HouseDef house)
        {
            var state = GameState.Current;
            if (!state.OwnsHouse(house.Id)) return;

            int inside = state.WorkersInHouse(house.Id).Count();

            state.ClearHouse(house.Id);
            state.OwnedHouses.Remove(house.Id);
            state.HouseHeat.Remove(house.Id);
            state.HouseLockedUntilDay.Remove(house.Id);

            if (house.IsContentHouse)
            {
                state.ContentWear.Remove(house.Id);
                state.ContentGear.Remove(house.Id);
                state.ContentShutAnnounced.Remove(house.Id);
            }

            Notify.Show(
                $"~o~{house.Display} is not yours any more.~s~ " +
                (inside > 0 ? $"{inside} off duty. " : string.Empty) +
                $"That is ${house.DailyRent:N0} a day you are not paying.", true);

            Persistence.SaveManager.Log($"Gave up the lease on {house.Id}.");
            RebuildHouses();
        }

        // BuyHouse lived here and sold a walk-up off a phone from the other
        // side of the city. Houses are taken in person now, at the door —
        // Systems/HouseDoors.cs owns that, and it is also the way in.

        private void TravelToHouse(HouseDef house)
        {
            if (_missions.Busy)
            {
                Notify.Show("~r~Not while something is going on.");
                return;
            }

            var player = Game.Player.Character;

            GTA.UI.Screen.FadeOut(600);
            Script.Wait(700);

            if (player.IsInVehicle())
            {
                Vehicle vehicle = player.CurrentVehicle;
                vehicle.Position = house.Door;
                vehicle.Heading = house.DoorHeading;
            }
            else
            {
                player.Position = house.Door;
                player.Heading = house.DoorHeading;
            }

            Script.Wait(300);
            GTA.UI.Screen.FadeIn(600);

            _houses.Visible = false;
            _main.Visible = false;
        }
    }
}

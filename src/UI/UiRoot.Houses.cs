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
            _main.AddSubMenu(_houses);
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

                    string why = !repOk
                        ? $"Nobody will hand you the keys at {state.Reputation} reputation. " +
                          $"You need {house.MinReputation}."
                        : !afford
                            ? $"~r~You need ${house.Cost:N0} in hand."
                            : $"{house.Rooms} rooms at {house.RateMultiplier:0.0}x the street rate, " +
                              $"and almost no heat. Rent is ${house.DailyRent:N0} a day whether " +
                              "anyone works or not.";

                    var buy = new NativeItem(house.Display, $"{house.Blurb}  |  {why}")
                    {
                        AltTitle = repOk ? $"${house.Cost:N0}" : $"~r~Rep {house.MinReputation}",
                        Enabled = repOk && afford
                    };

                    if (repOk && afford)
                    {
                        var def = house;
                        buy.Activated += (s, e) => BuyHouse(def);
                    }

                    _houses.Add(buy);
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
                _houses.Add(new NativeItem("Rent", "Charged at midnight across every house you hold.")
                {
                    AltTitle = $"~y~${state.DailyRentBill:N0}/day",
                    Enabled = false
                });

                _houses.Add(new NativeItem("Earned indoors", "Lifetime, from the houses.")
                {
                    AltTitle = $"${state.LifetimeHouseTake:N0}",
                    Enabled = false
                });
            }
        }

        private void BuyHouse(HouseDef house)
        {
            if (Game.Player.Money < house.Cost)
            {
                Notify.Show($"~r~You need ${house.Cost:N0}.");
                return;
            }

            Game.Player.Money -= house.Cost;
            GameState.Current.OwnedHouses.Add(house.Id);

            Notify.Show(
                $"~g~{house.Display} is yours.~s~ {house.Rooms} rooms, and rent of " +
                $"${house.DailyRent:N0} a day starting tonight. Post someone before it " +
                "starts costing you.", true);

            Persistence.SaveManager.Log($"Bought house {house.Id} for ${house.Cost}.");
            RebuildHouses();
        }

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

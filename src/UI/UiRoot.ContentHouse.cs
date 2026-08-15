using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// One content house, up close. Split from the main UiRoot file purely to
    /// keep both readable — it is one class.
    ///
    /// The principle here is the one <see cref="StandingOrders"/> established:
    /// the readout IS the feature. Three numbers are invisible without one — the
    /// quiet line, where her audience is settling, and the regulars she is giving
    /// up — and all three become traps if the menu does not say them out loud.
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _contentHouse;
        private string _boundContentHouseId;

        private void BuildContentHouseMenu()
        {
            _contentHouse = new NativeMenu("On The Blade", "THE HOUSE");

            // A menu missing from the pool goes Visible and never renders, because
            // the pool's Process is the only draw call.
            _pool.Add(_contentHouse);

            // Parent rather than AddSubMenu: RebuildHouses calls _houses.Clear()
            // on every Shown, so a submenu row added here would be destroyed the
            // first time the player opened INDOORS. Parent still gives Back
            // somewhere to go, and the row is created per-rebuild instead.
            _contentHouse.Parent = _houses;

            _contentHouse.Shown += (s, e) => RebuildContentHouse();
        }

        private void OpenContentHouse(HouseDef house)
        {
            if (house == null) return;

            _boundContentHouseId = house.Id;

            // Hidden before the child is shown. Two visible menus draw at the same
            // coordinates and read as garbled overlapping text.
            _houses.Visible = false;
            _contentHouse.Visible = true;
        }

        // ------------------------------------------------------------------

        private void RebuildContentHouse()
        {
            _contentHouse.Clear();

            var house = Houses.Get(_boundContentHouseId);
            if (house == null)
            {
                _contentHouse.Add(new NativeItem("(nothing selected)") { Enabled = false });
                return;
            }

            var state = GameState.Current;
            var cfg = Config.Current;

            _contentHouse.Name = house.Display.ToUpperInvariant();

            int used = state.ContentResidents(house.Id);
            float heat = state.GetHouseHeat(house.Id);
            float condition = state.ContentCondition(house.Id);
            float quiet = ContentHouses.QuietLine(house);
            string heatColour = heat > 0.7f ? "r" : heat > 0.4f ? "o" : "g";

            // --- status ---
            _contentHouse.Add(new NativeItem("The place",
                $"{used}/{house.Rooms} rooms lived in. Quiet up to {quiet:0.0}. " +
                $"Heat ~{heatColour}~{heat * 100:0}%~s~ — raided at " +
                $"{cfg.HouseRaidHeatThreshold * 100:0}%. Condition {condition * 100:0}%. " +
                $"Making ${ContentHouses.ProjectedHouseHourly(house):N0}/hr. " +
                $"Rent ${house.DailyRent:N0}/day.")
            {
                AltTitle = state.IsContentShut(house.Id)
                    ? "~r~shut"
                    : $"~g~{used}/{house.Rooms}",
                Enabled = false
            });

            // --- over the line ---
            float drift = ContentHouses.HourlyDrift(house);
            if (drift > 0f)
            {
                float daysToRaid = (cfg.HouseRaidHeatThreshold - heat) / (drift * 24f);

                _contentHouse.Add(new NativeItem("Too many of them here",
                    $"~o~{used - quiet:0.0} above the quiet line.~s~ Heat is climbing " +
                    $"{drift * 24f:0.000} a day — about {Math.Max(0f, daysToRaid):0} days to a raid " +
                    $"at this rate. Holding it steady costs about " +
                    $"${ContentHouses.HeatBillPerDay(house):N0} a day, or take somebody out.")
                {
                    AltTitle = "~o~climbing",
                    Enabled = false
                });
            }

            // --- the Always warning ---
            int onAlways = state.WorkersInHouse(house.Id)
                .Count(w => w.Shift == WorkerShift.Always);

            if (onAlways > 0)
            {
                _contentHouse.Add(new NativeItem("Nobody is resting",
                    $"~o~{onAlways} of them are set to Always.~s~ There is no recovery " +
                    "window on that shift, so they run down, earn less every hour and " +
                    "start losing loyalty. Days or Nights earns more from the same people.")
                {
                    AltTitle = $"~o~{onAlways}",
                    Enabled = false
                });
            }

            // --- heat relief ---
            int clearCost = ContentHouses.HeatClearCost(house);
            if (clearCost > 0)
            {
                float clearing = Math.Min(heat, cfg.ContentHeatClearMax);

                var quietRow = new NativeItem("Make it go away",
                    $"Somebody has a word and {clearing * 100:0} points of it stops being " +
                    "your problem. Bought again as often as you like — this is rent on " +
                    "being busy, not a thing you own.")
                {
                    AltTitle = $"${clearCost:N0}"
                };

                var h1 = house;
                quietRow.Activated += (s, e) => ClearContentHeat(h1);
                _contentHouse.Add(quietRow);
            }

            // --- repair ---
            float wear = state.ContentWearOn(house.Id);
            if (wear >= cfg.ContentRepairMinFraction)
            {
                int cost = ContentHouses.RepairCost(house);

                var fix = new NativeItem("Fix the place up",
                    $"Back to new. ${cfg.ContentRepairCallOut:N0} to get anyone out here at " +
                    $"all, then the work itself — so it is worth letting it go a while " +
                    $"rather than calling them every other day. Below " +
                    $"{cfg.ContentConditionShutBelow * 100:0}% nobody can work here.")
                {
                    AltTitle = $"${cost:N0}"
                };

                var h2 = house;
                fix.Activated += (s, e) => RepairContentHouse(h2);
                _contentHouse.Add(fix);
            }

            // --- fill ---
            if (!state.IsContentShut(house.Id) && used < house.Rooms)
            {
                var fill = new NativeItem("Fill the rooms",
                    "Moves everybody who is off duty and unposted in here, up to " +
                    "capacity. Mostly for after a raid, when fifteen people are " +
                    "suddenly standing in the street.")
                {
                    AltTitle = $"{house.Rooms - used} free"
                };

                var h3 = house;
                fill.Activated += (s, e) => FillContentHouse(h3);
                _contentHouse.Add(fill);
            }

            // --- travel ---
            var go = new NativeItem("Head over there", "Drive out to the front door.");
            var h4 = house;
            go.Activated += (s, e) => TravelToHouse(h4);
            _contentHouse.Add(go);

            // --- gear ---
            foreach (var gear in ContentGear.All)
                AddContentGearRow(house, gear);
        }

        private void AddContentGearRow(HouseDef house, ContentGearDef gear)
        {
            var state = GameState.Current;

            if (ContentGear.Owns(house.Id, gear.Id))
            {
                _contentHouse.Add(new NativeItem($"  {gear.Name}", gear.Description)
                {
                    AltTitle = "~g~installed",
                    Enabled = false
                });
                return;
            }

            bool afford = Game.Player.Money >= gear.Cost;

            var item = new NativeItem($"  {gear.Name}",
                $"{gear.Description} {AdviceFor(house, gear)}" +
                (afford ? string.Empty : $" ~r~You need ${gear.Cost:N0}."))
            {
                AltTitle = $"${gear.Cost:N0}",
                Enabled = afford
            };

            var h = house;
            var g = gear;
            item.Activated += (s, e) => BuyContentGear(h, g);

            _contentHouse.Add(item);
        }

        /// <summary>
        /// Says out loud when a purchase would be a mistake here. Every one of
        /// these is situational by design, and a flat price on a situational item
        /// is exactly how a player buys the wrong thing.
        /// </summary>
        private static string AdviceFor(HouseDef house, ContentGearDef gear)
        {
            var state = GameState.Current;
            int residents = state.ContentResidents(house.Id);

            if (gear.Id == ContentGear.Set)
            {
                return residents < 8
                    ? $"~o~Does almost nothing at {residents} people — it pays for itself " +
                      "on wear, and there is not much wear here yet.~s~"
                    : "Worth it at this headcount.";
            }

            if (gear.Id == ContentGear.Discreet)
            {
                return residents <= ContentHouses.QuietLine(house)
                    ? "~o~You are already under the quiet line, so there is nothing here " +
                      "for it to reduce.~s~"
                    : $"Saves most of the ~{ContentHouses.HeatBillPerDay(house):N0} a day " +
                      "you are paying to stay quiet.";
            }

            if (gear.Id == ContentGear.Editor)
            {
                int resting = state.WorkersInHouse(house.Id)
                    .Count(w => w.Shift != WorkerShift.Always);

                return resting == 0
                    ? "~o~Pays exactly nothing while everyone here is on Always — it only " +
                      "earns from people who are off shift.~s~"
                    : "Earns while they sleep.";
            }

            return residents == 0
                ? "~o~Nobody lives here yet.~s~"
                : string.Empty;
        }

        // ------------------------------------------------------------------

        private void BuyContentGear(HouseDef house, ContentGearDef gear)
        {
            if (Game.Player.Money < gear.Cost)
            {
                Notify.Show($"~r~You need ${gear.Cost:N0}.");
                return;
            }

            Game.Player.Money -= gear.Cost;
            ContentGear.Buy(house.Id, gear.Id);

            Notify.Show($"~g~{house.Display}~s~ — {gear.Name.ToLowerInvariant()} sorted.", true);
            Persistence.SaveManager.Log($"Bought {gear.Id} for {house.Id}.");

            RebuildContentHouse();
        }

        /// <summary>
        /// Repairs are cash or nothing, the same rule investment follows. Putting
        /// this through Charge would let the player fix a house on credit and then
        /// go under on the interest, which turns maintenance into a trap.
        /// </summary>
        private void RepairContentHouse(HouseDef house)
        {
            int cost = ContentHouses.RepairCost(house);
            if (cost <= 0) return;

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0} in hand.");
                return;
            }

            Game.Player.Money -= cost;
            GameState.Current.ClearContentWear(house.Id);

            Notify.Show($"~g~{house.Display}~s~ is back in order.", true);
            Persistence.SaveManager.Log($"Repaired {house.Id} for ${cost}.");

            RebuildContentHouse();
        }

        private void ClearContentHeat(HouseDef house)
        {
            int cost = ContentHouses.HeatClearCost(house);
            if (cost <= 0) return;

            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~You need ${cost:N0} in hand.");
                return;
            }

            var state = GameState.Current;
            float clearing = Math.Min(state.GetHouseHeat(house.Id),
                                      Config.Current.ContentHeatClearMax);

            Game.Player.Money -= cost;
            state.AddHouseHeat(house.Id, -clearing);

            Notify.Show($"~g~{house.Display}~s~ — that goes away for now.", true);
            RebuildContentHouse();
        }

        private void FillContentHouse(HouseDef house)
        {
            var state = GameState.Current;
            int free = house.Rooms - state.ContentResidents(house.Id);
            if (free <= 0) return;

            int moved = 0;

            foreach (var worker in state.Roster.ToList())
            {
                if (moved >= free) break;
                if (worker.State == WorkerState.Working) continue;
                if (worker.IsJailed() || worker.IsOnBooking) continue;
                if (!string.IsNullOrEmpty(worker.ZoneId)) continue;
                if (!string.IsNullOrEmpty(worker.HouseId)) continue;
                if (worker.IsManager) continue;

                worker.ZoneId = null;
                worker.ManagesZoneId = null;
                worker.HouseId = house.Id;
                worker.State = WorkerState.Working;
                _spawner.Despawn(worker.Id);

                moved++;
            }

            Notify.Show(moved > 0
                ? $"~g~{moved}~s~ moved into {house.Display}."
                : "~o~Nobody free to move in.");

            RebuildContentHouse();
        }
    }
}

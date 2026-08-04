using System;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Regulars, bookings and the people worth more than their wallet.
    ///
    /// Everything here is resolved from the economy tick and taken from a menu.
    /// No incident, no blip, no drive across the map — the mod already has six
    /// reasons to get in a car and the client side is meant to be the part you
    /// run rather than the part you chase.
    /// </summary>
    public static class ClientBook
    {
        private static readonly Random Rng = new Random();

        // --- regulars ------------------------------------------------------

        /// <summary>Passive income from clients who come back for her.</summary>
        public static int RegularIncome(WorkerData worker)
        {
            if (worker.Regulars <= 0) return 0;

            // Scaled by loyalty for the same reason everything else is: a regular
            // asks for her, and someone who has checked out is not who they met.
            return (int)Math.Round(
                worker.Regulars * Config.Current.RegularValuePerHour * (worker.Loyalty / 100f));
        }

        /// <summary>Called once per worked hour, indoors or out.</summary>
        public static void AccrueRegular(WorkerData worker)
        {
            var cfg = Config.Current;
            if (worker.Regulars >= cfg.MaxRegulars) return;

            // Higher tier and Magnetic pull them in faster — same appeal curve the
            // bookings use, so one stat improves both halves of the client system.
            // Pricing is the biggest term: cutting it is how you buy a client base.
            double chance = cfg.RegularGainChance
                            * (Clients.Appeal(worker) / 2f)
                            * Pricing.Regulars(worker.ZoneId);
            if (Rng.NextDouble() >= chance) return;

            worker.Regulars++;
            worker.Clamp();
        }

        /// <summary>
        /// Called once per game day, for everyone who is genuinely benched.
        ///
        /// Deliberately tests the posting rather than "is she working right now".
        /// This runs at midnight, and asking whether she was on shift at that
        /// moment marked every day-shift worker as idle — they would have bled a
        /// regular every single night while working full days.
        /// </summary>
        public static void DecayRegulars()
        {
            var cfg = Config.Current;

            foreach (var worker in GameState.Current.Roster)
            {
                if (worker.Regulars <= 0) continue;
                if (worker.IsOnBooking) continue;

                // Running a corner counts as being around. She is not seeing
                // clients, but she has not disappeared on them either — and
                // bleeding her regulars would make the promotion a punishment.
                bool posted = !string.IsNullOrEmpty(worker.ZoneId)
                              || worker.IsIndoors
                              || worker.IsManager;

                if (posted && worker.Shift != WorkerShift.Off) continue;

                if (Rng.NextDouble() >= cfg.RegularDecayChance) continue;

                worker.Regulars--;
                worker.Clamp();
            }
        }

        // --- offers --------------------------------------------------------

        /// <summary>
        /// Rolls for somebody asking. At most one offer stands at a time, and it
        /// is withdrawn if left unanswered.
        /// </summary>
        public static void RollOffer()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            if (state.HasOffer)
            {
                if (GameState.AbsoluteHour() >= state.OfferExpiresAtHour)
                {
                    string who = state.OfferClientName;
                    state.ClearOffer();
                    Notify.Show($"~y~{who} stopped waiting.~s~ He went somewhere else.");
                }
                return;
            }

            if (Rng.NextDouble() >= cfg.BookingOfferChance) return;

            int hour = GTA.Chrono.GameClock.Hour;

            // Only somebody actually available can be asked for. A worker mid-
            // booking or already in trouble is not on the market.
            var candidates = state.Roster
                .Where(w => w.IsWorkingThisHour(hour)
                            && !w.IsOnBooking
                            && w.State != WorkerState.InTrouble)
                .ToList();

            if (candidates.Count == 0) return;

            // Weighted by appeal rather than picked flat, so tier and traits decide
            // who gets asked for instead of it being a lottery across the roster.
            var worker = PickByAppeal(candidates);
            if (worker == null) return;

            bool connected = Rng.NextDouble() < cfg.ConnectedOfferShare;

            int payout = (int)Math.Round(
                Config.Current.BaseRateFor(worker.Tier)
                * cfg.BookingPayoutHours
                * Math.Max(1f, Clients.Appeal(worker)));

            var cover = state.EnforcerFor(worker.ZoneId);

            state.OfferKind = (int)(connected ? ClientKind.Connected : ClientKind.Whale);
            state.OfferWorkerId = worker.Id;
            state.OfferClientName = Clients.RollName(Rng);
            state.OfferPayout = payout;
            state.OfferRisk = Clients.Risk(worker, payout, cover);
            state.OfferHours = Rng.Next(cfg.BookingHoursMin, cfg.BookingHoursMax + 1);
            state.OfferExpiresAtHour = GameState.AbsoluteHour() + cfg.BookingOfferExpiryHours;

            state.OfferLeverage = connected
                ? (int)Clients.Connected[Rng.Next(Clients.Connected.Count)].Kind
                : (int)LeverageKind.None;

            Notify.Show(connected
                ? $"~b~{state.OfferClientName}~s~ asked for ~b~{worker.Name}~s~ by name. " +
                  "He is not just money. Check the phone."
                : $"~b~{state.OfferClientName}~s~ asked for ~b~{worker.Name}~s~ by name. " +
                  $"~g~${payout:N0}~s~ for the night. Check the phone.", true);
        }

        private static WorkerData PickByAppeal(System.Collections.Generic.List<WorkerData> pool)
        {
            float total = pool.Sum(w => Math.Max(0.1f, Clients.Appeal(w)));
            if (total <= 0f) return null;

            double roll = Rng.NextDouble() * total;
            foreach (var w in pool)
            {
                roll -= Math.Max(0.1f, Clients.Appeal(w));
                if (roll <= 0) return w;
            }

            return pool[pool.Count - 1];
        }

        /// <summary>Takes the offer. Cash if <paramref name="takeFavour"/> is false.</summary>
        public static bool Accept(bool takeFavour)
        {
            var state = GameState.Current;
            if (!state.HasOffer) return false;

            var worker = state.GetWorker(state.OfferWorkerId);
            if (worker == null)
            {
                state.ClearOffer();
                return false;
            }

            worker.BookingEndsAtHour = GameState.AbsoluteHour() + state.OfferHours;
            worker.BookingPayout = takeFavour ? 0 : state.OfferPayout;
            worker.BookingRisk = state.OfferRisk;
            worker.BookingClientName = state.OfferClientName;
            worker.BookingLeverage = takeFavour ? state.OfferLeverage : (int)LeverageKind.None;

            string what = takeFavour
                ? "He owes you instead."
                : $"~g~${state.OfferPayout:N0}~s~ when she is done.";

            Notify.Show(
                $"~b~{worker.Name}~s~ is with {state.OfferClientName} for " +
                $"{state.OfferHours} hours. {what}", true);

            state.ClearOffer();
            return true;
        }

        public static void Decline()
        {
            var state = GameState.Current;
            if (!state.HasOffer) return;

            Notify.Show($"~y~You passed on {state.OfferClientName}.");
            state.ClearOffer();
        }

        // --- resolution ----------------------------------------------------

        /// <summary>
        /// Settles any booking whose hours are up. Called once per economy tick.
        /// </summary>
        public static void ResolveBookings()
        {
            var state = GameState.Current;
            var cfg = Config.Current;
            int now = GameState.AbsoluteHour();

            foreach (var worker in state.Roster.ToList())
            {
                if (!worker.IsOnBooking) continue;
                if (now < worker.BookingEndsAtHour) continue;

                bool wentWrong = Rng.NextDouble() < worker.BookingRisk;

                string client = worker.BookingClientName ?? "He";
                var leverage = (LeverageKind)worker.BookingLeverage;
                int payout = worker.BookingPayout;

                // Cleared before the outcome is applied so nothing below can leave
                // her stuck on a booking that already ended.
                worker.BookingEndsAtHour = 0;
                worker.BookingPayout = 0;
                worker.BookingRisk = 0f;
                worker.BookingClientName = null;
                worker.BookingLeverage = (int)LeverageKind.None;

                worker.Stamina -= cfg.BookingStaminaCost;

                if (wentWrong)
                {
                    worker.Loyalty -= cfg.BookingLoyaltyPenalty;
                    worker.Clamp();

                    if (Config.Current.ReputationEnabled) Reputation.Award(-15);

                    Notify.Show(
                        $"~r~{client} was a problem.~s~ {worker.Name} is back and she is " +
                        "not alright. No money, and she will remember you sent her.", true);

                    Persistence.SaveManager.Log($"Booking failed for {worker.Name}.");
                    continue;
                }

                worker.Loyalty += cfg.BookingLoyaltyReward;
                AccrueRegular(worker);
                worker.Clamp();

                if (Config.Current.ReputationEnabled) Reputation.Award(12);

                if (leverage == LeverageKind.None)
                {
                    Game.Player.Money += payout;
                    worker.LifetimeEarnings += payout;
                    state.LifetimeTake += payout;
                    state.LifetimeBookingTake += payout;

                    Notify.Show(
                        $"~g~+${payout:N0}~s~  {worker.Name} is back, and {client} " +
                        "is asking when she is free again.", true);
                }
                else
                {
                    ApplyLeverage(leverage, worker, client);
                }
            }
        }

        /// <summary>
        /// Cashes in a favour. Each one is deliberately worth more than the money
        /// would have been in the situation it fixes, and worth nothing otherwise —
        /// which is what makes taking it a read on your own position rather than a
        /// better payout.
        /// </summary>
        private static void ApplyLeverage(LeverageKind kind, WorkerData worker, string client)
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            switch (kind)
            {
                case LeverageKind.Heat:
                {
                    int cooled = 0;
                    foreach (var zone in Zones.All.Where(z => state.PlayerOwns(z.Id)))
                    {
                        state.AddHeat(zone.Id, -cfg.LeverageHeatCleared);
                        cooled++;
                    }

                    foreach (var house in Houses.All.Where(h => state.OwnsHouse(h.Id)))
                        state.AddHouseHeat(house.Id, -cfg.LeverageHeatCleared);

                    Notify.Show(cooled > 0
                        ? $"~g~{client} lost the paperwork.~s~ {cooled} corner(s) cooled right down."
                        : $"~y~{client} owed you a favour on ground you don't hold.~s~ Wasted.",
                        true);
                    break;
                }

                case LeverageKind.Truce:
                {
                    var worst = state.Rivals
                        .Where(r => !r.IsBroken)
                        .OrderByDescending(r => r.Aggression)
                        .FirstOrDefault();

                    if (worst == null)
                    {
                        Notify.Show($"~y~{client} offered to make peace, and nobody is at war.");
                        break;
                    }

                    state.SetTruce(worst.Id, cfg.LeverageTruceDays);
                    Notify.Show(
                        $"~g~{client} made a call.~s~ {worst.Name} leave your corners alone " +
                        $"for {cfg.LeverageTruceDays} days.", true);
                    break;
                }

                case LeverageKind.Name:
                {
                    worker.Followers += cfg.LeverageFollowers;
                    worker.Clamp();

                    if (Config.Current.ReputationEnabled) Reputation.Award(25);

                    Notify.Show(
                        $"~g~{client} talked about her to the right people.~s~ " +
                        $"{worker.Name} picked up {cfg.LeverageFollowers:N0} followers.", true);
                    break;
                }
            }
        }
    }
}

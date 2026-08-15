using System;
using System.Linq;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>How a contest under a standing order is expected to go.</summary>
    public enum OrderVerdict
    {
        NoOrder,
        NoCrew,
        WillLose,
        CoinFlip,
        HeldWithCost,
        Guaranteed
    }

    /// <summary>
    /// Paying the crew to hold the block.
    ///
    /// The muscle system was built on a rule: if muscle could settle a turf
    /// battle alone there would be no reason to drive out. That rule was right
    /// when this was the only mod running — removing the reason to drive out
    /// would have removed the only content the mod had.
    ///
    /// It is amended rather than deleted. Muscle does not win it for **free**; it
    /// wins it for money, standing and equipment, all three, and enough of each
    /// that turning up yourself stays the cheap option. What a standing order
    /// sells is the conversion of a time cost into a money cost, which is a
    /// legitimate thing to offer somebody who is busy running a second business
    /// in the same city.
    ///
    /// The guarantee comes from what you have built, never from paying more at
    /// the moment of the fight. A cash button that buys certainty is a cash
    /// button that deletes the system.
    /// </summary>
    public static class StandingOrders
    {
        private static readonly Random Rng = new Random();

        // --- ratings ---------------------------------------------------------

        /// <summary>
        /// What your side is worth on a corner: the man, what he learned to do
        /// before you, what he is holding, what he is wearing, and what he turns
        /// up in.
        /// </summary>
        /// <summary>
        /// Who actually holds this ground. Deliberately the same lookup
        /// deterrence uses: a man assigned to mind one woman is standing next to
        /// her, not on the corner, and conscripting him into a turf battle he is
        /// not at would have made a bodyguard silently double as a garrison.
        /// </summary>
        public static EnforcerData Holder(string zoneId)
        {
            return GameState.Current.EnforcerFor(zoneId);
        }

        public static float CrewRating(string zoneId)
        {
            var region = Regions.ForZone(zoneId);
            if (region == null) return 0f;

            var enforcer = Holder(zoneId);
            if (enforcer == null || enforcer.IsInjured()) return 0f;

            // Raw Skill, not EffectiveSkill. EffectiveSkill already folds the
            // weapon in, and the first version of this multiplied by it a second
            // time — a carbine both pushed him to 100 and multiplied by 1.45.
            // Simulated over thirty days that put a mid leg-breaker past the
            // guarantee threshold against every crew in the game, which made the
            // top two thirds of the equipment ladder unbuyable dead money and
            // made a war with a standing order on it a fixed bill rather than a
            // fight. The weapon is worth what it is worth, once.
            //
            // The background nudges rather than multiplies for the same reason:
            // a leg-breaker's 1.4 on a 90-skill man was most of the rating.
            var cfg = Config.Current;
            float rating = enforcer.Skill
                           * (cfg.OrderSkillBase
                              + cfg.OrderSkillFromProfile * enforcer.Profile.DeterrenceMultiplier);

            rating += enforcer.Loadout?.Muscle ?? 0;
            rating += CrewGear.ArmourIn(region.Id).Rating;
            rating += CrewGear.VehicleIn(region.Id).Rating;

            return rating;
        }

        /// <summary>
        /// What is coming. Scaled so it sits in the same range as a crew rating —
        /// strength alone, nudged by how many they bring and whether this is a
        /// war rather than a test.
        /// </summary>
        public static float RivalRating(RivalCrew rival)
        {
            if (rival == null) return 0f;

            var cfg = Config.Current;

            float rating = rival.Strength
                           * (1f + (rival.EnforcerCount - 2) * cfg.OrderRivalPerBody);

            if (rival.IsAtWar) rating *= cfg.OrderWarMultiplier;

            return rating;
        }

        public static float Ratio(string zoneId, RivalCrew rival)
        {
            float theirs = RivalRating(rival);
            if (theirs <= 0f) return 99f;

            return CrewRating(zoneId) / theirs;
        }

        /// <summary>
        /// What the menu promises before you commit. A player paying a retainer
        /// for a guarantee has to be able to see whether they are buying one —
        /// that readout is the feature, and the rest is arithmetic behind it.
        /// </summary>
        public static OrderVerdict Verdict(string zoneId, RivalCrew rival)
        {
            if (OrderFor(zoneId) == StandingOrder.Off) return OrderVerdict.NoOrder;

            // Injured counts as nobody, the same way RetainerBill and CrewRating
            // already treat it. Without this, Verdict told an injured-holder
            // corner "they will lose" rather than "nobody covers it" — true in
            // effect, wrong in reason, and the next caller inherits it.
            var enforcer = Holder(zoneId);
            if (enforcer == null || enforcer.IsInjured()) return OrderVerdict.NoCrew;

            var cfg = Config.Current;
            float ratio = Ratio(zoneId, rival);

            if (ratio >= cfg.OrderGuaranteeRatio) return OrderVerdict.Guaranteed;
            if (ratio >= cfg.OrderHoldRatio) return OrderVerdict.HeldWithCost;
            if (ratio >= cfg.OrderCoinFlipRatio) return OrderVerdict.CoinFlip;

            return OrderVerdict.WillLose;
        }

        public static string VerdictLabel(OrderVerdict verdict)
        {
            switch (verdict)
            {
                case OrderVerdict.Guaranteed: return "~g~they hold it";
                case OrderVerdict.HeldWithCost: return "~g~they hold it, at a cost";
                case OrderVerdict.CoinFlip: return "~o~coin flip";
                case OrderVerdict.WillLose: return "~r~they will lose";
                case OrderVerdict.NoCrew: return "~r~nobody covers it";
                default: return "~o~no order";
            }
        }

        // --- the order -------------------------------------------------------

        public static StandingOrder OrderFor(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return StandingOrder.Off;

            int level;
            if (!GameState.Current.StandingOrders.TryGetValue(zoneId, out level))
                return StandingOrder.Off;

            if (level < 0) return StandingOrder.Off;
            if (level > 2) return StandingOrder.DefendAndTake;
            return (StandingOrder)level;
        }

        public static void SetOrder(string zoneId, StandingOrder order)
        {
            if (!string.IsNullOrEmpty(zoneId))
                GameState.Current.StandingOrders[zoneId] = (int)order;
        }

        /// <summary>Corners currently on retainer, which is what you pay for nightly.</summary>
        public static int RetainerBill()
        {
            var cfg = Config.Current;
            var state = GameState.Current;
            int bill = 0;

            foreach (var zone in Zones.All)
            {
                var order = OrderFor(zone.Id);
                if (order == StandingOrder.Off) continue;

                // Nothing is owed on ground that is not yours. This is the half
                // that repairs saves already carrying orphaned orders: the menu
                // row that could switch one off disappears the moment you stop
                // owning the corner, so the charge had no off switch at all and
                // cleared through Charge into interest-bearing debt forever.
                if (!state.PlayerOwns(zone.Id)) continue;

                // And nothing is owed to a man who is not there, or who is laid
                // up. You are paying for a crew to stand on a corner; if nobody
                // is standing on it there is no retainer, and the menu says so
                // instead of painting it green.
                var holder = Holder(zone.Id);
                if (holder == null || holder.IsInjured()) continue;

                bill += order == StandingOrder.DefendAndTake
                    ? cfg.OrderRetainerPerDay * 2
                    : cfg.OrderRetainerPerDay;
            }

            return bill;
        }

        /// <summary>Charged at midnight, through the same route as every other bill.</summary>
        public static void PayRetainers()
        {
            int bill = RetainerBill();
            if (bill <= 0) return;

            EconomyTick.Charge(bill, "Standing orders");
        }

        // --- resolving it without you -----------------------------------------

        /// <summary>
        /// Fights a contest off-screen. Returns true if it was handled here, so
        /// the caller knows not to start an incident.
        /// </summary>
        public static bool Resolve(ZoneDef zone, RivalCrew rival)
        {
            if (zone == null || rival == null) return false;
            if (OrderFor(zone.Id) == StandingOrder.Off) return false;

            var region = Regions.ForZone(zone.Id);
            var enforcer = Holder(zone.Id);

            // Nobody on the payroll here means the order is a piece of paper.
            if (enforcer == null || enforcer.IsInjured()) return false;

            var cfg = Config.Current;
            var state = GameState.Current;

            float ratio = Ratio(zone.Id, rival);
            var verdict = Verdict(zone.Id, rival);

            bool won = verdict == OrderVerdict.Guaranteed
                       || verdict == OrderVerdict.HeldWithCost
                       || (verdict == OrderVerdict.CoinFlip && Rng.NextDouble() < ratio / 2f)
                       || (verdict == OrderVerdict.WillLose && Rng.NextDouble() < 0.10);

            // --- what the night cost ---
            int fee = cfg.OrderBattleFee + (int)Math.Round(rival.Strength * cfg.OrderFeePerStrength);
            int ammo = enforcer.Loadout?.AmmoCost ?? 0;

            EconomyTick.Charge(fee + ammo, $"{enforcer.Name} on {zone.Display}");

            enforcer.RecordHandled();

            if (won) Won(zone, rival, enforcer, region.Id, ratio, verdict);
            else Lost(zone, rival, enforcer, region.Id);

            return true;
        }

        private static void Won(ZoneDef zone, RivalCrew rival, EnforcerData enforcer,
                                string regionId, float ratio, OrderVerdict verdict)
        {
            var cfg = Config.Current;
            var state = GameState.Current;

            rival.Strength -= cfg.OrderRivalStrengthLoss;
            rival.Clamp();

            // A corner held while you were somewhere else is worth a fraction of
            // one you held yourself. Everybody knows the difference, and this is
            // the cost that stops a standing order being strictly better than
            // turning up.
            if (cfg.ReputationEnabled)
                Reputation.Award((int)Math.Round(22 * cfg.OrderReputationShare));

            // Even a rout costs somebody occasionally. Without this a crew past
            // the guarantee threshold was never hurt again for the rest of the
            // run, which is what turned a war into an invoice.
            bool hurt = verdict == OrderVerdict.Guaranteed
                ? RollCasualty(regionId, enforcer, cfg.OrderCasualtyOnRout)
                : RollCasualty(regionId, enforcer, cfg.OrderCasualtyOnWin);

            Notify.Show(hurt
                ? $"~g~{enforcer.Name} held {zone.Display}.~s~ {rival.Name} came and did not " +
                  $"stay, and he is laid up for {enforcer.InjuryDaysLeft()} day(s)."
                : $"~g~{enforcer.Name} held {zone.Display}.~s~ {rival.Name} came and did not " +
                  "stay. You were not there, and everybody knows it.", true);

            Persistence.SaveManager.Log(
                $"Standing order HELD {zone.Id} vs {rival.Id} at ratio {ratio:0.00} ({verdict}).");

            // The second order level costs double the retainer, and until now
            // bought nothing whatsoever — it was a strictly worse copy of the
            // first. This is what the money is for.
            if (!hurt && OrderFor(zone.Id) == StandingOrder.DefendAndTake)
                TryTake(rival, enforcer, verdict);
        }

        /// <summary>
        /// Pushing back. Only off the crew that just came, only when they were
        /// routed rather than merely beaten, and only onto ground they hold — a
        /// standing order is not a conquest engine, it is the crew following
        /// somebody home on a night that went well.
        /// </summary>
        private static void TryTake(RivalCrew rival, EnforcerData enforcer, OrderVerdict verdict)
        {
            var cfg = Config.Current;
            var state = GameState.Current;

            if (verdict != OrderVerdict.Guaranteed) return;
            if (Rng.NextDouble() >= cfg.OrderTakeChance) return;

            // Their ground, and near enough that the same man could plausibly
            // have been on it that night.
            var theirs = Zones.All
                .Where(z => state.OwnerOf(z.Id) == rival.Id)
                .Where(z => Regions.ForZone(z.Id)?.Id == enforcer.RegionId)
                .ToList();

            if (theirs.Count == 0) return;

            var taken = theirs[Rng.Next(theirs.Count)];

            state.SetOwner(taken.Id, Ownership.Player);
            rival.Strength -= cfg.OrderTakeStrengthLoss;
            rival.Clamp();

            // Taking ground is loud. It is also the thing that decides whether
            // they come back angrier, so it is priced in aggression rather than
            // cash.
            rival.Aggression += cfg.OrderTakeAggression;
            if (rival.Aggression > 1f) rival.Aggression = 1f;

            if (cfg.ReputationEnabled)
                Reputation.Award((int)Math.Round(30 * cfg.OrderReputationShare));

            Notify.Show(
                $"~g~{enforcer.Name} did not stop at the kerb.~s~ {taken.Display} is " +
                $"yours — {rival.Name} were on the back foot and he took it. " +
                "Nobody there knows you yet, and they will come for it.", true);

            Persistence.SaveManager.Log($"Standing order TOOK {taken.Id} from {rival.Id}.");
        }

        private static void Lost(ZoneDef zone, RivalCrew rival, EnforcerData enforcer,
                                 string regionId)
        {
            var cfg = Config.Current;
            var state = GameState.Current;

            state.SetOwner(zone.Id, rival.Id);
            state.ClearZone(zone.Id);

            // The order dies with the corner. Leaving it set meant the retainer
            // kept clearing every midnight on ground somebody else now held,
            // with no row left in the menu to turn it off.
            SetOrder(zone.Id, StandingOrder.Off);

            rival.Strength += 10f;
            rival.Clamp();

            int lostUpgrades = ZoneUpgrades.CountOn(zone.Id);
            ZoneUpgrades.ClearZone(zone.Id);

            if (cfg.ReputationEnabled) Reputation.Award(-28);

            bool hurt = RollCasualty(regionId, enforcer, cfg.OrderCasualtyOnLoss);
            bool vanGone = RollVehicleLoss(regionId);

            Notify.Show(
                $"~r~{enforcer.Name} could not hold {zone.Display}.~s~ " +
                $"{rival.Name} have it, everyone posted there is off the street" +
                (lostUpgrades > 0 ? ", and everything you put into that corner went with it" : string.Empty) +
                (hurt ? $". He is laid up for {enforcer.InjuryDaysLeft()} day(s)" : string.Empty) +
                (vanGone ? ". They burned the van." : "."), true);

            Persistence.SaveManager.Log($"Standing order LOST {zone.Id} to {rival.Id}.");
        }

        /// <summary>
        /// Whether somebody is carried out. Armour and the vehicle are what this
        /// is for — a crew that wins every fight and spends half its life in
        /// hospital is still on the wage and still deterring nobody.
        /// </summary>
        private static bool RollCasualty(string regionId, EnforcerData enforcer, float chance)
        {
            var cfg = Config.Current;

            chance *= CrewGear.ArmourIn(regionId).CasualtyMultiplier;
            chance *= CrewGear.VehicleIn(regionId).CasualtyMultiplier;

            if (Rng.NextDouble() >= chance) return false;

            enforcer.InjuredUntilDay = GameState.AbsoluteDay() + cfg.MuscleInjuryDays;
            enforcer.TimesHurt++;
            enforcer.Clamp();

            return true;
        }

        /// <summary>A bad night can write the van off. Otherwise it is bought once, forever.</summary>
        private static bool RollVehicleLoss(string regionId)
        {
            var def = CrewGear.VehicleIn(regionId);
            if (def.Level <= 0) return false;
            if (Rng.NextDouble() >= def.LossChance) return false;

            GameState.Current.CrewVehicles[regionId] = 0;
            return true;
        }
    }
}

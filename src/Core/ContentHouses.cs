using System;
using System.Linq;

namespace OnTheBlade.Core
{
    /// <summary>
    /// Everything derived about a content house, in one place so the readout the
    /// player reads and the number the economy charges cannot drift apart.
    ///
    /// No state of its own. The principle is taken from
    /// <see cref="Systems.StandingOrders"/>: the readout IS the feature. Three
    /// numbers here are invisible without one — the quiet line, where her
    /// audience is settling, and the regulars she is giving up — and all three
    /// become traps if the menu does not say them out loud.
    /// </summary>
    public static class ContentHouses
    {
        /// <summary>Fallbacks for keys a player may have edited to zero.</summary>
        public const float DefaultBaseRate = 200f;
        public const float DefaultMaxFollowers = 25000f;

        /// <summary>
        /// The fraction of capacity a house can hold before heat starts to climb.
        /// Every HeatGain in the content ladder is solved from this, so it is the
        /// one number the whole occupancy decision turns on.
        /// </summary>
        public const float QuietFraction = 0.70f;

        public static float TierAppeal(int tier)
        {
            var cfg = Config.Current;
            if (tier >= 3) return cfg.ContentTierAppeal3;
            return tier == 2 ? cfg.ContentTierAppeal2 : cfg.ContentTierAppeal1;
        }

        /// <summary>
        /// Guarded because RestoreMissingKeys deliberately preserves a value the
        /// player set to zero, and a zero here would silently pay nothing for
        /// every hour anybody ever produces.
        /// </summary>
        public static float BaseRate()
        {
            float r = Config.Current.ContentBaseRatePerHour;
            return r > 0f ? r : DefaultBaseRate;
        }

        /// <summary>The follower cap, guarded the same way — nothing else divides by it.</summary>
        public static float FollowerCap()
        {
            float c = Config.Current.MaxFollowers;
            return c > 0f ? c : DefaultMaxFollowers;
        }

        /// <summary>1.00 with no audience, up to 2.20 at the cap. The feature.</summary>
        public static float AudienceFactor(WorkerData worker)
        {
            if (worker == null) return 1f;

            float ratio = worker.Followers / FollowerCap();
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;

            return 1f + Config.Current.ContentAudienceBonusMax * ratio;
        }

        /// <summary>A worn house works badly rather than appearing broken.</summary>
        public static float ConditionFactor(string houseId)
        {
            var cfg = Config.Current;
            float condition = GameState.Current.ContentCondition(houseId);
            float floor = cfg.ContentConditionFloor;

            return floor + (1f - floor) * condition;
        }

        /// <summary>
        /// Combined heat reduction for this house, floored so that neither
        /// equipment nor a houseful of Connected residents can push the quiet
        /// line past capacity and delete the raid.
        /// </summary>
        public static float HeatReduction(string houseId, WorkerTrait traits)
        {
            float m = ContentGear.HeatMultiplier(houseId) * Traits.HeatMultiplier(traits);
            float floor = Config.Current.ContentHeatReductionFloor;

            return m < floor ? floor : m;
        }

        /// <summary>
        /// How many people can live here before heat starts climbing. Stated in
        /// residents because that is the decision — every one past this costs
        /// money to keep quiet, or eventually costs you the house.
        /// </summary>
        public static float QuietLine(HouseDef house)
        {
            if (house == null || house.HeatGain <= 0f) return 0f;

            // The best case the player could reach here, so the number shown is a
            // floor on what they can hold rather than an average.
            float best = Math.Max(Config.Current.ContentHeatReductionFloor,
                                  ContentGear.HeatMultiplier(house.Id));

            return Config.Current.ContentHeatDecayPerHour / (house.HeatGain * best);
        }

        /// <summary>What it costs per game day to hold a house steady above the quiet line.</summary>
        public static int HeatBillPerDay(HouseDef house)
        {
            if (house == null) return 0;

            var state = GameState.Current;
            int residents = state.ContentResidents(house.Id);

            float over = residents - QuietLine(house);
            if (over <= 0f) return 0;

            float driftPerHour = over * house.HeatGain
                                 * Math.Max(Config.Current.ContentHeatReductionFloor,
                                            ContentGear.HeatMultiplier(house.Id));

            float perUnit = Config.Current.ContentHeatClearCostPerRoom * house.Rooms;
            return (int)Math.Round(driftPerHour * 24f * perUnit);
        }

        /// <summary>Heat this house gains in an hour, net of its own decay.</summary>
        public static float HourlyDrift(HouseDef house)
        {
            if (house == null) return 0f;

            var state = GameState.Current;
            float gain = 0f;

            foreach (var worker in state.WorkersInHouse(house.Id))
                gain += house.HeatGain * HeatReduction(house.Id, worker.TraitSet);

            return gain - Config.Current.ContentHeatDecayPerHour;
        }

        /// <summary>What one worker makes here in an hour she is producing.</summary>
        public static int ProjectedHourly(WorkerData worker, HouseDef house)
        {
            if (worker == null || house == null) return 0;

            float payout = BaseRate()
                           * TierAppeal(worker.Tier)
                           * house.RateMultiplier
                           * (worker.Loyalty / 100f)
                           * (worker.Stamina / 100f)
                           * ConditionFactor(house.Id)
                           * ContentGear.RateMultiplier(house.Id)
                           * Traits.ContentMultiplier(worker.TraitSet)
                           * (1f - GameState.Current.GetHouseHeat(house.Id))
                           * AudienceFactor(worker);

            return payout < 0f ? 0 : (int)Math.Round(payout);
        }

        /// <summary>Everything the whole house makes in a producing hour.</summary>
        public static int ProjectedHouseHourly(HouseDef house)
        {
            if (house == null) return 0;

            return GameState.Current.WorkersInHouse(house.Id)
                .Sum(w => ProjectedHourly(w, house));
        }

        /// <summary>What a full restore costs right now, call-out fee included.</summary>
        public static int RepairCost(HouseDef house)
        {
            if (house == null) return 0;

            var cfg = Config.Current;
            float wear = GameState.Current.ContentWearOn(house.Id);
            if (wear <= 0f) return 0;

            return cfg.ContentRepairCallOut
                   + (int)Math.Round(cfg.ContentRepairPerRoom * house.Rooms * wear);
        }

        /// <summary>Cost to clear heat here, matching the corner bribe's shape.</summary>
        public static int HeatClearCost(HouseDef house)
        {
            if (house == null) return 0;

            var cfg = Config.Current;
            float heat = GameState.Current.GetHouseHeat(house.Id);
            float clearing = Math.Min(heat, cfg.ContentHeatClearMax);
            if (clearing <= 0f) return 0;

            return (int)Math.Round(cfg.ContentHeatClearCostPerRoom * house.Rooms * clearing);
        }

        /// <summary>
        /// Where her audience is heading if she stays here, which is what the
        /// player needs to see before committing somebody.
        ///
        /// Churn is charged per hour of residency rather than per producing hour,
        /// so this barely moves with her shift — which is the point. Derived per
        /// day: gain is (producing hours x share + off hours) x g, loss is
        /// 24 x churn x F, so F* = ((p x share) + (24 - p)) x g / (24 x churn).
        /// </summary>
        public static float ProjectedStock(WorkerData worker)
        {
            if (worker == null) return 0f;

            var cfg = Config.Current;
            if (cfg.ContentChurnPerHour <= 0f) return FollowerCap();

            int producing = ProducingHours(worker.Shift);
            float g = Systems.Subscriptions.OffDutyGain(worker);

            float perDay = (producing * cfg.ContentGainShare + (24 - producing)) * g;
            float stock = perDay / (24f * cfg.ContentChurnPerHour);

            float cap = FollowerCap();
            return stock > cap ? cap : stock;
        }

        public static int ProducingHours(WorkerShift shift)
        {
            switch (shift)
            {
                case WorkerShift.Always: return 24;
                case WorkerShift.Days: return 14;
                case WorkerShift.Nights: return 10;
                default: return 0;
            }
        }
    }
}

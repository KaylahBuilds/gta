using System;
using System.Linq;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Some prospects already work for somebody. Signing one is the cheapest way
    /// to get an experienced earner and the fastest way to start a war.
    ///
    /// The trade is deliberate: a poached worker comes in a tier higher because
    /// she already knows the job, but starts on lower loyalty because she has been
    /// through something, and her crew may arrive within the minute. Taking her
    /// also nudges every *other* crew's aggression — word travels, and a pimp who
    /// poaches is a problem for all of them, not just the one he stole from.
    ///
    /// Once every rival is broken there is nobody left to poach from, so late-game
    /// recruiting quietly becomes safe. That falls out of the design rather than
    /// being special-cased.
    /// </summary>
    public static class Poaching
    {
        public static float ClaimChance(RecruitArea area)
        {
            var cfg = Config.Current;
            switch (area)
            {
                case RecruitArea.Gang: return cfg.ClaimChanceGang;
                case RecruitArea.Vinewood: return cfg.ClaimChanceVinewood;
                default: return cfg.ClaimChanceOrdinary;
            }
        }

        /// <summary>
        /// Which crew, if any, already had her. Weighted by strength — bigger
        /// operations have more people out. Returns null if she was unattached.
        /// </summary>
        public static RivalCrew RollClaim(RecruitArea area, Random rng)
        {
            if (rng.NextDouble() >= ClaimChance(area)) return null;

            var candidates = GameState.Current.Rivals.Where(r => !r.IsBroken).ToList();
            if (candidates.Count == 0) return null;

            float total = candidates.Sum(r => r.Strength);
            if (total <= 0f) return null;

            double pick = rng.NextDouble() * total;
            foreach (var rival in candidates)
            {
                pick -= rival.Strength;
                if (pick <= 0) return rival;
            }

            return candidates[candidates.Count - 1];
        }

        /// <summary>
        /// Applies the cost of taking her: her crew mobilises, everyone else takes
        /// note, and she arrives less sure of you.
        /// </summary>
        public static void ApplyTension(WorkerData worker, RivalCrew owner)
        {
            if (worker == null || owner == null) return;
            var cfg = Config.Current;
            var state = GameState.Current;

            worker.ClaimedFrom = owner.Id;

            // Experienced — she has done this before.
            worker.Tier = Math.Min(3, worker.Tier + 1);
            worker.Loyalty -= cfg.PoachLoyaltyPenalty;
            worker.Clamp();

            owner.Aggression += cfg.PoachAggressionHit;
            owner.Strength += 5f;   // they pull people in
            owner.Clamp();

            foreach (var other in state.Rivals.Where(r => r.Id != owner.Id && !r.IsBroken))
            {
                other.Aggression += cfg.PoachAggressionSpread;
                other.Clamp();
            }

            state.PoachedCount++;
        }

        public static bool RollRetaliation(RivalCrew owner, Random rng)
        {
            if (owner == null || owner.IsBroken) return false;
            return rng.NextDouble() < Config.Current.RetaliationChance;
        }
    }
}

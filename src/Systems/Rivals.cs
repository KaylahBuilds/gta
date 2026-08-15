using System;
using System.Linq;
using GTA;
using OnTheBlade.Core;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// The crews, doing things back.
    ///
    /// Until now the relationship ran one way in every respect: you took their
    /// people, you took their corners, and the worst they managed was turning up
    /// occasionally. Aggression climbed to 1 and then stopped meaning anything.
    ///
    /// Three things change that. They can take somebody off you, which finally
    /// gives loyalty a defensive job. Aggression now leads somewhere — open war.
    /// And a crew can be bought all the way over to your side rather than merely
    /// bought off.
    /// </summary>
    public static class Rivals
    {
        private static readonly Random Rng = new Random();

        // --- they take one of yours -----------------------------------------

        /// <summary>
        /// Rolled once per game day. Only crews with a reason and only workers
        /// with a reason to listen.
        /// </summary>
        public static void RollPoachAttempt(Runtime.SpawnManager spawner)
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            var crews = state.Rivals
                .Where(r => !r.IsBroken && !r.IsAllied() && !state.HasTruce(r.Id))
                .ToList();

            if (crews.Count == 0) return;

            // Weighted by aggression: the crew you have been provoking is the one
            // that comes looking.
            var crew = crews.OrderByDescending(r => r.Aggression * Rng.NextDouble()).First();

            double chance = cfg.RivalPoachChancePerDay * (0.5f + crew.Aggression);
            if (crew.IsAtWar) chance *= 2.0;
            if (Rng.NextDouble() >= chance) return;

            // Nobody loyal is listening, and nobody in a cell is being approached.
            var targets = state.Roster
                .Where(w => w.Loyalty < cfg.RivalPoachSafeLoyalty
                            && !w.IsJailed()
                            && !w.IsOnBooking)
                .ToList();

            if (targets.Count == 0) return;

            // The least loyal is the one they get to.
            var worker = targets.OrderBy(w => w.Loyalty).First();

            // Somebody minding her personally simply sends them away. Nothing else
            // in the mod protects a low-loyalty worker this reliably.
            var guard = state.GuardFor(worker.Id);
            if (guard != null && Rng.NextDouble() < cfg.GuardPoachDeterrence)
            {
                Notify.Show(
                    $"~g~{crew.Name} did not get near {worker.Name}.~s~ {guard.Name} was " +
                    "standing right there.");
                return;
            }

            // Armed muscle covering her ground is a reason not to try it.
            var cover = state.EnforcerFor(worker.ZoneId);
            if (cover != null && cover.IsArmed && !cover.IsInjured())
            {
                double deter = (cover.EffectiveSkill / 100f) * cfg.RivalPoachMuscleDeterrence;
                if (Rng.NextDouble() < deter)
                {
                    Notify.Show(
                        $"~g~{cover.Name} moved somebody on.~s~ {crew.Name} were talking to " +
                        $"{worker.Name}, and now they are not.");
                    return;
                }
            }

            // Loyalty is the whole defence. At the safe line she refuses outright;
            // at zero she is already halfway out of the door.
            float odds = 1f - (worker.Loyalty / cfg.RivalPoachSafeLoyalty);
            if (odds < 0f) odds = 0f;

            if (Rng.NextDouble() >= odds)
            {
                worker.Loyalty += cfg.RivalPoachRefusedLoyalty;
                worker.Clamp();

                Notify.Show(
                    $"~g~{crew.Name} tried to take {worker.Name}.~s~ She told them no, and " +
                    $"she told you about it. Loyalty {worker.Loyalty:0}.", true);
                return;
            }

            // Gone.
            spawner.Despawn(worker.Id);
            state.ReleaseGuardsFor(worker.Id);
            state.Roster.Remove(worker);

            crew.Strength += cfg.RivalPoachStrengthGain;
            crew.Clamp();

            if (Config.Current.ReputationEnabled) Reputation.Award(-18);

            Notify.Show(
                $"~r~{crew.Name} took {worker.Name}.~s~ She went with them, and at " +
                $"{worker.Loyalty:0} loyalty you can hardly say you didn't see it.", true);

            Persistence.SaveManager.Log(
                $"{crew.Name} poached {worker.Name} at {worker.Loyalty:0} loyalty.");
        }

        // --- war -------------------------------------------------------------

        /// <summary>
        /// Rolled once per game day. Aggression climbing to the ceiling now tips
        /// into something, and a crew beaten far enough down gives it up.
        /// </summary>
        public static void TickWars()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            foreach (var crew in state.Rivals)
            {
                if (crew.IsBroken)
                {
                    crew.WarSinceDay = 0;
                    continue;
                }

                if (crew.IsAtWar)
                {
                    bool beaten = crew.Strength <= cfg.WarEndStrength;
                    bool exhausted = GameState.AbsoluteDay() - crew.WarSinceDay >= cfg.WarMaxDays;

                    if (!beaten && !exhausted) continue;

                    crew.WarSinceDay = 0;
                    crew.Aggression *= beaten ? 0.4f : 0.7f;
                    crew.Clamp();

                    if (beaten)
                    {
                        if (Config.Current.ReputationEnabled) Reputation.Award(40);

                        Notify.Show(
                            $"~g~{crew.Name} are done fighting you.~s~ What's left of them " +
                            "wants nothing more to do with your corners.", true);
                    }
                    else
                    {
                        // Nobody won. A war nobody can finish stops being pressure
                        // and starts being a chore, so it burns itself out.
                        Notify.Show(
                            $"~y~{crew.Name} have stopped pushing.~s~ Nobody won it — " +
                            "they just ran out of appetite for the cost.", true);
                    }

                    Persistence.SaveManager.Log(
                        $"War with {crew.Id} ended ({(beaten ? "broken" : "burned out")}).");
                    continue;
                }

                // An allied or paid-off crew is not declaring anything.
                if (crew.IsAllied() || state.HasTruce(crew.Id)) continue;
                if (crew.Aggression < cfg.WarAggressionThreshold) continue;

                crew.WarSinceDay = GameState.AbsoluteDay();

                Notify.Show(
                    $"~r~{crew.Name} have stopped testing you.~s~ They are coming for " +
                    "everything now, and they will not take your money to stop.", true);

                Persistence.SaveManager.Log($"War declared by {crew.Id}.");
            }
        }

        /// <summary>Contest odds multiplier for a crew, accounting for war.</summary>
        public static float ContestMultiplier(RivalCrew crew)
        {
            if (crew == null) return 1f;
            if (crew.IsAllied()) return 0f;          // they are not coming at all
            return crew.IsAtWar ? Config.Current.WarContestMultiplier : 1f;
        }

        // --- alliances --------------------------------------------------------

        public static bool CanAlly(RivalCrew crew, out string reason)
        {
            reason = null;
            var state = GameState.Current;
            var cfg = Config.Current;

            if (crew.IsBroken)
            {
                reason = "There is nobody left to talk to.";
                return false;
            }

            if (crew.IsAtWar)
            {
                reason = "They are at war with you. Buy the peace first.";
                return false;
            }

            if (state.Reputation < cfg.AllianceMinReputation)
            {
                reason = $"They stand with people who are somebody. You need " +
                         $"{cfg.AllianceMinReputation} reputation and you have {state.Reputation}.";
                return false;
            }

            return true;
        }

        public static int AllianceCost(RivalCrew crew)
        {
            int baseCost = UI.UiRoot.ProtectionCost(crew);
            return (int)Math.Round(baseCost * Config.Current.AllianceCostMultiplier / 100f) * 100;
        }

        public static bool BuyAlliance(RivalCrew crew)
        {
            string reason;
            if (!CanAlly(crew, out reason))
            {
                Notify.Show($"~r~{reason}");
                return false;
            }

            int cost = AllianceCost(crew);
            if (Game.Player.Money < cost)
            {
                Notify.Show($"~r~They want ${cost:N0} in hand.");
                return false;
            }

            var cfg = Config.Current;
            Game.Player.Money -= cost;

            int from = Math.Max(GameState.AbsoluteDay(), crew.AlliedUntilDay);
            crew.AlliedUntilDay = from + cfg.AllianceDays;
            crew.Aggression *= 0.5f;
            crew.Clamp();

            Notify.Show(
                $"~g~{crew.Name} are with you for {crew.AllyDaysLeft()} days.~s~ " +
                $"They leave your corners alone, and they turn up when you take somebody " +
                "else's.", true);

            Persistence.SaveManager.Log($"Alliance bought with {crew.Id} for ${cost}.");
            return true;
        }

        /// <summary>The crew currently standing with you, if any.</summary>
        public static RivalCrew CurrentAlly()
        {
            return GameState.Current.Rivals.FirstOrDefault(r => r.IsAllied());
        }

        /// <summary>Warns a day out so an alliance never lapses silently.</summary>
        public static void TickAlliances()
        {
            foreach (var crew in GameState.Current.Rivals)
            {
                if (!crew.IsAllied()) continue;
                if (crew.AllyDaysLeft() != 1) continue;

                Notify.Show($"~y~{crew.Name} want paying again tomorrow if this is to hold.");
            }
        }
    }
}

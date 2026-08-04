using System;
using System.Linq;
using OnTheBlade.Core;
using OnTheBlade.Runtime;
using OnTheBlade.Systems.Incidents;

namespace OnTheBlade.Systems
{
    /// <summary>
    /// Decides whether the hour produces a problem. Called once per economy tick,
    /// and starts at most one incident.
    ///
    /// Priority is deliberate: a walk-off outranks everything else, because it is
    /// the only incident the player caused directly and the only one that costs
    /// them a crew member permanently.
    /// </summary>
    public class IncidentRoller
    {
        private readonly Random _rng = new Random();
        private readonly MissionController _missions;
        private readonly SpawnManager _spawner;

        public IncidentRoller(MissionController missions, SpawnManager spawner)
        {
            _missions = missions;
            _spawner = spawner;
        }

        public void RollHourly()
        {
            if (!_missions.Ready) return;

            var cfg = Config.Current;
            var state = GameState.Current;

            // Collectors outrank everything. They are not a problem on a corner —
            // they are at the player, and the debt they are here about is the only
            // thing in the mod that can end the run.
            if (state.CollectorsPending)
            {
                state.CollectorsPending = false;

                if (_missions.TryStart(new CollectorsIncident(
                        GTA.Game.Player.Character.Position, _spawner)))
                    return;
            }

            // Only people actually out this hour can get into trouble.
            int hour = GTA.Chrono.GameClock.Hour;
            var onPost = state.Roster
                .Where(w => w.ShouldBeOnStreet(hour) && Zones.Get(w.ZoneId) != null)
                .OrderBy(_ => _rng.Next())   // don't always pick the lowest id
                .ToList();

            if (onPost.Count == 0) return;

            // 1. Walk-offs first. Loyal workers never walk, whatever the number says.
            foreach (var worker in onPost.Where(
                         w => w.Loyalty < 25f && !Traits.ImmuneToWalkOff(w.TraitSet)))
            {
                if (_rng.NextDouble() >= cfg.WalkOffChance) continue;
                if (_missions.TryStart(new WalkOffIncident(worker.Id, _spawner))) return;
            }

            // 2. Rivals coming for turf you hold.
            if (RollRivalContest()) return;

            // 3. Vice stings, weighted entirely by how hot the corner is.
            foreach (var worker in onPost)
            {
                float chance = state.GetHeat(worker.ZoneId) * cfg.ViceStingHeatFactor;
                if (state.HasUpgrade(UpgradeCatalog.Retainer)) chance *= 0.75f;

                if (_rng.NextDouble() >= chance) continue;
                if (_missions.TryStart(new ViceStingIncident(worker.Id, _spawner))) return;
            }

            // 4. Bad clients — the routine one, and the only one muscle covers.
            foreach (var worker in onPost)
            {
                float chance = cfg.BadClientChance
                               * Traits.TroubleMultiplier(worker.TraitSet)
                               * Pricing.Trouble(worker.ZoneId);
                if (_rng.NextDouble() >= chance) continue;
                if (HandledByMuscle(worker)) return;
                if (_missions.TryStart(new BadClientIncident(worker.Id, _spawner))) return;
            }
        }

        /// <summary>
        /// An enforcer standing on the ground a crew is eyeing can wave them off
        /// before it becomes a turf battle.
        ///
        /// This is what muscle is actually for. Covering the occasional bad client
        /// was convenience; keeping a corner is the reason to pay a wage. It is
        /// deliberately a roll rather than a guarantee — turf you never have to
        /// defend is turf that stopped being interesting.
        /// </summary>
        private bool DeterredByMuscle(ZoneDef zone, RivalCrew rival)
        {
            var enforcer = GameState.Current.EnforcerFor(zone.Id);
            if (enforcer == null) return false;

            // EffectiveSkill folds in whatever he is carrying and returns zero
            // while he is laid up — a man in a hospital bed deters nobody.
            float chance = (enforcer.EffectiveSkill / 100f) * Config.Current.MuscleTurfDeterrence;
            if (_rng.NextDouble() >= chance) return false;

            enforcer.Handled++;

            Notify.Show(enforcer.IsArmed
                ? $"~g~{enforcer.Name} turned {rival.Name} away from {zone.Display}.~s~ " +
                  "Nobody argues with a man holding something."
                : $"~g~{enforcer.Name} turned {rival.Name} away from {zone.Display}.~s~ " +
                  "That's what the wage is for.");

            return true;
        }

        /// <summary>
        /// Gives an enforcer covering this zone a shot at resolving it off-screen.
        /// Returns true if the roll was consumed either way — a failed attempt
        /// still falls through to a real incident, it just costs the player the
        /// element of surprise rather than nothing.
        /// </summary>
        private bool HandledByMuscle(WorkerData worker)
        {
            var enforcer = GameState.Current.EnforcerFor(worker.ZoneId);
            if (enforcer == null) return false;

            // Capped so it can never be a certainty. Arming a man raises his
            // effective skill toward 100, and without this a carbine would mean
            // every client was dealt with off-screen — which would hide the very
            // thing the weapon was bought for. What gets through, he turns up for.
            float chance = enforcer.EffectiveSkill * Config.Current.MuscleClientHandleCap;
            if (_rng.NextDouble() * 100.0 >= chance) return false;

            enforcer.Handled++;
            worker.Loyalty += 4f;
            worker.Clamp();

            Notify.Show(
                $"~g~{enforcer.Name}~s~ dealt with a client near {worker.Name}. No action needed.");
            return true;
        }

        /// <summary>
        /// Rivals only come after turf you actually hold, and only where you have
        /// people posted. Being attacked over an empty corner you happen to own
        /// reads as noise rather than pressure.
        /// </summary>
        private bool RollRivalContest()
        {
            var state = GameState.Current;
            var cfg = Config.Current;

            var held = Zones.All
                .Where(z => state.PlayerOwns(z.Id))
                .Where(z => state.WorkersIn(z.Id).Any())
                .OrderBy(_ => _rng.Next())
                .ToList();

            if (held.Count == 0) return false;

            // A crew you are paying for peace does not come for your corners.
            // That is the whole product.
            foreach (var rival in state.Rivals
                         .Where(r => !r.IsBroken && !state.HasTruce(r.Id) && !r.IsAllied())
                         .OrderBy(_ => _rng.Next()))
            {
                // War is what aggression was always building toward: the same roll,
                // multiplied, so a crew that has tipped over comes constantly
                // rather than occasionally.
                float odds = rival.Aggression
                             * cfg.RivalContestChance
                             * Rivals.ContestMultiplier(rival);

                if (_rng.NextDouble() >= odds) continue;

                var target = held[_rng.Next(held.Count)];

                // Muscle on that ground gets a say before it becomes a fight.
                if (DeterredByMuscle(target, rival)) continue;

                if (_missions.TryStart(new TurfBattleIncident(target.Id, rival.Id, false, _spawner)))
                    return true;
            }

            return false;
        }
    }
}

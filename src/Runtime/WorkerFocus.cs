using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using OnTheBlade.Core;

namespace OnTheBlade.Runtime
{
    /// <summary>What kind of person is under focus.</summary>
    public enum FocusKind
    {
        None = 0,
        Worker = 1,
        Muscle = 2,

        /// <summary>An eligible stranger — somebody you could sign.</summary>
        Prospect = 3
    }

    /// <summary>
    /// Who the player is standing in front of.
    ///
    /// The game's answer to "who is this" is always a RECORD ID, never a ped
    /// handle — the engine recycles handles, so a handle is only ever compared
    /// within a single frame. Focus re-resolves through the spawner's live
    /// dictionaries every frame; if the runtime is pruned, focus clears the
    /// same frame.
    ///
    /// Selection is distance AND facing. The view-cone is minimum correctness,
    /// not polish: posts are 2.6 metres apart, so any radius above 1.3m holds
    /// two candidates and only the direction you are looking can tell them
    /// apart. Sticky hysteresis stops the label strobing between two women on
    /// a crowded kerb, and if two candidates stand within 0.3m of each other,
    /// focus REFUSES to pick — saying so beats silently binding one of two
    /// overlapping records.
    /// </summary>
    public class WorkerFocus
    {
        private readonly SpawnManager _spawner;
        private readonly Func<bool> _menuVisible;

        // Prospects are ambient peds, found by a native scan — so the scan runs
        // on the spotter's cadence, never per frame. Handles live at most one
        // refresh interval and are re-validated with Exists() every use.
        private readonly List<Ped> _prospects = new List<Ped>();
        private int _nextProspectScanAt;

        public FocusKind Kind { get; private set; }
        public int RecordId { get; private set; } = -1;

        public WorkerFocus(SpawnManager spawner, Func<bool> menuVisible)
        {
            _spawner = spawner;
            _menuVisible = menuVisible;
        }

        public bool HasFocus => Kind != FocusKind.None && RecordId >= 0;

        public void Update()
        {
            // No prompt over an open menu — two UIs on the same key is how the
            // wrong thing gets activated.
            if (_menuVisible != null && _menuVisible())
            {
                Clear();
                return;
            }

            var player = Game.Player.Character;
            if (player == null || !player.Exists() || player.IsInVehicle())
            {
                Clear();
                return;
            }

            var cfg = Config.Current;
            Vector3 here = player.Position;
            Vector3 forward = player.ForwardVector;

            // Gather candidates from handles the mod already owns. No native
            // scans, ever.
            var candidates = new List<Candidate>();

            foreach (var pair in _spawner.Live)
            {
                if (!pair.Value.IsValid) continue;

                float d = here.DistanceTo(pair.Value.Ped.Position);
                if (d > cfg.FocusStickyExit) continue;

                candidates.Add(new Candidate
                {
                    Kind = FocusKind.Worker,
                    Id = pair.Key,
                    Position = pair.Value.Ped.Position,
                    Distance = d
                });
            }

            foreach (var pair in _spawner.LiveMuscle)
            {
                if (!pair.Value.IsValid) continue;

                float d = here.DistanceTo(pair.Value.Ped.Position);
                if (d > cfg.FocusStickyExit) continue;

                candidates.Add(new Candidate
                {
                    Kind = FocusKind.Muscle,
                    Id = pair.Key,
                    Position = pair.Value.Ped.Position,
                    Distance = d
                });
            }

            // Prospects: only worth scanning when nothing of ours is nearby to
            // talk to, and only on a slow cadence.
            if (Game.GameTime >= _nextProspectScanAt)
            {
                _nextProspectScanAt = Game.GameTime + 600;
                _prospects.Clear();

                if (!GameState.Current.RosterFull)
                    _prospects.AddRange(Systems.Recruiter.FindProspects(_spawner, 3));
            }

            foreach (var ped in _prospects)
            {
                if (ped == null || !ped.Exists() || ped.IsDead) continue;

                float d = here.DistanceTo(ped.Position);
                if (d > cfg.FocusStickyExit) continue;

                candidates.Add(new Candidate
                {
                    Kind = FocusKind.Prospect,
                    Id = ped.Handle,
                    Position = ped.Position,
                    Distance = d
                });
            }

            if (candidates.Count == 0)
            {
                Clear();
                return;
            }

            // Facing filter for NEW acquisitions; the current target survives on
            // distance alone so turning your head does not drop a conversation.
            Candidate best = null;
            Candidate current = null;

            foreach (var c in candidates)
            {
                if (c.Kind == Kind && c.Id == RecordId) current = c;

                if (c.Distance > cfg.FocusRadius) continue;

                Vector3 to = c.Position - here;
                to.Z = 0f;
                if (to.Length() > 0.01f) to.Normalize();

                float dot = forward.X * to.X + forward.Y * to.Y;
                if (dot < cfg.FocusDotMin) continue;

                if (best == null || c.Distance < best.Distance) best = c;
            }

            // Coincidence refusal: two candidates effectively on one spot.
            if (best != null)
            {
                foreach (var c in candidates)
                {
                    if (c == best) continue;
                    if (c.Position.DistanceTo(best.Position) < cfg.FocusCoincidenceRefuse)
                    {
                        Clear();
                        GTA.UI.Screen.ShowHelpTextThisFrame(
                            "Two of them are standing together — step round so it's clear who you mean.");
                        return;
                    }
                }
            }

            // Sticky hold: keep the current target until it leaves the exit
            // radius or something meaningfully closer appears.
            if (current != null && current.Distance <= cfg.FocusStickyExit)
            {
                bool switchAway = best != null
                                  && (best.Kind != Kind || best.Id != RecordId)
                                  && best.Distance < current.Distance * cfg.FocusSwitchRatio;

                if (!switchAway)
                {
                    Show(current);
                    return;
                }
            }

            if (best == null)
            {
                Clear();
                return;
            }

            Kind = best.Kind;
            RecordId = best.Id;
            Show(best);
        }

        private void Show(Candidate c)
        {
            Kind = c.Kind;
            RecordId = c.Id;

            string line = Describe();
            if (line == null) { Clear(); return; }

            // The marker is the player-side confirmation BEFORE committing: you
            // can see who the game thinks you mean.
            World.DrawMarker(MarkerType.UpsideDownCone,
                c.Position + new Vector3(0f, 0f, 1.1f),
                Vector3.Zero, Vector3.Zero, new Vector3(0.22f, 0.22f, 0.22f),
                System.Drawing.Color.FromArgb(190, 255, 255, 255));

            GTA.UI.Screen.ShowHelpTextThisFrame(
                $"{line}~n~Press ~INPUT_CONTEXT~... ~b~{Config.Current.TalkKeyName}~s~ to talk.");
        }

        /// <summary>
        /// The volunteered line — what they would tell you before you say
        /// anything. This is the "talk to" the feature was asked for: the state
        /// the menus can only show as numbers, said as a person would say it.
        /// </summary>
        private string Describe()
        {
            var state = GameState.Current;

            if (Kind == FocusKind.Worker)
            {
                var worker = state.GetWorker(RecordId);
                if (worker == null) return null;

                string mood =
                    worker.WantsOut ? "~r~wants out~s~"
                    : worker.Stamina < Config.Current.DoctorOfferStaminaBelow ? "~o~running on empty~s~"
                    : worker.Loyalty >= 80f ? "~g~solid~s~"
                    : worker.Loyalty < 40f ? "~o~drifting~s~"
                    : "doing alright";

                return $"~b~{worker.Name}~s~ — {mood}, " +
                       $"{worker.Regulars} regular(s), tier {worker.Tier}.";
            }

            if (Kind == FocusKind.Prospect)
            {
                // Deliberately no numbers. You have not hired her yet; what you
                // can see is that she might listen.
                return "~b~She might listen~s~ — there's work if she wants it.";
            }

            if (Kind == FocusKind.Muscle)
            {
                var enforcer = state.Enforcers.Find(e => e.Id == RecordId);
                if (enforcer == null) return null;

                string condition = enforcer.IsInjured()
                    ? $"~r~laid up {enforcer.InjuryDaysLeft()}d~s~"
                    : enforcer.IsArmed
                        ? $"carrying {Armoury.NameOf(enforcer.WeaponId).ToLowerInvariant()}"
                        : "~o~empty-handed~s~";

                return $"~b~{enforcer.Name}~s~ — {condition}, skill {enforcer.Skill:0}.";
            }

            return null;
        }

        /// <summary>
        /// The focused prospect's ped, re-validated right now. For prospects the
        /// "record id" is a handle, which is exactly why every consumer must come
        /// back through here at the moment of use rather than trusting it.
        /// </summary>
        public bool TryGetProspect(out Ped ped)
        {
            ped = null;
            if (Kind != FocusKind.Prospect) return false;

            foreach (var p in _prospects)
            {
                if (p == null || !p.Exists() || p.IsDead) continue;
                if (p.Handle != RecordId) continue;

                ped = p;
                return true;
            }

            return false;
        }

        public void Clear()
        {
            Kind = FocusKind.None;
            RecordId = -1;
        }

        private class Candidate
        {
            public FocusKind Kind;
            public int Id;
            public Vector3 Position;
            public float Distance;
        }
    }
}

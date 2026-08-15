using System;
using System.Linq;
using GTA;
using LemonUI.Menus;
using OnTheBlade.Core;
using OnTheBlade.Runtime;
using OnTheBlade.Systems;

namespace OnTheBlade.UI
{
    /// <summary>
    /// The street menu — what you can do stood in front of one of your own.
    ///
    /// The split with the phone is structural, not stylistic: every row here is
    /// a strictly better version of a thing, and the better part only exists
    /// because a body does. The bonus lands harder handed over. The retention
    /// talk is cheaper because you came. The phone conditionally re-carries
    /// these verbs at full price for anyone with no live ped — indoor, jailed,
    /// off shift — so nothing is ever stranded, and nothing here can decay into
    /// "the phone, slower".
    /// </summary>
    public partial class UiRoot
    {
        private NativeMenu _street;
        private Runtime.WorkerFocus _focus;
        private FocusKind _streetKind;

        /// <summary>Handed over after construction — the focus system needs the
        /// UI for menu-visibility, so neither can own the other outright.</summary>
        public void BindFocus(Runtime.WorkerFocus focus) => _focus = focus;
        private int _streetId = -1;
        private bool _confirmFire;

        private void BuildStreetMenu()
        {
            _street = new NativeMenu("On The Blade", "FACE TO FACE");

            // However the conversation ends — signed, refused, Esc, walked away —
            // the woman held for it goes back to her day.
            _street.Closed += (s, e) => ReleaseProspect();
            _pool.Add(_street);
        }

        /// <summary>
        /// Lets the woman pinned for a conversation go back to her day. Safe on
        /// every path: signed (the ped is already deleted), refused, or the menu
        /// simply closed.
        /// </summary>
        private void ReleaseProspect()
        {
            // Same identity guard as the commit path: if the engine recycled the
            // handle (death screen, long menu), this reference now names some
            // OTHER ped — un-blocking and re-tasking that one would pull a
            // stranger, or one of our own workers, off whatever she was doing.
            if (_prospectPed != null && _prospectPed.Exists() && !_prospectPed.IsDead
                && _prospectPed.Model.Hash == _prospectModel)
            {
                _prospectPed.BlockPermanentEvents = false;
                _prospectPed.Task.ClearAll();
                _prospectPed.Task.WanderAround();
            }

            _prospectPed = null;
        }

        /// <summary>Called from the script's abort path — a reload while the
        /// street menu is open must not strand a frozen, event-blocked ped.</summary>
        public void AbortStreet() => ReleaseProspect();

        /// <summary>Any menu open, for the focus system's suppression rule.</summary>
        public bool AnyMenuVisible => _pool.AreAnyVisible;

        /// <summary>
        /// The talk key was pressed on a focused person.
        ///
        /// One special case bypasses the menu: a follower being pointed at a
        /// worker. "Roll with me", walk to her, press talk — he minds her now.
        /// That gesture replaces a sixteen-entry list, and it has to win over
        /// opening her menu or the gesture can never complete.
        /// </summary>
        private Ped _prospectPed;
        private int _prospectModel;

        public void OnTalk(FocusKind kind, int recordId)
        {
            if (kind == FocusKind.Worker && TryAssignFollower(recordId)) return;

            if (kind == FocusKind.Prospect)
            {
                Ped prospect;
                if (_focus == null || !_focus.TryGetProspect(out prospect)) return;

                // Pinned HERE, at the moment the conversation starts — the focus
                // system deliberately clears itself while any menu is open, so
                // commitment can never resolve back through it. It holds the ped
                // directly instead. And she stops to hear you out: an offer is
                // not shouted at a stranger's back halfway down the street.
                ReleaseProspect();

                _prospectPed = prospect;
                _prospectModel = prospect.Model.Hash;

                prospect.BlockPermanentEvents = true;
                prospect.Task.ClearAll();
                prospect.Task.StandStill(45000);
                prospect.Task.LookAt(Game.Player.Character, 45000);
            }

            _streetKind = kind;
            _streetId = recordId;
            _confirmFire = false;

            RebuildStreet();
            _street.Visible = true;
        }

        private bool TryAssignFollower(int workerId)
        {
            var state = GameState.Current;
            var follower = state.Enforcers.FirstOrDefault(e =>
            {
                EnforcerRuntime rt;
                return _spawner.TryGetMuscle(e.Id, out rt) && rt.Following;
            });

            if (follower == null) return false;

            var worker = state.GetWorker(workerId);
            if (worker == null) return false;

            state.ReleaseGuardsFor(workerId);
            follower.GuardingWorkerId = workerId;

            EnforcerRuntime runtime;
            if (_spawner.TryGetMuscle(follower.Id, out runtime))
            {
                runtime.Following = false;
                if (runtime.IsValid) runtime.Ped.Task.StandStill(-1);
            }

            Notify.Show($"~g~{follower.Name} minds {worker.Name} now.~s~ That's the whole job.");
            return true;
        }

        // ------------------------------------------------------------------

        private void RebuildStreet()
        {
            _street.Clear();

            if (_streetKind == FocusKind.Worker) BuildWorkerRows();
            else if (_streetKind == FocusKind.Muscle) BuildMuscleRows();
            else if (_streetKind == FocusKind.Prospect) BuildProspectRows();
        }

        private void BuildWorkerRows()
        {
            var state = GameState.Current;
            var worker = state.GetWorker(_streetId);
            var cfg = Config.Current;

            if (worker == null)
            {
                _street.Add(new NativeItem("(she's gone)") { Enabled = false });
                return;
            }

            _street.Name = worker.Name.ToUpperInvariant();

            // --- the bonus, handed over -----------------------------------
            float gain = NextBonusGain(worker);

            var bonus = new NativeItem("Here's something for you",
                $"${BonusCost} in her hand, worth +{gain:0.#} loyalty in person. " +
                "Handing money over loses its magic when it happens every day — " +
                "roughly one round a week is what actually lands.")
            {
                AltTitle = $"${BonusCost} (+{gain:0.#})",
                Enabled = Game.Player.Money >= BonusCost
            };
            bonus.Activated += (s, e) =>
            {
                // Re-validated at activation, not just at bind: she can walk
                // off, be jailed or be poached while the menu is open.
                var w = GameState.Current.GetWorker(_streetId);
                if (w == null || Game.Player.Money < BonusCost) return;

                Game.Player.Money -= BonusCost;

                int now = GameState.AbsoluteHour();
                if (now - w.LastBonusHour > cfg.BonusDiminishWindowHours) w.BonusHalvings = 0;

                w.Loyalty += NextBonusGain(w);
                w.LastBonusHour = now;
                if (w.BonusHalvings < 4) w.BonusHalvings++;
                w.Clamp();

                Notify.Show($"~g~{w.Name}~s~ — loyalty {w.Loyalty:0}.");
                RebuildStreet();
            };
            _street.Add(bonus);

            // --- the retention talk, discounted for showing up -------------
            if (worker.WantsOut)
            {
                int full = Crew.RetentionCost(worker);
                int cost = (int)Math.Round(full * cfg.InPersonRetentionMultiplier);

                var talk = new NativeItem("Talk it out",
                    $"She wants out. ${cost:N0} face to face against ${full:N0} through " +
                    "somebody else — showing up is most of the argument. It buys time, " +
                    "not a change of mind.")
                {
                    AltTitle = $"~g~${cost:N0}",
                    Enabled = Game.Player.Money >= cost
                };
                talk.Activated += (s, e) =>
                {
                    var w = GameState.Current.GetWorker(_streetId);
                    if (w == null || !w.WantsOut) { RebuildStreet(); return; }

                    if (Crew.RetainInPerson(w)) RebuildStreet();
                };
                _street.Add(talk);
            }

            // --- the doctor, offered when you can see she needs one --------
            if (worker.Stamina < cfg.DoctorOfferStaminaBelow)
            {
                var doctor = new NativeItem("See a doctor, take the week",
                    "She looks like she needs it, and it means more that you noticed.")
                {
                    AltTitle = $"${cfg.ClinicCost:N0}"
                };
                doctor.Activated += (s, e) =>
                {
                    var w = GameState.Current.GetWorker(_streetId);
                    if (w == null) return;

                    if (Progression.BuyClinic(w)) RebuildStreet();
                };
                _street.Add(doctor);
            }

            // --- tonight off ----------------------------------------------
            var rest = new NativeItem("Take the rest of the night",
                "Off the corner until her next shift, +4 loyalty. This is tonight — " +
                "the rota on the phone is a standing arrangement, and this is not that.")
            {
                Enabled = !worker.IsResting
            };
            rest.Activated += (s, e) =>
            {
                var w = GameState.Current.GetWorker(_streetId);
                if (w == null || w.IsResting) return;

                w.RestUntilHour = GameState.AbsoluteHour() + HoursUntilNextShift(w);
                w.Loyalty += 4f;
                w.Clamp();
                _spawner.Despawn(w.Id);

                Notify.Show($"~g~{w.Name}~s~ heads home. She'll remember it.");
                _street.Visible = false;
            };
            _street.Add(rest);

            // --- firing, said to her face, twice ---------------------------
            var fire = new NativeItem(
                _confirmFire ? "Say it — she's gone" : "You're done here",
                _confirmFire
                    ? "~r~This is the one that can't be taken back.~s~"
                    : "Let her go, in person. You will be asked once more.")
            {
                AltTitle = _confirmFire ? "~r~confirm" : string.Empty
            };
            fire.Activated += (s, e) =>
            {
                if (!_confirmFire)
                {
                    _confirmFire = true;
                    RebuildStreet();
                    return;
                }

                var w = GameState.Current.GetWorker(_streetId);
                if (w == null) return;

                GameState.Current.ReleaseGuardsFor(w.Id);
                _spawner.Despawn(w.Id);
                GameState.Current.Roster.Remove(w);

                Notify.Show($"~o~{w.Name} is off the books.~s~ She heard it from you, at least.");
                _street.Visible = false;
            };
            _street.Add(fire);
        }

        private void BuildMuscleRows()
        {
            var state = GameState.Current;
            var enforcer = state.Enforcers.FirstOrDefault(x => x.Id == _streetId);

            if (enforcer == null)
            {
                _street.Add(new NativeItem("(he's gone)") { Enabled = false });
                return;
            }

            _street.Name = enforcer.Name.ToUpperInvariant();

            EnforcerRuntime runtime;
            bool live = _spawner.TryGetMuscle(enforcer.Id, out runtime);

            // --- kit, bought at the man ------------------------------------
            foreach (var loadout in Armoury.All)
            {
                if (loadout.Id == enforcer.WeaponId) continue;

                var l = loadout;
                var row = new NativeItem($"Take this — {loadout.Name.ToLowerInvariant()}",
                    $"{loadout.Description} List price, because you brought it yourself.")
                {
                    AltTitle = $"${loadout.Cost:N0}",
                    Enabled = Game.Player.Money >= loadout.Cost
                };
                row.Activated += (s, e) =>
                {
                    BuyLoadout(_streetId, l);
                    RebuildStreet();
                };
                _street.Add(row);
            }

            // --- follow ----------------------------------------------------
            if (live && runtime.Following)
            {
                var stand = new NativeItem("Stand down",
                    "Back to his post. Nothing held against anybody.");
                stand.Activated += (s, e) =>
                {
                    EnforcerRuntime rt;
                    if (_spawner.TryGetMuscle(_streetId, out rt))
                    {
                        rt.Following = false;
                        if (rt.IsValid) rt.Ped.Task.StandStill(-1);
                    }

                    Notify.Show($"{enforcer.Name} heads back.");
                    RebuildStreet();
                };
                _street.Add(stand);
            }
            else if (live)
            {
                var roll = new NativeItem("Roll with me",
                    "He walks with you. Point him at one of the women — walk up to " +
                    "her and press the talk key — and he minds her from then on.");
                roll.Activated += (s, e) =>
                {
                    EnforcerRuntime rt;
                    if (!_spawner.TryGetMuscle(_streetId, out rt) || !rt.IsValid) return;

                    rt.Following = true;
                    rt.Ped.Task.ClearAll();
                    GTA.Native.Function.Call(GTA.Native.Hash.TASK_FOLLOW_TO_OFFSET_OF_ENTITY,
                        rt.Ped, Game.Player.Character, 0f, -1.5f, 0f, 2.0f, -1, 2.5f, true);

                    Notify.Show($"~g~{enforcer.Name} rolls with you.~s~ Walk up to her and press " +
                                $"~b~{Config.Current.TalkKeyName}~s~.");
                    _street.Visible = false;
                };
                _street.Add(roll);
            }

            // --- firing, twice ---------------------------------------------
            var fire = new NativeItem(
                _confirmFire ? "Say it — he's done" : "You're done",
                _confirmFire
                    ? "~r~Once more and he walks.~s~"
                    : "Let him go, to his face. You will be asked once more.")
            {
                AltTitle = _confirmFire ? "~r~confirm" : string.Empty
            };
            fire.Activated += (s, e) =>
            {
                if (!_confirmFire)
                {
                    _confirmFire = true;
                    RebuildStreet();
                    return;
                }

                var x = GameState.Current.Enforcers.FirstOrDefault(m => m.Id == _streetId);
                if (x == null) return;

                GameState.Current.Enforcers.Remove(x);
                Notify.Show($"~o~{x.Name} is off the payroll.");
                _street.Visible = false;
            };
            _street.Add(fire);
        }

        private void BuildProspectRows()
        {
            var state = GameState.Current;
            var area = _spotter.CurrentArea;

            _street.Name = "A PROSPECT";

            _street.Add(new NativeItem("What you can see",
                $"{RecruitAreas.Describe(area)}. " +
                (RecruitAreas.IsHotspot(area)
                    ? "Around here, people know what the work is before you say it."
                    : "Quieter ground — she may take more convincing, and the word " +
                      "travels differently.") +
                " You will not know her tier or her temperament until she signs.")
            {
                Enabled = false
            });

            var sign = new NativeItem("Offer her a contract",
                state.RosterFull
                    ? $"~r~Your book is full~s~ ({state.RosterCap}). Buy a bigger one under Upgrades."
                    : "Said to her face, which is the only way it is said now.")
            {
                Enabled = !state.RosterFull
            };

            sign.Activated += (s, e) =>
            {
                // Re-validated at commitment against the ped pinned when the
                // conversation opened — never through the focus system, which
                // clears itself while any menu is visible and so can vouch for
                // nobody here. Identity is the model hash (a recycled handle
                // practically never lands the same model in front of you);
                // presence is your distance to her NOW. She stood still for
                // this conversation — if she is gone, she is genuinely gone.
                var player = Game.Player.Character;
                Ped prospect = _prospectPed;

                // 8m, not the focus radius: the talk prompt itself is offered out
                // to FocusStickyExit (4.55m) and a walking woman takes another
                // stride to stop — a ceiling below that fails people who are
                // standing right there. Identity is the pinned ped + model hash;
                // distance only answers "did you walk off mid-offer".
                if (prospect == null || !prospect.Exists() || prospect.IsDead
                    || prospect.Model.Hash != _prospectModel
                    || player == null || !player.Exists()
                    || player.Position.DistanceTo(prospect.Position) > 8f)
                {
                    Notify.Show("~o~She's moved on.");
                    _street.Visible = false;
                    return;
                }

                var result = Recruiter.TryRecruitPed(prospect, _spawner);
                if (result == null)
                {
                    Notify.Show("~o~She's not interested.");
                    _street.Visible = false;
                    return;
                }

                var worker = result.Worker;
                Notify.Show(
                    $"~g~{worker.Name}~s~ signed on (tier {worker.Tier}). Assign her a post.");

                if (result.ClaimedFrom != null)
                {
                    Notify.Show(
                        $"~o~She was working for {result.ClaimedFrom.Name}.~s~ " +
                        "Experienced, but she doesn't trust you yet — and word travels.");

                    if (result.Retaliation)
                    {
                        _street.Visible = false;
                        _missions.TryStart(new Systems.Incidents.RetaliationIncident(
                            result.Where, result.ClaimedFrom.Id, worker.Id, _spawner));
                        return;
                    }
                }

                _street.Visible = false;
            };

            _street.Add(sign);
        }

        // ------------------------------------------------------------------

        private static float NextBonusGain(WorkerData worker)
        {
            var cfg = Config.Current;

            int halvings = GameState.AbsoluteHour() - worker.LastBonusHour
                           > cfg.BonusDiminishWindowHours
                ? 0
                : worker.BonusHalvings;

            float gain = cfg.InPersonBonusLoyalty / (1 << halvings);
            return gain < 1f ? 1f : gain;
        }

        private static int HoursUntilNextShift(WorkerData worker)
        {
            int hour = GTA.Chrono.GameClock.Hour;

            switch (worker.Shift)
            {
                case WorkerShift.Days: return ((6 - hour) % 24 + 24) % 24 + 1;
                case WorkerShift.Nights: return ((20 - hour) % 24 + 24) % 24 + 1;
                default: return 8;
            }
        }
    }
}

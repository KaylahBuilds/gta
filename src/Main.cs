using System;
using System.Windows.Forms;
using GTA;
using LemonUI;
using OnTheBlade.Core;
using OnTheBlade.Persistence;
using OnTheBlade.Runtime;
using OnTheBlade.Systems;
using OnTheBlade.UI;

namespace OnTheBlade
{
    /// <summary>
    /// Entry point. ScriptHookVDotNet instantiates this once when the game loads
    /// and again on every script reload (Insert key).
    /// </summary>
    public class Main : Script
    {
        private readonly ObjectPool _pool = new ObjectPool();

        private SpawnManager _spawner;
        private ProspectSpotter _spotter;
        private ZoneBlips _zoneBlips;
        private readonly Systems.HouseDoors _houseDoors = new Systems.HouseDoors();
        private readonly Systems.HouseTraffic _houseTraffic = new Systems.HouseTraffic();
        private readonly RivalGirls _rivalGirls = new RivalGirls();

        /// <summary>Controller: when d-pad LEFT went down, for the hold-to-open
        /// menu binding. 0 = not held.</summary>
        private int _padLeftDownAt;
        private CrewBlips _crewBlips;
        private MissionController _missions;
        private EconomyTick _economy;
        private UiRoot _ui;
        private PhoneBridge _phone;

        private bool _initialised;
        private int _autoSaveAt;

        public Main()
        {
            Interval = 0;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        /// <summary>
        /// Deferred to the first tick: the world is not reliably queryable from
        /// the Script constructor.
        /// </summary>
        private void Initialise()
        {
            Config.Load();

            // Logged because this mod ships one binary for both GTA V editions —
            // the first question on any bug report is which one it ran on.
            // DataDirectory resolves off the game root, so Legacy and Enhanced
            // installs keep separate configs and saves automatically.
            SaveManager.Log(
                $"Starting. GameFileVersion={Game.FileVersion}. Data={SaveManager.DataDirectory}");

            GameState.Current = SaveManager.LoadOrCreate();

            Factions.Reset();

            _spawner = new SpawnManager();
            _spawner.RegisterInstance();
            _spotter = new ProspectSpotter(_spawner);
            _zoneBlips = new ZoneBlips();
            _crewBlips = new CrewBlips(_spawner);
            _missions = new MissionController();
            _economy = new EconomyTick(new IncidentRoller(_missions, _spawner), _spawner);
            _ui = new UiRoot(_pool, _spawner, _missions, _spotter);
            _focus = new WorkerFocus(_spawner, () => _ui.AnyMenuVisible);
            _ui.BindFocus(_focus);

            _phone = new PhoneBridge(() => _ui.TogglePhone());
            _phone.Initialise();

            _autoSaveAt = Game.GameTime + Config.Current.AutoSaveIntervalMs;
            _initialised = true;

            Notify.Show(
                $"~g~On The Blade~s~ loaded — press ~b~{Config.Current.MenuKey}~s~ for the menu.");

            // Anything the loader needed to say but could not, because it ran
            // before the game could show a ticker. A damaged save or an
            // unparseable config is exactly the sort of thing a player must not
            // find out about from a log file two hours later.
            if (!string.IsNullOrEmpty(SaveManager.PendingNotice))
            {
                Notify.Show(SaveManager.PendingNotice, true);
                SaveManager.PendingNotice = null;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_initialised)
            {
                Initialise();
                return;
            }

            // Suppress everything during cutscenes and deaths. No loading-screen
            // check: SHV no longer starts scripts before the loading screen ends,
            // and Game.IsLoading is deprecated on that basis.
            if (!Game.Player.CanControlCharacter)
                return;

            Core.Notify.Pump();
            _pool.Process();

            // Order matters: stream peds first so an active incident can see the
            // worker's runtime on the same tick the player arrives.
            // Controller: HOLD D-PAD LEFT opens the business — the girls'
            // side of the couch setup; the product side lives on d-pad right.
            // A tap does nothing, so phone navigation is never stolen, and
            // nothing fires while a menu is already open.
            if (Game.LastInputMethod == InputMethod.GamePad && !_ui.AnyMenuVisible)
            {
                if (Game.IsControlJustPressed(GTA.Control.PhoneLeft))
                    _padLeftDownAt = Game.GameTime;

                if (_padLeftDownAt != 0 && Game.IsControlPressed(GTA.Control.PhoneLeft))
                {
                    if (Game.GameTime - _padLeftDownAt >= 450)
                    {
                        _padLeftDownAt = 0;
                        _ui.Toggle();
                    }
                }
                else
                {
                    _padLeftDownAt = 0;
                }
            }

            _spawner.Update();
            _focus?.Update();
            _spotter.Update();
            _zoneBlips.Update();
            _houseDoors.Update();
            _houseTraffic.Update();
            _rivalGirls.Update();
            _crewBlips.Update();
            _missions.Update();
            _economy.Update();
            _phone.Update();

            // THE SWEEP — every ped this mod has made, checked against the
            // ground under his own feet. Not opt-in: a spawner gets this by
            // going through SafeGround.CreatePed, which all of them now do.
            {
                var here = Game.Player.Character;
                if (here != null && here.Exists() && !here.IsInVehicle())
                    Core.SafeGround.HealAll(here.Position);
            }

            if (Game.GameTime >= _autoSaveAt)
            {
                _autoSaveAt = Game.GameTime + Config.Current.AutoSaveIntervalMs;
                SaveManager.Save(GameState.Current);
            }
        }

        private WorkerFocus _focus;

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_initialised) return;

            if (e.KeyCode == Config.Current.TalkKey && _focus != null && _focus.HasFocus)
            {
                _ui.OnTalk(_focus.Kind, _focus.RecordId);
                return;
            }

            // A door you are stood at: buy the place, or walk into it. After
            // the worker focus, because a girl in front of you outranks the
            // building behind her.
            if (e.KeyCode == Config.Current.TalkKey && _houseDoors.TryUse()) return;

            if (e.KeyCode == Config.Current.MenuKey)
                _ui.Toggle();
            else if (e.KeyCode == Config.Current.PhoneKey)
                _ui.TogglePhone();
        }

        /// <summary>
        /// Fired on script reload and on game exit. Every ped this mod owns must
        /// be released here or it leaks into the world permanently.
        /// </summary>
        private void OnAborted(object sender, EventArgs e)
        {
            if (!_initialised) return;

            // Missions first: aborting one un-sticks the worker's InTrouble state
            // before that state gets written to the save.
            _missions?.Abort();
            // A prospect pinned for a conversation is an ambient ped we froze —
            // the menu's Closed event can never fire again after this point.
            _ui?.AbortStreet();
            // Prospect blips sit on peds the mod does not own — leaking one leaves
            // a marker stuck on the map after the ped is gone.
            _spotter?.Clear();
            _zoneBlips?.Clear();
            _houseDoors?.Cleanup();
            _houseTraffic?.Cleanup();
            _rivalGirls.Clear();
            _crewBlips?.Clear();
            _spawner?.DespawnAll();

            // Handles, not ownership — every releaser above has already run.
            // This just stops a script reload sweeping stale handles.
            Core.SafeGround.ForgetAll();

            SaveManager.Save(GameState.Current);
        }
    }
}

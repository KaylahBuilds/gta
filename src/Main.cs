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
            _spotter = new ProspectSpotter(_spawner);
            _zoneBlips = new ZoneBlips();
            _missions = new MissionController();
            _economy = new EconomyTick(new IncidentRoller(_missions, _spawner));
            _ui = new UiRoot(_pool, _spawner, _missions, _spotter);

            _phone = new PhoneBridge(() => _ui.TogglePhone());
            _phone.Initialise();

            _autoSaveAt = Game.GameTime + Config.Current.AutoSaveIntervalMs;
            _initialised = true;

            Notify.Show(
                $"~g~On The Blade~s~ loaded — press ~b~{Config.Current.MenuKey}~s~ for the menu.");
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

            _pool.Process();

            // Order matters: stream peds first so an active incident can see the
            // worker's runtime on the same tick the player arrives.
            _spawner.Update();
            _spotter.Update();
            _zoneBlips.Update();
            _missions.Update();
            _economy.Update();
            _phone.Update();

            if (Game.GameTime >= _autoSaveAt)
            {
                _autoSaveAt = Game.GameTime + Config.Current.AutoSaveIntervalMs;
                SaveManager.Save(GameState.Current);
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_initialised) return;

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
            // Prospect blips sit on peds the mod does not own — leaking one leaves
            // a marker stuck on the map after the ped is gone.
            _spotter?.Clear();
            _zoneBlips?.Clear();
            _spawner?.DespawnAll();
            SaveManager.Save(GameState.Current);
        }
    }
}

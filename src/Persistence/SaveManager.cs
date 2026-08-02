using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using OnTheBlade.Core;

namespace OnTheBlade.Persistence
{
    /// <summary>
    /// JSON persistence via DataContractJsonSerializer. Deliberately uses only
    /// the BCL — dropping Newtonsoft.Json into scripts/ collides with whatever
    /// version another installed mod already loaded.
    /// </summary>
    public static class SaveManager
    {
        public static string DataDirectory
        {
            get
            {
                string dir = Path.Combine(ScriptsDirectory(), "OnTheBlade");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// The game's scripts folder.
        ///
        /// Two wrong answers were tried in-game before this one:
        ///
        /// 1. <c>Combine(BaseDirectory, "scripts")</c> — SHVDN's AppDomain already
        ///    has the scripts folder as its base, so this produced
        ///    <c>scripts\scripts\</c>.
        /// 2. <c>Assembly.GetExecutingAssembly().Location</c> — SHVDN shadow-copies
        ///    scripts, so this pointed into
        ///    <c>%LOCALAPPDATA%\assembly\dl3\...</c>, which is worse: a path that
        ///    changes per load and gets cleaned up by the runtime.
        ///
        /// BaseDirectory is correct; it just must not be blindly appended to.
        /// The check below handles both "base is the scripts folder" (SHVDN) and
        /// "base is the game root" (anything else) without guessing.
        /// </summary>
        private static string ScriptsDirectory()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

            return string.Equals(Path.GetFileName(root), "scripts", StringComparison.OrdinalIgnoreCase)
                ? root
                : Path.Combine(root, "scripts");
        }

        public static string SavePath => Path.Combine(DataDirectory, "save.json");

        public static T ReadJson<T>(string path) where T : class
        {
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serialiser = new DataContractJsonSerializer(typeof(T));
                    return serialiser.ReadObject(stream) as T;
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to read {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        public static void WriteJson<T>(string path, T value)
        {
            try
            {
                // Write to a temp file first so a crash mid-write cannot corrupt
                // an existing good save.
                string temp = path + ".tmp";
                using (var stream = File.Create(temp))
                {
                    var serialiser = new DataContractJsonSerializer(typeof(T));
                    serialiser.WriteObject(stream, value);
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (Exception ex)
            {
                Log($"Failed to write {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        public static GameState LoadOrCreate()
        {
            if (File.Exists(SavePath))
            {
                var state = ReadJson<GameState>(SavePath);
                if (state != null)
                {
                    state.EnsureCollections();
                    return state;
                }
            }

            // A brand-new game needs seeding too. Only loaded saves used to get
            // EnsureCollections, so a first run started with no rival crews and no
            // territory at all — no poaching, no turf war, half the milestones
            // unreachable.
            var fresh = new GameState();
            // Stamp the version first so the migrator does not log a spurious
            // "migrated from 0" line for a save that never existed.
            fresh.SaveVersion = GameState.CurrentSaveVersion;
            fresh.EnsureCollections();
            return fresh;
        }

        public static void Save(GameState state)
        {
            if (state == null) return;
            WriteJson(SavePath, state);
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(DataDirectory, "OnTheBlade.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch
            {
                // Logging must never take the script down.
            }
        }
    }
}

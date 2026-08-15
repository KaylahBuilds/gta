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

        /// <summary>
        /// Set when a file existed but could not be read. The caller has to be
        /// able to tell "no save yet" from "your save is damaged", because those
        /// two want opposite behaviour and treating them the same is how a
        /// forty-hour run silently becomes a new game.
        /// </summary>
        public static bool LastReadFailed { get; private set; }

        /// <summary>
        /// True once a load has failed. While it is set, nothing writes to disk —
        /// otherwise the 120-second autosave overwrites the damaged-but-possibly-
        /// recoverable original with a blank state before the player has noticed
        /// anything is wrong.
        /// </summary>
        public static bool SaveBlocked { get; private set; }

        /// <summary>What to tell the player on the first tick, if anything.</summary>
        public static string PendingNotice { get; set; }

        /// <summary>
        /// Adds a line to the pending notice rather than replacing it. There are
        /// three producers and one slot, and a damaged config and a damaged save
        /// usually arrive from the same shutdown — so a plain assignment meant
        /// the player was told about exactly one of them.
        /// </summary>
        public static string Append(string existing, string line)
        {
            if (string.IsNullOrEmpty(line)) return existing;
            return string.IsNullOrEmpty(existing) ? line : existing + "  " + line;
        }

        public static T ReadJson<T>(string path) where T : class
        {
            LastReadFailed = false;

            try
            {
                // A zero-length file is what a crash mid-write leaves behind. It
                // is not a save and it is not an absence; treating it as damaged
                // is what gets it quarantined rather than silently replaced.
                if (new FileInfo(path).Length == 0)
                {
                    LastReadFailed = true;
                    Log($"{Path.GetFileName(path)} is empty.");
                    return null;
                }

                using (var stream = File.OpenRead(path))
                {
                    var serialiser = new DataContractJsonSerializer(typeof(T));
                    var value = serialiser.ReadObject(stream) as T;

                    if (value == null) LastReadFailed = true;
                    return value;
                }
            }
            catch (Exception ex)
            {
                LastReadFailed = true;
                Log($"Failed to read {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Moves a file out of the way under a timestamped name so it is still
        /// there to be looked at, rather than overwritten by the next write.
        /// Returns the name it was moved to, or null.
        /// </summary>
        public static string Quarantine(string path, string tag)
        {
            try
            {
                if (!File.Exists(path)) return null;

                string moved = $"{path}.{tag}-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(path, moved);
                return Path.GetFileName(moved);
            }
            catch (Exception ex)
            {
                Log($"Could not quarantine {Path.GetFileName(path)}: {ex.Message}");
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

                // Replace rather than delete-then-move. The old pair left a
                // window in which neither file existed, and it is that window —
                // not a serialiser fault — that produces the truncated save this
                // code was written to prevent. Replace is atomic where the
                // filesystem supports it and keeps the previous generation as a
                // .bak either way, so there is always one save behind.
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
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

                // The file was there and could not be read. Everything below this
                // point builds a brand-new operation, so without this branch a
                // damaged save presented as "the mod forgot your run" and the
                // autosave finished the job two minutes later.
                if (LastReadFailed)
                {
                    // The .bak WriteJson now keeps is the most likely thing to
                    // still be good, so try it before giving up.
                    string backup = SavePath + ".bak";
                    if (File.Exists(backup))
                    {
                        var previous = ReadJson<GameState>(backup);
                        if (previous != null)
                        {
                            previous.EnsureCollections();
                            Quarantine(SavePath, "bad");
                            PendingNotice = Append(PendingNotice, "~o~Your save would not load.~s~ The previous one did, "
                                          + "and that is what you are playing. The damaged file was "
                                          + "kept alongside it.");
                            Log("save.json was unreadable; recovered from save.json.bak.");
                            return previous;
                        }
                    }

                    string moved = Quarantine(SavePath, "bad");
                    SaveBlocked = true;
                    PendingNotice = Append(PendingNotice, "~r~Your save could not be read and nothing will be written "
                                  + "this session.~s~ " + (moved != null
                                        ? $"It was kept as {moved}. "
                                        : string.Empty)
                                  + "Close the game before playing on, or you will overwrite it.");
                    Log("save.json was unreadable and no usable backup existed; saving is blocked.");
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

            // Refusing to write is the whole protection. A session started from a
            // blank state after a failed load must never be allowed to become the
            // save on disk.
            if (SaveBlocked) return;

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

using System;
using System.IO;
using System.Runtime.Serialization.Json;
using OnTheBlade.Persistence;

namespace OnTheBlade.BladeWorld
{
    /// <summary>
    /// Reading and writing scripts/BladeWorld/world.json.
    ///
    /// **This must never be able to break the mod.** A missing file means the
    /// other mod is not installed; an unreadable one means something went wrong
    /// on somebody else's side. Both drop straight to standalone and log it once.
    /// Nothing here throws upward and nothing here blocks a tick.
    ///
    /// The directory is deliberately not under either mod's folder. It is owned
    /// by neither, and naming it after one would make the other a guest in it.
    /// </summary>
    public static class WorldFile
    {
        private const string FolderName = "BladeWorld";
        private const string FileName = "world.json";

        /// <summary>Who we are in the file. The other mod writes "blade".</summary>
        public const string Me = "blade";

        private static WorldData _cache;
        private static DateTime _cacheStamp;
        private static int _nextCheckMs;
        /// <summary>Read failures have been reported once. Reset by a good read.</summary>
        private static bool _warnedRead;

        /// <summary>The newer-schema notice has been given once.</summary>
        private static bool _warnedSchema;

        /// <summary>Write failures have been reported once. Reset by a good write.</summary>
        private static bool _warnedWrite;

        /// <summary>Whether a world file exists at all — i.e. whether we are integrated.</summary>
        public static bool Linked { get; private set; }

        public static string Directory
        {
            get
            {
                string scripts = Path.GetDirectoryName(SaveManager.DataDirectory.TrimEnd('\\', '/'));
                string dir = Path.Combine(scripts ?? ".", FolderName);
                return dir;
            }
        }

        public static string FilePath => Path.Combine(Directory, FileName);

        /// <summary>
        /// The current world, or null if there is no usable file.
        ///
        /// Cached and only re-read when the file's timestamp moves, checked at
        /// most every couple of seconds — this runs on a tick and the other mod
        /// writes it several times a minute.
        /// </summary>
        public static WorldData Read()
        {
            if (GTA.Game.GameTime < _nextCheckMs) return _cache;
            _nextCheckMs = GTA.Game.GameTime + 2000;

            try
            {
                if (!File.Exists(FilePath))
                {
                    Linked = false;
                    _cache = null;
                    return null;
                }

                // A crash mid-write leaves a zero-length file behind. That is an
                // absence, not a corruption — quarantining it would move a file
                // the other mod is about to rewrite anyway.
                if (new FileInfo(FilePath).Length == 0)
                {
                    Linked = false;
                    _cache = null;
                    return null;
                }

                DateTime stamp = File.GetLastWriteTimeUtc(FilePath);
                if (_cache != null && stamp == _cacheStamp) return _cache;

                using (var stream = File.OpenRead(FilePath))
                {
                    var serialiser = new DataContractJsonSerializer(typeof(WorldData));
                    var data = serialiser.ReadObject(stream) as WorldData;

                    if (data == null) return FailRead("world.json parsed to nothing");

                    data.EnsureCollections();

                    // A newer schema is read for what we recognise. Refusing it
                    // would be worse: the file is shared, and a mod that cannot
                    // read a newer world should degrade, not disconnect.
                    if (data.SchemaVersion > WorldData.CurrentSchema && !_warnedSchema)
                    {
                        SaveManager.Log(
                            $"world.json is schema {data.SchemaVersion}, we know " +
                            $"{WorldData.CurrentSchema}. Reading what we recognise.");
                        _warnedSchema = true;
                    }

                    _cache = data;
                    _cacheStamp = stamp;
                    Linked = true;
                    _warnedRead = false;
                    return data;
                }
            }
            catch (Exception ex)
            {
                // Unparseable. Moved aside rather than left in place: nothing in
                // either mod ever deletes this file, so a single bad write used
                // to mean both mods ran standalone forever with one log line
                // between them, and the only cure was for the player to find and
                // delete a file nobody had told them about.
                Quarantine(ex.Message);
                return FailRead(ex.Message);
            }
        }

        /// <summary>
        /// Moves a corrupt world.json out of the way so the next write starts
        /// clean. Best-effort — if it cannot be moved we simply carry on
        /// standalone, which is what would have happened anyway.
        /// </summary>
        private static void Quarantine(string why)
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                string moved = $"{FilePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(FilePath, moved);

                SaveManager.Log(
                    $"world.json was unreadable ({why}); moved to " +
                    $"{System.IO.Path.GetFileName(moved)} so the link can rebuild.");
            }
            catch (Exception ex)
            {
                SaveManager.Log($"Could not move the unreadable world.json aside: {ex.Message}");
            }
        }

        /// <summary>
        /// A read failed. Only a READ may drop the cache — a write failure used
        /// to come through the same path and throw away a perfectly good read,
        /// which is what made the other mod's corners read as unowned and let
        /// them be worked when they should not have been.
        /// </summary>
        private static WorldData FailRead(string why)
        {
            if (!_warnedRead)
            {
                SaveManager.Log($"world.json unusable ({why}). Running standalone.");
                _warnedRead = true;
            }

            Linked = false;
            _cache = null;
            return null;
        }

        /// <summary>A write failed. The cache is still whatever we last read.</summary>
        private static void FailWrite(string why)
        {
            if (_warnedWrite) return;

            SaveManager.Log($"Could not write world.json ({why}). Still reading it.");
            _warnedWrite = true;
        }

        /// <summary>
        /// Read-modify-write, so we only ever touch our own fields.
        ///
        /// The re-read happens immediately before writing rather than using the
        /// cache, to keep the window in which the other mod's update could be
        /// lost as small as possible. If one is lost anyway it self-corrects on
        /// the next write, because every value here is recomputed from each mod's
        /// own state rather than accumulated in the file.
        /// </summary>
        public static void Update(Action<WorldData> mutate)
        {
            if (mutate == null) return;

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                WorldData data = null;

                if (File.Exists(FilePath))
                {
                    try
                    {
                        using (var stream = File.OpenRead(FilePath))
                        {
                            var reader = new DataContractJsonSerializer(typeof(WorldData));
                            data = reader.ReadObject(stream) as WorldData;
                        }
                    }
                    catch
                    {
                        // Unreadable. Do not overwrite somebody else's file with a
                        // blank one just because we could not parse it.
                        FailWrite("could not re-read before write");
                        return;
                    }
                }

                if (data == null) data = new WorldData();
                data.EnsureCollections();

                // Never write a version down. If the file is newer than us, the
                // fields we do not know about are already loaded and will survive
                // the round trip untouched.
                if (data.SchemaVersion < WorldData.CurrentSchema)
                    data.SchemaVersion = WorldData.CurrentSchema;

                mutate(data);
                data.UpdatedBy = Me;

                // Per-mod temp name. Both mods computed the same world.json.tmp,
                // so two writes landing in the same tick could each create, and
                // then each move, the other's half-written file.
                string temp = $"{FilePath}.{Me}.tmp";
                using (var stream = File.Create(temp))
                {
                    var writer = new DataContractJsonSerializer(typeof(WorldData));
                    writer.WriteObject(stream, data);
                }

                // Replace rather than delete-then-move: the pair left a window
                // in which the file did not exist at all, and a read from the
                // other mod landing in that window is exactly the transient that
                // used to unlink the two.
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);

                _cache = data;
                _cacheStamp = File.GetLastWriteTimeUtc(FilePath);
                Linked = true;
                _warnedWrite = false;
            }
            catch (Exception ex)
            {
                FailWrite(ex.Message);
            }
        }
    }
}

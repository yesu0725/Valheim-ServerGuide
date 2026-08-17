using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ValheimServerGuide.State
{
    /// On-disk shape of one character's progress file.
    /// Everything above `progress:` is human-readable bookkeeping — only `progress:` is read back
    /// into the game, so an admin can safely hand-edit a quest key without breaking anything.
    public class PlayerProgressFile
    {
        public string PlayerName { get; set; }
        public long CharacterId { get; set; }
        public bool MigratedFromCharacterFile { get; set; }
        public string MigratedAt { get; set; }
        public string LastSaved { get; set; }
        public Dictionary<string, string> Progress { get; set; } = new Dictionary<string, string>();
    }

    /// Server-side per-character progress files: one folder, one file per character.
    ///
    ///   <config>/ValheimServerGuide/PlayerProgress/<PlayerName>_<characterId>.yml
    ///
    /// The character id (Player.GetPlayerID(), stable for the life of the character) is the real
    /// key; the name is in the filename only so the folder is readable. A renamed character is
    /// found by id and its file renamed to match, so progress survives a rename.
    ///
    /// Only the authoritative process touches this: the dedicated server, a listen-server host,
    /// or a single-player session (which is its own server and so keeps its own separate folder —
    /// this is exactly what stops offline play from counting on the server).
    ///
    /// Writes are coalesced: mutations mark a record dirty and Tick() flushes at most every
    /// SaveIntervalSeconds. SaveAll() forces a flush on disconnect and shutdown.
    public static class PlayerProgressStore
    {
        private const double SaveIntervalSeconds = 3.0;
        private const string FileExtension = ".yml";

        /// Folder name under the plugin's config directory. Excluded from the guidance YAML
        /// watcher (see GuidanceConfigLoader.IsProgressPath) — these files are save data, not config.
        public const string FolderName = "PlayerProgress";

        private class Record
        {
            public string FileKey;
            public string PlayerName;
            public long CharacterId;
            public Dictionary<string, string> Data;
            public bool MigratedFromCharacterFile;
            public string MigratedAt;
            public bool Dirty;
        }

        private static readonly Dictionary<string, Record> _records =
            new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastSave = DateTime.MinValue;
        private static string _dir;

        /// Resolved once. Override with the `PlayerProgress.ProgressPath` BepInEx setting to keep
        /// progress outside the config tree (recommended if a mod manager rewrites config/).
        public static string Directory
        {
            get
            {
                if (_dir != null) return _dir;
                var configured = Plugin.ProgressPath?.Value;
                _dir = string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(BepInEx.Paths.ConfigPath, Plugin.PluginName, FolderName)
                    : configured.Trim();
                return _dir;
            }
        }

        // ── Keys and paths ───────────────────────────────────────────────────────────────────

        public static string FileKeyFor(string playerName, long characterId)
            => Sanitize(playerName) + "_" + characterId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "player";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => invalid.Contains(c) || c == '_' ? '-' : c).ToArray();
            var cleaned = new string(chars).Trim('.', ' ');
            return string.IsNullOrEmpty(cleaned) ? "player" : cleaned;
        }

        private static string PathFor(string fileKey)
            => Path.Combine(Directory, fileKey + FileExtension);

        /// Finds an existing file for this character id even if the character was renamed
        /// (filename holds the old name). Returns null when the character has no file yet.
        private static string FindExistingPath(string fileKey, long characterId)
        {
            var expected = PathFor(fileKey);
            if (File.Exists(expected)) return expected;
            if (!System.IO.Directory.Exists(Directory)) return null;

            var suffix = "_" + characterId.ToString(System.Globalization.CultureInfo.InvariantCulture) + FileExtension;
            return System.IO.Directory
                .EnumerateFiles(Directory, "*" + FileExtension)
                .FirstOrDefault(p => Path.GetFileName(p).EndsWith(suffix, StringComparison.Ordinal));
        }

        // ── Binding / loading ────────────────────────────────────────────────────────────────

        /// Returns the live progress dictionary for this character, loading it from disk on first
        /// use. When no file exists yet, one is created from `seedFactory()` — the one-time
        /// migration of whatever was still stored in the character save — and `migrated` is set
        /// true. The returned dictionary is the record's own instance: mutate it and call
        /// MarkDirty(fileKey).
        public static Dictionary<string, string> Bind(
            string fileKey, string playerName, long characterId,
            Func<Dictionary<string, string>> seedFactory, out bool migrated)
        {
            migrated = false;
            if (_records.TryGetValue(fileKey, out var cached))
            {
                // A rename between sessions: keep the loaded data, refresh the display name.
                cached.PlayerName = playerName;
                return cached.Data;
            }

            var record = LoadRecord(fileKey, playerName, characterId);
            if (record == null)
            {
                var seed = seedFactory?.Invoke() ?? new Dictionary<string, string>(StringComparer.Ordinal);
                migrated = seed.Count > 0;
                record = new Record
                {
                    FileKey = fileKey,
                    PlayerName = playerName,
                    CharacterId = characterId,
                    Data = new Dictionary<string, string>(seed, StringComparer.Ordinal),
                    MigratedFromCharacterFile = migrated,
                    MigratedAt = migrated ? Timestamp() : null,
                    Dirty = true,
                };
                Plugin.Log.LogInfo(migrated
                    ? $"[progress] MIGRATION: '{playerName}' (character {characterId}) had {seed.Count} " +
                      "progress key(s) in their character file — copied into the server store. " +
                      "This runs once; future logins read the server file."
                    : $"[progress] new store for '{playerName}' (character {characterId}) — no prior progress found.");
                _records[fileKey] = record;
                SaveRecord(record); // persist immediately so the migration cannot be repeated
                return record.Data;
            }

            _records[fileKey] = record;
            Plugin.Log.LogInfo($"[progress] loaded {record.Data.Count} key(s) for '{playerName}' (character {characterId}).");
            return record.Data;
        }

        private static Record LoadRecord(string fileKey, string playerName, long characterId)
        {
            string path;
            try { path = FindExistingPath(fileKey, characterId); }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[progress] could not scan '{Directory}': {ex.Message}");
                return null;
            }
            if (path == null) return null;

            PlayerProgressFile parsed;
            try
            {
                var yaml = File.ReadAllText(path);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                parsed = deserializer.Deserialize<PlayerProgressFile>(yaml);
            }
            catch (Exception ex)
            {
                // Never silently start a player from zero on a parse error — that would look
                // exactly like "the server wiped my quests". Keep the bad file for inspection.
                var backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                try { File.Move(path, backup); } catch { /* best effort */ }
                Plugin.Log.LogError($"[progress] '{Path.GetFileName(path)}' failed to parse ({ex.Message}). " +
                                    $"Moved to '{Path.GetFileName(backup)}'; starting a fresh store for '{playerName}'.");
                return null;
            }

            var record = new Record
            {
                FileKey = fileKey,
                PlayerName = playerName,
                CharacterId = characterId,
                Data = parsed?.Progress != null
                    ? new Dictionary<string, string>(parsed.Progress, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal),
                MigratedFromCharacterFile = parsed?.MigratedFromCharacterFile ?? false,
                MigratedAt = parsed?.MigratedAt,
            };

            // Character was renamed: move the file so the folder stays readable.
            var expected = PathFor(fileKey);
            if (!string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Move(path, expected);
                    Plugin.Log.LogInfo($"[progress] character {characterId} renamed — " +
                                       $"'{Path.GetFileName(path)}' -> '{Path.GetFileName(expected)}'.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[progress] could not rename '{Path.GetFileName(path)}': {ex.Message}");
                }
            }

            return record;
        }

        // ── Server-side mutation (deltas from a client) ───────────────────────────────────────

        /// Applies a client's queued changes. Creates and migrates the record if this is the
        /// character's first contact (a client whose request RPC was lost still lands here).
        public static void ApplyDeltas(string fileKey, string playerName, long characterId,
                                       IEnumerable<ProgressDelta> deltas)
        {
            if (deltas == null) return;
            if (!_records.TryGetValue(fileKey, out var record))
            {
                Bind(fileKey, playerName, characterId, null, out _);
                if (!_records.TryGetValue(fileKey, out record)) return;
            }

            var applied = 0;
            foreach (var d in deltas)
            {
                if (string.IsNullOrEmpty(d.Key)) continue;
                if (!d.Key.StartsWith(PlayerProgress.KeyPrefix, StringComparison.Ordinal))
                {
                    Plugin.Log.LogWarning($"[progress] rejected out-of-namespace key '{d.Key}' from '{playerName}'.");
                    continue;
                }
                if (d.Removed) record.Data.Remove(d.Key);
                else record.Data[d.Key] = d.Value ?? "";
                applied++;
            }

            if (applied == 0) return;
            record.Dirty = true;
            Plugin.Log.LogDebug($"[progress] applied {applied} change(s) for '{playerName}'.");
        }

        /// A copy of the character's progress, for pushing to their client.
        public static Dictionary<string, string> Snapshot(string fileKey)
            => _records.TryGetValue(fileKey, out var r)
                ? new Dictionary<string, string>(r.Data, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

        public static void MarkDirty(string fileKey)
        {
            if (fileKey != null && _records.TryGetValue(fileKey, out var r)) r.Dirty = true;
        }

        // ── Persistence ──────────────────────────────────────────────────────────────────────

        /// Pumped from Plugin.Update. Writes dirty records at most every SaveIntervalSeconds so a
        /// burst of quest activity is one disk write, not twenty.
        public static void Tick()
        {
            if (_records.Count == 0) return;
            if ((DateTime.UtcNow - _lastSave).TotalSeconds < SaveIntervalSeconds) return;
            SaveAll();
        }

        public static void SaveAll()
        {
            _lastSave = DateTime.UtcNow;
            foreach (var record in _records.Values.Where(r => r.Dirty).ToList())
                SaveRecord(record);
        }

        /// Save and forget a character (their peer disconnected). Keeps the server's memory flat
        /// on a long-running world with many visitors.
        public static void Unload(string fileKey)
        {
            if (fileKey == null || !_records.TryGetValue(fileKey, out var record)) return;
            if (record.Dirty) SaveRecord(record);
            _records.Remove(fileKey);
        }

        private static void SaveRecord(Record record)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                var dto = new PlayerProgressFile
                {
                    PlayerName = record.PlayerName,
                    CharacterId = record.CharacterId,
                    MigratedFromCharacterFile = record.MigratedFromCharacterFile,
                    MigratedAt = record.MigratedAt,
                    LastSaved = Timestamp(),
                    Progress = record.Data.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
                };

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();
                var yaml = Header + serializer.Serialize(dto);

                // Write beside the target and swap, so a crash mid-write cannot leave a
                // half-written progress file where a complete one used to be.
                var target = PathFor(record.FileKey);
                var temp = target + ".tmp";
                File.WriteAllText(temp, yaml);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);

                record.Dirty = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[progress] failed to save '{record.FileKey}': {ex.Message}");
            }
        }

        private static string Timestamp()
            => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

        private const string Header =
@"# ValheimServerGuide — server-side quest progress for one character.
# Managed by the mod; edit only while the character is offline (values are cached in memory
# while they are connected and will be overwritten on the next save).
# 'character_id' is the real identity — the name in the filename is for readability only.
";
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ValheimServerGuide.Config
{
    /// Watches the config\ValheimServerGuide folder and merges every *.yaml / *.yml
    /// file in it into a single GuidanceConfig. Any filename works as long as the
    /// content matches the guidance YAML schema.
    ///
    /// THREADING: FileSystemWatcher raises its events on a background ThreadPool
    /// thread. The downstream of ConfigChanged touches Unity / ZRoutedRpc (broadcast
    /// to clients, MessageHud, tutorial registration) which MUST run on the Unity
    /// main thread. So the watcher only flags a pending reload; the actual reload +
    /// ConfigChanged invoke is pumped from the main thread via Tick() (called from
    /// Plugin.Update). This is what makes live-reload reach connected clients without
    /// a server restart.
    public class GuidanceConfigLoader : IDisposable
    {
        private readonly string _dir;
        private FileSystemWatcher _watcher;

        // Set on the watcher thread, consumed on the main thread in Tick().
        private volatile bool _pending;
        private DateTime _lastEvent = DateTime.MinValue;
        private const double DebounceMs = 500;

        public event Action<GuidanceConfig> ConfigChanged;

        public GuidanceConfigLoader(string dir)
        {
            _dir = dir;
        }

        public void Start()
        {
            Directory.CreateDirectory(_dir);

            if (!HasAnyYaml())
            {
                WriteStarterFile();
            }

            // Initial load runs synchronously on the caller's (main) thread.
            Reload();

            // No Filter set: FileSystemWatcher.Filter only takes a single pattern, and we
            // want both *.yaml and *.yml. We filter by extension in the handler instead.
            _watcher = new FileSystemWatcher(_dir)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                             | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
        }

        private bool HasAnyYaml()
        {
            return Directory.Exists(_dir) && EnumerateYamlFiles().Any();
        }

        private IEnumerable<string> EnumerateYamlFiles()
        {
            return Directory.EnumerateFiles(_dir)
                .Where(IsYamlPath)
                // Deterministic, case-insensitive order so duplicate-id resolution and
                // tracker-section precedence don't depend on filesystem enumeration order.
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsYamlPath(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase);
        }

        // Runs on a background ThreadPool thread — do NOT touch Unity here.
        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            bool relevant = IsYamlPath(e.FullPath);
            if (!relevant && e is RenamedEventArgs r)
                relevant = IsYamlPath(r.OldFullPath); // a yaml renamed to something else
            if (!relevant) return;

            _lastEvent = DateTime.UtcNow;
            _pending = true;
        }

        /// Pumped from the Unity main thread (Plugin.Update). Applies a pending reload
        /// once the debounce window has elapsed, so editors that emit several events per
        /// save (and files still being flushed) coalesce into one reload.
        public void Tick()
        {
            if (!_pending) return;
            if ((DateTime.UtcNow - _lastEvent).TotalMilliseconds < DebounceMs) return;
            _pending = false;

            try { Reload(); }
            catch (Exception ex) { Plugin.Log.LogError($"YAML reload failed: {ex.Message}"); }
        }

        public void Reload()
        {
            var merged = LoadAndMerge();
            var config = Validate(merged);
            Plugin.Log.LogInfo($"Loaded {config.Guidances.Count} guidance entries from {_dir}");
            ConfigChanged?.Invoke(config);
        }

        /// Reads and deserializes every YAML file in the folder, concatenating their
        /// guidance entries. Duplicate ids across files are reported and dropped by
        /// Validate (first occurrence wins). The first file (alphabetically) that
        /// defines a `tracker:` section provides it.
        private GuidanceConfig LoadAndMerge()
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var merged = new GuidanceConfig();
            var files = EnumerateYamlFiles().ToList();

            foreach (var file in files)
            {
                GuidanceConfig part;
                try
                {
                    var yaml = File.ReadAllText(file);
                    part = deserializer.Deserialize<GuidanceConfig>(yaml);
                }
                catch (Exception ex)
                {
                    // One malformed file must not blank out every other file's guidance.
                    Plugin.Log.LogError($"VSG: failed to parse '{Path.GetFileName(file)}' — skipped. {ex.Message}");
                    continue;
                }

                if (part == null) continue;

                if (part.Guidances != null)
                    merged.Guidances.AddRange(part.Guidances);

                if (part.Tracker != null)
                {
                    if (merged.Tracker == null) merged.Tracker = part.Tracker;
                    else Plugin.Log.LogWarning(
                        $"VSG: '{Path.GetFileName(file)}' also defines a tracker: section — ignored (first file wins).");
                }
            }

            Plugin.Log.LogInfo($"VSG: merged {merged.Guidances.Count} raw entries from {files.Count} YAML file(s).");
            return merged;
        }

        private static readonly HashSet<string> _validCategories = new HashSet<string>
        {
            "Companions", "Trading", "Building", "Skills", "Exploration", "Inventory", "Groups", "General"
        };

        private static GuidanceConfig Validate(GuidanceConfig raw)
        {
            var result = new GuidanceConfig { Tracker = raw.Tracker };
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in raw.Guidances)
            {
                if (string.IsNullOrEmpty(entry.Id))
                {
                    Plugin.Log.LogWarning("VSG: Guidance entry with empty id — skipped.");
                    continue;
                }
                if (seenIds.Contains(entry.Id))
                {
                    Plugin.Log.LogError($"VSG: Duplicate guidance id '{entry.Id}' — second entry skipped.");
                    continue;
                }
                seenIds.Add(entry.Id);

                if (entry.Steps != null && entry.Steps.Count > 0)
                {
                    if (entry.Trigger != null || entry.Display != null || !string.IsNullOrEmpty(entry.Message))
                        Plugin.Log.LogWarning($"VSG: Entry '{entry.Id}': steps present — top-level trigger/display/message are ignored.");

                    for (int i = 0; i < entry.Steps.Count; i++)
                    {
                        var step = entry.Steps[i];
                        if (step.Trigger == null)
                            Plugin.Log.LogWarning($"VSG: Entry '{entry.Id}' step {i}: missing trigger.");
                        if (string.IsNullOrEmpty(step.Message))
                            Plugin.Log.LogWarning($"VSG: Entry '{entry.Id}' step {i}: missing message.");
                        if (step.ProgressGoal > 0 && step.ProgressTrigger == null)
                        {
                            Plugin.Log.LogWarning($"VSG: Entry '{entry.Id}' step {i}: progress_goal > 0 requires progress_trigger — treated as 0.");
                            step.ProgressGoal = 0;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(entry.Category) && !_validCategories.Contains(entry.Category))
                    Plugin.Log.LogWarning($"VSG: Entry '{entry.Id}': unrecognized category '{entry.Category}'.");

                Rewards.RewardDispatcher.ValidateRewards(entry.Rewards, $"entry '{entry.Id}'");
                if (entry.Conversation?.Choices != null)
                {
                    for (int i = 0; i < entry.Conversation.Choices.Count; i++)
                        Rewards.RewardDispatcher.ValidateRewards(entry.Conversation.Choices[i].Rewards,
                            $"entry '{entry.Id}' choice {i}");
                }

                result.Guidances.Add(entry);
            }

            foreach (var entry in result.Guidances)
            {
                if (entry.Requires == null) continue;
                foreach (var req in entry.Requires)
                {
                    if (!seenIds.Contains(req))
                        Plugin.Log.LogWarning($"VSG: Entry '{entry.Id}': requires unknown id '{req}'.");
                }
            }

            return result;
        }

        private void WriteStarterFile()
        {
            var path = Path.Combine(_dir, "guidance.yaml");
            File.WriteAllText(path, StarterYaml);
            Plugin.Log.LogInfo($"Wrote starter guidance config to {path}");
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }

        // Starter uses basic arrows so a fresh character can hit every entry.
        // Wood arrows need only wood -> any new character can craft them immediately.
        private const string StarterYaml =
@"# ValheimServerGuide — server-authoritative guidance config.
# Reloads automatically when any *.yaml / *.yml file in this folder is saved.
# You can split your guidance across multiple files — they are all merged.
#
# Test plan: every display mode is wired to a different basic arrow.
# Craft each arrow type to verify the corresponding mode renders correctly.
guidances:
  - id: test_raven_wood_arrow
    trigger: { type: craft, item: ArrowWood }
    display:
      mode: raven
      topic: ""First Shafts""
      text: ""Wooden arrows — a hunter's first answer to the wild.""
    once: true

  - id: test_message_flint_arrow
    trigger: { type: craft, item: ArrowFlint }
    display:
      mode: message
      position: Center
      text: ""Flint bites deeper than wood. Aim true.""
    once: false

  - id: test_chat_fire_arrow
    trigger: { type: craft, item: ArrowFire }
    display:
      mode: chat
      text: ""[lore] Fire arrows: useful against the unliving.""
    once: false

  - id: test_rune_bronze_arrow
    trigger: { type: craft, item: ArrowBronze }
    display:
      mode: rune
      topic: ""Of Bronze and Bowyers""
      text: ""Bronze tips were the dwarves' gift to the bow — pierce armor, fell beasts. While this stone glows, no eye may find you.""
    once: true

  - id: test_intro_iron_arrow
    trigger: { type: craft, item: ArrowIron }
    display:
      mode: intro
      topic: ""The Iron Volley""
      text: ""When iron flew from yew, even the trolls knelt. The realm shifts beneath your bow, traveler.""
    once: true
";
    }
}

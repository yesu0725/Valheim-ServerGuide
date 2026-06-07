using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ValheimServerGuide.Config
{
    public class GuidanceConfigLoader : IDisposable
    {
        private readonly string _path;
        private FileSystemWatcher _watcher;
        private DateTime _lastReload = DateTime.MinValue;

        public event Action<GuidanceConfig> ConfigChanged;

        public GuidanceConfigLoader(string path)
        {
            _path = path;
        }

        public void Start()
        {
            if (!File.Exists(_path))
            {
                WriteStarterFile();
            }

            Reload();

            var dir = Path.GetDirectoryName(_path);
            var file = Path.GetFileName(_path);
            _watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce — editors often fire multiple events for one save.
            var now = DateTime.UtcNow;
            if ((now - _lastReload).TotalMilliseconds < 500) return;
            _lastReload = now;

            try { Reload(); }
            catch (Exception ex) { Plugin.Log.LogError($"YAML reload failed: {ex.Message}"); }
        }

        public void Reload()
        {
            var yaml = File.ReadAllText(_path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var raw = deserializer.Deserialize<GuidanceConfig>(yaml) ?? GuidanceConfig.Empty;
            var config = Validate(raw);
            Plugin.Log.LogInfo($"Loaded {config.Guidances.Count} guidance entries from {_path}");
            ConfigChanged?.Invoke(config);
        }

        private static readonly HashSet<string> _validCategories = new HashSet<string>
        {
            "Companions", "Trading", "Building", "Skills", "Exploration", "Inventory", "Groups"
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
            File.WriteAllText(_path, StarterYaml);
            Plugin.Log.LogInfo($"Wrote starter guidance config to {_path}");
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }

        // Starter uses basic arrows so a fresh character can hit every entry.
        // Wood arrows need only wood -> any new character can craft them immediately.
        private const string StarterYaml =
@"# ValheimServerGuide — server-authoritative guidance config.
# Reloads automatically when this file is saved.
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

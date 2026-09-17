using System.Collections.ObjectModel;

using Bloxstrap.Models.Entities;

namespace Bloxstrap.Integrations.AssetProxy
{
    public static class AssetProxyConfigFiles
    {
        private const string PresetResourcePrefix = "Bloxstrap.Resources.AssetProxyPresets.";

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static IReadOnlyList<string> PresetNames { get; } = LoadPresetNames();

        public static AssetProxyConfigFile Parse(string json)
        {
            AssetProxyConfigFile? file = JsonSerializer.Deserialize<AssetProxyConfigFile>(json);

            if (file is null)
                throw new InvalidDataException("Config file was empty");

            file.ReplacementRules ??= new List<AssetProxyConfigEntry>();

            return file;
        }

        public static string Serialize(AssetProxyConfigFile file) => JsonSerializer.Serialize(file, WriteOptions);

        public static string ReadPreset(string name) => ReadResource(PresetResourcePrefix + name + ".json");

        public static ObservableCollection<AssetProxyRule> ToRules(AssetProxyConfigFile file)
        {
            var rules = new ObservableCollection<AssetProxyRule>();

            foreach (AssetProxyConfigEntry entry in file.ReplacementRules)
                Flatten(entry, null, rules);

            return rules;
        }

        public static AssetProxyConfigFile ToConfigFile(IEnumerable<AssetProxyRule> rules)
        {
            var file = new AssetProxyConfigFile();

            foreach (AssetProxyRule rule in rules)
            {
                var entry = new AssetProxyConfigEntry
                {
                    Name = String.IsNullOrWhiteSpace(rule.Name) ? "Rule" : rule.Name,
                    ReplaceIds = new List<long> { rule.FromAssetId },
                    Enabled = rule.Enabled
                };

                switch (rule.Action)
                {
                    case AssetProxyAction.Replace:
                        entry.Mode = "id";
                        entry.WithId = rule.ToAssetId;
                        break;

                    case AssetProxyAction.Redirect:
                        entry.Mode = "cdn";
                        entry.CdnUrl = rule.RedirectTarget;
                        break;

                    default:
                        entry.Mode = "id";
                        entry.Remove = true;
                        break;
                }

                file.ReplacementRules.Add(entry);
            }

            return file;
        }

        private static void Flatten(AssetProxyConfigEntry entry, string? group, ObservableCollection<AssetProxyRule> rules)
        {
            if (entry.Type == "group" || entry.Children is { Count: > 0 })
            {
                string groupName = String.IsNullOrWhiteSpace(entry.Name) ? "Group" : entry.Name;

                foreach (AssetProxyConfigEntry child in entry.Children ?? new List<AssetProxyConfigEntry>())
                    Flatten(child, groupName, rules);

                return;
            }

            string label = String.IsNullOrWhiteSpace(entry.Name) ? "Rule" : entry.Name;
            string displayName = group is null ? label : $"{group} / {label}";
            string mode = (entry.Mode ?? "id").ToLowerInvariant();
            bool enabled = entry.Enabled ?? true;
            long targetId = entry.WithId ?? 0;

            foreach (long fromId in entry.ReplaceIds ?? new List<long>())
            {
                if (fromId <= 0)
                    continue;

                var rule = new AssetProxyRule
                {
                    Enabled = enabled,
                    Name = displayName,
                    FromAssetId = fromId
                };

                if (mode == "cdn" && !String.IsNullOrWhiteSpace(entry.CdnUrl))
                {
                    rule.Action = AssetProxyAction.Redirect;
                    rule.RedirectTarget = entry.CdnUrl;
                }
                else if (mode == "file" && !String.IsNullOrWhiteSpace(entry.FilePath))
                {
                    rule.Action = AssetProxyAction.Redirect;
                    rule.RedirectTarget = entry.FilePath;
                }
                else if (mode == "remove" || entry.Remove == true || targetId <= 0)
                {
                    rule.Action = AssetProxyAction.Remove;
                }
                else
                {
                    rule.Action = AssetProxyAction.Replace;
                    rule.ToAssetId = targetId;
                }

                rules.Add(rule);
            }
        }

        private static IReadOnlyList<string> LoadPresetNames()
        {
            var names = new List<string>();

            foreach (string resource in typeof(AssetProxyConfigFiles).Assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(PresetResourcePrefix, StringComparison.Ordinal) || !resource.EndsWith(".json", StringComparison.Ordinal))
                    continue;

                names.Add(resource[PresetResourcePrefix.Length..^".json".Length]);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);

            return names;
        }

        private static string ReadResource(string resourceName)
        {
            using Stream? stream = typeof(AssetProxyConfigFiles).Assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
                throw new FileNotFoundException($"Bundled preset '{resourceName}' is missing");

            using var reader = new StreamReader(stream, Encoding.UTF8);

            return reader.ReadToEnd();
        }
    }
}

using System.Text.Json;

namespace Ivirius.Zippy
{
    public class GuildAiConfig
    {
        public bool AiEnabled { get; set; } = true;
        public bool AutoMessagingEnabled { get; set; } = false;
        public bool ActionBan { get; set; } = true;
        public bool ActionKick { get; set; } = true;
        public bool ActionMute { get; set; } = true;
        public bool ActionUnban { get; set; } = true;
        public bool ActionUnmute { get; set; } = true;
        public bool ActionWarn { get; set; } = true;
        public bool ActionLock { get; set; } = true;
        public bool ActionSlowmode { get; set; } = true;
        public ulong? LogChannelId { get; set; } = null;
        public ulong? ContextChannelId { get; set; } = null;
        public ulong? RulesChannelId { get; set; } = null;
    }

    public static class AiConfigStorage
    {
        private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "aiconfig.json");
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        private static Dictionary<string, GuildAiConfig> Load()
        {
            if (!File.Exists(Path))
                return [];

            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<Dictionary<string, GuildAiConfig>>(json) ?? [];
        }

        private static void Save(Dictionary<string, GuildAiConfig> data)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
        }

        public static GuildAiConfig Get(ulong guildId)
        {
            var data = Load();
            var key = guildId.ToString();
            return data.TryGetValue(key, out var config) ? config : new GuildAiConfig();
        }

        public static void Set(ulong guildId, GuildAiConfig config)
        {
            var data = Load();
            data[guildId.ToString()] = config;
            Save(data);
        }
    }
}

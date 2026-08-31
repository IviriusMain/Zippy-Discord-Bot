using Discord;
using Discord.Interactions;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ivirius.Zippy;

#region Attributes & Enums

public enum SettingType
{
    Text,
    Role,
    Channel,
    Boolean,
    Integer
}

[AttributeUsage(AttributeTargets.Property)]
public class ConfigSettingAttribute(string friendlyName, SettingType type) : Attribute
{
    public string FriendlyName { get; } = friendlyName;
    public SettingType Type { get; } = type;
}

public enum ConfigSetting
{
    [ChoiceDisplay("Detention Role")] DetentionRole,
    [ChoiceDisplay("Detention Channel")] DetentionChannel,
    [ChoiceDisplay("Log Channel")] LogChannel,
    [ChoiceDisplay("Security Whitelist Role")] SecurityWhitelistRole,
    [ChoiceDisplay("Instant Kill Role")] InstantKillRole,
    [ChoiceDisplay("Honeypot Channel")] HoneypotChannel
}

#endregion

#region Configuration Model

public class GuildConfig
{
    [ConfigSetting("Detention Role", SettingType.Role)]
    public string DetentionRole { get; set; } = "0";

    [ConfigSetting("Detention Channel", SettingType.Channel)]
    public string DetentionChannel { get; set; } = "0";

    [ConfigSetting("Log Channel", SettingType.Channel)]
    public string LogChannel { get; set; } = "0";

    [ConfigSetting("Security Whitelist Role", SettingType.Role)]
    public string SecurityWhitelistRole { get; set; } = "0";

    [ConfigSetting("Instant Kill Role", SettingType.Role)]
    public string InstantKillRole { get; set; } = "0";

    [ConfigSetting("Honeypot Channel", SettingType.Channel)]
    public string HoneypotChannel { get; set; } = "0";
}

#endregion

#region Module

/// <summary>
/// Handles server configuration settings per server via reflection.
/// </summary>
public class ConfigModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("configure", "Configure server settings")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task ConfigureAsync(
        [Summary("setting", "The setting to change")] ConfigSetting setting,
        [Summary("value", "Value for setting (ID, mention, boolean 1/0, or string)")] string value)
    {
        await DeferAsync();

        if (string.IsNullOrWhiteSpace(value))
        {
            await SendErrorAsync("You must provide a valid value.");
            return;
        }

        var propName = setting.ToString();
        var prop = typeof(GuildConfig).GetProperty(propName);
        var attr = prop?.GetCustomAttribute<ConfigSettingAttribute>();

        if (prop is null || attr is null)
        {
            await SendErrorAsync("Selected setting is missing a configuration mapping.");
            return;
        }

        string cleanedValue = CleanValue(value, attr.Type);
        var currentConfig = ConfigStorage.GetConfig(Context.Guild.Id);
        string oldValue = FormatPropertyValue(prop, currentConfig);

        try
        {
            ConfigStorage.UpdateConfig(Context.Guild.Id, c => prop.SetValue(c, cleanedValue));
        }
        catch (Exception ex)
        {
            await SendErrorAsync(ex.Message);
            return;
        }

        var updatedConfig = ConfigStorage.GetConfig(Context.Guild.Id);
        string newValue = FormatPropertyValue(prop, updatedConfig);

        var successEmbed = new EmbedBuilder()
            .WithTitle("Configuration Updated")
            .WithColor(Color.Parse("#66dea0"))
            .AddField("Setting", attr.FriendlyName, inline: true)
            .AddField("Old Value", oldValue, inline: true)
            .AddField("New Value", newValue, inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: successEmbed);
    }

    [SlashCommand("config-view", "View the current server configuration")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public async Task ViewConfigAsync()
    {
        await DeferAsync();

        var config = ConfigStorage.GetConfig(Context.Guild.Id);
        var embed = new EmbedBuilder()
            .WithTitle($"Server Settings for {Context.Guild.Name}")
            .WithColor(Color.Parse("#66dea0"))
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp();

        // Dynamically build all embed fields via reflection
        foreach (var prop in typeof(GuildConfig).GetProperties())
        {
            var attr = prop.GetCustomAttribute<ConfigSettingAttribute>();
            if (attr is null) continue;

            string formattedValue = FormatPropertyValue(prop, config);
            embed.AddField(attr.FriendlyName, formattedValue, inline: true);
        }

        await FollowupAsync(embed: embed.Build());
    }

    #region Helpers

    private static string CleanValue(string raw, SettingType type)
    {
        string trimmed = raw.Trim();
        return type switch
        {
            SettingType.Role or SettingType.Channel => Regex.Replace(trimmed, @"[<@&#>]", ""),
            SettingType.Boolean => trimmed is "1" or "true" or "yes" ? "true" : "false",
            _ => trimmed
        };
    }

    private static string FormatPropertyValue(PropertyInfo prop, GuildConfig config)
    {
        var attr = prop.GetCustomAttribute<ConfigSettingAttribute>();
        string val = prop.GetValue(config)?.ToString() ?? "0";

        if (attr is null) return val;

        return attr.Type switch
        {
            SettingType.Role when ulong.TryParse(val, out var id) && id != 0 => $"<@&{id}>",
            SettingType.Channel when ulong.TryParse(val, out var id) && id != 0 => $"<#{id}>",
            SettingType.Boolean => val.Equals("true", StringComparison.OrdinalIgnoreCase) ? "Enabled" : "Disabled",
            SettingType.Role or SettingType.Channel when val is "0" => "*None*",
            _ => string.IsNullOrWhiteSpace(val) ? "*None*" : val
        };
    }

    private async Task SendErrorAsync(string message)
    {
        var errorEmbed = new EmbedBuilder()
            .WithTitle("Configuration Failed")
            .WithDescription(message)
            .WithColor(Color.Parse("#d13a08"))
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: errorEmbed, ephemeral: true);
    }

    #endregion
}

#endregion

#region Storage Helper

public static class ConfigStorage
{
    private static readonly string DataDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                                          "Zippy-Discord-Bot",
                                                                          "data");
    private static readonly string Path = System.IO.Path.Combine(DataDirectory, "configs.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, GuildConfig> Load()
    {
        if (!File.Exists(Path))
            return [];

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Dictionary<string, GuildConfig>>(json) ?? [];
    }

    private static void Save(Dictionary<string, GuildConfig> data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
    }

    public static GuildConfig GetConfig(ulong guildId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        return data.TryGetValue(gKey, out var config) ? config : new GuildConfig();
    }

    public static GuildConfig UpdateConfig(ulong guildId, Action<GuildConfig> updateAction)
    {
        var data = Load();
        var gKey = guildId.ToString();

        if (!data.TryGetValue(gKey, out var config))
        {
            config = new GuildConfig();
            data[gKey] = config;
        }

        updateAction(config);
        Save(data);
        return config;
    }

    public static bool ResetConfig(ulong guildId)
    {
        var data = Load();
        var gKey = guildId.ToString();

        if (!data.Remove(gKey))
            return false;

        Save(data);
        return true;
    }
}

#endregion

using Discord;
using Discord.Interactions;
using System.Text.Json;

namespace Ivirius.Zippy.Moderation;

/// <summary>
/// Handles and keeps track of warns for a specific user.
/// </summary>
public class WarnModule : InteractionModuleBase<SocketInteractionContext>
{
    // Command declarations

    [SlashCommand("warn", "Warn a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task WarnAsync([Summary("user")] IUser user, [Summary("reason")] string reason)
        => await ExecuteWarnAsync(user.Id.ToString(), reason);

    [SlashCommand("warn-bulk", "Warn one or more users by ID")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task WarnBulkAsync(
        [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds,
        [Summary("reason")] string reason)
        => await ExecuteWarnAsync(userIds, reason);

    [SlashCommand("remove-warn", "Remove a specific warn from a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task RemoveWarnAsync(
        [Summary("user")] IUser user,
        [Summary("warn_id", "The GUID of the warn to remove")] string warnId)
        => await ExecuteRemoveWarnAsync(user, warnId);

    [SlashCommand("warns", "View all warns for a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task WarnsAsync([Summary("user")] IUser user)
        => await ExecuteGetWarnsAsync(user);

    [SlashCommand("clear-warns", "Clear all warns from a user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task ClearWarnsAsync([Summary("user", "The user to clear warns from")] IUser user)
        => await ExecuteClearWarnsAsync(user);

    public async Task ExecuteWarnAsync(string userIds, string reason)
    {
        await DeferAsync();

        var ids = userIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => ulong.TryParse(id, out var uid) ? uid : 0)
            .Where(id => id != 0)
            .ToList();

        if (ids.Count == 0)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("No valid user IDs were provided.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
        }

        var successLines = new List<string>();
        var successPings = new List<string>();
        var failureLines = new List<string>();

        foreach (var userId in ids)
        {
            try
            {
                var guildUser = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, userId);
                if (guildUser is null)
                    throw new Exception("User not found in this server.");

                // Apply timeout
                WarnStorage.AddWarn(Context.Guild.Id, userId, Context.User.Id, reason);

                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
                successPings.Add($"<@{userId}>");
            }
            catch (Exception ex)
            {
                var errorMsg =
                ex.Message.Contains("error 50013") ? "Missing permissions." :
                ex.Message;

                failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
            }
        }

        bool allFailed = successLines.Count == 0;
        bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

        var title = allFailed ? "Warn Failed"
            : partialFail ? "Warn Partially Failed"
            : "Warn Successful";

        var color = allFailed ? Color.Parse("#d13a08")
            : partialFail ? Color.Parse("#e0a021")
            : Color.Parse("#66dea0");

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .AddField("Status", string.Join("\n", successLines.Concat(failureLines)), inline: false)
            .AddField("Reason", reason, inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        var message = string.Join(", ", successPings) + ", you have been warned!";

        await FollowupAsync(embed: embed, text: message);
    }

    public async Task ExecuteRemoveWarnAsync(IUser user, string warnId)
    {
        await DeferAsync();

        bool removed = WarnStorage.RemoveWarn(Context.Guild.Id, user.Id, warnId);

        if (!removed)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("Warn not found.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Warn Removed")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Warn `{warnId}` has been removed from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }


    public async Task ExecuteGetWarnsAsync(IUser user, bool silent = true)
    {
        await DeferAsync(ephemeral: silent);

        var warns = WarnStorage.GetWarns(Context.Guild.Id, user.Id);

        if (warns == null || warns.Count == 0)
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle($"Warns for {user.Username}")
                .WithDescription("No warns found for this user.")
                .WithColor(Color.Parse("#66dea0"))
                .Build();

            await FollowupAsync(embed: emptyEmbed, ephemeral: silent);
            return;
        }

        var chunkedWarns = warns.Chunk(10).Take(10);
        var embedsToSend = new List<Embed>();

        int page = 1;
        foreach (var chunk in chunkedWarns)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"Warns for {user.Username} (Page {page})")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"**Total warns:** {warns.Count}")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp();

            foreach (var warn in chunk)
            {
                string safeReason = warn.Reason.Length > 900
                    ? $"{warn.Reason[..900]}..."
                    : warn.Reason;

                embedBuilder.AddField(
                    $"`{warn.Id}`",
                    $"**Moderator:** <@{warn.ModeratorId}>\n**Timestamp:** <t:{((DateTimeOffset)warn.Timestamp).ToUnixTimeSeconds()}:R>\n```\n{safeReason}\n```",
                    true
                );
            }

            embedsToSend.Add(embedBuilder.Build());
            page++;
        }

        await FollowupAsync(embeds: embedsToSend.ToArray(), ephemeral: silent);
    }

    public async Task ExecuteClearWarnsAsync(IUser user)
    {
        await DeferAsync();

        int count = WarnStorage.ClearWarns(Context.Guild.Id, user.Id);

        var embed = new EmbedBuilder()
            .WithTitle("Warns Cleared")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Cleared **{count} warn{(count != 1 ? "s" : "")}** from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }
}

// Warn model
public class WarnEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Reason { get; set; } = string.Empty;
    public ulong ModeratorId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// JSON storage helper
public static class WarnStorage
{
    private static readonly string DataDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                                            "Zippy-Discord-Bot",
                                                                            "data");
    private static readonly string Path = System.IO.Path.Combine(DataDirectory, "warns.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, Dictionary<string, List<WarnEntry>>> Load()
    {
        if (!File.Exists(Path))
            return [];

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<WarnEntry>>>>(json) ?? [];
    }

    private static void Save(Dictionary<string, Dictionary<string, List<WarnEntry>>> data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
    }

    public static List<WarnEntry> GetWarns(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();
        return data.TryGetValue(gKey, out var guild) && guild.TryGetValue(uKey, out var warns) ? warns : [];
    }

    public static WarnEntry AddWarn(ulong guildId, ulong userId, ulong moderatorId, string reason)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.ContainsKey(gKey)) data[gKey] = [];
        if (!data[gKey].ContainsKey(uKey)) data[gKey][uKey] = [];

        var entry = new WarnEntry { Reason = reason, ModeratorId = moderatorId };
        data[gKey][uKey].Add(entry);
        Save(data);
        return entry;
    }

    public static bool RemoveWarn(ulong guildId, ulong userId, string warnId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var warns))
            return false;

        var entry = warns.FirstOrDefault(w => w.Id == warnId);
        if (entry is null) return false;

        warns.Remove(entry);
        Save(data);
        return true;
    }

    public static int ClearWarns(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var warns))
            return 0;

        int count = warns.Count;
        warns.Clear();
        Save(data);
        return count;
    }
}

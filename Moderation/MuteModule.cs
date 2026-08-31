using Discord;
using Discord.Interactions;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ivirius.Zippy.Moderation;

/// <summary>
/// Handles user mute tasks using Discord's timeout functionality
/// and keeps track of them for each particular user.
/// </summary>
public class MuteModule : InteractionModuleBase<SocketInteractionContext>
{
    // Discord doesn't allow timeouts larger than 28 days
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromDays(28);

    private static TimeSpan ParseDuration(string input)
    {
        var match = Regex.Match(input.Trim().ToLower(),
            @"(?:(\d+)d)?(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?");

        int days = int.TryParse(match.Groups[1].Value, out var d) ? d : 0;
        int hours = int.TryParse(match.Groups[2].Value, out var h) ? h : 0;
        int minutes = int.TryParse(match.Groups[3].Value, out var m) ? m : 0;
        int seconds = int.TryParse(match.Groups[4].Value, out var s) ? s : 0;

        var result = new TimeSpan(days, hours, minutes, seconds);
        return result > MaxTimeout ? MaxTimeout : result;
    }

    // Command declarations

    [SlashCommand("mute", "Timeout one user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task MuteAsync(
        [Summary("user")] IGuildUser user,
        [Summary("duration", "Duration in format: 1d2h3m4s")] string duration,
        [Summary("reason")] string reason)
        => await ExecuteMuteAsync(user.Id.ToString(), duration, reason);

    [SlashCommand("mute-bulk", "Timeout one or more users by ID")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task MuteBulkAsync(
        [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds,
        [Summary("duration", "Duration in format: 1d2h3m4s")] string duration,
        [Summary("reason")] string reason)
        => await ExecuteMuteAsync(userIds, duration, reason);

    [SlashCommand("unmute", "Remove timeout from one user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task UnmuteAsync([Summary("user")] IGuildUser user)
        => await ExecuteUnmuteAsync(user.Id.ToString());

    [SlashCommand("remove-mute", "Remove a specific mute from a user's history")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task RemoveMuteAsync(
        [Summary("user")] IUser user,
        [Summary("mute_id", "The GUID of the mute to remove from the user's history")] string muteId)
        => await ExecuteRemoveMuteAsync(user, muteId);

    [SlashCommand("mutes", "View a user's history of mutes")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task MutesAsync(
        [Summary("user", "The user to view mutes for")] IUser user,
        [Summary("silent", "If true, only you will see the mutes. Otherwise, false. Default is true.")] bool silent = true)
        => await ExecuteGetMutesAsync(user, silent);

    [SlashCommand("clear-mutes", "Clear all mutes from a user's history")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task ClearMutesAsync([Summary("user")] IUser user)
        => await ExecuteClearMutesAsync(user);

    // Implementations

    public async Task ExecuteMuteAsync(string userIds, string duration, string reason)
    {
        await DeferAsync();

        var successLines = new List<string>();
        var failureLines = new List<string>();

        var timeout = ParseDuration(duration);
        bool wasClamped = timeout == MaxTimeout;

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

        foreach (var userId in ids)
        {
            try
            {
                var guildUser = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, userId);
                if (guildUser is null)
                    throw new Exception("User not found in this server.");

                // Apply timeout
                await guildUser.SetTimeOutAsync(timeout);
                MuteStorage.AddMute(Context.Guild.Id, userId, Context.User.Id, duration, reason);

                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
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

        var title = allFailed ? "Mute Failed"
            : partialFail ? "Mute Partially Failed"
            : "Mute Successful";

        var color = allFailed ? Color.Parse("#d13a08")
            : partialFail ? Color.Parse("#e0a021")
            : Color.Parse("#66dea0");

        var clampNote = wasClamped ? "\n⚠ Duration was clamped to 28 days (Discord maximum)." : string.Empty;

        var embedBuilder = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .AddField("Status", string.Join("\n", successLines.Concat(failureLines)), inline: false)
            .AddField("Duration", duration, inline: true)
            .AddField("Reason", reason, inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp();
        var embed = wasClamped ? embedBuilder.WithDescription(clampNote).Build() : embedBuilder.Build();

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteUnmuteAsync(string userIds)
    {
        await DeferAsync();

        var successLines = new List<string>();
        var failureLines = new List<string>();
        string failures = string.Empty;

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

        foreach (var userId in ids)
        {
            try
            {
                var guildUser = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, userId);
                if (guildUser is null)
                    throw new Exception("User not found in this server.");

                await guildUser.RemoveTimeOutAsync();
                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
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

        var title = allFailed ? "Unmute Failed"
            : partialFail ? "Unmute Partially Failed"
            : "User Unmuted";

        var color = allFailed ? Color.Parse("#d13a08")
            : partialFail ? Color.Parse("#e0a021")
            : Color.Parse("#66dea0");

        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .AddField("Status", string.Join("\n", successLines.Concat(failureLines)), inline: false)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteRemoveMuteAsync(IUser user, string muteId)
    {
        await DeferAsync();

        bool removed = MuteStorage.RemoveMute(Context.Guild.Id, user.Id, muteId);

        if (!removed)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("Mute not found.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Mute Removed")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Mute `{muteId}` has been removed from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteGetMutesAsync(IUser user, bool silent = true)
    {
        await DeferAsync(ephemeral: silent);

        var mutes = MuteStorage.GetMutes(Context.Guild.Id, user.Id);

        if (mutes == null || mutes.Count == 0)
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle($"Mutes for {user.Username}")
                .WithDescription("No mutes found for this user.")
                .WithColor(Color.Parse("#66dea0"))
                .Build();

            await FollowupAsync(embed: emptyEmbed, ephemeral: silent);
            return;
        }

        var chunkedMutes = mutes.Chunk(10).Take(10);
        var embedsToSend = new List<Embed>();

        int page = 1;
        foreach (var chunk in chunkedMutes)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"Mutes for {user.Username} (Page {page})")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"**Total mutes:** {mutes.Count}")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp();

            foreach (var mute in chunk)
            {
                string safeReason = mute.Reason.Length > 900
                    ? $"{mute.Reason[..900]}..."
                    : mute.Reason;

                embedBuilder.AddField(
                    $"`{mute.Id}`",
                    $"**Mute duration:** {mute.Duration}\n**Timestamp:** <t:{((DateTimeOffset)mute.Timestamp).ToUnixTimeSeconds()}:R>\n**Moderator:** <@{mute.ModeratorId}>\n```\n{safeReason}\n```",
                    true
                );
            }

            embedsToSend.Add(embedBuilder.Build());
            page++;
        }

        await FollowupAsync(embeds: embedsToSend.ToArray(), ephemeral: silent);
    }

    public async Task ExecuteClearMutesAsync(IUser user)
    {
        await DeferAsync();

        int count = MuteStorage.ClearMutes(Context.Guild.Id, user.Id);

        var embed = new EmbedBuilder()
            .WithTitle("Mutes Cleared")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Cleared **{count} mute{(count != 1 ? "s" : "")}** from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }
}

// Mute data model
public class MuteEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Duration { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public ulong ModeratorId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// JSON storage helper
public static class MuteStorage
{
    private static readonly string DataDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Zippy-Discord-Bot",
        "data");
    private static readonly string Path = System.IO.Path.Combine(DataDirectory, "mutes.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, Dictionary<string, List<MuteEntry>>> Load()
    {
        if (!File.Exists(Path))
            return [];

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<MuteEntry>>>>(json) ?? [];
    }

    private static void Save(Dictionary<string, Dictionary<string, List<MuteEntry>>> data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
    }

    public static List<MuteEntry> GetMutes(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();
        return data.TryGetValue(gKey, out var guild) && guild.TryGetValue(uKey, out var mutes) ? mutes : [];
    }

    public static MuteEntry AddMute(ulong guildId, ulong userId, ulong moderatorId, string duration, string reason)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.ContainsKey(gKey)) data[gKey] = [];
        if (!data[gKey].ContainsKey(uKey)) data[gKey][uKey] = [];

        var entry = new MuteEntry { Duration = duration, Reason = reason, ModeratorId = moderatorId };
        data[gKey][uKey].Add(entry);
        Save(data);
        return entry;
    }

    public static bool RemoveMute(ulong guildId, ulong userId, string muteId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var mutes))
            return false;

        var entry = mutes.FirstOrDefault(w => w.Id == muteId);
        if (entry is null) return false;

        mutes.Remove(entry);
        Save(data);
        return true;
    }

    public static int ClearMutes(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var mutes))
            return 0;

        int count = mutes.Count;
        mutes.Clear();
        Save(data);
        return count;
    }
}

using Discord;
using Discord.Interactions;
using System.Text.Json;

namespace Ivirius.Zippy.Moderation;

/// <summary>
/// Handles user detention tasks by setting a configured role
/// and keeping track of detentions for each particular user.
/// </summary>
public class DetentionModule : InteractionModuleBase<SocketInteractionContext>
{
    // Command declarations

    [SlashCommand("detain", "Detain one user")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task DetainAsync([Summary("user")] IGuildUser user, [Summary("reason")] string reason)
        => await ExecuteDetainAsync(user.Id.ToString(), reason);

    [SlashCommand("detain-bulk", "Detain one or more users by ID")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task DetainBulkAsync(
        [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds,
        [Summary("reason")] string reason)
        => await ExecuteDetainAsync(userIds, reason);

    [SlashCommand("undetain", "Release a user from detention")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task UndetainAsync([Summary("user")] IGuildUser user)
        => await ExecuteUndetainAsync(user.Id.ToString());

    [SlashCommand("remove-detention", "Remove a specific detention from a user's history")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task RemoveDetentionAsync(
        [Summary("user")] IUser user,
        [Summary("detention_id", "The GUID of the detention to remove from the user's history")] string detentionId)
        => await ExecuteRemoveDetentionAsync(user, detentionId);

    [SlashCommand("detentions", "View a user's history of detentions")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task DetentionsAsync(
        [Summary("user", "The user to view detentions for")] IUser user,
        [Summary("silent", "If true, only you will see the detentions. Otherwise, false. Default is true.")] bool silent = true)
        => await ExecuteGetDetentionsAsync(user, silent);

    [SlashCommand("clear-detentions", "Clear all detentions from a user's history")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task ClearDetentionsAsync([Summary("user")] IUser user)
        => await ExecuteClearDetentionsAsync(user);

    // Implementations

    public async Task ExecuteDetainAsync(string userIds, string reason)
    {
        await DeferAsync();

        var successLines = new List<string>();
        var successPings = new List<string>();
        var failureLines = new List<string>();

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
            return;
        }

        foreach (var userId in ids)
        {
            try
            {
                var guildUser = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, userId);
                if (guildUser is null)
                    throw new Exception("User not found in this server.");

                // Apply detention role
                var detentionRole = ConfigStorage.GetConfig(Context.Guild.Id).DetentionRole;
                await guildUser.AddRoleAsync(ulong.Parse(detentionRole));

                // Save detention to storage
                DetentionStorage.AddDetention(Context.Guild.Id, userId, Context.User.Id, reason);

                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
                successPings.Add($"<@{userId.ToString()}>");
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message.Contains("error 50013")
                ? "Missing permissions."
                : ex.Message;

                failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
            }
        }

        bool allFailed = successLines.Count == 0;
        bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

        var title = allFailed ? "Detention Failed"
            : partialFail ? "Detention Partially Failed"
            : "Detention Successful";

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

        var detentionEmbed = new EmbedBuilder()
            .WithTitle("Detention Camp")
            .WithColor(Color.Parse("#e0a021"))
            .WithDescription("You have been detained for breaking the rules! Make yourself comfortable, a moderator will arrive shortly.")
            .AddField("Reason", reason, inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        var detentionChannel = ConfigStorage.GetConfig(Context.Guild.Id).DetentionChannel;
        if (ulong.TryParse(detentionChannel, out var chId) && chId != 0)
        {
            if (Context.Guild.GetChannel(chId) is ITextChannel channel)
            {
                await channel.SendMessageAsync(embed: detentionEmbed, text: $"{(string.Join(", ", successPings))}, you have been detained!");
            }
        }

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteUndetainAsync(string userIds)
    {
        await DeferAsync();

        var successLines = new List<string>();
        var failureLines = new List<string>();

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
            return;
        }

        foreach (var userId in ids)
        {
            try
            {
                var guildUser = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, userId);
                if (guildUser is null)
                    throw new Exception("User not found in this server.");

                var detentionRole = ConfigStorage.GetConfig(Context.Guild.Id).DetentionRole;
                await guildUser.RemoveRoleAsync(ulong.Parse(detentionRole));
                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message.Contains("error 50013")
                    ? "Missing permissions."
                    : ex.Message;

                failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
            }
        }

        bool allFailed = successLines.Count == 0;
        bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

        var title = allFailed ? "Undetain Failed"
            : partialFail ? "Undetain Partially Failed"
            : "User Undetained";

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

    public async Task ExecuteRemoveDetentionAsync(IUser user, string detentionId)
    {
        await DeferAsync();

        bool removed = DetentionStorage.RemoveDetention(Context.Guild.Id, user.Id, detentionId);

        if (!removed)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("Detention not found.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Detention Removed")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Detention `{detentionId}` has been removed from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteGetDetentionsAsync(IUser user, bool silent = true)
    {
        await DeferAsync(ephemeral: silent);

        var detentions = DetentionStorage.GetDetentions(Context.Guild.Id, user.Id);

        if (detentions == null || detentions.Count == 0)
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle($"Detentions for {user.Username}")
                .WithDescription("No detentions found for this user.")
                .WithColor(Color.Parse("#66dea0"))
                .Build();

            await FollowupAsync(embed: emptyEmbed, ephemeral: silent);
            return;
        }

        var chunkedDetentions = detentions.Chunk(10).Take(10);
        var embedsToSend = new List<Embed>();

        int page = 1;
        foreach (var chunk in chunkedDetentions)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"Detentions for {user.Username} (Page {page})")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"**Total detentions:** {detentions.Count}")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp();

            foreach (var detention in chunk)
            {
                string safeReason = detention.Reason.Length > 900
                    ? $"{detention.Reason[..900]}..."
                    : detention.Reason;

                embedBuilder.AddField(
                    $"`{detention.Id}`",
                    $"**Moderator:** <@{detention.ModeratorId}>\n**Timestamp:** <t:{((DateTimeOffset)detention.Timestamp).ToUnixTimeSeconds()}:R>\n```\n{safeReason}\n```",
                    true
                );
            }

            embedsToSend.Add(embedBuilder.Build());
            page++;
        }

        await FollowupAsync(embeds: embedsToSend.ToArray(), ephemeral: silent);
    }

    public async Task ExecuteClearDetentionsAsync(IUser user)
    {
        await DeferAsync();

        int count = DetentionStorage.ClearDetentions(Context.Guild.Id, user.Id);

        var embed = new EmbedBuilder()
            .WithTitle("Detentions Cleared")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Cleared **{count} detention{(count != 1 ? "s" : "")}** from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }
}

// Detention model
public class DetentionEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Reason { get; set; } = string.Empty;
    public ulong ModeratorId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// JSON storage helper
public static class DetentionStorage
{
    private static readonly string DataDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                                          "Zippy-Discord-Bot",
                                                                          "data");
    private static readonly string Path = System.IO.Path.Combine(DataDirectory, "detentions.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, Dictionary<string, List<DetentionEntry>>> Load()
    {
        if (!File.Exists(Path))
            return [];

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<DetentionEntry>>>>(json) ?? [];
    }

    private static void Save(Dictionary<string, Dictionary<string, List<DetentionEntry>>> data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
    }

    public static List<DetentionEntry> GetDetentions(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();
        return data.TryGetValue(gKey, out var guild) && guild.TryGetValue(uKey, out var detentions) ? detentions : [];
    }

    public static DetentionEntry AddDetention(ulong guildId, ulong userId, ulong moderatorId, string reason)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.ContainsKey(gKey)) data[gKey] = [];
        if (!data[gKey].ContainsKey(uKey)) data[gKey][uKey] = [];

        var entry = new DetentionEntry { Reason = reason, ModeratorId = moderatorId };
        data[gKey][uKey].Add(entry);
        Save(data);
        return entry;
    }

    public static bool RemoveDetention(ulong guildId, ulong userId, string detentionId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var detentions))
            return false;

        var entry = detentions.FirstOrDefault(w => w.Id == detentionId);
        if (entry is null) return false;

        detentions.Remove(entry);
        Save(data);
        return true;
    }

    public static int ClearDetentions(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var detentions))
            return 0;

        int count = detentions.Count;
        detentions.Clear();
        Save(data);
        return count;
    }
}

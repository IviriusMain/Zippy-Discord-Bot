using System.Text.Json;
using Discord;
using Discord.Interactions;

namespace Ivirius.Zippy.Moderation;

/// <summary>
/// Bans a user and keeps track of their ban history.
/// </summary>
public class BanModule : InteractionModuleBase<SocketInteractionContext>
{
    // Command declarations

    [SlashCommand("ban", "Ban a user")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task BanAsync(
        [Summary("user")] IUser user,
        [Summary("reason")] string reason,
        [Summary("notify", "Notify the user they have been banned. Default is true.")] bool notify = true,
        [Summary("purge", "Delete the user's recent messages (last 14 days). Default: false.")] bool purge = false)
        => await ExecuteBanAsync(user.Id.ToString(), reason, notify, purge, false);

    [SlashCommand("ban-quick", "Quickly ban a user for trolling, botting, etc.")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task BanQuickAsync(
        [Summary("user")] IUser user,
        [Summary("reason")] string reason)
        => await ExecuteBanAsync(user.Id.ToString(), reason, false, true, true);

    [SlashCommand("ban-bulk", "Ban multiple users")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task BanAsync(
        [Summary("user_ids", "Comma-separated list of user IDs")] string userIds,
        [Summary("reason")] string reason,
        [Summary("notify", "Notify the user they have been banned. Default is true.")] bool notify = true,
        [Summary("purge", "Delete the user's recent messages (last 14 days). Default: false.")] bool purge = false)
        => await ExecuteBanAsync(userIds, reason, notify, purge, false);

    [SlashCommand("ban-quick-bulk", "Quickly ban multiple users for trolling, botting, etc.")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task BanQuickAsync(
        [Summary("user_ids", "Comma-separated list of user IDs")] string userIds,
        [Summary("reason")] string reason)
        => await ExecuteBanAsync(userIds, reason, false, true, true);

    [SlashCommand("unban", "Unban a user")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task UnbanAsync([Summary("user")] IUser user)
        => await ExecuteUnbanAsync(user.Id.ToString());

    [SlashCommand("remove-ban", "Remove a specific ban from a user's history")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task RemoveBanAsync(
        [Summary("user")] IUser user,
        [Summary("ban_id", "The GUID of the ban to remove from the user's history")] string banId)
        => await ExecuteRemoveBanAsync(user, banId);

    [SlashCommand("bans", "View a user's history of bans")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task BansAsync(
        [Summary("user", "The user to view bans for")] IUser user,
        [Summary("silent", "If true, only you will see the bans. Otherwise, false. Default is true.")] bool silent = true)
        => await ExecuteGetBansAsync(user, silent);

    [SlashCommand("clear-bans", "Clear all bans from a user's history")]
    [DefaultMemberPermissions(GuildPermission.BanMembers)]
    public async Task ClearBansAsync([Summary("user")] IUser user)
        => await ExecuteClearBansAsync(user);

    // Implementations

    public async Task ExecuteBanAsync(string userIds, string reason, bool notify, bool purge, bool quick)
    {
        await DeferAsync(ephemeral: quick);

        var successLines = new List<string>();
        var failureLines = new List<string>();

        int purgeDays =
        // If it's a quick ban, always purge
        quick ? 7 :
        // Otherwise, check config
        purge ? 7 : 0;

        // Query the list of user IDs
        var ids = userIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => ulong.TryParse(id, out var uid) ? uid : 0)
            .Where(id => id != 0)
            .ToList();

        // If none, return an error
        if (ids.Count == 0)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("No valid user IDs were provided.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await RespondAsync(embed: errorEmbed, ephemeral: true);
        }

        // Monitor failures for the return value
        string failures = string.Empty;

        // Command execution
        foreach (var userId in ids)
        {
            try
            {
                if (notify)
                {
                    try
                    {
                        // DM the user that they got banned
                        var user = await Context.Client.GetUserAsync(userId);
                        if (user is not null)
                        {
                            var banEmbed = new EmbedBuilder()
                                .WithTitle($"You have been banned from **{Context.Guild.Name}**.")
                                .WithDescription($"**Reason:** {reason}")
                                .WithColor(Color.Parse("#d13a08"))
                                .Build();

                            var dm = await user.CreateDMChannelAsync();
                            await dm.SendMessageAsync(embed: banEmbed);
                        }
                    }
                    catch
                    {
                        // User has DMs closed or isn't a member of the server — continue with ban
                    }
                }

                // Ban the user
                await Context.Guild.AddBanAsync(userId, purgeDays, reason);
                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
                BanStorage.AddBan(Context.Guild.Id, userId, Context.User.Id, notify, purge, reason);
            }
            catch (Exception ex)
            {
                var errorMsg =
                ex.Message.Contains("error 50013") ? "Missing permissions." : // Missing permissions
                ex.Message; // Default

                failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
            }
        }

        // Monitor ban fails
        bool allFailed = successLines.Count == 0;
        bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

        // Set variables
        var title = allFailed ? "Ban Failed"
            : partialFail ? "Ban Partially Failed"
            : "User Banned";

        var color = allFailed ? Color.Parse("#d13a08")
            : partialFail ? Color.Parse("#e0a021")
            : Color.Parse("#66dea0");

        var description = (allFailed, partialFail) switch
        {
            (true, _) => $"**Couldn't ban** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
            (_, true) => $"**Banned** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't ban** {failureLines.Count} for breaking the server rules.",
            _ => $"{(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **banned** for breaking the server rules."
        };

        var purgeText = purge ? $"`[ Y ]` Purge ({purgeDays} days)" : "`[ N ]` Purge";
        var notifyText = notify ? "`[ Y ]` Notify" : "`[ N ]` Notify";

        // Build the ban embed
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .WithDescription(description)
            .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
            .AddField("Reason", reason, inline: false)
            .AddField("Parameters", $"{purgeText}\n{notifyText}", inline: false)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        // If it is a quick ban, don't post the message in chat
        await FollowupAsync(embed: embed, ephemeral: quick);
    }

    public async Task ExecuteUnbanAsync(string userIds)
    {
        await DeferAsync(ephemeral: false);

        var successLines = new List<string>();
        var failureLines = new List<string>();

        // Query the list of user IDs
        var ids = userIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => ulong.TryParse(id, out var uid) ? uid : 0)
            .Where(id => id != 0)
            .ToList();

        // If none, return an error
        if (ids.Count == 0)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("No valid user IDs were provided.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await RespondAsync(embed: errorEmbed, ephemeral: true);
        }

        // Monitor failures for the return value
        string failures = string.Empty;

        // Command execution
        foreach (var userId in ids)
        {
            try
            {
                // Unban the user
                await Context.Guild.RemoveBanAsync(userId);
                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
            }
            catch (Exception ex)
            {
                var errorMsg =
                    ex.Message.Contains("error 50013") ? "Missing permissions." : // Missing permissions
                    ex.Message; // Default

                failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
            }
        }

        // Monitor unban fails
        bool allFailed = successLines.Count == 0;
        bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

        // Set variables
        var title = allFailed ? "Unban Failed"
            : partialFail ? "Unban Partially Failed"
            : "User Unbanned";

        var color = allFailed ? Color.Parse("#d13a08")
            : partialFail ? Color.Parse("#e0a021")
            : Color.Parse("#66dea0");

        var description = (allFailed, partialFail) switch
        {
            (true, _) => $"**Couldn't unban** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
            (_, true) => $"**Unbanned** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't unban** {failureLines.Count}.",
            _ => $"{(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **unbanned**."
        };

        // Build the unban embed
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .WithDescription(description)
            .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed, ephemeral: false);
    }

    public async Task ExecuteRemoveBanAsync(IUser user, string banId)
    {
        await DeferAsync();

        bool removed = BanStorage.RemoveBan(Context.Guild.Id, user.Id, banId);

        if (!removed)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("Ban not found.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Ban Removed")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Ban `{banId}` has been removed from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteGetBansAsync(IUser user, bool silent = true)
    {
        await DeferAsync(ephemeral: silent);

        var bans = BanStorage.GetBans(Context.Guild.Id, user.Id);

        if (bans == null || bans.Count == 0)
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle($"Bans for {user.Username}")
                .WithDescription("No bans found for this user.")
                .WithColor(Color.Parse("#66dea0"))
                .Build();

            await FollowupAsync(embed: emptyEmbed, ephemeral: silent);
            return;
        }

        var chunkedBans = bans.Chunk(10).Take(10);
        var embedsToSend = new List<Embed>();

        int page = 1;
        foreach (var chunk in chunkedBans)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"Bans for {user.Username} (Page {page})")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"**Total bans:** {bans.Count}")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp();

            foreach (var ban in chunk)
            {
                string safeReason = ban.Reason.Length > 900
                ? $"{ban.Reason[..900]}..."
                : ban.Reason;

                embedBuilder.AddField(
                    $"`{ban.Id}`",
                    $"**Purge:** {ban.Purge}\n**Notify:** {ban.Notify}\n**Timestamp:** <t:{((DateTimeOffset)ban.Timestamp).ToUnixTimeSeconds()}:R>\n**Moderator:** <@{ban.ModeratorId}>\n```\n{safeReason}\n```",
                    true
                );
            }

            embedsToSend.Add(embedBuilder.Build());
            page++;
        }

        await FollowupAsync(embeds: embedsToSend.ToArray(), ephemeral: silent);
    }

    public async Task ExecuteClearBansAsync(IUser user)
    {
        await DeferAsync();

        int count = BanStorage.ClearBans(Context.Guild.Id, user.Id);

        var embed = new EmbedBuilder()
            .WithTitle("Bans Cleared")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Cleared **{count} ban{(count != 1 ? "s" : "")}** from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }
}

// Ban data model
public class BanEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Purge { get; set; }
    public bool Notify { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ulong ModeratorId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// JSON storage helper
public static class BanStorage
{
    private static readonly string DataDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                                          "Zippy-Discord-Bot",
                                                                          "data");
    private static readonly string Path = System.IO.Path.Combine(DataDirectory, "bans.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, Dictionary<string, List<BanEntry>>> Load()
    {
        if (!File.Exists(Path))
            return [];

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<BanEntry>>>>(json) ?? [];
    }

    private static void Save(Dictionary<string, Dictionary<string, List<BanEntry>>> data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
    }

    public static List<BanEntry> GetBans(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();
        return data.TryGetValue(gKey, out var guild) && guild.TryGetValue(uKey, out var bans) ? bans : [];
    }

    public static BanEntry AddBan(ulong guildId, ulong userId, ulong moderatorId, bool notify, bool purge, string reason)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.ContainsKey(gKey)) data[gKey] = [];
        if (!data[gKey].ContainsKey(uKey)) data[gKey][uKey] = [];

        var entry = new BanEntry { Notify = notify, Purge = purge, Reason = reason, ModeratorId = moderatorId };
        data[gKey][uKey].Add(entry);
        Save(data);
        return entry;
    }

    public static bool RemoveBan(ulong guildId, ulong userId, string banId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var bans))
            return false;

        var entry = bans.FirstOrDefault(w => w.Id == banId);
        if (entry is null) return false;

        bans.Remove(entry);
        Save(data);
        return true;
    }

    public static int ClearBans(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var bans))
            return 0;

        int count = bans.Count;
        bans.Clear();
        Save(data);
        return count;
    }
}

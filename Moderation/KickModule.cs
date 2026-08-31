using System.Text.Json;
using Discord;
using Discord.Interactions;

namespace Ivirius.Zippy.Moderation;

/// <summary>
/// Kicks a user and keeps track of their kick history.
/// </summary>
public class KickModule : InteractionModuleBase<SocketInteractionContext>
{
    // Command declarations

    [SlashCommand("kick", "Kick a user")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    public async Task KickAsync(
        [Summary("user")] IGuildUser user,
        [Summary("reason")] string reason,
        [Summary("notify", "Notify the user they have been kicked. Default is true.")] bool notify = true)
        => await ExecuteKickAsync(user.Id.ToString(), reason, notify, false);

    [SlashCommand("kick-quick", "Quickly kick a user for trolling, botting, etc.")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    public async Task KickQuickAsync(
        [Summary("user")] IGuildUser user,
        [Summary("reason")] string reason)
        => await ExecuteKickAsync(user.Id.ToString(), reason, false, true);

    [SlashCommand("kick-bulk", "Kick multiple users by ID")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    public async Task KickBulkAsync(
        [Summary("user_ids", "Comma-separated list of user IDs")] string userIds,
        [Summary("reason")] string reason,
        [Summary("notify", "Notify the user they have been kicked. Default is true.")] bool notify = true)
        => await ExecuteKickAsync(userIds, reason, notify, false);

    [SlashCommand("kick-quick-bulk", "Quickly kick multiple users for trolling, botting, etc.")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    public async Task KickQuickBulkAsync(
        [Summary("user_ids", "Comma-separated list of user IDs")] string userIds,
        [Summary("reason")] string reason)
        => await ExecuteKickAsync(userIds, reason, false, true);

    [SlashCommand("remove-kick", "Remove a specific kick from a user's history")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    public async Task RemoveKickAsync(
        [Summary("user")] IUser user,
        [Summary("kick_id", "The GUID of the kick to remove from the user's history")] string kickId)
        => await ExecuteRemoveKickAsync(user, kickId);

    [SlashCommand("kicks", "View a user's history of kicks")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    public async Task KicksAsync(
        [Summary("user", "The user to view kicks for")] IUser user,
        [Summary("silent", "If true, only you will see the kicks. Otherwise, false. Default is true.")] bool silent = true)
        => await ExecuteGetKicksAsync(user, silent);

    [SlashCommand("clear-kicks", "Clear all kicks from a user's history")]
    [DefaultMemberPermissions(GuildPermission.KickMembers)]
    public async Task ClearKicksAsync([Summary("user")] IUser user)
        => await ExecuteClearKicksAsync(user);

    // Implementations

    public async Task ExecuteKickAsync(string userIds, string reason, bool notify, bool quick)
    {
        await DeferAsync(ephemeral: quick);

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

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        // Command execution
        foreach (var userId in ids)
        {
            try
            {
                var guildUser = await Context.Client.Rest.GetGuildUserAsync(Context.Guild.Id, userId);
                if (guildUser is null)
                    throw new Exception("User not found in this server.");

                if (notify)
                {
                    try
                    {
                        // DM the user that they got kicked
                        var kickEmbed = new EmbedBuilder()
                            .WithTitle($"You have been kicked from **{Context.Guild.Name}**.")
                            .WithDescription($"**Reason:** {reason}")
                            .WithColor(Color.Parse("#d13a08"))
                            .Build();

                        var dm = await guildUser.CreateDMChannelAsync();
                        await dm.SendMessageAsync(embed: kickEmbed);
                    }
                    catch
                    {
                        // User has DMs closed — continue with kick
                    }
                }

                // Kick the user
                await guildUser.KickAsync(reason);
                successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
                KickStorage.AddKick(Context.Guild.Id, userId, Context.User.Id, notify, reason);
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message.Contains("error 50013")
                ? "Missing permissions."
                : ex.Message;

                failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
            }
        }

        // Monitor kick fails
        bool allFailed = successLines.Count == 0;
        bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

        // Set variables
        var title = allFailed ? "Kick Failed"
            : partialFail ? "Kick Partially Failed"
            : "User Kicked";

        var color = allFailed ? Color.Parse("#d13a08")
            : partialFail ? Color.Parse("#e0a021")
            : Color.Parse("#66dea0");

        var description = (allFailed, partialFail) switch
        {
            (true, _) => $"**Couldn't kick** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
            (_, true) => $"**Kicked** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't kick** {failureLines.Count} for breaking the server rules.",
            _ => $"{(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **kicked** for breaking the server rules."
        };

        var notifyText = notify ? "`[ Y ]` Notify" : "`[ N ]` Notify";

        // Build the kick embed
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(color)
            .WithDescription(description)
            .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
            .AddField("Reason", reason, inline: false)
            .AddField("Parameters", notifyText, inline: false)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed, ephemeral: quick);
    }

    public async Task ExecuteRemoveKickAsync(IUser user, string kickId)
    {
        await DeferAsync();

        bool removed = KickStorage.RemoveKick(Context.Guild.Id, user.Id, kickId);

        if (!removed)
        {
            var errorEmbed = new EmbedBuilder()
                .WithTitle("Kick not found.")
                .WithColor(Color.Parse("#d13a08"))
                .Build();

            await FollowupAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle("Kick Removed")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Kick `{kickId}` has been removed from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }

    public async Task ExecuteGetKicksAsync(IUser user, bool silent = true)
    {
        await DeferAsync(ephemeral: silent);

        var kicks = KickStorage.GetKicks(Context.Guild.Id, user.Id);

        if (kicks == null || kicks.Count == 0)
        {
            var emptyEmbed = new EmbedBuilder()
                .WithTitle($"Kicks for {user.Username}")
                .WithDescription("No kicks found for this user.")
                .WithColor(Color.Parse("#66dea0"))
                .Build();

            await FollowupAsync(embed: emptyEmbed, ephemeral: silent);
            return;
        }

        var chunkedKicks = kicks.Chunk(10).Take(10);
        var embedsToSend = new List<Embed>();

        int page = 1;
        foreach (var chunk in chunkedKicks)
        {
            var embedBuilder = new EmbedBuilder()
                .WithTitle($"Kicks for {user.Username} (Page {page})")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"**Total kicks:** {kicks.Count}")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp();

            foreach (var kick in chunk)
            {
                string safeReason = kick.Reason.Length > 900
                ? $"{kick.Reason[..900]}..."
                : kick.Reason;

                embedBuilder.AddField(
                    $"`{kick.Id}`",
                    $"**Notify:** {kick.Notify}\n**Timestamp:** <t:{((DateTimeOffset)kick.Timestamp).ToUnixTimeSeconds()}:R>\n**Moderator:** <@{kick.ModeratorId}>\n```\n{safeReason}\n```",
                    true
                );
            }

            embedsToSend.Add(embedBuilder.Build());
            page++;
        }

        await FollowupAsync(embeds: embedsToSend.ToArray(), ephemeral: silent);
    }

    public async Task ExecuteClearKicksAsync(IUser user)
    {
        await DeferAsync();

        int count = KickStorage.ClearKicks(Context.Guild.Id, user.Id);

        var embed = new EmbedBuilder()
            .WithTitle("Kicks Cleared")
            .WithColor(Color.Parse("#66dea0"))
            .WithDescription($"Cleared **{count} kick{(count != 1 ? "s" : "")}** from <@{user.Id}>.")
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }
}

// Kick model
public class KickEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public bool Notify { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ulong ModeratorId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

// JSON storage helper
public static class KickStorage
{
    private static readonly string DataDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                                                          "Zippy-Discord-Bot",
                                                                          "data");
    private static readonly string Path = System.IO.Path.Combine(DataDirectory, "kicks.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, Dictionary<string, List<KickEntry>>> Load()
    {
        if (!File.Exists(Path))
            return [];

        var json = File.ReadAllText(Path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<KickEntry>>>>(json) ?? [];
    }

    private static void Save(Dictionary<string, Dictionary<string, List<KickEntry>>> data)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(data, Options));
    }

    public static List<KickEntry> GetKicks(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();
        return data.TryGetValue(gKey, out var guild) && guild.TryGetValue(uKey, out var kicks) ? kicks : [];
    }

    public static KickEntry AddKick(ulong guildId, ulong userId, ulong moderatorId, bool notify, string reason)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.ContainsKey(gKey)) data[gKey] = [];
        if (!data[gKey].ContainsKey(uKey)) data[gKey][uKey] = [];

        var entry = new KickEntry { Notify = notify, Reason = reason, ModeratorId = moderatorId };
        data[gKey][uKey].Add(entry);
        Save(data);
        return entry;
    }

    public static bool RemoveKick(ulong guildId, ulong userId, string kickId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var kicks))
            return false;

        var entry = kicks.FirstOrDefault(w => w.Id == kickId);
        if (entry is null) return false;

        kicks.Remove(entry);
        Save(data);
        return true;
    }

    public static int ClearKicks(ulong guildId, ulong userId)
    {
        var data = Load();
        var gKey = guildId.ToString();
        var uKey = userId.ToString();

        if (!data.TryGetValue(gKey, out var guild) || !guild.TryGetValue(uKey, out var kicks))
            return 0;

        int count = kicks.Count;
        kicks.Clear();
        Save(data);
        return count;
    }
}

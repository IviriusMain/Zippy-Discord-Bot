using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Diagnostics;
using System.Text.Json;

namespace Ivirius.Zippy
{
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
        private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "warns.json");
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

    public class WarnModule : InteractionModuleBase<SocketInteractionContext>
    {
        /// <summary>
        /// Warn a user.
        /// </summary>
        [SlashCommand("warn", "Warn a user")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task WarnAsync(
            [Summary("user_id", "The user to warn")] string userId,
            [Summary("reason", "The reason for the warn")] string reason,
            [Summary("silent_warn", "Warn without notifying the user via DM. Default: false.")] bool silent = false)
        {
            await ExecuteWarnAsync(userId, reason, silent, false);
        }

        /// <summary>
        /// Remove a specific warn from a user.
        /// </summary>
        [SlashCommand("removewarn", "Remove a specific warn from a user")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task RemoveWarnAsync(
            [Summary("user_id", "The user to remove the warn from")] string userId,
            [Summary("warn_id", "The ID of the warn to remove")] string warnId)
        {
            await ExecuteRemoveWarnAsync(userId, warnId, false);
        }

        /// <summary>
        /// Clear all warns from a user.
        /// </summary>
        [SlashCommand("clearwarns", "Clear all warns from a user")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task ClearWarnsAsync(
            [Summary("user_id", "The user to clear warns from")] string userId)
        {
            await ExecuteClearWarnsAsync(userId, false);
        }

        /// <summary>
        /// View all warns for a user.
        /// </summary>
        [SlashCommand("warns", "View all warns for a user")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task WarnsAsync(
            [Summary("user_id", "The user to view warns for")] string userId)
        {
            await ExecuteGetWarnsAsync(userId, false);
        }

        public async Task<string> ExecuteWarnAsync(string userId, string reason, bool silent, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null, IUser? userP = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;
            var resolvedUser = aiTriggered ? userP! : Context.User;

            if (!aiTriggered)
                await DeferAsync(ephemeral: silent);

            if (!ulong.TryParse(userId, out var uid))
            {
                if (!aiTriggered)
                {
                    await FollowupAsync(embed: ErrorEmbed("❌ Invalid user ID."), ephemeral: true);
                    return string.Empty;
                }
                Debug.WriteLine("Couldn't warn - invalid user ID.");
                return "Couldn't warn - invalid user ID.";
            }

            var entry = WarnStorage.AddWarn(resolvedGuild.Id, uid, resolvedUser.Id, reason);

            if (!silent)
            {
                try
                {
                    var user = await resolvedClient.GetUserAsync(uid);
                    if (user is not null)
                    {
                        var dmEmbed = new EmbedBuilder()
                            .WithTitle($"You have been warned in **{resolvedGuild.Name}**.")
                            .WithDescription($"**Reason:** {reason}\n**Warn ID:** `{entry.Id}`")
                            .WithColor(Color.Parse("#66dea0"))
                            .Build();

                        var dm = await user.CreateDMChannelAsync();
                        await dm.SendMessageAsync(embed: dmEmbed);
                    }
                }
                catch
                {
                    // DMs closed — continue
                }
            }

            if (aiTriggered)
            {
                Debug.WriteLine($"Warned <@{uid}>. Warn ID: {entry.Id}");
                return $"Warned <@{uid}>. Warn ID: {entry.Id}";
            }

            var warns = WarnStorage.GetWarns(resolvedGuild.Id, uid);
            var embed = new EmbedBuilder()
                .WithTitle("User Warned")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"⚠️ <@{uid}> has been **warned**.")
                .AddField("Reason", reason, inline: false)
                .AddField("Warn ID", $"`{entry.Id}`", inline: false)
                .AddField("Total Warns", warns.Count.ToString(), inline: false)
                .WithFooter($"Requested by {resolvedUser.Username}", resolvedUser.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: silent);
            return $"Warned <@{uid}>. Warn ID: {entry.Id}";
        }

        public async Task<string> ExecuteRemoveWarnAsync(string userId, string warnId, bool aiTriggered)
        {
            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

            if (!ulong.TryParse(userId, out var uid))
            {
                if (!aiTriggered)
                {
                    await FollowupAsync(embed: ErrorEmbed("❌ Invalid user ID."), ephemeral: true);
                    return string.Empty;
                }
                return "Couldn't remove warn - invalid user ID.";
            }

            bool removed = WarnStorage.RemoveWarn(Context.Guild.Id, uid, warnId);

            if (!removed)
            {
                if (!aiTriggered)
                {
                    await FollowupAsync(embed: ErrorEmbed("❌ Warn not found."), ephemeral: true);
                    return string.Empty;
                }
                return "Couldn't remove warn - warn ID not found.";
            }

            if (aiTriggered)
                return $"Removed warn `{warnId}` from <@{uid}>.";

            var embed = new EmbedBuilder()
                .WithTitle("Warn Removed")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"✅ Warn `{warnId}` has been removed from <@{uid}>.")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
            return $"Removed warn `{warnId}` from <@{uid}>.";
        }

        public async Task<string> ExecuteClearWarnsAsync(string userId, bool aiTriggered)
        {
            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

            if (!ulong.TryParse(userId, out var uid))
            {
                if (!aiTriggered)
                {
                    await FollowupAsync(embed: ErrorEmbed("❌ Invalid user ID."), ephemeral: true);
                    return string.Empty;
                }
                return "Couldn't clear warns - invalid user ID.";
            }

            int count = WarnStorage.ClearWarns(Context.Guild.Id, uid);

            if (aiTriggered)
                return $"Cleared {count} warn{(count != 1 ? "s" : "")} from <@{uid}>.";

            var embed = new EmbedBuilder()
                .WithTitle("Warns Cleared")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"🗑️ Cleared **{count}** warn{(count != 1 ? "s" : "")} from <@{uid}>.")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
            return $"Cleared {count} warn(s) from <@{uid}>.";
        }

        public async Task<string> ExecuteGetWarnsAsync(string userId, bool aiTriggered)
        {
            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

            if (!ulong.TryParse(userId, out var uid))
            {
                if (!aiTriggered)
                {
                    await FollowupAsync(embed: ErrorEmbed("❌ Invalid user ID."), ephemeral: true);
                    return string.Empty;
                }
                return "Couldn't get warns - invalid user ID.";
            }

            var warns = WarnStorage.GetWarns(Context.Guild.Id, uid);

            if (aiTriggered)
            {
                if (warns.Count == 0)
                    return $"<@{uid}> has no warns.";
                return string.Join("\n", warns.Select(w => $"- `{w.Id}` | {w.Reason} | <@{w.ModeratorId}> | {w.Timestamp:yyyy-MM-dd HH:mm} UTC"));
            }

            var description = warns.Count == 0
                ? $"<@{uid}> has no warns."
                : string.Join("\n", warns.Select(w => $"`{w.Id}`\n**Reason:** {w.Reason}\n**By:** <@{w.ModeratorId}> • {w.Timestamp:yyyy-MM-dd HH:mm} UTC\n"));

            var embed = new EmbedBuilder()
                .WithTitle($"Warns for {uid}")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription(description)
                .AddField("Total", warns.Count.ToString(), inline: false)
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
            return description;
        }

        private static Embed ErrorEmbed(string message) => new EmbedBuilder()
            .WithTitle(message)
            .WithColor(Color.Parse("#d1533d"))
            .Build();
    }
}

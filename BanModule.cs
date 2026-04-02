using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Ivirius.Zippy
{
    public class BanModule : InteractionModuleBase<SocketInteractionContext>
    {
        /// <summary>
        /// Primary ban function, interactive.
        /// </summary>
        [SlashCommand("ban", "Ban one or more users by ID")]
        [DefaultMemberPermissions(GuildPermission.BanMembers)]
        public async Task BanAsync(
            [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds,
            [Summary("reason", "The reason for the ban.")] string reason,
            [Summary("silent_ban", "Ban without notifying the user via DM. Default: false.")] bool silent = false,
            [Summary("purge", "Delete the user's recent messages (last 14 days). Default: false.")] bool purge = false)
        {
            await ExecuteBanAsync(userIds, reason, silent, purge, false, false);
        }

        /// <summary>
        /// Quick ban function, for trolls, bots, etc.
        /// </summary>
        [SlashCommand("quickban", "Quickly ban one or more users by ID")]
        [DefaultMemberPermissions(GuildPermission.BanMembers)]
        public async Task QuickBanAsync(
            [Summary("user_ids", "Comma-separated list of user IDs")] string userIds,
            [Summary("reason", "Ban reason")] string reason)
        {
            await ExecuteBanAsync(userIds, reason, false, true, true, false);
        }

        [SlashCommand("unban", "Unban one or more users by ID")]
        [DefaultMemberPermissions(GuildPermission.BanMembers)]
        public async Task UnbanAsync(
            [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds)
        {
            await ExecuteUnbanAsync(userIds, false);
        }

        public async Task<string> ExecuteUnbanAsync(string userIds, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null, SocketUser? user = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;
            var resolvedUser = aiTriggered ? user! : Context.User;

            if (!aiTriggered)
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
            if (ids.Count == 0 && !aiTriggered)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ No valid user IDs were provided.")
                    .WithColor(Color.Parse("#66dea0"))
                    .Build();

                await RespondAsync(embeds: [errorEmbed], ephemeral: true);
                return "Couldn't unban - you didn't provide any user IDs.";
            }

            // Monitor failures for the return value
            string failures = string.Empty;

            // Command execution
            foreach (var userId in ids)
            {
                try
                {
                    // Unban the user
                    await resolvedGuild.RemoveBanAsync(userId);
                    successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
                }
                catch (Exception ex)
                {
                    if (!aiTriggered)
                    {
                        var errorMsg =
                            ex.Message.Contains("error 50013") ? "Missing permissions." : // Missing permissions
                            ex.Message; // Default

                        failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
                    }
                    else
                    {
                        var errorMsg =
                            ex.Message.Contains("error 50013") ? "you don't have enough permissions." : // Missing permissions
                            ex.Message; // Default

                        failures += "Couldn't unban - " + errorMsg + "\n";
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(failures))
                return string.Join("\n", successLines) + (string.IsNullOrWhiteSpace(failures) ? "" : "\n" + failures);

            // Monitor unban fails
            bool allFailed = successLines.Count == 0;
            bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

            // Set variables
            var title = allFailed ? "Unban Failed"
                      : partialFail ? "Unban Partially Failed"
                      : "User Unbanned";

            var color = allFailed ? Color.Parse("#d1533d")
                      : partialFail ? Color.Parse("#ede04c")
                      : Color.Parse("#66dea0");

            var description = (allFailed, partialFail) switch
            {
                (true, _) => $"❌ **Couldn't unban** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
                (_, true) => $"⚠️ **Unbanned** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't unban** {failureLines.Count}.",
                _ => $"🔨 {(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **unbanned**."
            };

            // Build the unban embed
            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(color)
                .WithDescription(description)
                .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
                .WithFooter($"Requested by {resolvedUser.Username}", resolvedUser.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            if (!aiTriggered)
                await FollowupAsync(embed: embed, ephemeral: false);
            return string.Join("\n", successLines.Concat(failureLines));
        }

        public async Task<string> ExecuteBanAsync(string userIds, string reason, bool silent, bool purge, bool quick, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null, SocketUser? userP = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;
            var resolvedUser = aiTriggered ? userP! : Context.User;

            if (!aiTriggered)
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
            if (ids.Count == 0 && !aiTriggered)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ No valid user IDs were provided.")
                    .WithColor(Color.Parse("#66dea0"))
                    .Build();

                await RespondAsync(embeds: [errorEmbed], ephemeral: true);
                return "Couldn't ban - you didn't provide any user IDs.";
            }

            // Monitor failures for the return value
            string failures = string.Empty;

            // Command execution
            foreach (var userId in ids)
            {
                try
                {
                    if (!silent)
                    {
                        try
                        {
                            // DM the user that they got banned
                            var user = await resolvedClient.GetUserAsync(userId);
                            if (user is not null)
                            {
                                var banEmbed = new EmbedBuilder()
                                    .WithTitle($"You have been banned from **{resolvedGuild.Name}**.")
                                    .WithDescription($"**Reason:** {reason}")
                                    .WithColor(Color.Parse("#66dea0"))
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
                    await resolvedGuild.AddBanAsync(userId, purgeDays, reason);
                    successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
                }
                catch (Exception ex)
                {
                    if (!aiTriggered)
                    {
                        var errorMsg =
                            ex.Message.Contains("error 50013") ? "Missing permissions." : // Missing permissions
                            ex.Message; // Default

                        failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
                    }
                    else
                    {
                        var errorMsg =
                            ex.Message.Contains("error 50013") ? "you don't have enough permissions." : // Missing permissions
                            ex.Message; // Default

                        failures += "Couldn't ban - " + errorMsg + "\n";
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(failures))
                return string.Join("\n", successLines) + (string.IsNullOrWhiteSpace(failures) ? "" : "\n" + failures);

            // Monitor ban fails
            bool allFailed = successLines.Count == 0;
            bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

            // Set variables
            var title = allFailed ? "Ban Failed"
                      : partialFail ? "Ban Partially Failed"
                      : "User Banned";

            var color = allFailed ? Color.Parse("#d1533d")
                      : partialFail ? Color.Parse("#ede04c")
                      : Color.Parse("#66dea0");

            var description = (allFailed, partialFail) switch
            {
                (true, _) => $"❌ **Couldn't ban** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
                (_, true) => $"⚠️ **Banned** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't ban** {failureLines.Count} for breaking the server rules.",
                _ => $"🔨 {(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **banned** for breaking the server rules."
            };

            var purgeText = purge ? $"`[ Y ]` Purge ({purgeDays} days)" : "`[ N ]` Purge";
            var notifyText = silent ? "`[ Y ]` Silent" : "`[ N ]` Silent";

            // Build the ban embed
            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(color)
                .WithDescription(description)
                .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
                .AddField("Reason", reason, inline: false)
                .AddField("Parameters", $"{purgeText}\n{notifyText}", inline: false)
                .WithFooter($"Requested by {resolvedUser.Username}", resolvedUser.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();


            if (!aiTriggered)
            {
                // If it is a quick ban, don't post the message in chat
                await FollowupAsync(embed: embed, ephemeral: quick);
            }
            return string.Join("\n", successLines.Concat(failureLines));
        }
    }
}

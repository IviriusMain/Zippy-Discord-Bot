using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Ivirius.Zippy.Moderation
{
    public class KickModule : InteractionModuleBase<SocketInteractionContext>
    {
        /// <summary>
        /// Primary kick function, interactive.
        /// </summary>
        [SlashCommand("kick", "Kick one or more users by ID")]
        [DefaultMemberPermissions(GuildPermission.KickMembers)]
        public async Task KickAsync(
            [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds,
            [Summary("reason", "The reason for the kick.")] string reason,
            [Summary("silent_kick", "Kick without notifying the user via DM. Default: false.")] bool silent = false)
        {
            await ExecuteKickAsync(userIds, reason, silent, false);
        }

        public async Task<string> ExecuteKickAsync(string userIds, string reason, bool silent, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;

            if (!aiTriggered)
                await DeferAsync(ephemeral: silent);

            var successLines = new List<string>();
            var failureLines = new List<string>();
            string failures = string.Empty;

            var ids = userIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => ulong.TryParse(id, out var uid) ? uid : 0)
                .Where(id => id != 0)
                .ToList();

            if (ids.Count == 0 && !aiTriggered)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("❌ No valid user IDs were provided.")
                    .WithColor(Color.Parse("#66dea0"))
                    .Build(), ephemeral: true);
                return "Couldn't kick - you didn't provide any user IDs.";
            }

            foreach (var userId in ids)
            {
                try
                {
                    var guildUser = await resolvedClient.Rest.GetGuildUserAsync(resolvedGuild.Id, userId);
                    if (guildUser is null)
                        throw new Exception("User not found in this server.");

                    if (!silent)
                    {
                        try
                        {
                            var user = await resolvedClient.GetUserAsync(userId);
                            if (user is not null)
                            {
                                var kickEmbed = new EmbedBuilder()
                                    .WithTitle($"You have been kicked from **{resolvedGuild.Name}**.")
                                    .WithDescription($"**Reason:** {reason}")
                                    .WithColor(Color.Parse("#66dea0"))
                                    .Build();

                                var dm = await user.CreateDMChannelAsync();
                                await dm.SendMessageAsync(embed: kickEmbed);
                            }
                        }
                        catch
                        {
                            // DMs closed — continue
                        }
                    }

                    await guildUser.KickAsync(reason);
                    successLines.Add($"`[ PASS ]` <@{userId}> (`{userId}`)");
                }
                catch (Exception ex)
                {
                    var errorMsg =
                        ex.Message.Contains("error 50013") ? "Missing permissions." :
                        ex.Message;

                    if (!aiTriggered)
                        failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
                    else
                        failures += $"Couldn't kick <@{userId}> - {errorMsg}\n";
                }
            }

            if (aiTriggered)
                return string.Join("\n", successLines) + (string.IsNullOrWhiteSpace(failures) ? "" : "\n" + failures);

            bool allFailed = successLines.Count == 0;
            bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

            var title = allFailed ? "Kick Failed"
                      : partialFail ? "Kick Partially Failed"
                      : "User Kicked";

            var color = allFailed ? Color.Parse("#d1533d")
                      : partialFail ? Color.Parse("#ede04c")
                      : Color.Parse("#66dea0");

            var description = (allFailed, partialFail) switch
            {
                (true, _) => $"❌ **Couldn't kick** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
                (_, true) => $"⚠️ **Kicked** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't kick** {failureLines.Count}.",
                _ => $"👢 {(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **kicked**."
            };

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(color)
                .WithDescription(description)
                .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
                .AddField("Reason", reason, inline: false)
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: silent);
            return string.Join("\n", successLines.Concat(failureLines));
        }
    }
}

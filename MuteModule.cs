using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text.RegularExpressions;

namespace Ivirius.Zippy
{
    public class MuteModule : InteractionModuleBase<SocketInteractionContext>
    {
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

        /// <summary>
        /// Primary mute function, interactive.
        /// </summary>
        [SlashCommand("mute", "Timeout one or more users by ID")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task MuteAsync(
            [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds,
            [Summary("duration", "Duration in format: 1d2h3m4s")] string duration,
            [Summary("reason", "The reason for the mute.")] string reason,
            [Summary("silent_mute", "Mute without notifying the user via DM. Default: false.")] bool silent = false)
        {
            await ExecuteMuteAsync(userIds, duration, reason, silent, false);
        }

        [SlashCommand("unmute", "Remove timeout from one or more users by ID")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task UnmuteAsync(
            [Summary("user_ids", "Comma-separated list of user IDs (<ID>, <ID>, <ID>)")] string userIds)
        {
            await ExecuteUnmuteAsync(userIds, false);
        }

        public async Task<string> ExecuteMuteAsync(string userIds, string duration, string reason, bool silent, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;

            if (!aiTriggered)
                await DeferAsync(ephemeral: silent);

            var successLines = new List<string>();
            var failureLines = new List<string>();
            string failures = string.Empty;

            var timeout = ParseDuration(duration);
            bool wasClamped = timeout == MaxTimeout;

            var ids = userIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => ulong.TryParse(id, out var uid) ? uid : 0)
                .Where(id => id != 0)
                .ToList();

            if (ids.Count == 0 && !aiTriggered)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ No valid user IDs were provided.")
                    .WithColor(Color.Parse("#66dea0"))
                    .Build();

                await RespondAsync(embeds: [errorEmbed], ephemeral: true);
                return "Couldn't mute - you didn't provide any user IDs.";
            }

            foreach (var userId in ids)
            {
                try
                {
                    var guildUser = await resolvedClient.Rest.GetGuildUserAsync(resolvedGuild.Id, userId);
                    if (guildUser is null)
                        throw new Exception("User not found in this server.");

                    // Apply timeout
                    await guildUser.SetTimeOutAsync(timeout);

                    if (!silent)
                    {
                        try
                        {
                            var user = await resolvedClient.GetUserAsync(userId);
                            if (user is not null)
                            {
                                var muteEmbed = new EmbedBuilder()
                                    .WithTitle($"You have been muted in **{resolvedGuild.Name}**.")
                                    .WithDescription($"**Duration:** {duration}\n**Reason:** {reason}")
                                    .WithColor(Color.Parse("#66dea0"))
                                    .Build();

                                var dm = await user.CreateDMChannelAsync();
                                await dm.SendMessageAsync(embed: muteEmbed);
                            }
                        }
                        catch
                        {
                            // User has DMs closed — continue
                        }
                    }

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
                        failures += $"Couldn't mute <@{userId}> - {errorMsg}\n";
                }
            }

            if (aiTriggered)
                return string.Join("\n", successLines) + (string.IsNullOrWhiteSpace(failures) ? "" : "\n" + failures);

            bool allFailed = successLines.Count == 0;
            bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

            var title = allFailed ? "Mute Failed"
                      : partialFail ? "Mute Partially Failed"
                      : "User Muted";

            var color = allFailed ? Color.Parse("#d1533d")
                      : partialFail ? Color.Parse("#ede04c")
                      : Color.Parse("#66dea0");

            var description = (allFailed, partialFail) switch
            {
                (true, _) => $"❌ **Couldn't mute** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
                (_, true) => $"⚠️ **Muted** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't mute** {failureLines.Count}.",
                _ => $"🔇 {(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **muted**."
            };

            var clampNote = wasClamped ? "\n⚠️ Duration was clamped to 28 days (Discord maximum)." : string.Empty;

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(color)
                .WithDescription(description + clampNote)
                .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
                .AddField("Duration", duration, inline: false)
                .AddField("Reason", reason, inline: false)
                .AddField("Parameters", $"{(silent ? "`[ N ]` Silent Mute" : "`[ Y ]` Silent Mute")}", inline: false)
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: silent);
            return string.Join("\n", successLines.Concat(failureLines));
        }

        public async Task<string> ExecuteUnmuteAsync(string userIds, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;

            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

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
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ No valid user IDs were provided.")
                    .WithColor(Color.Parse("#66dea0"))
                    .Build();

                await RespondAsync(embeds: [errorEmbed], ephemeral: true);
                return "Couldn't unmute - you didn't provide any user IDs.";
            }

            foreach (var userId in ids)
            {
                try
                {
                    var guildUser = await resolvedClient.Rest.GetGuildUserAsync(resolvedGuild.Id, userId);
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

                    if (!aiTriggered)
                        failureLines.Add($"`[ FAIL ]` <@{userId}> (`{userId}`) - {errorMsg}");
                    else
                        failures += $"Couldn't unmute <@{userId}> - {errorMsg}\n";
                }
            }

            if (aiTriggered)
                return string.Join("\n", successLines) + (string.IsNullOrWhiteSpace(failures) ? "" : "\n" + failures);

            bool allFailed = successLines.Count == 0;
            bool partialFail = failureLines.Count > 0 && successLines.Count > 0;

            var title = allFailed ? "Unmute Failed"
                      : partialFail ? "Unmute Partially Failed"
                      : "User Unmuted";

            var color = allFailed ? Color.Parse("#d1533d")
                      : partialFail ? Color.Parse("#ede04c")
                      : Color.Parse("#66dea0");

            var description = (allFailed, partialFail) switch
            {
                (true, _) => $"❌ **Couldn't unmute** {(ids.Count > 1 ? "any of the specified users" : "the specified user")}.",
                (_, true) => $"⚠️ **Unmuted** {successLines.Count} user{(successLines.Count > 1 ? "s" : "")} and **couldn't unmute** {failureLines.Count}.",
                _ => $"🔊 {(ids.Count > 1 ? "Users" : "User")} {string.Join(", ", ids.Select(id => $"<@{id}>"))} {(ids.Count > 1 ? "were" : "was")} **unmuted**."
            };

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(color)
                .WithDescription(description)
                .AddField("Users", string.Join("\n", successLines.Concat(failureLines)), inline: false)
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
            return string.Join("\n", successLines.Concat(failureLines));
        }
    }
}

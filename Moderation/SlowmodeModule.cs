using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System.Text.RegularExpressions;

namespace Ivirius.Zippy.Moderation
{
    public class SlowmodeModule : InteractionModuleBase<SocketInteractionContext>
    {
        private static readonly int MaxSlowmode = 21600; // 6 hours in seconds

        private static int ParseDuration(string input)
        {
            var match = Regex.Match(input.Trim().ToLower(),
                @"(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?");

            int hours = int.TryParse(match.Groups[1].Value, out var h) ? h : 0;
            int minutes = int.TryParse(match.Groups[2].Value, out var m) ? m : 0;
            int seconds = int.TryParse(match.Groups[3].Value, out var s) ? s : 0;

            int total = (hours * 3600) + (minutes * 60) + seconds;
            return Math.Min(total, MaxSlowmode);
        }

        /// <summary>
        /// Set slowmode in the current channel.
        /// </summary>
        [SlashCommand("slowmode", "Set slowmode in the current channel")]
        [DefaultMemberPermissions(GuildPermission.ManageChannels)]
        public async Task SlowmodeAsync(
            [Summary("duration", "Duration in format: 1h2m3s. Use 0 to disable.")] string duration)
        {
            await ExecuteSlowmodeAsync(duration, false);
        }

        public async Task<string> ExecuteSlowmodeAsync(string duration, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;

            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

            // Handle disable case
            int seconds;
            if (duration.Trim() == "0")
                seconds = 0;
            else
            {
                seconds = ParseDuration(duration);
                if (seconds == 0)
                {
                    if (!aiTriggered)
                    {
                        await FollowupAsync(embed: new EmbedBuilder()
                            .WithTitle("❌ Invalid duration.")
                            .WithColor(Color.Parse("#d1533d"))
                            .Build(), ephemeral: true);
                        return string.Empty;
                    }
                    return "Couldn't set slowmode - invalid duration.";
                }
            }

            bool wasClamped = seconds == MaxSlowmode;

            try
            {
                var channel = Context.Channel as ITextChannel;
                if (channel is null)
                {
                    if (!aiTriggered)
                    {
                        await FollowupAsync(embed: new EmbedBuilder()
                            .WithTitle("❌ This command can only be used in a text channel.")
                            .WithColor(Color.Parse("#d1533d"))
                            .Build(), ephemeral: true);
                        return string.Empty;
                    }
                    return "Couldn't set slowmode - not a text channel.";
                }

                await channel.ModifyAsync(c => c.SlowModeInterval = seconds);

                if (aiTriggered)
                    return seconds == 0
                        ? "Slowmode disabled."
                        : $"Slowmode set to {seconds} seconds{(wasClamped ? " (clamped to 6 hour maximum)" : "")}.";

                var description = seconds == 0
                    ? "🐇 Slowmode has been **disabled**."
                    : $"🐢 Slowmode set to **{seconds}s**{(wasClamped ? "\n⚠️ Duration was clamped to 6 hours (Discord maximum)." : "")}.";

                var embed = new EmbedBuilder()
                    .WithTitle(seconds == 0 ? "Slowmode Disabled" : "Slowmode Set")
                    .WithColor(Color.Parse("#66dea0"))
                    .WithDescription(description)
                    .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                    .WithCurrentTimestamp()
                    .Build();

                await FollowupAsync(embed: embed, ephemeral: true);
                return $"Slowmode set to {seconds} seconds.";
            }
            catch (Exception ex)
            {
                var errorMsg = ex.Message.Contains("error 50013") ? "Missing permissions." : ex.Message;

                if (!aiTriggered)
                {
                    await FollowupAsync(embed: new EmbedBuilder()
                        .WithTitle($"❌ {errorMsg}")
                        .WithColor(Color.Parse("#d1533d"))
                        .Build(), ephemeral: true);
                    return string.Empty;
                }
                return $"Couldn't set slowmode - {errorMsg}";
            }
        }
    }
}

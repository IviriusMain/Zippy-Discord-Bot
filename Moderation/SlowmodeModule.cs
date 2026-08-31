using Discord;
using Discord.Interactions;
using System.Text.RegularExpressions;

namespace Ivirius.Zippy.Moderation;

/// <summary>
/// Sets the slowmode for a channel.
/// </summary>
public class SlowmodeModule : InteractionModuleBase<SocketInteractionContext>
{
    // Discord doesn't allow slowmode larger than 6 hours
    private static readonly int MaxSlowmode = 21600;

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

    // Command declarations

    [SlashCommand("slowmode", "Set slowmode in the current channel")]
    [DefaultMemberPermissions(GuildPermission.ManageChannels)]
    public async Task SlowmodeAsync([Summary("duration", "Duration in format: 1h2m3s. Use 0 to disable.")] string duration)
        => await ExecuteSlowmodeAsync(duration);

    // Implementations

    public async Task ExecuteSlowmodeAsync(string duration)
    {
        await DeferAsync();

        // Handle disable case
        int seconds;
        if (duration.Trim() == "0")
            seconds = 0;
        else
        {
            seconds = ParseDuration(duration);
            if (seconds == 0)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("Invalid duration.")
                    .WithColor(Color.Parse("#d13a08"))
                    .Build(), ephemeral: true);
                return;
            }
        }

        bool wasClamped = seconds == MaxSlowmode;

        try
        {
            var channel = Context.Channel as ITextChannel;
            if (channel is null)
            {
                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("This command can only be used in a text channel.")
                    .WithColor(Color.Parse("#d13a08"))
                    .Build(), ephemeral: true);
                return;
            }

            await channel?.ModifyAsync(c => c.SlowModeInterval = seconds)!;

            var description = seconds == 0
                ? "Slowmode has been **disabled**."
                : $"Slowmode set to **{seconds}s**{(wasClamped ? "\n⚠️ Duration was clamped to 6 hours (Discord maximum)." : "")}.";

            var embed = new EmbedBuilder()
                .WithTitle(seconds == 0 ? "Slowmode Disabled" : "Slowmode Set")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription(description)
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed);
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message.Contains("error 50013") ? "Missing permissions." : ex.Message;

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle($"{errorMsg}")
                .WithColor(Color.Parse("#d13a08"))
                .Build(), ephemeral: true);
        }
    }
}

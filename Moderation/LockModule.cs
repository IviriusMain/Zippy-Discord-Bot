using Discord;
using Discord.Interactions;

namespace Ivirius.Zippy.Moderation;

/// <summay>
/// Locks a channel by preventing the everyone role from typing messages.
/// </summary>
public class LockModule : InteractionModuleBase<SocketInteractionContext>
{
    // Command declarations

    [SlashCommand("lock", "Lock the current channel")]
    [DefaultMemberPermissions(GuildPermission.ManageChannels)]
    public async Task LockAsync([Summary("reason")] string reason = "No reason provided.")
        => await ExecuteLockAsync(reason);

    [SlashCommand("unlock", "Unlock the current channel")]
    [DefaultMemberPermissions(GuildPermission.ManageChannels)]
    public async Task UnlockAsync()
        => await ExecuteUnlockAsync();

    // Implementations

    public async Task ExecuteLockAsync(string reason)
    {
        await DeferAsync(ephemeral: true);

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

            await channel?.AddPermissionOverwriteAsync(
                Context.Guild.EveryoneRole,
                new OverwritePermissions(sendMessages: PermValue.Deny),
                new RequestOptions())!;

            var lockEmbed = new EmbedBuilder()
                .WithTitle("This Channel Is Locked")
                .WithDescription($"<@{Context.User.Id}> has locked this channel. You can no longer send messages here.\n**Reason:** {reason}")
                .WithColor(Color.Parse("#e0a021"))
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: lockEmbed);

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("Channel Locked")
                .WithDescription($"This channel has been locked.")
                .WithColor(Color.Parse("#66dea0"))
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build());
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

    public async Task ExecuteUnlockAsync()
    {
        await DeferAsync(ephemeral: true);

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

            await channel?.AddPermissionOverwriteAsync(
                Context.Guild.EveryoneRole,
                new OverwritePermissions(sendMessages: PermValue.Inherit),
                new RequestOptions())!;

            var unlockEmbed = new EmbedBuilder()
                .WithTitle("This Channel Is Unlocked")
                .WithDescription($"<@{Context.User.Id}> has unlocked this channel. You can now send messages here.")
                .WithColor(Color.Parse("#66dea0"))
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await ReplyAsync(embed: unlockEmbed);

            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle("Channel Unlocked")
                .WithDescription($"This channel has been unlocked.")
                .WithColor(Color.Parse("#66dea0"))
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build());
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

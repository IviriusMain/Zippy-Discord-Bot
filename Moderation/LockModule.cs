using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Ivirius.Zippy.Moderation
{
    public class LockModule : InteractionModuleBase<SocketInteractionContext>
    {
        /// <summary>
        /// Lock the current channel.
        /// </summary>
        [SlashCommand("lock", "Lock the current channel")]
        [DefaultMemberPermissions(GuildPermission.ManageChannels)]
        public async Task LockAsync(
            [Summary("reason", "The reason for the lock.")] string reason = "No reason provided.")
        {
            await ExecuteLockAsync(reason, false);
        }

        /// <summary>
        /// Unlock the current channel.
        /// </summary>
        [SlashCommand("unlock", "Unlock the current channel")]
        [DefaultMemberPermissions(GuildPermission.ManageChannels)]
        public async Task UnlockAsync()
        {
            await ExecuteUnlockAsync(false);
        }

        public async Task<string> ExecuteLockAsync(string reason, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;

            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

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
                    return "Couldn't lock - not a text channel.";
                }

                // Post the lock message first, then lock
                var lockEmbed = new EmbedBuilder()
                    .WithTitle("🔒 Channel Locked")
                    .WithDescription($"This channel has been locked.\n**Reason:** {reason}")
                    .WithColor(Color.Parse("#d1533d"))
                    .WithFooter($"Locked by {Context.User.Username}", Context.User.GetAvatarUrl())
                    .WithCurrentTimestamp()
                    .Build();

                await channel.SendMessageAsync(embed: lockEmbed);

                // Now lock
                await channel.AddPermissionOverwriteAsync(
                    resolvedGuild.EveryoneRole,
                    new OverwritePermissions(sendMessages: PermValue.Deny));

                if (aiTriggered)
                    return $"Channel #{channel.Name} locked.";

                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("✅ Channel locked.")
                    .WithColor(Color.Parse("#66dea0"))
                    .Build(), ephemeral: true);

                return $"Channel #{channel.Name} locked.";
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
                return $"Couldn't lock - {errorMsg}";
            }
        }

        public async Task<string> ExecuteUnlockAsync(bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;

            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

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
                    return "Couldn't unlock - not a text channel.";
                }

                // Post the unlock message first, then unlock
                await channel.AddPermissionOverwriteAsync(
                    resolvedGuild.EveryoneRole,
                    new OverwritePermissions(sendMessages: PermValue.Inherit));

                var unlockEmbed = new EmbedBuilder()
                    .WithTitle("🔓 Channel Unlocked")
                    .WithDescription("This channel has been unlocked.")
                    .WithColor(Color.Parse("#66dea0"))
                    .WithFooter($"Unlocked by {Context.User.Username}", Context.User.GetAvatarUrl())
                    .WithCurrentTimestamp()
                    .Build();

                await channel.SendMessageAsync(embed: unlockEmbed);

                if (aiTriggered)
                    return $"Channel #{channel.Name} unlocked.";

                await FollowupAsync(embed: new EmbedBuilder()
                    .WithTitle("✅ Channel unlocked.")
                    .WithColor(Color.Parse("#66dea0"))
                    .Build(), ephemeral: true);

                return $"Channel #{channel.Name} unlocked.";
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
                return $"Couldn't unlock - {errorMsg}";
            }
        }
    }
}

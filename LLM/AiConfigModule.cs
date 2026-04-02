using Discord;
using Discord.Interactions;

namespace Ivirius.Zippy
{
    public enum AiConfigChannelSetting
    {
        LogChannel,
        ContextChannel,
        RulesChannel
    }

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class AiConfigModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("aiconfig_channel", "Set a channel for Zippy to use")]
        public async Task AiConfigChannelAsync(
            [Summary("setting", "The channel setting to change")] AiConfigChannelSetting setting,
            [Summary("channel", "The channel to set")] ITextChannel channel)
        {
            await DeferAsync(ephemeral: true);
            var config = AiConfigStorage.Get(Context.Guild.Id);
            switch (setting)
            {
                case AiConfigChannelSetting.LogChannel: config.LogChannelId = channel.Id; break;
                case AiConfigChannelSetting.ContextChannel: config.ContextChannelId = channel.Id; break;
                case AiConfigChannelSetting.RulesChannel: config.RulesChannelId = channel.Id; break;
            }
            AiConfigStorage.Set(Context.Guild.Id, config);
            var embed = new EmbedBuilder()
                .WithTitle("AI Config Updated")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"✅ **{setting}** set to {channel.Mention}.")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();
            await FollowupAsync(embed: embed, ephemeral: true);
        }

        [SlashCommand("aiconfig", "Configure the AI for this server")]
        public async Task AiConfigAsync(
            [Summary("setting", "The setting to change")] AiConfigSetting setting,
            [Summary("value", "Enable or disable")] bool value)
        {
            await DeferAsync(ephemeral: true);

            var config = AiConfigStorage.Get(Context.Guild.Id);

            switch (setting)
            {
                case AiConfigSetting.AiEnabled: config.AiEnabled = value; break;
                case AiConfigSetting.AutoMessaging: config.AutoMessagingEnabled = value; break;
                case AiConfigSetting.ActionBan: config.ActionBan = value; break;
                case AiConfigSetting.ActionKick: config.ActionKick = value; break;
                case AiConfigSetting.ActionMute: config.ActionMute = value; break;
                case AiConfigSetting.ActionUnban: config.ActionUnban = value; break;
                case AiConfigSetting.ActionUnmute: config.ActionUnmute = value; break;
                case AiConfigSetting.ActionWarn: config.ActionWarn = value; break;
                case AiConfigSetting.ActionLock: config.ActionLock = value; break;
                case AiConfigSetting.ActionSlowmode: config.ActionSlowmode = value; break;
            }

            AiConfigStorage.Set(Context.Guild.Id, config);

            var embed = new EmbedBuilder()
                .WithTitle("AI Config Updated")
                .WithColor(Color.Parse("#66dea0"))
                .WithDescription($"✅ **{setting}** set to `{value}`.")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
        }
    }

    public enum AiConfigSetting
    {
        AiEnabled,
        AutoMessaging,
        ActionBan,
        ActionKick,
        ActionMute,
        ActionUnban,
        ActionUnmute,
        ActionWarn,
        ActionLock,
        ActionSlowmode
    }
}

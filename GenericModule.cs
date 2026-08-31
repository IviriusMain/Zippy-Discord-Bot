using Discord;
using Discord.Interactions;

namespace Ivirius.Zippy;


public class GenericModule : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// Learn more about Zippy.
    /// </summary>
    [SlashCommand("about", "Learn more about Zippy")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task AboutAsync()
    {
        await ExecuteAboutAsync();
    }

    public async Task ExecuteAboutAsync()
    {
        await DeferAsync();

        var embed = new EmbedBuilder()
        .WithTitle("About Zippy")
        .WithThumbnailUrl("https://github.com/user-attachments/assets/a25f75c6-d8bf-44eb-8eda-3adc4d940853")
        .WithColor(Color.Parse("#00ceba"))
        .AddField("Description", "Simple Discord bot designed to work in the [official Ivirius Community Discord server](https://ivirius.com/discord/).", inline: false)
        .AddField("GitHub Repository", "https://github.com/IviriusMain/Zippy-Discord-Bot/", inline: false)
        .AddField("License", "https://github.com/IviriusMain/Zippy-Discord-Bot/blob/main/LICENSE", inline: false)
        .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
        .WithCurrentTimestamp()
        .Build();

        await FollowupAsync(embed: embed);
    }
}

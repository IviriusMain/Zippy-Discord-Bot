using Discord;
using Discord.Interactions;

namespace Ivirius.Zippy;

public class GenericModule : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("ping", "Check the bot's latency and gateway status")]
    public async Task PingAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await DeferAsync();

        stopwatch.Stop();

        int wsLatency = Context.Client.Latency;
        long apiLatency = stopwatch.ElapsedMilliseconds;

        var embed = new EmbedBuilder()
            .WithTitle($"Pong!")
            .WithColor(wsLatency < 200 ? Color.Parse("#66dea0") : Color.Parse("#e0a021"))
            .AddField("WebSocket Latency", $"`{wsLatency} ms`", inline: true)
            .AddField("API Round-Trip", $"`{apiLatency} ms`", inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        await FollowupAsync(embed: embed);
    }

    [SlashCommand("about", "Learn more about Zippy")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task AboutAsync()
    {
        await DeferAsync();

        var embed = new EmbedBuilder()
            .WithTitle("About Zippy")
            .WithThumbnailUrl(Context.Client.CurrentUser.GetAvatarUrl())
            .WithImageUrl("https://github.com/user-attachments/assets/c1b73b22-37fc-49b8-b0f6-9a3f417b34a3")
            .WithColor(Color.Parse("#f6b696"))
            .AddField("Version", "2.1.0", inline: false)
            .AddField("Description", "Simple Discord bot designed to work in the official Ivirius™ Community Discord server.", inline: false)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        var builder = new ComponentBuilder()
            .WithButton("GitHub Repository", url: "https://github.com/IviriusMain/Zippy-Discord-Bot/", style: ButtonStyle.Link)
            .WithButton("License", url: "https://github.com/IviriusMain/Zippy-Discord-Bot/blob/main/LICENSE", style: ButtonStyle.Link)
            .WithButton("Privacy Policy", url: "https://github.com/IviriusMain/Zippy-Discord-Bot/blob/main/PRIVACY", style: ButtonStyle.Link)
            .WithButton("Terms of Service", url: "https://github.com/IviriusMain/Zippy-Discord-Bot/blob/main/TERMS", style: ButtonStyle.Link);

        await FollowupAsync(embed: embed, components: builder.Build());
    }

    [SlashCommand("about-ivirius", "Learn more about Ivirius™ Community")]
    [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
    public async Task AboutIviriusAsync()
    {
        await DeferAsync();

        var embed = new EmbedBuilder()
            .WithTitle("About Ivirius™ Community")
            .WithImageUrl("https://github.com/user-attachments/assets/c753c98d-d405-4dcd-ad6a-b256deddee8e")
            .WithColor(Color.Parse("#55aaff"))
            .WithDescription("**Fluent design, brought to you everywhere.**\n-# *Grow together, develop for the people.*\n\nWe are a group of passionate developers, working to #bringwinuieverywhere. Since 2020, we've been developing native WinUI apps for Windows 11. Check them out below!")
            .AddField("Website", "https://ivirius.com/\nhttps://ivirius.com/contact", inline: true)
            .AddField("Socials", "https://ivirius.com/discord/\nhttps://ivirius.com/fluxer/", inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

        var builder = new ComponentBuilder()
            .WithButton("Rebound", url: "https://ivirius.com/rebound", style: ButtonStyle.Link)
            .WithButton("Ivirius Text Editor", url: "https://ivirius.com/ivirius-text-editor", style: ButtonStyle.Link)
            .WithButton("Ivirius Text Editor Plus", url: "https://ivirius.com/ivirius-text-editor-plus", style: ButtonStyle.Link);

        await FollowupAsync(embed: embed, components: builder.Build());
    }
}

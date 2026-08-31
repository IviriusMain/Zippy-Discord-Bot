using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Ivirius.Zippy.Moderation
{
    public class UserInfoModule : InteractionModuleBase<SocketInteractionContext>
    {
        /// <summary>
        /// Retrieve information about a user.
        /// </summary>
        [SlashCommand("userinfo", "Retrieve information about a user")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task UserInfoAsync(
            [Summary("user_id", "The user to retrieve information about")] string userId)
        {
            await ExecuteUserInfoAsync(userId, false);
        }

        public async Task<string> ExecuteUserInfoAsync(string userId, bool aiTriggered, DiscordSocketClient? client = null, IGuild? guild = null)
        {
            var resolvedGuild = aiTriggered ? guild! : Context.Guild;
            var resolvedClient = aiTriggered ? client! : Context.Client;

            if (!aiTriggered)
                await DeferAsync(ephemeral: true);

            if (!ulong.TryParse(userId, out var uid))
            {
                if (!aiTriggered)
                {
                    await FollowupAsync(embed: new EmbedBuilder()
                        .WithTitle("❌ Invalid user ID.")
                        .WithColor(Color.Parse("#d1533d"))
                        .Build(), ephemeral: true);
                    return string.Empty;
                }
                return "Couldn't get user info - invalid user ID.";
            }

            var guildUser = await resolvedClient.Rest.GetGuildUserAsync(resolvedGuild.Id, uid);
            if (guildUser is null)
            {
                if (!aiTriggered)
                {
                    await FollowupAsync(embed: new EmbedBuilder()
                        .WithTitle("❌ User not found in this server.")
                        .WithColor(Color.Parse("#d1533d"))
                        .Build(), ephemeral: true);
                    return string.Empty;
                }
                return "Couldn't get user info - user not found in this server.";
            }

            var warns = WarnStorage.GetWarns(resolvedGuild.Id, uid);

            // Get roles, sorted by position descending, excluding @everyone
            var roles = guildUser.RoleIds
                .Where(r => r != resolvedGuild.Id)
                .Select(r => resolvedGuild.GetRole(r))
                .Where(r => r is not null)
                .OrderByDescending(r => r!.Position)
                .ToList();

            var topRole = roles.FirstOrDefault();
            var embedColor = Color.Parse("#66dea0");

            var rolesText = roles.Count > 0
                ? string.Join(", ", roles.Select(r => $"<@&{r!.Id}>"))
                : "None";

            // Truncate roles if too long for embed
            if (rolesText.Length > 1024)
                rolesText = rolesText[..1021] + "...";

            var nickname = string.IsNullOrWhiteSpace(guildUser.Nickname) ? "None" : guildUser.Nickname;
            var displayName = string.IsNullOrWhiteSpace(guildUser.DisplayName) ? guildUser.Username : guildUser.DisplayName;
            var avatarUrl = guildUser.GetAvatarUrl() ?? guildUser.GetDefaultAvatarUrl();
            var createdAt = guildUser.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm UTC");
            var joinedAt = guildUser.JoinedAt.HasValue
                ? guildUser.JoinedAt.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm UTC")
                : "Unknown";

            if (aiTriggered)
            {
                return $"""
                    Username: {guildUser.Username}
                    Display Name: {displayName}
                    Nickname: {nickname}
                    User ID: {uid}
                    Created: {createdAt}
                    Joined: {joinedAt}
                    Top Role: {(topRole is not null ? topRole.Name : "None")}
                    Roles: {string.Join(", ", roles.Select(r => r!.Name))}
                    Total Warns: {warns.Count}
                    """;
            }

            var embed = new EmbedBuilder()
                .WithTitle(guildUser.Username)
                .WithThumbnailUrl(avatarUrl)
                .WithColor(embedColor)
                .AddField("Display Name", displayName, inline: false)
                .AddField("Nickname", nickname, inline: false)
                .AddField("User ID", uid.ToString(), inline: false)
                .AddField("Account Created", createdAt, inline: false)
                .AddField("Joined Server", joinedAt, inline: false)
                .AddField("Top Role", topRole is not null ? $"<@&{topRole.Id}>" : "None", inline: false)
                .AddField("Roles", rolesText, inline: false)
                .AddField("Total Warns", warns.Count.ToString(), inline: false)
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithCurrentTimestamp()
                .Build();

            await FollowupAsync(embed: embed, ephemeral: true);
            return $"Retrieved info for {guildUser.Username}.";
        }
    }
}

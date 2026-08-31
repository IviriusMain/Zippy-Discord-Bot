using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Ivirius.Zippy.Moderation
{
    public class UserInfoModule : InteractionModuleBase<SocketInteractionContext>
    {
        /// <summary>
        /// Retrieve information and security analysis about a user.
        /// </summary>
        [SlashCommand("userinfo", "Retrieve information and security risk analysis for a user.")]
        [DefaultMemberPermissions(GuildPermission.ModerateMembers)]
        public async Task UserInfoAsync(
            [Summary("user", "The user to inspect (defaults to self if left empty).")] IUser? targetUser = null,
                                        [Summary("silent", "If true, the result will only be shown to you. Default: false.")] bool silent = true)
        {
            await DeferAsync(ephemeral: silent);

            targetUser ??= Context.User;

            // Attempt to fetch full Guild Member object if available
            var guildUser = targetUser as IGuildUser ?? Context.Guild.GetUser(targetUser.Id);

            // Fetch all moderation logs for the target user
            var warns = WarnStorage.GetWarns(Context.Guild.Id, targetUser.Id);
            var mutes = MuteStorage.GetMutes(Context.Guild.Id, targetUser.Id);
            var kicks = KickStorage.GetKicks(Context.Guild.Id, targetUser.Id);
            var detentions = DetentionStorage.GetDetentions(Context.Guild.Id, targetUser.Id);
            var bans = BanStorage.GetBans(Context.Guild.Id, targetUser.Id);

            // Security risk evaluation
            var flags = new List<string>();
            int riskScore = 0;

            var accountAgeDays = (DateTimeOffset.UtcNow - targetUser.CreatedAt).TotalDays;

            // Account Age Checks
            if (accountAgeDays < 1)
            {
                riskScore += 40;
                flags.Add("`[  HIGH  ]` Account created less than 24 hours ago");
            }
            else if (accountAgeDays < 7)
            {
                riskScore += 25;
                flags.Add("`[ MEDIUM ]` Account created less than 7 days ago");
            }
            else if (accountAgeDays < 30)
            {
                riskScore += 10;
                flags.Add("`[  INFO  ]` Account created less than 30 days ago");
            }

            // Avatar Check
            bool hasDefaultAvatar = string.IsNullOrEmpty(targetUser.AvatarId);
            if (hasDefaultAvatar)
            {
                riskScore += 15;
                flags.Add("`[ MEDIUM ]` Default avatar image");
            }

            // Server Join Checks (if user is in guild)
            if (guildUser?.JoinedAt.HasValue == true)
            {
                var joinAgeHours = (DateTimeOffset.UtcNow - guildUser.JoinedAt.Value).TotalHours;
                if (joinAgeHours < 24)
                {
                    riskScore += 20;
                    flags.Add("`[  HIGH  ]` Joined server less than 24 hours ago");
                }
            }

            // Assign overall Risk Level
            string riskBadge;
            Color embedColor;

            if (riskScore >= 45)
            {
                riskBadge = "**High risk (Potential Bot / Throwaway)**";
                embedColor = Color.Parse("#d13a08");
            }
            else if (riskScore >= 20)
            {
                riskBadge = "**Medium risk (Suspicious / New)**";
                embedColor = Color.Parse("#e0a021");
            }
            else
            {
                riskBadge = "Low risk (Legitimate User)";
                embedColor = Color.Parse("#66dea0");
            }

            // Moderation History Evaluation & Badging
            int totalInfractions = warns.Count + detentions.Count;
            bool isHighlyProblematic = bans.Count > 0
            || kicks.Count >= 2
            || mutes.Count >= 3
            || totalInfractions >= 10;

            bool isProblematic = kicks.Count == 1
            || (mutes.Count >= 1 && mutes.Count < 3)
            || (totalInfractions >= 5 && totalInfractions < 10);

            bool isLowQuality = totalInfractions >= 3 && totalInfractions < 5;

            string qualityBadge;

            if (isHighlyProblematic)
            {
                qualityBadge = "**Highly problematic**";
                embedColor = Color.Parse("#870000");
            }
            else if (isProblematic)
            {
                qualityBadge = "**Problematic/troll**";
                embedColor = Color.Parse("#d13a08");
            }
            else if (isLowQuality)
            {
                qualityBadge = "**Low quality user**";
                if (riskScore < 45)
                    embedColor = Color.Parse("#e0a021");
            }
            else if (totalInfractions > 0 || mutes.Count > 0 || kicks.Count > 0)
            {
                qualityBadge = "Not inherently problematic";
            }
            else
            {
                qualityBadge = "No moderation history";
            }

            // Roles & details preparation
            string rolesText = "None / Not in Server";
            string topRoleText = "None";
            int roleCount = 0;

            if (guildUser != null)
            {
                var roles = guildUser.RoleIds
                .Where(r => r != Context.Guild.Id)
                .Select(r => Context.Guild.GetRole(r))
                .Where(r => r is not null)
                .OrderByDescending(r => r!.Position)
                .ToList();

                if (roles.Count > 0)
                {
                    roleCount = roles.Count;
                    topRoleText = $"<@&{roles.First()!.Id}>";
                    rolesText = string.Join(", ", roles.Select(r => $"<@&{r!.Id}>"));
                    if (rolesText.Length > 1024)
                        rolesText = rolesText[..1021] + "...";
                }
            }

            var displayName = guildUser?.DisplayName ?? targetUser.GlobalName ?? targetUser.Username;
            var avatarUrl = targetUser.GetAvatarUrl() ?? targetUser.GetDefaultAvatarUrl();
            var createdAt = $"<t:{targetUser.CreatedAt.ToUnixTimeSeconds()}:F> (<t:{targetUser.CreatedAt.ToUnixTimeSeconds()}:R>)";
            var joinedAt = guildUser?.JoinedAt.HasValue == true
            ? $"<t:{guildUser.JoinedAt.Value.ToUnixTimeSeconds()}:F> (<t:{guildUser.JoinedAt.Value.ToUnixTimeSeconds()}:R>)"
            : "Not in server";

            var flagsFormatted = flags.Count > 0
            ? string.Join("\n", flags)
            : "No security risk indicators found.";

            // Mod History Summary formatting
            var historyFormatted = $"• **Warns:** {warns.Count}\n" +
            $"• **Mutes:** {mutes.Count}\n" +
            $"• **Kicks:** {kicks.Count}\n" +
            $"• **Detentions:** {detentions.Count}\n" +
            $"• **Bans:** {bans.Count}";

            // Main User Info Embed
            var embed = new EmbedBuilder()
            .WithTitle($"User Info - {targetUser.Username}")
            .WithThumbnailUrl(avatarUrl)
            .WithColor(Color.Parse("#66dea0"))
            .AddField("Display Name", displayName, inline: true)
            .AddField("User ID", $"`{targetUser.Id}`", inline: true)
            .AddField("Account Created", createdAt, inline: false)
            .AddField("Joined Server", joinedAt, inline: false)
            .AddField("Top Role", topRoleText, inline: true)
            .AddField("Total Roles", roleCount.ToString(), inline: true)
            .AddField("Roles", rolesText, inline: false)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

            // Security Analysis Embed
            var securityEmbed = new EmbedBuilder()
            .WithTitle("Security")
            .WithDescription("View a security summary for the current user to determine whether or not they could be an automated account, an alt, troll, or a throwaway account.")
            .WithColor(embedColor)
            .AddField("Risk evaluation", riskBadge, inline: true)
            .AddField("Quality evaluation", qualityBadge, inline: true)
            .AddField("Statistics", "Individual user statistics used for evaluation. You can determine what moderation actions need to be done regarding a user with the data provided here.", inline: false)
            .AddField("Flags", flagsFormatted, inline: true)
            .AddField("Moderation Record", historyFormatted, inline: true)
            .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
            .WithCurrentTimestamp()
            .Build();

            await FollowupAsync(embeds: [embed, securityEmbed], ephemeral: silent);
        }
    }
}

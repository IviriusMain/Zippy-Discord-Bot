using Discord;
using Discord.WebSocket;

namespace Ivirius.Zippy
{
    public class ActionExecutor
    {
        private readonly DiscordSocketClient _client;

        public ActionExecutor(DiscordSocketClient client)
        {
            _client = client;
        }

        public async Task<string> ExecuteAsync(ParsedAction action, IGuild guild, IUser requestingUser)
        {
            var config = AiConfigStorage.Get(guild.Id);
            if (!config.AiEnabled)
                return "AI actions are disabled for this server.";

            if (!IsActionAllowed(action.Type, config))
                return $"Action '{action.Type}' is disabled for this server.";

            // Handle read-only context queries first — no permission checks needed
            switch (action.Type)
            {
                case "query_userinfo":
                    return await QueryUserInfoAsync(action.Targets, guild);

                case "query_messages":
                    return await QueryMessagesAsync(action.ChannelId, guild);

                case "query_roles":
                    return await QueryRolesAsync(action.Targets, guild);

                case "query_permissions":
                    return await QueryPermissionsAsync(action.Targets, guild);

                case "query_ivrc_info":
                    return @"We are the Ivirius Community. Fluent design, brought to you everywhere. Since 2020, Ivirius™ Community has delivered open-source tools and applications that elevate the Windows 10 and 11 experience through modern, native development in C#. At the heart of our ecosystem is Rebound - the flagship Windows customization framework built with WinUI and the Community Toolkit. We also maintain additional programs like Ivirius Text Editor Plus and various other tools, all designed to be powerful, streamlined, and user-centric.

                    We are a community of passionate developers dedicated to enhancing the Windows experience through modern, fluent, and consistent interfaces. While some flagship projects, such as Ivirius Text Editor and Ivirius Text Editor Plus, are proprietary and commercial, the core of our ecosystem - including projects like Rebound - remains open source, free to use, and actively maintained with contributions from developers worldwide.

                    # Ivirius Text Editor
                    The #1 WinUI WordPad replacement in the Microsoft Store. Keep your documents updated with autosave, multitask with tabs, use Windows voice typing, view your document in dark mode, and more! Download here: https://ivirius.com/download/ivirius-text-editor/
                    > TECH STACK: WinUI 3; C#; XAML;

                    # Ivirius Text Editor Plus
                    The best WinUI WordPad replacement with advanced features. Draw and insert tables, write with generative AI, apply stunning themes, manage your workflow with the homepage, customize the layout as you desire, use vertical tabs and ribbon, and so much more! Download here: https://ivirius.com/download/ivirius-text-editor-plus/
                    > TECH STACK: WinUI 3; C#; XAML;
                    > Note: Ivirius Text Editor Plus features you, Zippy, as the built in AI assistant.

                    # Rebound - Modern Windows done right. Rewritten.
                    Rebound is a Windows modernization project focused on improving the user experience and updating system functionality. It replaces legacy or unmaintained Windows applications with modern implementations built using current technologies. The project aligns with the Windows 11 design direction by extending WinUI to areas of the system that still rely on older frameworks, with the goal of providing a cleaner and more consistent application experience. Our vision for Windows 11 is a consistent OS with no leftovers and many cross application features. We believe that both consumers and powerusers deserve to have the Windows 11 they love, and we want to provide as much customizability and consistency as possible while keeping it lightweight and easy to use. Download here: https://ivirius.com/download/rebound/
                    > TECH STACK: UWP XAML islands; WinUI 3; C#; XAML; C++;

                    # About Zippy (you)
                    > From the Ivirius Community website: Zippy is our mascot featured primarily in AI-related products and branding. Originally created as an April Fools’ joke in 2021, community enthusiasm led us to keep him around. Today, Zippy appears throughout our Discord server and in Ivirius Text Editor Plus.";

                case "query_joinorder":
                    return await QueryJoinOrderAsync(guild);
            }

            // For moderation actions, check permissions first
            var permCheck = await CheckModerationPermissionsAsync(action, guild, requestingUser);
            if (permCheck != null)
                return permCheck; // returns the denial reason

            var targetIds = string.Join(",", action.Targets);

            return action.Type switch
            {
                "ban" => await new BanModule().ExecuteBanAsync(targetIds, action.Reason, true, action.DeleteMessageHistory, false, true, _client, guild),
                "unban" => await new BanModule().ExecuteUnbanAsync(targetIds, true, _client, guild),
                "kick" => await new KickModule().ExecuteKickAsync(targetIds, action.Reason, true, true, _client, guild),
                "mute" => await new MuteModule().ExecuteMuteAsync(targetIds, action.Duration ?? "1h", action.Reason, true, true, _client, guild),
                "unmute" => await new MuteModule().ExecuteUnmuteAsync(targetIds, true, _client, guild),
                "warn" => await new WarnModule().ExecuteWarnAsync(targetIds, action.Reason, true, true, _client, guild, requestingUser),
                "lock" => await new LockModule().ExecuteLockAsync(action.Reason, true, _client, guild),
                "slowmode" => await new SlowmodeModule().ExecuteSlowmodeAsync(action.Duration ?? "0", true, _client, guild),
                _ => $"Unknown action type: {action.Type}"
            };
        }

        private async Task<string> QueryJoinOrderAsync(IGuild guild)
        {
            var users = await guild.GetUsersAsync();
            var lines = users
                .Where(u => u.JoinedAt.HasValue)
                .OrderByDescending(u => u.JoinedAt!.Value)
                .Take(20)
                .Select(u => $"{u.Username} (ID: {u.Id}) — joined {u.JoinedAt!.Value:yyyy-MM-dd HH:mm UTC}");
            return string.Join("\n", lines);
        }

        public async Task<string> QueryChannelContentAsync(ulong? channelId, string notConfiguredMessage, IGuild guild)
        {
            if (channelId is null) return notConfiguredMessage;
            var channel = await guild.GetTextChannelAsync(channelId.Value);
            if (channel is null) return "Channel not found.";
            var messages = await channel.GetMessagesAsync(50).FlattenAsync();
            var lines = messages
                .OrderBy(m => m.Timestamp)
                .Select(m =>
                {
                    var content = m.Content ?? "";
                    var embeds = m.Embeds.Select(e => $"[Embed: {e.Title} — {e.Description}]");
                    return $"{m.Author.Username} (ID: {m.Author.Id}) (MSG ID: {m.Id}): {string.Join(" ", new[] { content }.Concat(embeds))}";
                })
                .Where(c => !string.IsNullOrWhiteSpace(c));
            return string.Join("\n", lines);
        }

        // Returns null if allowed, or a denial string if not
        private async Task<string?> CheckModerationPermissionsAsync(ParsedAction action, IGuild guild, IUser requestingUser)
        {
            // Get the bot's guild member
            var botMember = await guild.GetUserAsync(_client.CurrentUser.Id);
            if (botMember is null)
                return "Bot member not found in guild.";

            // If self_authorized, only check bot's own role hierarchy against targets
            if (action.SelfAuthorized)
            {
                foreach (var targetId in action.Targets)
                {
                    if (!ulong.TryParse(targetId, out var uid)) continue;
                    var target = await guild.GetUserAsync(uid);
                    if (target is null) continue;

                    if (botMember.Hierarchy <= target.Hierarchy)
                        return $"Cannot moderate <@{uid}> — bot role is not high enough.";

                    if (target.Id == guild.OwnerId)
                        return $"Cannot moderate <@{uid}> — they are the server owner.";
                }
                return null;
            }

            // User-requested: check the requesting user's permissions too
            var executor = await guild.GetUserAsync(requestingUser.Id);
            if (executor is null)
                return "Requesting user not found in guild.";

            // Check if requesting user has the required Discord permission
            var requiredPerm = action.Type switch
            {
                "ban" or "unban" => GuildPermission.BanMembers,
                "kick" => GuildPermission.KickMembers,
                "mute" or "unmute" or "warn" => GuildPermission.ModerateMembers,
                "lock" or "slowmode" => GuildPermission.ManageChannels,
                _ => GuildPermission.ModerateMembers
            };

            if (!executor.GuildPermissions.Has(requiredPerm))
                return $"Requesting user does not have permission to {action.Type}.";

            foreach (var targetId in action.Targets)
            {
                if (!ulong.TryParse(targetId, out var uid)) continue;
                var target = await guild.GetUserAsync(uid);
                if (target is null) continue;

                if (target.Id == guild.OwnerId)
                    return $"Cannot moderate <@{uid}> — they are the server owner.";

                if (executor.Hierarchy <= target.Hierarchy)
                    return $"Cannot moderate <@{uid}> — your role is not high enough.";

                if (botMember.Hierarchy <= target.Hierarchy)
                    return $"Cannot moderate <@{uid}> — bot role is not high enough.";
            }

            return null;
        }

        // Context query helpers — these return plain text for the LLM to read
        private async Task<string> QueryUserInfoAsync(List<string> targetIds, IGuild guild)
        {
            var lines = new List<string>();
            foreach (var id in targetIds)
            {
                if (!ulong.TryParse(id, out var uid)) continue;
                var user = await _client.Rest.GetGuildUserAsync(guild.Id, uid);
                if (user is null) { lines.Add($"{id}: not found"); continue; }

                var warns = WarnStorage.GetWarns(guild.Id, uid);
                var roles = user.RoleIds
                    .Where(r => r != guild.Id)
                    .Select(r => guild.GetRole(r)?.Name ?? r.ToString())
                    .ToList();

                lines.Add($"""
                    User: {user.Username} (ID: {uid})
                    Display Name: {user.DisplayName}
                    Nickname: {(string.IsNullOrWhiteSpace(user.Nickname) ? "None" : user.Nickname)}
                    Created: {user.CreatedAt:yyyy-MM-dd HH:mm UTC}
                    Joined: {(user.JoinedAt.HasValue ? user.JoinedAt.Value.ToString("yyyy-MM-dd HH:mm UTC") : "Unknown")}
                    Roles: {string.Join(", ", roles)}
                    Warns: {(warns.Count == 0 ? "None" : string.Join(", ", warns.Select(w => $"`{w.Id}` {w.Reason}")))}
                    """);
            }
            return string.Join("\n---\n", lines);
        }

        private async Task<string> QueryMessagesAsync(string? channelId, IGuild guild)
        {
            if (channelId is null) return "No channel ID provided.";
            if (!ulong.TryParse(channelId, out var cid)) return "Invalid channel ID.";

            var channel = await guild.GetTextChannelAsync(cid);
            if (channel is null) return "Channel not found.";

            var messages = await channel.GetMessagesAsync(20).FlattenAsync();
            var lines = messages
                .OrderBy(m => m.Timestamp)
                .Select(m => $"{m.Author.Username} (ID: {m.Author.Id}) (MSG ID: {m.Id}): {m.Content?[..Math.Min(m.Content.Length, 200)]}");

            return string.Join("\n", lines);
        }

        private async Task<string> QueryRolesAsync(List<string> targetIds, IGuild guild)
        {
            var lines = new List<string>();
            foreach (var id in targetIds)
            {
                if (!ulong.TryParse(id, out var uid)) continue;
                var user = await guild.GetUserAsync(uid);
                if (user is null) { lines.Add($"{id}: not found"); continue; }

                var roles = user.RoleIds
                    .Where(r => r != guild.Id)
                    .Select(r => guild.GetRole(r)?.Name ?? r.ToString());

                lines.Add($"{user.Username}: {string.Join(", ", roles)}");
            }
            return string.Join("\n", lines);
        }

        private async Task<string> QueryPermissionsAsync(List<string> targetIds, IGuild guild)
        {
            var lines = new List<string>();
            foreach (var id in targetIds)
            {
                if (!ulong.TryParse(id, out var uid)) continue;
                var user = await guild.GetUserAsync(uid);
                if (user is null) { lines.Add($"{id}: not found"); continue; }
                lines.Add($"{user.Username}: {string.Join(", ", user.GuildPermissions.ToList())}");
            }
            return string.Join("\n", lines);
        }

        private static bool IsActionAllowed(string type, GuildAiConfig config) => type switch
        {
            "ban" => config.ActionBan,
            "kick" => config.ActionKick,
            "mute" => config.ActionMute,
            "unmute" => config.ActionUnmute,
            "unban" => config.ActionUnban,
            "warn" => config.ActionWarn,
            "lock" => config.ActionLock,
            "slowmode" => config.ActionSlowmode,
            "query_userinfo" => true,
            "query_messages" => true,
            "query_roles" => true,
            "query_permissions" => true,
            "query_ivrc_info" => true,
            "query_joinorder" => true,
            _ => false
        };
    }
}

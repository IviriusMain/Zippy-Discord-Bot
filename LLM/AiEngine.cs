using System.Diagnostics;
using Discord;
using Discord.WebSocket;

namespace Ivirius.Zippy
{
    public class AiEngine
    {
        private readonly DiscordSocketClient _client;
        private readonly ActionExecutor _executor;
        private const int MaxIterations = 5;

        public AiEngine(DiscordSocketClient client)
        {
            _client = client;
            _executor = new ActionExecutor(client);
        }

        public async Task<string> RespondAsync(IMessage triggerMessage, IGuild guild)
        {
            var config = AiConfigStorage.Get(guild.Id);
            if (!config.AiEnabled)
                return string.Empty;

            // Step 1: Build context
            var context = await ContextBuilder.BuildAsync(triggerMessage.Channel, triggerMessage, _client.CurrentUser.Id);

            // Step 2: Clean the triggering message
            var userMessage = SecurityFilter.SanitizeUserMessage(triggerMessage.Content);
            userMessage = userMessage.Replace($"<@{_client.CurrentUser.Id}>", "").Trim();

            // Step 3: Build prompts
            var systemPrompt = PromptBuilder.Build(guild, triggerMessage.Channel as IGuildChannel);
            var prefix = "";

            if (config.RulesChannelId.HasValue)
            {
                var rules = await _executor.QueryChannelContentAsync(config.RulesChannelId, "", guild);
                if (!string.IsNullOrWhiteSpace(rules))
                    prefix += $"Server rules:\n{rules}\n\n";
            }

            if (config.ContextChannelId.HasValue)
            {
                var serverContext = await _executor.QueryChannelContentAsync(config.ContextChannelId, "", guild);
                if (!string.IsNullOrWhiteSpace(serverContext))
                    prefix += $"Server context:\n{serverContext}\n\n";
            }

            var userPrompt = prefix + BuildUserPrompt(triggerMessage.Author, triggerMessage.Channel, context, userMessage);

            // Step 4: Send initial status message
            var statusMessage = await triggerMessage.Channel.SendMessageAsync(
                "<:ZippyStareRight:1400924222448209920> *Zippy is typing...*",
                messageReference: new MessageReference(triggerMessage.Id));

            var statusLines = new List<string>();
            var actionResults = new List<string>();
            string finalReply = string.Empty;

            // Step 5: Agentic loop
            for (int i = 0; i < MaxIterations; i++)
            {
                var fullPrompt = actionResults.Count > 0
                    ? userPrompt + "\n\nAction results from previous step:\n" + string.Join("\n", actionResults)
                    : userPrompt;

                Console.WriteLine(fullPrompt);
                var raw = await LlmClient.CompleteAsync(systemPrompt, fullPrompt);
                Console.WriteLine(raw);
                var parsed = ActionParser.Parse(raw);

                // No actions — we're done
                if (parsed.Actions.Count == 0)
                {
                    finalReply = parsed.TextReply;
                    break;
                }

                // Build and display status lines for this iteration
                foreach (var action in parsed.Actions)
                {
                    var statusLine = action.Type switch
                    {
                        "query_userinfo" => $"> 🔍 Querying user info for: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "query_messages" => $"> 📜 Fetching messages from <#{action.ChannelId}>...",
                        "query_roles" => "> 📋 Fetching user roles...",
                        "query_rules" => "> 📙 Checking the rules...",
                        "query_ivrc_info" => "> 📙 Reading more about Ivirius Community...",
                        "query_permissions" => $"> 🔐 Checking permissions for: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "ban" => $"> 🔨 Banning: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "unban" => $"> 🔓 Unbanning: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "kick" => $"> 👢 Kicking: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "mute" => $"> 🔇 Muting: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "unmute" => $"> 🔊 Unmuting: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "warn" => $"> ⚠️ Warning: {string.Join(", ", action.Targets.Select(t => $"<@{t}>"))}",
                        "lock" => "> 🔒 Locking channel",
                        "slowmode" => $"> 🐢 Setting slowmode to {action.Duration}",
                        _ => $"> ⚙️ Executing: {action.Type}"
                    };
                    statusLines.Add(statusLine);
                }

                await statusMessage.ModifyAsync(m => m.Content = string.Join("\n", statusLines));

                // Execute all actions and collect results
                actionResults.Clear();
                foreach (var action in parsed.Actions)
                {
                    var result = await _executor.ExecuteAsync(action, guild, triggerMessage.Author);
                    actionResults.Add($"[{action.Type}] {result}");
                }

                // If this was the last iteration, use whatever text reply we got
                if (i == MaxIterations - 1)
                    finalReply = parsed.TextReply;
            }

            // Step 6: Edit the status message to append the final reply
            var finalContent = statusLines.Count > 0
                ? string.Join("\n", statusLines) + "\n\n" + SecurityFilter.SanitizeText(finalReply)
                : SecurityFilter.SanitizeText(finalReply);

            await statusMessage.ModifyAsync(m => m.Content = finalContent);

            // Return empty since we already sent/edited the message directly
            return string.Empty;
        }

        private static string BuildUserPrompt(IUser author, IMessageChannel channel, string context, string message)
        {
            return $"You are in #{channel.Name}.\n\nRecent conversation:\n{context}\n\n>>> {author.Username} said: \"{message}\"\n\nRespond to what {author.Username} just said.";
        }
    }
}

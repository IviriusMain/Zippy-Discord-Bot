using Discord;
using Discord.WebSocket;

namespace Ivirius.Zippy
{
    public class AutoMessenger
    {
        private readonly DiscordSocketClient _client;
        private readonly AiEngine _engine;
        private readonly Random _random = new();

        // Tracks last message time and whether Zippy sent it, per channel
        private readonly Dictionary<ulong, (DateTime LastMessage, bool LastWasZippy)> _channelState = [];
        private bool _waitingForPing = false;

        public AutoMessenger(DiscordSocketClient client, AiEngine engine)
        {
            _client = client;
            _engine = engine;
        }

        public void Start()
        {
            _ = Task.Run(LoopAsync);
        }

        public void OnMessageReceived(IMessage message, ulong botId)
        {
            // Track channel activity
            _channelState[message.Channel.Id] = (DateTime.UtcNow, message.Author.Id == botId);

            // If Zippy was pinged, reset the waiting flag
            if (message.MentionedUserIds.Contains(botId))
                _waitingForPing = false;
        }

        private async Task LoopAsync()
        {
            while (true)
            {
                // Wait a random interval between 1 and 6 hours
                var wait = TimeSpan.FromMinutes(_random.Next(60, 360));
                await Task.Delay(wait);

                if (_waitingForPing)
                    continue;

                await TrySendAutoMessageAsync();
            }
        }

        private async Task TrySendAutoMessageAsync()
        {
            foreach (var guild in _client.Guilds)
            {
                var config = AiConfigStorage.Get(guild.Id);
                if (!config.AiEnabled || !config.AutoMessagingEnabled)
                    continue;

                // Find eligible channels: text, everyone can read and send, last message wasn't Zippy
                var eligible = guild.TextChannels
                    .Where(c => IsChannelEligible(c, guild))
                    .ToList();

                if (eligible.Count == 0) continue;

                // Pick a random eligible channel
                var channel = eligible[_random.Next(eligible.Count)];

                // Check last message in channel
                var messages = await channel.GetMessagesAsync(1).FlattenAsync();
                var last = messages.FirstOrDefault();

                if (last is null || last.Author.Id == _client.CurrentUser.Id)
                    continue;

                // Check it's actually been quiet for at least an hour
                if ((DateTime.UtcNow - last.Timestamp.UtcDateTime).TotalHours < 1)
                    continue;

                // Generate a casual message
                var context = await ContextBuilder.BuildAsync(channel, last, _client.CurrentUser.Id, 10);
                var systemPrompt = PromptBuilder.Build(guild, channel);
                var prompt = $"The chat has been quiet for a while. Say something casual and brief to spark conversation. Recent context:\n{context}";

                try
                {
                    var reply = await LlmClient.CompleteAsync(systemPrompt, prompt);
                    var clean = SecurityFilter.SanitizeText(reply);

                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        await channel.SendMessageAsync(clean);
                        _waitingForPing = true; // wait for a ping before auto-messaging again
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutoMessenger] Error: {ex.Message}");
                }
            }
        }

        private bool IsChannelEligible(SocketTextChannel channel, SocketGuild guild)
        {
            var everyoneRole = guild.EveryoneRole;
            var perms = channel.GetPermissionOverwrite(everyoneRole);

            // Must not have SendMessages or ViewChannel denied for @everyone
            if (perms.HasValue)
            {
                if (perms.Value.SendMessages == PermValue.Deny) return false;
                if (perms.Value.ViewChannel == PermValue.Deny) return false;
            }

            return true;
        }
    }
}

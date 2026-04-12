using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Ivirius.Zippy
{
    public static class Program
    {
        private static AiEngine _aiEngine;
        private static AutoMessenger _autoMessenger;

        private static DiscordSocketClient _client = null!;
        private static InteractionService _interactions = null!;

        static async Task Main()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
                LogLevel = LogSeverity.Info
            });

            _interactions = new InteractionService(_client);

            _client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };
            _client.Ready += OnReady;
            _client.InteractionCreated += OnInteraction;

            await _interactions.AddModuleAsync<BanModule>(null);
            await _interactions.AddModuleAsync<MuteModule>(null);
            await _interactions.AddModuleAsync<WarnModule>(null);
            await _interactions.AddModuleAsync<KickModule>(null);
            await _interactions.AddModuleAsync<UserInfoModule>(null);
            await _interactions.AddModuleAsync<SlowmodeModule>(null);
            await _interactions.AddModuleAsync<LockModule>(null);

            _aiEngine = new AiEngine(_client);
            _autoMessenger = new AutoMessenger(_client, _aiEngine);
            _autoMessenger.Start();
            await _interactions.AddModuleAsync<AiConfigModule>(null);

            _client.MessageReceived += async msg =>
            {
                if (msg.Author.IsBot && msg.Author.Id != _client.CurrentUser.Id) return;
                if (msg is not IUserMessage userMsg) return;
                if (msg.Channel is not IGuildChannel guildChannel) return;

                _autoMessenger.OnMessageReceived(msg, _client.CurrentUser.Id);

                // Only respond to pings
                if (!msg.MentionedUserIds.Contains(_client.CurrentUser.Id)) return;
                if (msg.Author.Id == _client.CurrentUser.Id) return;

                var guild = (guildChannel as IGuildChannel)!.Guild;

                using (msg.Channel.EnterTypingState())
                {
                    try
                    {
                        await _aiEngine.RespondAsync(msg, guild);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AiEngine] Error: {ex.Message}");
                    }
                }
            };

            EnvLoader.Load();
            var token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN");
            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            await Task.Delay(-1);
        }

        private static async Task OnReady()
        {
            await _interactions.RegisterCommandsGloballyAsync();
            Console.WriteLine($"Ready! Logged in as {_client.CurrentUser.Username}");
        }

        private static async Task OnInteraction(SocketInteraction interaction)
        {
            ulong.TryParse(Environment.GetEnvironmentVariable("FALLBACK_LOG_CHANNEL_ID"), out ulong fallbackLogChannelId);

            var ctx = new SocketInteractionContext(_client, interaction);
            var result = await _interactions.ExecuteCommandAsync(ctx, null);

            Console.WriteLine(result.Error?.ToString() + " --- " + result.ErrorReason);

            if (!result.IsSuccess)
            {
                var guildChannel = interaction.Channel as IGuildChannel;
                if (guildChannel != null)
                {
                    var config = AiConfigStorage.Get(guildChannel.Guild.Id);
                    var logChannelId = config.LogChannelId != 0 ? config.LogChannelId : fallbackLogChannelId;
                    if (logChannelId != null && logChannelId != 0 &&
                        await _client.GetChannelAsync(logChannelId ?? 0) is ITextChannel logChannel)
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("Interaction Error")
                            .WithColor(Color.Red)
                            .AddField("Error", result.Error?.ToString() ?? "Unknown")
                            .AddField("Reason", result.ErrorReason ?? "None")
                            .WithCurrentTimestamp()
                            .Build();
                        await logChannel.SendMessageAsync(embed: embed);
                    }
                }
            }
        }
    }
}

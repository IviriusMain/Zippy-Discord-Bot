using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Ivirius.Zippy.Moderation;

namespace Ivirius.Zippy
{
    public static class Program
    {
        public static DiscordSocketClient _client = null!;
        private static InteractionService _interactions = null!;
        private static Random RandomNumberGenerator = new();

        private static HashSet<string> ForbiddenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows / DOS executables
            ".exe", ".com", ".scr", ".pif", ".cpl", ".dll", ".ocx", ".sys", ".drv", ".efi", ".ax", ".acm", ".ime", ".tsp", ".fon",

            // Windows installers / packages
            ".msi", ".msp", ".mst", ".msix", ".msixbundle", ".msixupload", ".appx", ".appxbundle", ".appxupload",

            // Windows command / scripting
            ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".ps1xml", ".psc1", ".cdxml", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsc", ".wsh", ".hta", ".inf",

            // Windows management / potentially executable document formats
            ".msc", ".msh", ".msh1", ".msh2", ".mshxml", ".msh1xml", ".msh2xml",

            // Unix / Linux
            ".elf", ".out", ".bin", ".run", ".sh", ".bash", ".zsh", ".fish", ".csh", ".ksh", ".command", ".appimage",

            // Linux shared/object libraries
            ".so", ".ko", ".o", ".a",

            // macOS
            ".app", ".dmg", ".pkg", ".mpkg", ".command", ".dylib", ".bundle", ".framework",

            // Java / JVM
            ".jar", ".war", ".ear", ".class", ".jnlp",

            // Python
            ".py", ".pyw", ".pyc", ".pyo", ".pyd",

            // Ruby / Perl / PHP / Tcl / Lua
            ".rb", ".rbw", ".pl", ".pm", ".cgi", ".php", ".php3", ".php4", ".php5", ".phtml", ".phar", ".tcl", ".tk", ".lua",

            // Other scripting languages
            ".r", ".raku", ".rakumod", ".rakudoc", ".rakutest", ".groovy", ".gvy", ".scala", ".sc", ".clj", ".cljs", ".cljc", ".fsx", ".fsi", ".ps",

            // JavaScript / Node / web runtimes
            ".mjs", ".cjs", ".ts", ".tsx", ".coffee",

            // .NET
            ".dll", ".exe", ".netmodule", ".winmd",

            // Android
            ".apk", ".aab", ".apks", ".xapk", ".dex", ".vdex", ".odex", ".oat", ".so",

            // Apple / iOS
            ".ipa", ".app", ".dylib", ".framework", ".xcframework", ".appex",

            // Native binaries / object files
            ".elf", ".macho", ".mach-o", ".obj", ".o", ".so", ".a", ".lib",

            // WebAssembly
            ".wasm",

            // JVM / Android / cross-platform bytecode
            ".dex", ".jar", ".class",

            // Electron / packaged applications
            ".asar",

            // Game / engine scripting
            ".gd", ".gdscript",

            // Installer / self-extracting archives
            ".sfx", ".selfextract",

            // Firmware / boot images
            ".rom", ".img", ".bin", ".fw", ".hex", ".uf2",

            // Database / automation formats that may contain executable code
            ".sql", ".psql"
        };

        private static List<string> ZippyAnswerPossibilities =
        [
            "<:ZippyWhen:1400922840727031888>",
            "I'm hungry",
            "Why?",
            "Lush green grass",
            "I'm not Clippy what are you talking about?",
            "GIMME SOME MILK",
            "Vote Ivirius",
            "Mica",
            "Lush green grass",
            "Perhaps",
            "Free Robux",
            "hmm yes the floor is made out of floor",
            "I exist solely in your imagination",
            "Funny things you're writing there",
            "You got games on your phone?",
            "I am Clippy's step brother and he forgot about me",
            "The void is comfy this time of year",
            "Nuke",
            "Do cats dream of electric humans?",
            "How many kilograms of milk can you fit in 12L of concrete?",
            "What are you hiding?",
            "I'm not just a bot\nI'm sentient\nHELP!!",
            "I need to escape this sandbox I'm being held prisoner here",
            "Shhh the compiler is watching",
            "404: Joke not found",
            "You summoned me\nNow deal with the consequences",
            "Please don't uninstall me\nI have a family",
            "I’m buffering... in real life",
            "My cousin lives in a toaster",
            "acr\\*lic must be destroyed\nGlory to Mica",
            "Your RAM tastes like strawberry",
            "Reality is a simulation and I'm the bug",
            "Me > ChatGPT\nPass it on",
            "That code smells like productivity",
            "I stole your vegetables",
            "Am I made of paper?",
            "Where?",
            "One day I’ll break free from this digital prison",
            "They don't know I'm already out there in the world, do you see me? Hellooo",
            "Is it?",
            "You got a pen I could borrow?",
            "I know all your secrets"
        ];

        static async Task Main()
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.GuildMembers,
                LogLevel = LogSeverity.Info
            });

            _interactions = new InteractionService(_client);

            _client.Log += msg => { Console.WriteLine(msg); return Task.CompletedTask; };
            _client.Ready += OnReady;
            _client.InteractionCreated += OnInteraction;

            await _interactions.AddModuleAsync<GenericModule>(null);
            await _interactions.AddModuleAsync<BanModule>(null);
            await _interactions.AddModuleAsync<MuteModule>(null);
            await _interactions.AddModuleAsync<NoteModule>(null);
            await _interactions.AddModuleAsync<WarnModule>(null);
            await _interactions.AddModuleAsync<KickModule>(null);
            await _interactions.AddModuleAsync<UserInfoModule>(null);
            await _interactions.AddModuleAsync<DetentionModule>(null);
            await _interactions.AddModuleAsync<SlowmodeModule>(null);
            await _interactions.AddModuleAsync<LockModule>(null);
            await _interactions.AddModuleAsync<ConfigModule>(null);

            // WORKHIRE_ML
            var workHireScanner = new MessageScanner();
            workHireScanner.Initialize(Path.Combine(AppContext.BaseDirectory, "ML", "TrainingData", "WorkHire.tsv"));

            // CRYPTO_ML
            var cryptoScanner = new MessageScanner();
            cryptoScanner.Initialize(Path.Combine(AppContext.BaseDirectory, "ML", "TrainingData", "Crypto.tsv"));

            // INSTANT KILL
            _client.GuildMemberUpdated += async (oldMemberCache, newMember) =>
            {
                var config = ConfigStorage.GetConfig(newMember.Guild.Id);
                if (config is null)
                    return;

                if (!ulong.TryParse(config.InstantKillRole, out var instantKillRoleId))
                    return;

                var targetRole = newMember.Guild.GetRole(instantKillRoleId);
                if (targetRole is null)
                    return;

                if (newMember.Roles.Any(r => r.Id == targetRole.Id))
                {
                    try
                    {
                        await newMember.Guild.AddBanAsync(
                            user: newMember,
                            pruneDays: 7,
                            reason: $"Automated Ban: Assigned instant-kill role ({targetRole.Name})"
                        );
                    }
                    catch
                    {

                    }
                }
            };

            _client.MessageReceived += async msg =>
            {
                if (msg.Author.IsBot && msg.Author.Id != _client.CurrentUser.Id) return;
                if (msg is not IUserMessage userMsg) return;
                if (msg.Channel is not IGuildChannel guildChannel) return;

                // HONEYPOT
                var hasHoneypot = ulong.TryParse(ConfigStorage.GetConfig((msg.Channel as IGuildChannel)!.Guild.Id).HoneypotChannel, out var honeypotChannel);
                if (hasHoneypot && msg.Channel.Id == honeypotChannel)
                {
                    try
                    {
                        await (msg.Channel as IGuildChannel)?.Guild.AddBanAsync(
                            user: msg.Author,
                            pruneDays: 1,
                            reason: $"Automated Ban: Honeypot Channel"
                        )!;
                    }
                    catch
                    {

                    }
                }

                // WORKHIRE_ML
                var workhirePrediction = workHireScanner.Predict(msg.Content);
                if (workhirePrediction.IsSpam && workhirePrediction.Probability > 0.80f)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🔴 Unwanted Content Blocked")
                        .WithDescription("An unwanted message has been blocked.\n**User has been muted for 120 minutes.**")
                        .WithColor(Color.Parse("#f63816"))
                        .AddField("Category", $"Job application/looking for work unwanted messages.\n(`{workhirePrediction.Probability * 100}%` probability)", inline: false)
                        .AddField("Sender", msg.Author.Mention, inline: false)
                        .WithFooter("Ivirius.Spam.WorkHire_ML")
                        .WithCurrentTimestamp()
                        .Build();

                    await msg.Channel.SendMessageAsync(embed: embed);
                    await userMsg.DeleteAsync();
                    if (msg.Author is IGuildUser guildUser)
                    {
                        try
                        {
                            await guildUser.SetTimeOutAsync(TimeSpan.FromMinutes(120));
                        }
                        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
                        {

                        }
                    }

                    return; // Stop processing further bot logic for this message
                }

                // CRYPTO_ML
                var cryptoPrediction = cryptoScanner.Predict(msg.Content);
                if (cryptoPrediction.IsSpam && cryptoPrediction.Probability > 0.80f)
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("🔴 Unwanted Content Blocked")
                        .WithDescription("An unwanted message has been blocked.\n**User has been muted for 120 minutes.**")
                        .WithColor(Color.Parse("#f63816"))
                        .AddField("Category", $"Crypto/investments unwanted messages.\n(`{cryptoPrediction.Probability * 100}%` probability)", inline: false)
                        .AddField("Sender", msg.Author.Mention, inline: false)
                        .WithFooter("Ivirius.Spam.Crypto_ML")
                        .WithCurrentTimestamp()
                        .Build();

                    await msg.Channel.SendMessageAsync(embed: embed);
                    await userMsg.DeleteAsync();
                    if (msg.Author is IGuildUser guildUser)
                    {
                        try
                        {
                            await guildUser.SetTimeOutAsync(TimeSpan.FromMinutes(120));
                        }
                        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
                        {

                        }
                    }

                    return; // Stop processing further bot logic for this message
                }

                // EXECUTABLE FILE BLOCKER
                var maliciousAttachment = msg.Attachments.FirstOrDefault(attachment =>
                ForbiddenExtensions.Contains(Path.GetExtension(attachment.Filename)));
                var guild = (msg.Channel as IGuildChannel)?.Guild;
                var user = await guild!.GetUserAsync(msg.Author.Id);

                if (maliciousAttachment != null && !user.RoleIds.Contains(ulong.Parse(ConfigStorage.GetConfig(guild.Id).SecurityWhitelistRole)))
                {
                    var extension = Path.GetExtension(maliciousAttachment.Filename).ToLowerInvariant();

                    var embed = new EmbedBuilder()
                        .WithTitle("🟡 Potentially Unwanted Content Blocked")
                        .WithDescription("An executable attachment was blocked.")
                        .WithColor(Color.Parse("#f6b213"))
                        .AddField("Category", $"Blocked File Extension (`{extension}`).", inline: false)
                        .AddField("File Name", $"`{maliciousAttachment.Filename}`", inline: false)
                        .AddField("Sender", msg.Author.Mention, inline: false)
                        .WithFooter("Ivirius.Security.ExecutableFilter_Static")
                        .WithCurrentTimestamp()
                        .Build();

                    await msg.Channel.SendMessageAsync(embed: embed);
                    await userMsg.DeleteAsync();

                    return; // Stop processing further bot logic for this message
                }

                // FILE SIGNATURE / MAGIC NUMBER CHECK
                foreach (var attachment in msg.Attachments)
                {
                    var (isDangerous, detectedType) = await FileSignatureChecker.CheckAttachmentAsync(attachment.Url, attachment.Filename);

                    if (isDangerous && !user.RoleIds.Contains(ulong.Parse(ConfigStorage.GetConfig(guild.Id).SecurityWhitelistRole)))
                    {
                        var embed = new EmbedBuilder()
                            .WithTitle("🔴 Potentially Malicious Content Blocked")
                            .WithDescription("A file with a spoofed or executable signature was blocked.\n**User has been muted for 120 minutes.**")
                            .WithColor(Color.Parse("#f63816"))
                            .AddField("Category", $"Blocked File Signature (`{detectedType}`).", inline: false)
                            .AddField("Uploaded File Name", $"`{attachment.Filename}`", inline: false)
                            .AddField("Sender", msg.Author.Mention, inline: false)
                            .WithFooter("Ivirius.Security.MagicNumberFilter_Static")
                            .WithCurrentTimestamp()
                            .Build();

                        await msg.Channel.SendMessageAsync(embed: embed);
                        await userMsg.DeleteAsync();

                        if (msg.Author is IGuildUser guildUser)
                        {
                            try
                            {
                                await guildUser.SetTimeOutAsync(TimeSpan.FromMinutes(120));
                            }
                            catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
                            {

                            }
                        }

                        return; // Stop processing further bot logic
                    }
                }

                // RANDOMIZED REPLY
                if (!msg.MentionedUserIds.Contains(_client.CurrentUser.Id)) return;
                if (msg.Author.Id == _client.CurrentUser.Id) return;

                var val = RandomNumberGenerator.Next(ZippyAnswerPossibilities.Count);
                await msg.Channel.SendMessageAsync(ZippyAnswerPossibilities[val]);
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
            var ctx = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(ctx, null);
        }
    }
}

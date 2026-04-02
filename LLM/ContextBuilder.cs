using Discord;

namespace Ivirius.Zippy
{
    public static class ContextBuilder
    {
        public static async Task<string> BuildAsync(IMessageChannel channel, IMessage triggerMessage, ulong botId, int limit = 25)
        {
            var lines = new List<string>();
            var seen = new HashSet<ulong>();

            await foreach (var batch in channel.GetMessagesAsync(limit))
            {
                foreach (var msg in batch)
                {
                    if (seen.Contains(msg.Id)) continue;
                    seen.Add(msg.Id);

                    var isBot = msg.Author.Id == botId;
                    var author = isBot
                        ? "YOU (Zippy)"
                        : $"{msg.Author.Username} (ID: {msg.Author.Id}) (MSG ID: {msg.Id})";

                    var content = msg.Content?.Length > 200
                        ? msg.Content[..200]
                        : msg.Content ?? string.Empty;

                    // Read embeds
                    var embedParts = new List<string>();
                    foreach (var embed in msg.Embeds)
                    {
                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(embed.Title))
                            parts.Add($"Title: {embed.Title}");
                        if (!string.IsNullOrWhiteSpace(embed.Description))
                            parts.Add($"Desc: {embed.Description[..Math.Min(embed.Description.Length, 150)]}");
                        foreach (var field in embed.Fields)
                            parts.Add($"{field.Name}: {field.Value[..Math.Min(field.Value.Length, 100)]}");
                        if (parts.Count > 0)
                            embedParts.Add($"[Embed: {string.Join(" | ", parts)}]");
                    }

                    var embedText = embedParts.Count > 0 ? " " + string.Join(" ", embedParts) : string.Empty;

                    // Handle reply chain
                    var replyText = string.Empty;
                    if (msg.Reference?.MessageId.IsSpecified == true)
                    {
                        try
                        {
                            var replied = await channel.GetMessageAsync(msg.Reference.MessageId.Value);
                            if (replied is not null)
                            {
                                var repliedAuthor = replied.Author.Id == botId ? "Zippy" : replied.Author.Username;
                                var repliedContent = replied.Content?.Length > 100
                                    ? replied.Content[..100]
                                    : replied.Content ?? string.Empty;
                                replyText = $" [replying to {repliedAuthor}: \"{repliedContent}\"]";
                            }
                        }
                        catch { /* ignore */ }
                    }

                    lines.Add($"{author}{replyText}: {content}{embedText}");
                }
            }

            lines.Reverse();
            return string.Join("\n", lines.TakeLast(15));
        }
    }
}

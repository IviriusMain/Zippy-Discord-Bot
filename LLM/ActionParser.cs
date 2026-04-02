using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ivirius.Zippy
{
    public class ParsedAction
    {
        public string Type { get; set; } = string.Empty;
        public bool SelfAuthorized { get; set; }
        public List<string> Targets { get; set; } = [];
        public string Reason { get; set; } = "No reason provided.";
        public string? Duration { get; set; }
        public bool DeleteMessageHistory { get; set; }
        public string? ChannelId { get; set; }
    }

    public class ParsedLlmResponse
    {
        public List<ParsedAction> Actions { get; set; } = [];
        public string TextReply { get; set; } = string.Empty;
    }

    public static class ActionParser
    {
        private const string Separator = "JSON_DAT_SEPARATE_QUERY";

        public static ParsedLlmResponse Parse(string raw)
        {
            var result = new ParsedLlmResponse();

            var parts = raw.Split(Separator, StringSplitOptions.TrimEntries);

            // Last part is always the text reply
            result.TextReply = SecurityFilter.SanitizeText(parts.Last());

            // Everything before the last part may contain JSON blocks
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var part = parts[i].Trim();
                if (string.IsNullOrWhiteSpace(part)) continue;

                try
                {
                    var node = JsonNode.Parse(part);
                    if (node is JsonArray arr)
                    {
                        foreach (var item in arr)
                            TryParseAction(item, result.Actions);
                    }
                    else
                    {
                        TryParseAction(node, result.Actions);
                    }
                }
                catch { /* malformed JSON, skip */ }
            }

            return result;
        }

        private static void TryParseAction(JsonNode? node, List<ParsedAction> actions)
        {
            if (node is null) return;

            try
            {
                var action = new ParsedAction
                {
                    Type = node["type"]?.GetValue<string>() ?? string.Empty,
                    SelfAuthorized = node["self_authorized"]?.GetValue<bool>() ?? false,
                    Reason = node["reason"]?.GetValue<string>() ?? "No reason provided.",
                    Duration = node["duration"]?.GetValue<string>(),
                    DeleteMessageHistory = node["delete_message_history"]?.GetValue<bool>() ?? false,
                    Targets = node["targets"]?.AsArray()
                        .Select(t => t?.GetValue<string>() ?? string.Empty)
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .ToList() ?? [],
                    ChannelId = node["channel_id"]?.GetValue<string>(),
                };

                if (!string.IsNullOrWhiteSpace(action.Type))
                    actions.Add(action);
            }
            catch { /* skip malformed action */ }
        }
    }
}

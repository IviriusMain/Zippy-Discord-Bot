using System.Text.RegularExpressions;

namespace Ivirius.Zippy
{
    public static class SecurityFilter
    {
        // Strip @everyone and @here from any text, including obfuscated variants
        public static string SanitizeText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            // Normalize unicode
            content = content.Normalize(System.Text.NormalizationForm.FormKC);

            // Remove zero-width characters
            content = Regex.Replace(content, @"[\u200b\u200c\u200d\ufeff]", "");

            // Strip @everyone and @here variants
            content = Regex.Replace(content, @"@+\s*everyone", "everyone", RegexOptions.IgnoreCase);
            content = Regex.Replace(content, @"@+\s*here", "here", RegexOptions.IgnoreCase);

            // Em dash cleanup
            content = content.Replace("—", " - ").Replace("–", " - ");

            return content;
        }

        // Block prompt injection — strip JSON blocks from user messages before feeding to LLM
        public static string StripJsonBlocks(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            // Remove anything that looks like a JSON object or array
            content = Regex.Replace(content, @"\{[^{}]*\}", "[REDACTED]", RegexOptions.Singleline);
            content = Regex.Replace(content, @"\[[^\[\]]*\]", "[REDACTED]", RegexOptions.Singleline);

            // Strip the separator keyword so users can't fake MCP commands
            content = content.Replace("JSON_DAT_SEPARATE_QUERY", "[REDACTED]");

            return content;
        }

        public static string SanitizeUserMessage(string content)
        {
            content = StripJsonBlocks(content);
            content = SanitizeText(content);
            return content;
        }
    }
}

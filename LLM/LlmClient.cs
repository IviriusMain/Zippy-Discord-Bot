using System.Net.Http.Json;
using System.Text.Json;

namespace Ivirius.Zippy
{
    public static class LlmClient
    {
        private static readonly HttpClient Http = new();
        private const string Url = "https://text.pollinations.ai/openai";
        private const string Model = "openai-fast";

        public static async Task<string> CompleteAsync(string systemPrompt, string userPrompt)
        {
            var payload = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user",   content = userPrompt   }
                }
            };

            var response = await Http.PostAsJsonAsync(Url, payload);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            return data
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
    }
}

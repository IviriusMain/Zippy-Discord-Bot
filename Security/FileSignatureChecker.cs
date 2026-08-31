using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

public static class FileSignatureChecker
{
    private static readonly HttpClient _httpClient = new HttpClient();

    public static async Task<(bool IsDangerous, string? DetectedType)> CheckAttachmentAsync(string attachmentUrl, string filename)
    {
        try
        {
            // Only fetch the first 16 bytes (enough to check magic headers)
            using var request = new HttpRequestMessage(HttpMethod.Get, attachmentUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 15);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return (false, null);

            byte[] header = new byte[16];
            using var stream = await response.Content.ReadAsStreamAsync();
            int bytesRead = await stream.ReadAsync(header, 0, header.Length);

            if (bytesRead < 4)
                return (false, null);

            // Windows PE (.exe, .dll, .sys, .scr) -> Magic "MZ"
            if (header[0] == 0x4D && header[1] == 0x5A)
            {
                return (true, "Windows Executable (PE)");
            }

            // Linux ELF Binary (.elf, .so, .bin) -> Magic "\x7FE L F"
            if (header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46)
            {
                return (true, "Linux ELF Binary");
            }

            // macOS Mach-O Binary -> Magic 0xFEEDFACE or 0xFEEDFACF
            if ((header[0] == 0xFE && header[1] == 0xED && header[2] == 0xFA && (header[3] == 0xCE || header[3] == 0xCF)) ||
                (header[0] == 0xCF && header[1] == 0xFA && header[2] == 0xED && header[3] == 0xFE) ||
                (header[0] == 0xCA && header[1] == 0xFE && header[2] == 0xBA && header[3] == 0xBE)) // Fat Binary
            {
                return (true, "macOS Mach-O Binary");
            }

            // Java Class File (.class) -> Magic 0xCAFEBABE
            if (header[0] == 0xCA && header[1] == 0xFE && header[2] == 0xBA && header[3] == 0xBE)
            {
                return (true, "Java Compiled Bytecode");
            }

            // Shell Scripts / Hashbang (#!)
            if (header[0] == 0x23 && header[1] == 0x21) // "#!"
            {
                return (true, "Executable Script (Shebang)");
            }
        }
        catch
        {

        }

        return (false, null);
    }
}

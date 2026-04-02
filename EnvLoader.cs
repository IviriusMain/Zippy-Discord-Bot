namespace Ivirius.Zippy;

public static class EnvLoader
{
    public static void Load(string path = ".env")
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx < 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

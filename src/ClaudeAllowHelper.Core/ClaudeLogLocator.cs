namespace ClaudeAllowHelper.Core;

public static class ClaudeLogLocator
{
    public static string? FindMainLog()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var packages = Path.Combine(localAppData, "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var dir in Directory.GetDirectories(packages, "Claude_*"))
            {
                var packaged = Path.Combine(dir, "LocalCache", "Local", "Claude", "logs", "main.log");
                if (File.Exists(packaged))
                {
                    return packaged;
                }
            }
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(roaming, "Claude", "logs", "main.log"),
                     Path.Combine(localAppData, "Claude", "logs", "main.log"),
                     Path.Combine(localAppData, "Claude", "Local", "Claude", "logs", "main.log")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

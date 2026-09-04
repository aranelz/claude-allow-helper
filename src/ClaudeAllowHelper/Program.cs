using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper;

internal static class Program
{
    private const string MutexName = @"Local\ClaudeAllowHelper.Teknomice";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var created);
        if (!created && !args.Contains("--dump-ui", StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show("Claude Allow Helper is already running.", "Claude Allow Helper",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var store = new ConfigStore();
        Directory.CreateDirectory(store.ConfigDirectory);
        var logger = new FileLogger(Path.Combine(store.ConfigDirectory, "helper.log"));

        if (args.Contains("--dump-ui", StringComparer.OrdinalIgnoreCase))
        {
            DumpUi(store, logger);
            return;
        }

        var loaded = store.Load();
        if (!loaded.FromDisk && loaded.IsValid)
        {
            store.Save(loaded.Config);
            logger.Info("Wrote default config to " + store.ConfigPath);
        }
        else if (!loaded.IsValid)
        {
            logger.Error("Startup config invalid: " + loaded.Error);
        }

        logger.Info("Claude Allow Helper starting.");
        using var watcher = new PermissionWatcher(store, logger);
        Application.Run(new TrayApplication(store, logger, watcher));
        logger.Info("Claude Allow Helper exited.");
    }

    private static void DumpUi(ConfigStore store, FileLogger logger)
    {
        using var scanner = new UiAutomationScanner();
        var dump = scanner.DumpTree();
        var path = Path.Combine(store.ConfigDirectory, "ui-dump.txt");
        File.WriteAllText(path, dump);
        logger.Info("Wrote UI dump to " + path);
        MessageBox.Show("Wrote UI dump to:\n" + path, "Claude Allow Helper");
    }
}

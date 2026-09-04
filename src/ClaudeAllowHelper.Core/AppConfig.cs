namespace ClaudeAllowHelper.Core;

public sealed class AppConfig
{
    public bool Enabled { get; set; }

    public List<string> AllowedSites { get; set; } = [];

    public int PollIntervalMs { get; set; } = 500;

    public int SafetyDebounceMs { get; set; } = 200;

    public int HiddenDialogMaxAgeMs { get; set; } = 2000;

    public bool StartWithWindows { get; set; }

    public static AppConfig CreateDefault() => new()
    {
        Enabled = false,
        AllowedSites =
        [
            "analitikcms.test",
            "localhost",
            "127.0.0.1"
        ],
        PollIntervalMs = 500,
        SafetyDebounceMs = 200,
        HiddenDialogMaxAgeMs = 2000,
        StartWithWindows = false
    };
}

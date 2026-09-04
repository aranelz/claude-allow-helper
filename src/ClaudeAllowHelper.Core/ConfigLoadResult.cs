namespace ClaudeAllowHelper.Core;

public sealed class ConfigLoadResult
{
    public required AppConfig Config { get; init; }

    public bool FromDisk { get; init; }

    public bool IsValid { get; init; }

    public string? Error { get; init; }

    public static ConfigLoadResult Missing(AppConfig defaults) => new()
    {
        Config = defaults,
        FromDisk = false,
        IsValid = true,
        Error = null
    };

    public static ConfigLoadResult Ok(AppConfig config) => new()
    {
        Config = config,
        FromDisk = true,
        IsValid = true
    };

    public static ConfigLoadResult Invalid(string error, AppConfig safeDisabled) => new()
    {
        Config = safeDisabled,
        FromDisk = true,
        IsValid = false,
        Error = error
    };
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeAllowHelper.Core;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string ConfigDirectory { get; }
    public string ConfigPath { get; }

    public ConfigStore(string? configDirectory = null)
    {
        ConfigDirectory = configDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeAllowHelper");
        ConfigPath = Path.Combine(ConfigDirectory, "config.json");
    }

    public ConfigLoadResult Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return ConfigLoadResult.Missing(AppConfig.CreateDefault());
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return Parse(json);
        }
        catch (Exception ex)
        {
            return DisabledInvalid($"Could not read config: {ex.Message}");
        }
    }

    public ConfigLoadResult Parse(string? json)
    {
        if (json is null || string.IsNullOrWhiteSpace(json))
        {
            return DisabledInvalid("Config file is empty.");
        }

        AppConfig? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return DisabledInvalid($"Config JSON is malformed: {ex.Message}");
        }

        if (parsed is null)
        {
            return DisabledInvalid("Config JSON deserialized to null.");
        }

        parsed.AllowedSites ??= [];
        parsed.AllowedSites = parsed.AllowedSites
            .Where(site => !string.IsNullOrWhiteSpace(site))
            .Select(site => site.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parsed.PollIntervalMs < 200)
        {
            parsed.PollIntervalMs = 200;
        }

        if (parsed.PollIntervalMs > 5000)
        {
            parsed.PollIntervalMs = 5000;
        }

        if (parsed.SafetyDebounceMs < 100)
        {
            parsed.SafetyDebounceMs = 100;
        }

        if (parsed.SafetyDebounceMs > 300)
        {
            parsed.SafetyDebounceMs = 300;
        }

        if (parsed.HiddenDialogMaxAgeMs < 500)
        {
            parsed.HiddenDialogMaxAgeMs = 500;
        }

        if (parsed.HiddenDialogMaxAgeMs > 5000)
        {
            parsed.HiddenDialogMaxAgeMs = 5000;
        }

        return ConfigLoadResult.Ok(parsed);
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    private static ConfigLoadResult DisabledInvalid(string error)
    {
        var safe = AppConfig.CreateDefault();
        safe.Enabled = false;
        safe.AllowedSites = [];
        return ConfigLoadResult.Invalid(error, safe);
    }
}

using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void MissingConfigReturnsDefaultsWithoutError()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cah-missing-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigStore(dir);

        var loaded = store.Load();

        Assert.True(loaded.IsValid);
        Assert.False(loaded.FromDisk);
        Assert.False(loaded.Config.Enabled);
        Assert.Contains("analitikcms.test", loaded.Config.AllowedSites);
        Assert.Null(loaded.Error);
    }

    [Fact]
    public void MalformedJsonDisablesHelperAndClearsAllowlist()
    {
        var store = new ConfigStore(Path.Combine(Path.GetTempPath(), "cah-bad-" + Guid.NewGuid().ToString("N")));
        var loaded = store.Parse("{ this is not json");

        Assert.False(loaded.IsValid);
        Assert.False(loaded.Config.Enabled);
        Assert.Empty(loaded.Config.AllowedSites);
        Assert.Contains("malformed", loaded.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyFileIsInvalid()
    {
        var store = new ConfigStore(Path.GetTempPath());
        var loaded = store.Parse("   ");

        Assert.False(loaded.IsValid);
        Assert.False(loaded.Config.Enabled);
    }

    [Fact]
    public void RoundTripsValidConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cah-ok-" + Guid.NewGuid().ToString("N"));
        var store = new ConfigStore(dir);
        var config = new AppConfig
        {
            Enabled = true,
            AllowedSites = ["analitikcms.test", "localhost"],
            PollIntervalMs = 400
        };

        store.Save(config);
        var loaded = store.Load();

        Assert.True(loaded.IsValid);
        Assert.True(loaded.FromDisk);
        Assert.True(loaded.Config.Enabled);
        Assert.Equal(["analitikcms.test", "localhost"], loaded.Config.AllowedSites);
    }

    [Fact]
    public void DropsBlankAllowlistEntries()
    {
        var store = new ConfigStore(Path.GetTempPath());
        var loaded = store.Parse("""{"enabled":true,"allowedSites":["localhost","", "  "]}""");

        Assert.True(loaded.IsValid);
        Assert.Equal(["localhost"], loaded.Config.AllowedSites);
    }

    [Fact]
    public void ClampsSafetyDebounceToConfiguredWindow()
    {
        var store = new ConfigStore(Path.GetTempPath());
        var low = store.Parse("""{"enabled":false,"allowedSites":["localhost"],"safetyDebounceMs":1,"hiddenDialogMaxAgeMs":10}""");
        var high = store.Parse("""{"enabled":false,"allowedSites":["localhost"],"safetyDebounceMs":5000,"hiddenDialogMaxAgeMs":99999}""");

        Assert.Equal(100, low.Config.SafetyDebounceMs);
        Assert.Equal(500, low.Config.HiddenDialogMaxAgeMs);
        Assert.Equal(300, high.Config.SafetyDebounceMs);
        Assert.Equal(5000, high.Config.HiddenDialogMaxAgeMs);
    }
}

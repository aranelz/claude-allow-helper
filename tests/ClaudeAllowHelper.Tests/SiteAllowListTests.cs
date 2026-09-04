using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class SiteAllowListTests
{
    private static readonly string[] Allowed =
    [
        "analitikcms.test",
        "localhost",
        "127.0.0.1"
    ];

    [Theory]
    [InlineData("analitikcms.test")]
    [InlineData("http://analitikcms.test")]
    [InlineData("https://analitikcms.test/admin")]
    [InlineData("ANALITIKCMS.TEST")]
    [InlineData("http://analitikcms.test:80")]
    [InlineData("localhost")]
    [InlineData("http://localhost:5173")]
    [InlineData("127.0.0.1")]
    [InlineData("http://127.0.0.1:8080/index.html")]
    public void AllowsConfiguredLocalHosts(string candidate)
    {
        Assert.True(SiteAllowList.IsAllowed(candidate, Allowed));
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("https://github.com")]
    [InlineData("analitikcms.test.evil.com")]
    [InlineData("not-analitikcms.test")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsHostsOutsideTheAllowlist(string? candidate)
    {
        Assert.False(SiteAllowList.IsAllowed(candidate, Allowed));
    }

    [Fact]
    public void WildcardMatchesSubdomainOnly()
    {
        string[] allowed = ["*.analitikcms.test"];
        Assert.True(SiteAllowList.IsAllowed("app.analitikcms.test", allowed));
        Assert.False(SiteAllowList.IsAllowed("analitikcms.test", allowed));
        Assert.False(SiteAllowList.IsAllowed("analitikcms.test.evil.com", allowed));
    }

    [Fact]
    public void PortInAllowlistIsHonored()
    {
        string[] allowed = ["localhost:3000"];
        Assert.True(SiteAllowList.IsAllowed("http://localhost:3000", allowed));
        Assert.False(SiteAllowList.IsAllowed("http://localhost:5173", allowed));
    }

    [Fact]
    public void EmptyAllowlistMatchesNothing()
    {
        Assert.False(SiteAllowList.IsAllowed("localhost", []));
        Assert.False(SiteAllowList.IsAllowed("localhost", null));
    }

    [Theory]
    [InlineData("analitikcms.test")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.10")]
    [InlineData("app.local")]
    public void RecognizesLocalDevelopmentHosts(string host)
    {
        Assert.True(SiteAllowList.LooksLikeLocalDevelopmentHost(host));
    }

    [Theory]
    [InlineData("github.com")]
    [InlineData("https://example.com")]
    public void RecognizesNonLocalHosts(string host)
    {
        Assert.False(SiteAllowList.LooksLikeLocalDevelopmentHost(host));
    }
}

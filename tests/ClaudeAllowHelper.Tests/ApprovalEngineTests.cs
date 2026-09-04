using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class ApprovalEngineTests
{
    private static AppConfig EnabledConfig() => new()
    {
        Enabled = true,
        AllowedSites = ["analitikcms.test", "localhost", "127.0.0.1"]
    };

    [Fact]
    public void ApprovesMatchingAllowlistedJavaScriptDialog()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to execute JavaScript on analitikcms.test?\nDeny\nAllow once");

        var result = ApprovalEngine.Decide(EnabledConfig(), ui, null);

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal("analitikcms.test", result.Host);
    }

    [Fact]
    public void IgnoresJavaScriptDialogForUnknownHost()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to execute JavaScript on github.com?\nDeny\nAllow once");

        var result = ApprovalEngine.Decide(EnabledConfig(), ui, null);

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("not in the allowlist", result.Reason);
    }

    [Fact]
    public void IgnoresUnrelatedPermissionDialogs()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to permanently delete files in this folder on your computer?\nDeny\nAllow once");
        var log = new ClaudePermissionLogEvent
        {
            Origin = "http://analitikcms.test",
            ToolName = "javascript_tool"
        };

        var result = ApprovalEngine.Decide(EnabledConfig(), ui, log);

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("Unrelated", result.Reason);
    }

    [Fact]
    public void DisabledToggleBlocksApproval()
    {
        var config = EnabledConfig();
        config.Enabled = false;
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to execute JavaScript on analitikcms.test?\nDeny\nAllow once");

        var result = ApprovalEngine.Decide(config, ui, null);

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("turned off", result.Reason);
    }

    [Fact]
    public void ApprovesFromClaudeLogWhenUiTreeIsIncomplete()
    {
        var ui = PermissionDialogParser.Parse(["Claude"], ["Kapat"]);
        var log = new ClaudePermissionLogEvent
        {
            Origin = "http://analitikcms.test",
            ToolName = "javascript_tool"
        };

        var result = ApprovalEngine.Decide(EnabledConfig(), ui, log);

        Assert.True(ui.TreeAppearsIncomplete);
        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal("analitikcms.test", result.Host);
        Assert.Equal("log", result.Source);
    }

    [Fact]
    public void ApprovesFromClaudeLogWhenChromiumExposesPermissionChromeWithoutJavaScriptTitle()
    {
        var ui = PermissionDialogParser.Parse(
            ["Site-level permissions are disabled for this site. You'll be asked for each action."],
            ["Deny", "Allow once"]);
        var log = new ClaudePermissionLogEvent
        {
            Origin = "http://analitikcms.test",
            ToolName = "javascript_tool"
        };

        var result = ApprovalEngine.Decide(EnabledConfig(), ui, log);

        Assert.False(ui.TreeAppearsIncomplete);
        Assert.False(ui.LooksLikeJavaScriptPermission);
        Assert.False(ui.IsIdentifiedNonJavaScriptDialog);
        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal("analitikcms.test", result.Host);
        Assert.Equal("log", result.Source);
    }

    [Fact]
    public void IgnoresJavaScriptLogWhenADifferentAllowClaudeDialogIsVisible()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to click the submit button?\nDeny\nAllow once");
        var log = new ClaudePermissionLogEvent
        {
            Origin = "http://analitikcms.test",
            ToolName = "javascript_tool"
        };

        var result = ApprovalEngine.Decide(EnabledConfig(), ui, log);

        Assert.True(ui.IsIdentifiedNonJavaScriptDialog);
        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("different visible dialog", result.Reason);
    }

    [Fact]
    public void IgnoresNonJavaScriptLogEvents()
    {
        var log = new ClaudePermissionLogEvent
        {
            Origin = "http://analitikcms.test",
            ToolName = "computer"
        };

        var result = ApprovalEngine.Decide(EnabledConfig(), null, log);

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("not JavaScript", result.Reason);
    }
}

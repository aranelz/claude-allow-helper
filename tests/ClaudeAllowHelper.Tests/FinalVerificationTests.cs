using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class FinalVerificationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 4, 13, 24, 0, TimeSpan.Zero);

    private static AppConfig EnabledConfig() => new()
    {
        Enabled = true,
        AllowedSites = ["analitikcms.test", "localhost", "127.0.0.1"],
        HiddenDialogMaxAgeMs = 2000
    };

    private static ClaudePermissionLogEvent JsLog(
        string origin = "http://analitikcms.test",
        DateTimeOffset? at = null,
        string? id = "req-js") => new()
    {
        Origin = origin,
        ToolName = "javascript_tool",
        RequestId = id,
        ObservedAt = at ?? T0
    };

    private static ClaudePermissionLogEvent UnrelatedLog(
        string tool = "computer",
        DateTimeOffset? at = null) => new()
    {
        Origin = "http://analitikcms.test",
        ToolName = tool,
        RequestId = "req-other",
        ObservedAt = at ?? T0.AddMilliseconds(50)
    };

    private static UiPermissionSnapshot JsDialog(string host = "analitikcms.test") =>
        PermissionDialogParser.ParseBlob(
            $"Allow Claude to execute JavaScript on {host}?\nSite-level permissions are disabled for this site.\nDeny\nAllow once");

    private static UiPermissionSnapshot HiddenTree() =>
        PermissionDialogParser.Parse(["Claude"], ["Kapat"]);

    private static FinalVerificationInput HiddenJs(
        bool foreground = true,
        ClaudePermissionLogEvent? js = null,
        ClaudePermissionLogEvent? unrelated = null,
        string? detectedHost = "analitikcms.test",
        DateTimeOffset? now = null) => new()
    {
        Config = EnabledConfig(),
        DetectedHost = detectedHost,
        DetectedJavaScriptLogEvent = js ?? JsLog(),
        DetectedAt = T0,
        Now = now ?? T0.AddMilliseconds(150),
        ForegroundIsExpectedClaudeWindow = foreground,
        FinalUi = HiddenTree(),
        LatestJavaScriptLogEvent = js ?? JsLog(),
        LatestUnrelatedLogEvent = unrelated,
        HiddenDialogMaxAgeMs = 2000
    };

    [Fact]
    public void SuccessfulFinalVerification_AccessibleJavaScriptDialog()
    {
        var result = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = EnabledConfig(),
            DetectedHost = "analitikcms.test",
            DetectedJavaScriptLogEvent = JsLog(),
            DetectedAt = T0,
            Now = T0.AddMilliseconds(150),
            ForegroundIsExpectedClaudeWindow = true,
            FinalUi = JsDialog(),
            LatestJavaScriptLogEvent = JsLog(),
            HiddenDialogMaxAgeMs = 2000
        });

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.InvokeAllowOnceButton, result.Method);
        Assert.Equal("analitikcms.test", result.Host);
        Assert.Contains("accessible JavaScript dialog", result.Reason);
    }

    [Fact]
    public void SuccessfulFinalVerification_HiddenDialogWithRecentJavaScriptLog()
    {
        var result = FinalVerification.Verify(HiddenJs());

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
        Assert.Equal("analitikcms.test", result.Host);
        Assert.Contains("recent javascript_tool", result.Reason);
    }

    [Fact]
    public void MatchingJavaScriptEventButForegroundWindowChanged_RefusesCtrlEnter()
    {
        var result = FinalVerification.Verify(HiddenJs(foreground: false));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Equal(ApprovalMethod.None, result.Method);
        Assert.Contains("foreground window is no longer the expected Claude window", result.Reason);
    }

    [Fact]
    public void MatchingJavaScriptEventFollowedByUnrelatedPermissionEvent_RefusesCtrlEnter()
    {
        var result = FinalVerification.Verify(HiddenJs(
            js: JsLog(at: T0),
            unrelated: UnrelatedLog("scroll", T0.AddMilliseconds(80)),
            now: T0.AddMilliseconds(150)));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Equal(ApprovalMethod.None, result.Method);
        Assert.Contains("newer unrelated permission event", result.Reason);
        Assert.Contains("scroll", result.Reason);
    }

    [Fact]
    public void HostChangedBetweenDetectionAndApproval_Refuses()
    {
        var result = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = EnabledConfig(),
            DetectedHost = "analitikcms.test",
            DetectedAt = T0,
            Now = T0.AddMilliseconds(150),
            ForegroundIsExpectedClaudeWindow = true,
            FinalUi = JsDialog("localhost"),
            HiddenDialogMaxAgeMs = 2000
        });

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("host changed from 'analitikcms.test' to 'localhost'", result.Reason);
    }

    [Fact]
    public void DialogTextNoLongerMatches_Refuses()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to permanently delete files in this folder on your computer?\nDeny\nAllow once");

        var result = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = EnabledConfig(),
            DetectedHost = "analitikcms.test",
            DetectedJavaScriptLogEvent = JsLog(),
            DetectedAt = T0,
            Now = T0.AddMilliseconds(150),
            ForegroundIsExpectedClaudeWindow = true,
            FinalUi = ui,
            LatestJavaScriptLogEvent = JsLog(),
            HiddenDialogMaxAgeMs = 2000
        });

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("unrelated permission", result.Reason);
    }

    [Fact]
    public void AccessibleDialogWithoutJavaScriptTitle_Refuses()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to click the submit button?\nDeny\nAllow once");

        var result = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = EnabledConfig(),
            DetectedHost = "analitikcms.test",
            DetectedAt = T0,
            Now = T0.AddMilliseconds(150),
            ForegroundIsExpectedClaudeWindow = true,
            FinalUi = ui,
            LatestJavaScriptLogEvent = JsLog(),
            HiddenDialogMaxAgeMs = 2000
        });

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("dialog text no longer matches JavaScript execution permission", result.Reason);
    }

    [Fact]
    public void PartialPermissionChromeWithoutJavaScriptTitle_UsesRecentJavaScriptLog()
    {
        var ui = PermissionDialogParser.Parse(
            ["Site-level permissions are disabled for this site. You'll be asked for each action."],
            ["Deny", "Allow once"]);

        var result = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = EnabledConfig(),
            DetectedHost = "analitikcms.test",
            DetectedJavaScriptLogEvent = JsLog(),
            DetectedAt = T0,
            Now = T0.AddMilliseconds(150),
            ForegroundIsExpectedClaudeWindow = true,
            FinalUi = ui,
            LatestJavaScriptLogEvent = JsLog(),
            HiddenDialogMaxAgeMs = 2000
        });

        Assert.False(ui.LooksLikeJavaScriptPermission);
        Assert.False(ui.IsIdentifiedNonJavaScriptDialog);
        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
        Assert.Contains("recent javascript_tool", result.Reason);
    }

    [Fact]
    public void StaleLogEventWithoutOutstandingRequest_RefusesCtrlEnter()
    {
        var stale = JsLog(at: T0, id: null);
        var result = FinalVerification.Verify(HiddenJs(
            js: stale,
            now: T0.AddSeconds(5)));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("stale", result.Reason);
    }

    [Fact]
    public void ApprovalCancelledBecauseFinalVerificationFailed()
    {
        var result = FinalVerification.Verify(HiddenJs(foreground: false, now: T0.AddSeconds(8)));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Equal(ApprovalMethod.None, result.Method);
        Assert.StartsWith("Final verification refused:", result.Reason);
    }

    [Fact]
    public void HiddenPathWithoutJavaScriptLog_Refuses()
    {
        var result = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = EnabledConfig(),
            DetectedHost = "analitikcms.test",
            DetectedAt = T0,
            Now = T0.AddMilliseconds(150),
            ForegroundIsExpectedClaudeWindow = true,
            FinalUi = HiddenTree(),
            HiddenDialogMaxAgeMs = 2000
        });

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("no matching javascript_tool log event", result.Reason);
    }

    [Fact]
    public void AccessibleJavaScriptDialogWithoutAllowOnce_Refuses()
    {
        var ui = PermissionDialogParser.Parse(
            ["Allow Claude to execute JavaScript on analitikcms.test?"],
            ["Deny"]);

        var result = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = EnabledConfig(),
            DetectedHost = "analitikcms.test",
            DetectedAt = T0,
            Now = T0.AddMilliseconds(150),
            ForegroundIsExpectedClaudeWindow = true,
            FinalUi = ui,
            HiddenDialogMaxAgeMs = 2000
        });

        Assert.True(ui.LooksLikeJavaScriptPermission);
        Assert.False(ui.HasAllowOnceButton);
        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("not \"Allow once\"", result.Reason);
    }

    [Fact]
    public void OlderUnrelatedEventDoesNotBlockRecentJavaScriptLog()
    {
        var result = FinalVerification.Verify(HiddenJs(
            js: JsLog(at: T0),
            unrelated: UnrelatedLog("computer", T0.AddMilliseconds(-400)),
            now: T0.AddMilliseconds(120)));

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
    }
}

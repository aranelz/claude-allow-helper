using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class HiddenPathFreshnessTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 4, 17, 8, 0, TimeSpan.FromHours(3));

    private static AppConfig EnabledConfig() => new()
    {
        Enabled = true,
        AllowedSites = ["analitikcms.test", "localhost", "127.0.0.1"],
        HiddenDialogMaxAgeMs = 2000
    };

    private static ClaudePermissionLogEvent JsLog(
        DateTimeOffset? logTimestamp = null,
        DateTimeOffset? ingestedAt = null,
        string origin = "http://analitikcms.test",
        string? id = "e108a40e-7127-4ff5-b523-83d9d183e2ac") => new()
    {
        Origin = origin,
        ToolName = "javascript_tool",
        RequestId = id,
        ObservedAt = logTimestamp ?? T0,
        IngestedAt = ingestedAt ?? T0.AddMilliseconds(173)
    };

    private static UiPermissionSnapshot HiddenTree() =>
        PermissionDialogParser.Parse(["Claude"], ["Kapat"]);

    private static FinalVerificationInput Hidden(
        ClaudePermissionLogEvent js,
        DateTimeOffset now,
        bool foreground = true,
        UiPermissionSnapshot? ui = null,
        ClaudePermissionLogEvent? unrelated = null,
        bool answered = false,
        string? detectedHost = "analitikcms.test") => new()
    {
        Config = EnabledConfig(),
        DetectedHost = detectedHost,
        DetectedJavaScriptLogEvent = js,
        DetectedAt = js.IngestedAt,
        Now = now,
        ForegroundIsExpectedClaudeWindow = foreground,
        FinalUi = ui ?? HiddenTree(),
        LatestJavaScriptLogEvent = js,
        LatestUnrelatedLogEvent = unrelated,
        HiddenDialogMaxAgeMs = 2000,
        RequestHasBeenAnswered = answered
    };

    [Fact]
    public void WholeSecondClaudeTimestamp_UsesHelperIngestTimeForFreshness()
    {
        var js = JsLog(logTimestamp: T0, ingestedAt: T0.AddMilliseconds(173));
        var result = FinalVerification.Verify(Hidden(js, now: T0.AddMilliseconds(173 + 1800)));

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
        Assert.Contains("recent javascript_tool", result.Reason);
    }

    [Fact]
    public void SlowUiScanOfOneToTwoSeconds_StillAllowsActiveRequest()
    {
        var ingest = T0.AddMilliseconds(173);
        var js = JsLog(logTimestamp: T0, ingestedAt: ingest);
        var afterSlowScan = ingest.AddMilliseconds(1900);

        var result = FinalVerification.Verify(Hidden(js, now: afterSlowScan));

        Assert.True((afterSlowScan - T0).TotalMilliseconds > 2000);
        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
        Assert.Contains("recent javascript_tool", result.Reason);
    }

    [Fact]
    public void ActiveUnansweredRequestAfterMoreThanTwoSeconds_StillEligible()
    {
        var js = JsLog();
        var result = FinalVerification.Verify(Hidden(js, now: js.IngestedAt.AddSeconds(5)));

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
        Assert.Contains("outstanding unanswered javascript_tool", result.Reason);
    }

    [Fact]
    public void SameRequestBecomesAnswered_Rejects()
    {
        var js = JsLog();
        var result = FinalVerification.Verify(Hidden(
            js,
            now: js.IngestedAt.AddMilliseconds(400),
            answered: true));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("already answered", result.Reason);
    }

    [Fact]
    public void NewerUnrelatedPermission_Rejects()
    {
        var js = JsLog();
        var unrelated = new ClaudePermissionLogEvent
        {
            Origin = "http://analitikcms.test",
            ToolName = "computer",
            RequestId = "req-other",
            ObservedAt = js.ObservedAt.AddSeconds(1),
            IngestedAt = js.IngestedAt.AddSeconds(1)
        };

        var result = FinalVerification.Verify(Hidden(
            js,
            now: js.IngestedAt.AddSeconds(2),
            unrelated: unrelated));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("newer unrelated permission event", result.Reason);
        Assert.Contains("computer", result.Reason);
    }

    [Fact]
    public void ForegroundClaudeWindowChanges_Rejects()
    {
        var js = JsLog();
        var result = FinalVerification.Verify(Hidden(
            js,
            now: js.IngestedAt.AddMilliseconds(400),
            foreground: false));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("foreground window is no longer the expected Claude window", result.Reason);
    }

    [Fact]
    public void IdentifiedNonJavaScriptDialog_Rejects()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to click the submit button?\nDeny\nAllow once");
        var js = JsLog();
        var result = FinalVerification.Verify(Hidden(
            js,
            now: js.IngestedAt.AddMilliseconds(400),
            ui: ui));

        Assert.True(ui.IsIdentifiedNonJavaScriptDialog);
        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("dialog text no longer matches JavaScript execution permission", result.Reason);
    }

    [Fact]
    public void AllowlistedHost_ApprovesHiddenPath()
    {
        var js = JsLog(origin: "http://analitikcms.test");
        var result = FinalVerification.Verify(Hidden(js, now: js.IngestedAt.AddMilliseconds(400)));

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal("analitikcms.test", result.Host);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
    }

    [Fact]
    public void NonAllowlistedHost_Rejects()
    {
        var js = JsLog(origin: "https://github.com");
        var result = FinalVerification.Verify(Hidden(
            js,
            now: js.IngestedAt.AddMilliseconds(400),
            detectedHost: "github.com"));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("not in the allowlist", result.Reason);
    }

    [Fact]
    public void SuccessfulHiddenApproval_SelectsCtrlEnter()
    {
        var js = JsLog();
        var result = FinalVerification.Verify(Hidden(js, now: js.IngestedAt.AddMilliseconds(400)));

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Equal(ApprovalMethod.SendCtrlEnter, result.Method);
        Assert.Equal(PermissionKind.JavaScriptExecution, result.Kind);
    }

    [Fact]
    public void PostApprovalVerification_ConfirmsClaudeRecordedOnce()
    {
        var result = PostApprovalCheck.Inspect(
            HiddenTree(),
            new ClaudePermissionResponseEvent
            {
                RequestId = "e108a40e-7127-4ff5-b523-83d9d183e2ac",
                Decision = "once",
                ToolName = "javascript_tool"
            },
            "analitikcms.test",
            "e108a40e-7127-4ff5-b523-83d9d183e2ac");

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Contains("recorded once", result.Reason);
        Assert.Contains("javascript_tool", result.Reason);
    }

    [Fact]
    public void EventWithoutRequestIdPastFreshnessWindow_IsStale()
    {
        var js = JsLog(id: null);
        var result = FinalVerification.Verify(Hidden(js, now: js.IngestedAt.AddSeconds(5)));

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("stale", result.Reason);
    }
}

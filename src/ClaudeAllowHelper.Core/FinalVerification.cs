namespace ClaudeAllowHelper.Core;

public sealed class FinalVerificationInput
{
    public required AppConfig Config { get; init; }

    public string? DetectedHost { get; init; }

    public ClaudePermissionLogEvent? DetectedJavaScriptLogEvent { get; init; }

    public DateTimeOffset DetectedAt { get; init; }

    public DateTimeOffset Now { get; init; }

    public bool ForegroundIsExpectedClaudeWindow { get; init; }

    public UiPermissionSnapshot? FinalUi { get; init; }

    public ClaudePermissionLogEvent? LatestJavaScriptLogEvent { get; init; }

    public ClaudePermissionLogEvent? LatestUnrelatedLogEvent { get; init; }

    public int HiddenDialogMaxAgeMs { get; init; } = 2000;

    /// <summary>
    /// True when Claude has already recorded a decision for this javascript_tool request id.
    /// </summary>
    public bool RequestHasBeenAnswered { get; init; }
}

public static class FinalVerification
{
    public static ApprovalResult Verify(FinalVerificationInput input)
    {
        if (input.Config is not { Enabled: true })
        {
            return ApprovalResult.Ignore("Final verification refused: helper is turned off.", source: "final");
        }

        if (!input.ForegroundIsExpectedClaudeWindow)
        {
            return ApprovalResult.Ignore(
                "Final verification refused: foreground window is no longer the expected Claude window.",
                input.DetectedHost,
                "final");
        }

        var ui = input.FinalUi;
        if (ui?.LooksLikeUnrelatedPermission == true)
        {
            return ApprovalResult.Ignore(
                $"Final verification refused: visible dialog is an unrelated permission ({ui.UnrelatedKind}).",
                input.DetectedHost,
                "final-ui");
        }

        if (ui is { TreeAppearsIncomplete: false, LooksLikeJavaScriptPermission: true })
        {
            return VerifyAccessibleJavaScriptDialog(input, ui);
        }

        if (ui is { LooksLikeJavaScriptPermission: false, IsIdentifiedNonJavaScriptDialog: true })
        {
            return ApprovalResult.Ignore(
                "Final verification refused: dialog text no longer matches JavaScript execution permission.",
                input.DetectedHost,
                "final-ui");
        }

        return VerifyHiddenChromiumPath(input);
    }

    private static ApprovalResult VerifyAccessibleJavaScriptDialog(FinalVerificationInput input, UiPermissionSnapshot ui)
    {
        if (!ui.ContainsJavaScriptExecutionTitle)
        {
            return ApprovalResult.Ignore(
                "Final verification refused: dialog text does not contain \"Allow Claude to execute JavaScript\".",
                ui.Host,
                "final-ui");
        }

        if (string.IsNullOrWhiteSpace(ui.Host))
        {
            return ApprovalResult.Ignore(
                "Final verification refused: JavaScript dialog host could not be parsed.",
                source: "final-ui");
        }

        if (!SiteAllowList.IsAllowed(ui.Host, input.Config.AllowedSites))
        {
            return ApprovalResult.Ignore(
                $"Final verification refused: host '{ui.Host}' is not in the allowlist.",
                ui.Host,
                "final-ui");
        }

        if (!string.IsNullOrWhiteSpace(input.DetectedHost) &&
            !SiteAllowList.SameHost(input.DetectedHost, ui.Host))
        {
            return ApprovalResult.Ignore(
                $"Final verification refused: host changed from '{input.DetectedHost}' to '{ui.Host}' between detection and approval.",
                ui.Host,
                "final-ui");
        }

        if (!ui.HasAllowOnceButton)
        {
            return ApprovalResult.Ignore(
                "Final verification refused: the accessible action is not \"Allow once\".",
                ui.Host,
                "final-ui");
        }

        return ApprovalResult.Approve(
            $"Final verification passed: accessible JavaScript dialog for allowlisted host '{ui.Host}' with Allow once.",
            ui.Host,
            "final-ui",
            ApprovalMethod.InvokeAllowOnceButton);
    }

    private static ApprovalResult VerifyHiddenChromiumPath(FinalVerificationInput input)
    {
        var js = input.LatestJavaScriptLogEvent ?? input.DetectedJavaScriptLogEvent;
        if (js is null || !js.IsJavaScriptTool)
        {
            return ApprovalResult.Ignore(
                "Final verification refused: Chromium hid the dialog and no matching javascript_tool log event is available.",
                input.DetectedHost,
                "final-log");
        }

        if (input.RequestHasBeenAnswered)
        {
            return ApprovalResult.Ignore(
                "Final verification refused: javascript_tool request was already answered by Claude.",
                input.DetectedHost,
                "final-log");
        }

        string? host = null;
        if (string.IsNullOrWhiteSpace(js.Origin) ||
            !SiteAllowList.TryParseHost(js.Origin, out host, out _) ||
            !SiteAllowList.IsAllowed(js.Origin, input.Config.AllowedSites))
        {
            return ApprovalResult.Ignore(
                $"Final verification refused: log origin '{js.Origin}' is not in the allowlist.",
                host,
                "final-log");
        }

        if (!string.IsNullOrWhiteSpace(input.DetectedHost) &&
            !SiteAllowList.SameHost(input.DetectedHost, js.Origin))
        {
            return ApprovalResult.Ignore(
                $"Final verification refused: host changed from '{input.DetectedHost}' to '{js.Origin}' between detection and approval.",
                host,
                "final-log");
        }

        if (input.DetectedJavaScriptLogEvent is { Origin: not null } detected &&
            !string.IsNullOrWhiteSpace(detected.Origin) &&
            !SiteAllowList.SameHost(detected.Origin, js.Origin))
        {
            return ApprovalResult.Ignore(
                $"Final verification refused: javascript_tool origin changed from '{detected.Origin}' to '{js.Origin}'.",
                host,
                "final-log");
        }

        var unrelated = input.LatestUnrelatedLogEvent;
        if (unrelated is not null && unrelated.FreshnessTimestamp >= js.FreshnessTimestamp)
        {
            return ApprovalResult.Ignore(
                $"Final verification refused: a newer unrelated permission event appeared ({unrelated.ToolName}).",
                host,
                "final-log");
        }

        if (!IsHiddenPathFreshOrStillOutstanding(input, js, out var freshnessReason))
        {
            return ApprovalResult.Ignore(freshnessReason, host, "final-log");
        }

        return ApprovalResult.Approve(
            $"Final verification passed: hidden dialog, {freshnessReason} for allowlisted origin '{js.Origin}', Claude still foreground, no newer unrelated permission.",
            host,
            "final-log",
            ApprovalMethod.SendCtrlEnter,
            PermissionKind.JavaScriptExecution);
    }

    private static bool IsHiddenPathFreshOrStillOutstanding(
        FinalVerificationInput input,
        ClaudePermissionLogEvent js,
        out string reason)
    {
        var age = input.Now - js.FreshnessTimestamp;
        if (age < TimeSpan.Zero)
        {
            reason =
                $"Final verification refused: javascript_tool log event freshness timestamp is in the future ({age.TotalMilliseconds:0} ms).";
            return false;
        }

        if (age <= TimeSpan.FromMilliseconds(input.HiddenDialogMaxAgeMs))
        {
            reason = "recent javascript_tool";
            return true;
        }

        var sameOutstandingRequest = !string.IsNullOrWhiteSpace(js.RequestId) && !input.RequestHasBeenAnswered;
        if (sameOutstandingRequest)
        {
            reason = "outstanding unanswered javascript_tool";
            return true;
        }

        reason =
            $"Final verification refused: javascript_tool log event is stale ({age.TotalMilliseconds:0} ms old) and is not an outstanding unanswered request.";
        return false;
    }
}

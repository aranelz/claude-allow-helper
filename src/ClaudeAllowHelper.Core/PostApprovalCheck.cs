namespace ClaudeAllowHelper.Core;

public static class PostApprovalCheck
{
    public static ApprovalResult Inspect(
        UiPermissionSnapshot? uiAfter,
        ClaudePermissionResponseEvent? matchingResponse,
        string expectedHost,
        string? requestId)
    {
        if (matchingResponse is not null &&
            string.Equals(matchingResponse.ToolName, ApprovalEngine.JavaScriptToolName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(requestId) ||
             string.Equals(matchingResponse.RequestId, requestId, StringComparison.OrdinalIgnoreCase)))
        {
            return ApprovalResult.Approve(
                $"Post-check: Claude recorded {matchingResponse.Decision} for browser:javascript_tool ({matchingResponse.RequestId}).",
                expectedHost,
                "post");
        }

        if (uiAfter?.LooksLikeJavaScriptPermission == true &&
            SiteAllowList.SameHost(uiAfter.Host, expectedHost))
        {
            return ApprovalResult.Ignore(
                "Post-check: JavaScript permission dialog is still visible after the approval attempt.",
                expectedHost,
                "post");
        }

        if (uiAfter?.LooksLikeUnrelatedPermission == true)
        {
            return ApprovalResult.Ignore(
                $"Post-check: an unrelated permission ({uiAfter.UnrelatedKind}) is visible; not sending another keystroke.",
                expectedHost,
                "post");
        }

        if (uiAfter is null || uiAfter.TreeAppearsIncomplete || !uiAfter.LooksLikeJavaScriptPermission)
        {
            return ApprovalResult.Approve(
                "Post-check: JavaScript permission dialog is no longer visible.",
                expectedHost,
                "post");
        }

        return ApprovalResult.Ignore(
            "Post-check: could not confirm that the matching prompt was consumed.",
            expectedHost,
            "post");
    }
}

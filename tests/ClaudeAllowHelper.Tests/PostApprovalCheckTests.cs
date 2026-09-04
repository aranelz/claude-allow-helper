using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class PostApprovalCheckTests
{
    [Fact]
    public void ConfirmsWhenClaudeRecordsJavaScriptOnceResponse()
    {
        var result = PostApprovalCheck.Inspect(
            PermissionDialogParser.Parse(["Claude"], ["Kapat"]),
            new ClaudePermissionResponseEvent
            {
                RequestId = "abc",
                Decision = "once",
                ToolName = "javascript_tool"
            },
            "analitikcms.test",
            "abc");

        Assert.Equal(ApprovalAction.Approve, result.Action);
        Assert.Contains("recorded once", result.Reason);
    }

    [Fact]
    public void WarnsWhenJavaScriptDialogStillVisible()
    {
        var ui = PermissionDialogParser.ParseBlob(
            "Allow Claude to execute JavaScript on analitikcms.test?\nDeny\nAllow once");

        var result = PostApprovalCheck.Inspect(ui, null, "analitikcms.test", "abc");

        Assert.Equal(ApprovalAction.Ignore, result.Action);
        Assert.Contains("still visible", result.Reason);
    }
}

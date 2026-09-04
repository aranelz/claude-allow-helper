using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper.Tests;

public class PermissionDialogParserTests
{
    private const string UserDialog = """
        Allow Claude to execute JavaScript on analitikcms.test?

        Site-level permissions are disabled for this site. You'll be asked for each action.

        document.querySelector('h1').innerText

        Deny   Esc
        Allow once   Ctrl+Enter
        """;

    [Fact]
    public void DetectsExactJavaScriptPermissionDialog()
    {
        var snapshot = PermissionDialogParser.ParseBlob(UserDialog);

        Assert.True(snapshot.LooksLikeJavaScriptPermission);
        Assert.Equal("analitikcms.test", snapshot.Host);
        Assert.True(snapshot.ContainsJavaScriptExecutionTitle);
        Assert.True(snapshot.HasAllowOnceButton);
        Assert.True(snapshot.HasDenyButton);
        Assert.False(snapshot.LooksLikeUnrelatedPermission);
        Assert.False(snapshot.TreeAppearsIncomplete);
    }

    [Fact]
    public void ExtractsHostFromSplitAccessibilityNodes()
    {
        var snapshot = PermissionDialogParser.Parse(
            [
                "Allow Claude to",
                "execute JavaScript on",
                "localhost:3000?"
            ],
            ["Deny", "Allow once"]);

        Assert.True(snapshot.LooksLikeJavaScriptPermission);
        Assert.Equal("localhost:3000", snapshot.Host);
    }

    [Fact]
    public void IgnoresUnrelatedDeleteDialog()
    {
        var snapshot = PermissionDialogParser.ParseBlob(
            """
            Allow Claude to permanently delete files in this folder on your computer?

            Deny
            Allow once
            """);

        Assert.False(snapshot.LooksLikeJavaScriptPermission);
        Assert.True(snapshot.LooksLikeUnrelatedPermission);
        Assert.Equal("delete", snapshot.UnrelatedKind);
    }

    [Fact]
    public void IgnoresBrowserAccessDialog()
    {
        var snapshot = PermissionDialogParser.ParseBlob(
            "Allow Claude to use the browser on example.com?\nDeny\nAllow once");

        Assert.False(snapshot.LooksLikeJavaScriptPermission);
        Assert.True(snapshot.LooksLikeUnrelatedPermission);
        Assert.Equal("browser-access", snapshot.UnrelatedKind);
    }

    [Fact]
    public void IgnoresTerminalDialog()
    {
        var snapshot = PermissionDialogParser.ParseBlob(
            "Allow Claude to run command in powershell?\nDeny\nAllow once");

        Assert.False(snapshot.LooksLikeJavaScriptPermission);
        Assert.True(snapshot.LooksLikeUnrelatedPermission);
        Assert.Equal("terminal", snapshot.UnrelatedKind);
    }

    [Fact]
    public void DoesNotTreatWindowChromeAsAPermissionDialog()
    {
        var snapshot = PermissionDialogParser.Parse(
            ["Claude", "Kapat", "Büyüt"],
            ["Kapat"]);

        Assert.True(snapshot.TreeAppearsIncomplete);
        Assert.False(snapshot.LooksLikeJavaScriptPermission);
        Assert.False(snapshot.LooksLikeUnrelatedPermission);
    }

    [Fact]
    public void PartialSiteLevelChromeWithoutTitleIsNotAnIdentifiedNonJavaScriptDialog()
    {
        var snapshot = PermissionDialogParser.Parse(
            ["Site-level permissions are disabled for this site. You'll be asked for each action."],
            ["Deny", "Allow once"]);

        Assert.False(snapshot.TreeAppearsIncomplete);
        Assert.False(snapshot.LooksLikeJavaScriptPermission);
        Assert.False(snapshot.LooksLikeUnrelatedPermission);
        Assert.False(snapshot.ContainsJavaScriptExecutionTitle);
        Assert.False(snapshot.ContainsAllowClaudeTitle);
        Assert.False(snapshot.IsIdentifiedNonJavaScriptDialog);
        Assert.True(snapshot.HasAllowOnceButton);
    }

    [Fact]
    public void IgnoresJavaScriptPayloadMentionsOfDeleteWhenTitleIsJavaScript()
    {
        var snapshot = PermissionDialogParser.Parse(
            [
                "Allow Claude to execute JavaScript on analitikcms.test?",
                "Site-level permissions are disabled for this site."
            ],
            ["Deny", "Allow once"]);

        Assert.True(snapshot.LooksLikeJavaScriptPermission);
        Assert.False(snapshot.LooksLikeUnrelatedPermission);
    }
}

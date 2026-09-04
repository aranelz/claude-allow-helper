namespace ClaudeAllowHelper.Core;

public sealed class UiPermissionSnapshot
{
    public bool LooksLikeJavaScriptPermission { get; init; }

    public string? Host { get; init; }

    public bool HasAllowOnceButton { get; init; }

    public bool HasDenyButton { get; init; }

    public bool LooksLikeUnrelatedPermission { get; init; }

    public string? UnrelatedKind { get; init; }

    public string Summary { get; init; } = "";

    public bool TreeAppearsIncomplete { get; init; }

    public bool ContainsJavaScriptExecutionTitle { get; init; }

    public bool ContainsAllowClaudeTitle { get; init; }

    public bool LooksLikeSiteAccessPermission { get; init; }

    public bool ContainsSiteAccessTitle { get; init; }

    public PermissionKind Kind =>
        LooksLikeJavaScriptPermission ? PermissionKind.JavaScriptExecution
        : LooksLikeSiteAccessPermission ? PermissionKind.SiteAccess
        : LooksLikeUnrelatedPermission ? PermissionKind.Unrelated
        : PermissionKind.None;

    /// <summary>
    /// True when UI Automation identified a permission prompt that is not JavaScript execution.
    /// Partial Chromium chrome (Allow once / Site-level copy without a title) is not identified.
    /// </summary>
    public bool IsIdentifiedNonJavaScriptDialog =>
        LooksLikeUnrelatedPermission ||
        (ContainsAllowClaudeTitle && !ContainsJavaScriptExecutionTitle && !LooksLikeJavaScriptPermission);
}

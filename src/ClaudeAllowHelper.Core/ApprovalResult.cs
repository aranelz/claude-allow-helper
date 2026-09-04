namespace ClaudeAllowHelper.Core;

public enum ApprovalAction
{
    Approve,
    Ignore
}

public enum ApprovalMethod
{
    None,
    InvokeAllowOnceButton,
    SendCtrlEnter
}

public sealed class ApprovalResult
{
    public required ApprovalAction Action { get; init; }

    public required string Reason { get; init; }

    public string? Host { get; init; }

    public string Source { get; init; } = "";

    public ApprovalMethod Method { get; init; }

    public PermissionKind Kind { get; init; }

    public static ApprovalResult Ignore(string reason, string? host = null, string source = "") => new()
    {
        Action = ApprovalAction.Ignore,
        Reason = reason,
        Host = host,
        Source = source,
        Method = ApprovalMethod.None,
        Kind = PermissionKind.None
    };

    public static ApprovalResult Approve(
        string reason,
        string host,
        string source,
        ApprovalMethod method = ApprovalMethod.None,
        PermissionKind kind = PermissionKind.None) => new()
    {
        Action = ApprovalAction.Approve,
        Reason = reason,
        Host = host,
        Source = source,
        Method = method,
        Kind = kind
    };
}

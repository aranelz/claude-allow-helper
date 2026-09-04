namespace ClaudeAllowHelper.Core;

public sealed class ClaudePermissionLogEvent
{
    public required string Origin { get; init; }

    public required string ToolName { get; init; }

    public string? RequestId { get; init; }

    public string Raw { get; init; } = "";

    public DateTimeOffset ObservedAt { get; init; }

    /// <summary>
    /// Helper-side observation time. Used for hidden-dialog freshness so Claude's
    /// whole-second log timestamp cannot make an event look up to ~999 ms older than it is.
    /// </summary>
    public DateTimeOffset IngestedAt { get; init; }

    public DateTimeOffset FreshnessTimestamp =>
        IngestedAt != default ? IngestedAt : ObservedAt;

    public bool IsJavaScriptTool =>
        AllowedPermissionTypes.FromToolName(ToolName) == PermissionKind.JavaScriptExecution;

    public bool IsSiteAccessTool =>
        AllowedPermissionTypes.FromToolName(ToolName) == PermissionKind.SiteAccess;

    public PermissionKind Kind => AllowedPermissionTypes.FromToolName(ToolName);
}

public sealed class ClaudePermissionResponseEvent
{
    public required string RequestId { get; init; }

    public required string Decision { get; init; }

    public required string ToolName { get; init; }

    public DateTimeOffset ObservedAt { get; init; }
}

public sealed class ClaudeLogBatch
{
    public List<ClaudePermissionLogEvent> Permissions { get; init; } = [];

    public List<ClaudePermissionResponseEvent> Responses { get; init; } = [];
}

namespace ClaudeAllowHelper.Core;

public enum PermissionKind
{
    None,
    JavaScriptExecution,
    SiteAccess,
    Unrelated
}

public static class AllowedPermissionTypes
{
    public const string JavaScriptTool = "javascript_tool";
    public const string OpenSiteTool = "open_site";

    public static PermissionKind FromToolName(string? toolName)
    {
        if (string.Equals(toolName, JavaScriptTool, StringComparison.OrdinalIgnoreCase))
        {
            return PermissionKind.JavaScriptExecution;
        }

        if (string.Equals(toolName, OpenSiteTool, StringComparison.OrdinalIgnoreCase))
        {
            return PermissionKind.SiteAccess;
        }

        return PermissionKind.Unrelated;
    }

    public static bool IsAutoApprovable(string? toolName) =>
        FromToolName(toolName) is PermissionKind.JavaScriptExecution or PermissionKind.SiteAccess;
}

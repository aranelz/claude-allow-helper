namespace ClaudeAllowHelper.Core;

public static class ApprovalEngine
{
    public const string JavaScriptToolName = AllowedPermissionTypes.JavaScriptTool;

    public const string OpenSiteToolName = AllowedPermissionTypes.OpenSiteTool;

    public static ApprovalResult Decide(AppConfig? config, UiPermissionSnapshot? ui, ClaudePermissionLogEvent? logEvent)
    {
        if (config is null)
        {
            return ApprovalResult.Ignore("No configuration is loaded.", source: "config");
        }

        if (!config.Enabled)
        {
            return ApprovalResult.Ignore("Helper is turned off.", source: "toggle");
        }

        if (ui?.LooksLikeUnrelatedPermission == true)
        {
            return ApprovalResult.Ignore(
                $"Unrelated Claude permission left untouched ({ui.UnrelatedKind}).",
                source: "ui");
        }

        if (ui?.LooksLikeJavaScriptPermission == true)
        {
            if (string.IsNullOrWhiteSpace(ui.Host))
            {
                return ApprovalResult.Ignore("JavaScript dialog was detected but no host could be parsed.", source: "ui");
            }

            if (!SiteAllowList.IsAllowed(ui.Host, config.AllowedSites))
            {
                return ApprovalResult.Ignore(
                    $"JavaScript dialog host '{ui.Host}' is not in the allowlist.",
                    ui.Host,
                    "ui");
            }

            return ApprovalResult.Approve(
                $"UI matched JavaScript permission for allowlisted host '{ui.Host}'.",
                ui.Host,
                "ui");
        }

        if (logEvent is not null)
        {
            if (!string.Equals(logEvent.ToolName, JavaScriptToolName, StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalResult.Ignore(
                    $"Claude log tool '{logEvent.ToolName}' is not JavaScript execution.",
                    source: "log");
            }

            if (!SiteAllowList.TryParseHost(logEvent.Origin, out var host, out _) || string.IsNullOrWhiteSpace(host))
            {
                return ApprovalResult.Ignore(
                    $"Claude log origin '{logEvent.Origin}' could not be parsed.",
                    source: "log");
            }

            if (!SiteAllowList.IsAllowed(logEvent.Origin, config.AllowedSites))
            {
                return ApprovalResult.Ignore(
                    $"Claude log origin '{logEvent.Origin}' is not in the allowlist.",
                    host,
                    "log");
            }

            if (ui is { LooksLikeJavaScriptPermission: false, IsIdentifiedNonJavaScriptDialog: true })
            {
                return ApprovalResult.Ignore(
                    $"Claude log requested JavaScript, but a different visible dialog was present ({ui.Summary}).",
                    host,
                    "log+ui");
            }

            return ApprovalResult.Approve(
                $"Claude log requested javascript_tool for allowlisted origin '{logEvent.Origin}'.",
                host,
                "log");
        }

        return ApprovalResult.Ignore("No matching JavaScript permission dialog was found.", source: "none");
    }
}

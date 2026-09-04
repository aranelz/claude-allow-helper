using System.Text.RegularExpressions;

namespace ClaudeAllowHelper.Core;

public static partial class PermissionDialogParser
{
    [GeneratedRegex(
        @"Allow\s+Claude\s+to\s+execute\s+JavaScript\s+on\s+(?<host>[A-Za-z0-9._~\-:\[\]]+)\s*\??",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaScriptTitleRegex();

    [GeneratedRegex(
        @"execute\s+JavaScript\s+on\s+(?<host>[A-Za-z0-9._~\-:\[\]]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaScriptActionRegex();

    private static readonly (string Needle, string Kind)[] UnrelatedNeedles =
    [
        ("permanently delete", "delete"),
        ("change files", "filesystem"),
        ("use your computer", "computer"),
        ("run a dynamic workflow", "workflow"),
        ("execute code on a server", "server-code"),
        ("use the browser on", "browser-access"),
        ("read page content", "read-page"),
        ("fetch a page", "fetch"),
        ("fetch pages", "fetch"),
        ("bash", "terminal"),
        ("powershell", "terminal"),
        ("cmd.exe", "terminal"),
        ("terminal command", "terminal"),
        ("run command", "terminal"),
        ("download file", "download"),
        ("delete files", "delete")
    ];

    public static UiPermissionSnapshot Parse(IEnumerable<string>? shortTexts, IEnumerable<string>? buttonNames)
    {
        var texts = Normalize(shortTexts);
        var buttons = Normalize(buttonNames);
        var joined = string.Join("\n", texts);

        var hasAllowOnce = ContainsButton(buttons, "allow once") || ContainsButton(texts, "allow once");
        var hasDeny = ContainsButton(buttons, "deny") || HasStandaloneDeny(texts);
        var host = ExtractJavaScriptHost(joined);
        var containsJsTitle = ContainsJavaScriptExecutionTitle(joined);
        var containsAllowClaudeTitle = ContainsAllowClaudeTitle(joined);
        var looksLikeJs = host is not null && containsJsTitle;
        var unrelatedKind = looksLikeJs ? null : DetectUnrelated(texts);
        var hasPermissionChrome = HasPermissionChrome(texts, buttons, looksLikeJs, unrelatedKind is not null);

        if (!hasPermissionChrome)
        {
            return new UiPermissionSnapshot
            {
                TreeAppearsIncomplete = true,
                HasAllowOnceButton = hasAllowOnce,
                HasDenyButton = hasDeny,
                ContainsJavaScriptExecutionTitle = containsJsTitle,
                ContainsAllowClaudeTitle = containsAllowClaudeTitle,
                Summary = "Accessibility tree did not expose a Claude permission dialog."
            };
        }

        return new UiPermissionSnapshot
        {
            LooksLikeJavaScriptPermission = looksLikeJs,
            Host = host,
            HasAllowOnceButton = hasAllowOnce,
            HasDenyButton = hasDeny,
            LooksLikeUnrelatedPermission = unrelatedKind is not null,
            UnrelatedKind = unrelatedKind,
            TreeAppearsIncomplete = false,
            ContainsJavaScriptExecutionTitle = containsJsTitle,
            ContainsAllowClaudeTitle = containsAllowClaudeTitle,
            Summary = BuildSummary(looksLikeJs, host, hasAllowOnce, hasDeny, unrelatedKind, joined, string.Join(", ", buttons))
        };
    }

    public static UiPermissionSnapshot ParseBlob(string? blob)
    {
        if (string.IsNullOrWhiteSpace(blob))
        {
            return Parse([], []);
        }

        var lines = blob
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var buttons = lines
            .Where(line =>
                StartsWithButton(line, "Allow once") ||
                StartsWithButton(line, "Deny"))
            .ToList();

        var texts = lines.Where(line => line.Length <= 280).ToList();
        return Parse(texts, buttons);
    }

    private static bool StartsWithButton(string line, string name)
    {
        var trimmed = line.Trim();
        return trimmed.Equals(name, StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith(name + "\t", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractJavaScriptHost(string joined)
    {
        var collapsed = CollapseWhitespace(joined);
        var title = JavaScriptTitleRegex().Match(collapsed);
        if (title.Success)
        {
            return title.Groups["host"].Value.Trim().TrimEnd('?', '.', ',');
        }

        var action = JavaScriptActionRegex().Match(collapsed);
        if (action.Success && collapsed.Contains("Allow Claude", StringComparison.OrdinalIgnoreCase))
        {
            return action.Groups["host"].Value.Trim().TrimEnd('?', '.', ',');
        }

        return null;
    }

    public static bool ContainsJavaScriptExecutionTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return CollapseWhitespace(text)
            .Contains("Allow Claude to execute JavaScript", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsAllowClaudeTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return CollapseWhitespace(text)
            .Contains("Allow Claude to", StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string text) =>
        JavaScriptWhitespaceRegex().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex JavaScriptWhitespaceRegex();

    private static string? DetectUnrelated(IReadOnlyList<string> texts)
    {
        foreach (var text in texts)
        {
            if (text.Contains("execute JavaScript", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lower = text.ToLowerInvariant();
            foreach (var (needle, kind) in UnrelatedNeedles)
            {
                if (lower.Contains(needle, StringComparison.Ordinal))
                {
                    return kind;
                }
            }
        }

        return null;
    }

    private static bool HasPermissionChrome(
        IReadOnlyList<string> texts,
        IReadOnlyList<string> buttons,
        bool looksLikeJs,
        bool looksUnrelated)
    {
        if (looksLikeJs || looksUnrelated)
        {
            return true;
        }

        foreach (var value in texts.Concat(buttons))
        {
            if (value.Contains("Allow Claude", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Allow once", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Site-level permissions", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("execute JavaScript", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStandaloneDeny(IEnumerable<string> texts) =>
        texts.Any(text =>
        {
            var trimmed = text.Trim();
            return trimmed.Equals("Deny", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("Deny ", StringComparison.OrdinalIgnoreCase);
        });

    private static bool ContainsButton(IEnumerable<string> values, string needle) =>
        values.Any(value => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static List<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Where(value => value.Length <= 280)
        .ToList();

    private static string BuildSummary(
        bool looksLikeJs,
        string? host,
        bool hasAllowOnce,
        bool hasDeny,
        string? unrelatedKind,
        string joined,
        string buttons)
    {
        if (looksLikeJs)
        {
            return $"JavaScript permission for '{host}'. Allow once={hasAllowOnce}, Deny={hasDeny}.";
        }

        if (unrelatedKind is not null)
        {
            return $"Unrelated permission kind '{unrelatedKind}'. Buttons: {buttons}";
        }

        var preview = joined.Length > 160 ? joined[..160] + "..." : joined;
        return string.IsNullOrWhiteSpace(preview)
            ? "No permission dialog text matched."
            : "Unmatched permission dialog text: " + preview;
    }
}

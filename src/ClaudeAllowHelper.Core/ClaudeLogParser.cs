using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeAllowHelper.Core;

public sealed partial class ClaudeLogParser
{
    [GeneratedRegex(@"origin:\s*'(?<origin>[^']+)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginRegex();

    [GeneratedRegex(@"toolName:\s*'(?<tool>[^']+)'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ToolNameRegex();

    [GeneratedRegex(
        @"Emitted tool permission request\s+(?<id>[0-9a-f-]+)\s+for\s+browser:(?<tool>[A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmittedRequestRegex();

    [GeneratedRegex(
        @"Received permission response for\s+(?<id>[0-9a-f-]+):\s*(?<decision>\w+)\s*\(tool:\s*browser:(?<tool>[A-Za-z0-9_]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResponseRegex();

    [GeneratedRegex(@"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();

    private string _carry = "";
    private bool _inApprovalBlock;
    private string? _origin;
    private string? _tool;
    private DateTimeOffset? _blockObservedAt;
    private ClaudePermissionLogEvent? _lastEvent;

    public ClaudeLogBatch Push(string? chunk, DateTimeOffset? observedAt = null)
    {
        var batch = new ClaudeLogBatch();
        if (string.IsNullOrEmpty(chunk))
        {
            return batch;
        }

        var now = observedAt ?? DateTimeOffset.Now;
        var text = _carry + chunk;
        _carry = "";
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        if (!normalized.EndsWith('\n'))
        {
            _carry = lines[^1];
            lines = lines.Take(lines.Length - 1).ToArray();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var lineAt = ParseTimestamp(trimmed) ?? now;

            if (IsCat3ApprovalStart(trimmed))
            {
                _inApprovalBlock = true;
                _origin = null;
                _tool = null;
                _blockObservedAt = lineAt;
            }

            if (_inApprovalBlock)
            {
                var originMatch = OriginRegex().Match(trimmed);
                if (originMatch.Success)
                {
                    _origin = originMatch.Groups["origin"].Value;
                }

                var toolMatch = ToolNameRegex().Match(trimmed);
                if (toolMatch.Success)
                {
                    _tool = toolMatch.Groups["tool"].Value;
                }

                if (_origin is not null && _tool is not null)
                {
                    var evt = new ClaudePermissionLogEvent
                    {
                        Origin = _origin,
                        ToolName = _tool,
                        Raw = $"origin={_origin}; tool={_tool}",
                        ObservedAt = _blockObservedAt ?? lineAt,
                        IngestedAt = now
                    };
                    batch.Permissions.Add(evt);
                    _lastEvent = evt;
                    _inApprovalBlock = false;
                    _origin = null;
                    _tool = null;
                    _blockObservedAt = null;
                }
            }

            var emitted = EmittedRequestRegex().Match(trimmed);
            if (emitted.Success)
            {
                var tool = emitted.Groups["tool"].Value;
                var id = emitted.Groups["id"].Value;
                if (_lastEvent is not null &&
                    _lastEvent.RequestId is null &&
                    string.Equals(_lastEvent.ToolName, tool, StringComparison.OrdinalIgnoreCase))
                {
                    _lastEvent = WithRequestId(_lastEvent, id);
                    if (batch.Permissions.Count > 0)
                    {
                        batch.Permissions[^1] = _lastEvent;
                    }
                    else
                    {
                        batch.Permissions.Add(_lastEvent);
                    }
                }
                else if (_lastEvent is null ||
                         !string.Equals(_lastEvent.RequestId, id, StringComparison.OrdinalIgnoreCase))
                {
                    var evt = new ClaudePermissionLogEvent
                    {
                        Origin = _lastEvent?.Origin ?? "",
                        ToolName = tool,
                        RequestId = id,
                        Raw = trimmed,
                        ObservedAt = lineAt,
                        IngestedAt = now
                    };
                    batch.Permissions.Add(evt);
                    _lastEvent = evt;
                }
            }

            var response = ResponseRegex().Match(trimmed);
            if (response.Success)
            {
                batch.Responses.Add(new ClaudePermissionResponseEvent
                {
                    RequestId = response.Groups["id"].Value,
                    Decision = response.Groups["decision"].Value,
                    ToolName = response.Groups["tool"].Value,
                    ObservedAt = lineAt
                });
            }
        }

        return batch;
    }

    private static bool IsCat3ApprovalStart(string line) =>
        line.Contains("cat3 per-action approval requested", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("cat3 per-action read approval requested", StringComparison.OrdinalIgnoreCase);

    private static ClaudePermissionLogEvent WithRequestId(ClaudePermissionLogEvent evt, string id) => new()
    {
        Origin = evt.Origin,
        ToolName = evt.ToolName,
        RequestId = id,
        Raw = evt.Raw,
        ObservedAt = evt.ObservedAt,
        IngestedAt = evt.IngestedAt
    };

    private static DateTimeOffset? ParseTimestamp(string line)
    {
        var match = TimestampRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        if (DateTime.TryParseExact(
                match.Groups["ts"].Value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var parsed))
        {
            return new DateTimeOffset(parsed);
        }

        return null;
    }
}

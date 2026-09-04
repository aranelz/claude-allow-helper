using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper;

internal sealed class PermissionWatcher : IDisposable
{
    private readonly ConfigStore _store;
    private readonly FileLogger _logger;
    private readonly UiAutomationScanner _scanner = new();
    private readonly ClaudeLogParser _logParser = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly HashSet<string> _handledKeys = [];
    private readonly Queue<string> _handledOrder = new();
    private readonly List<ClaudePermissionResponseEvent> _recentResponses = [];

    private FileTailer? _tailer;
    private string? _logPath;
    private ClaudePermissionLogEvent? _pendingJsLog;
    private ClaudePermissionLogEvent? _pendingUnrelatedLog;
    private PendingApproval? _pendingApproval;
    private PendingPostCheck? _postCheck;
    private DateTimeOffset _lastApproveAt = DateTimeOffset.MinValue;
    private string? _lastIgnoreSignature;
    private bool _enabled;
    private bool _disposed;

    public event EventHandler<bool>? EnabledChanged;

    public PermissionWatcher(ConfigStore store, FileLogger logger)
    {
        _store = store;
        _logger = logger;
        _timer.Interval = 500;
        _timer.Tick += (_, _) => Tick();
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            var config = CurrentConfig();
            config.Enabled = value;
            _store.Save(config);
            if (!value)
            {
                _pendingApproval = null;
                _postCheck = null;
            }

            _logger.Info(value ? "Helper enabled." : "Helper disabled.");
            EnabledChanged?.Invoke(this, value);
        }
    }

    public void Start()
    {
        var loaded = _store.Load();
        if (!loaded.IsValid)
        {
            _logger.Error("Configuration is invalid. Auto-approve is disabled. " + loaded.Error);
            _enabled = false;
        }
        else
        {
            _enabled = loaded.Config.Enabled;
            _timer.Interval = loaded.Config.PollIntervalMs;
        }

        _logPath = ClaudeLogLocator.FindMainLog();
        if (_logPath is null)
        {
            _logger.Warn("Claude main.log was not found. UI Automation-only detection will be used.");
        }
        else
        {
            _logger.Info("Watching Claude log: " + _logPath);
            _tailer = new FileTailer(_logPath);
            _ = _tailer.ReadNewText();
        }

        _timer.Start();
        _logger.Info($"Watcher started. Enabled={_enabled}.");
    }

    public AppConfig CurrentConfig()
    {
        var loaded = _store.Load();
        return loaded.Config;
    }

    public string DumpUi() => _scanner.DumpTree();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _scanner.Dispose();
    }

    private void Tick()
    {
        try
        {
            var loaded = _store.Load();
            if (!loaded.IsValid)
            {
                if (_enabled)
                {
                    _enabled = false;
                    _pendingApproval = null;
                    _logger.Error("Config became invalid; emergency disable. " + loaded.Error);
                    EnabledChanged?.Invoke(this, false);
                }

                return;
            }

            _enabled = loaded.Config.Enabled;
            DrainLog();

            if (!_enabled)
            {
                _pendingJsLog = null;
                _pendingUnrelatedLog = null;
                _pendingApproval = null;
                return;
            }

            if (_postCheck is not null)
            {
                RunPostCheck();
            }

            var waiting = _pendingApproval is not null &&
                          DateTimeOffset.UtcNow < _pendingApproval.ReadyAt;
            if (!waiting)
            {
                _timer.Interval = loaded.Config.PollIntervalMs;
            }

            if (waiting)
            {
                return;
            }

            if (_pendingApproval is not null)
            {
                RunFinalVerification(loaded.Config);
                return;
            }

            var scan = _scanner.Scan(_logger);
            var logEvent = FreshJavaScriptLog();
            var decision = ApprovalEngine.Decide(loaded.Config, scan.Snapshot, logEvent);

            if (decision.Action == ApprovalAction.Ignore)
            {
                LogIgnore(decision, scan.Snapshot, logEvent);
                return;
            }

            var key = logEvent?.RequestId ?? $"{decision.Host}:{DateTime.UtcNow:yyyyMMddHHmmss}";
            if (IsHandled(key) || RecentlyApproved())
            {
                return;
            }

            var debounce = loaded.Config.SafetyDebounceMs;
            _pendingApproval = new PendingApproval
            {
                Host = decision.Host,
                LogEvent = logEvent,
                DetectedAt = DateTimeOffset.UtcNow,
                WindowHandle = scan.WindowHandle != IntPtr.Zero ? scan.WindowHandle : KeyboardApprover.FindClaudeMainWindow(),
                ReadyAt = DateTimeOffset.UtcNow.AddMilliseconds(debounce),
                Key = key
            };
            _timer.Interval = debounce;
            _logger.Info($"Detected candidate for '{decision.Host}'. Waiting {debounce} ms before final verification. source={decision.Source}");
        }
        catch (Exception ex)
        {
            _logger.Error("Watcher tick failed: " + ex);
        }
    }

    private void RunFinalVerification(AppConfig config)
    {
        var pending = _pendingApproval;
        _pendingApproval = null;
        if (pending is null)
        {
            return;
        }

        DrainLog();
        var now = DateTimeOffset.UtcNow;
        var expectedWindow = pending.WindowHandle != IntPtr.Zero
            ? pending.WindowHandle
            : KeyboardApprover.FindClaudeMainWindow();
        var foregroundOk = KeyboardApprover.IsExpectedForeground(expectedWindow);
        var js = FreshJavaScriptLog() ?? pending.LogEvent;
        var requestId = js?.RequestId ?? pending.LogEvent?.RequestId;
        if (requestId is not null && HasAnsweredJavaScriptRequest(requestId))
        {
            Remember(requestId);
            if (_pendingJsLog?.RequestId is { } pendingId &&
                string.Equals(pendingId, requestId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingJsLog = null;
            }

            _logger.Info($"Dropping javascript_tool candidate; Claude already recorded a decision for {requestId}.");
            return;
        }

        var scan = _scanner.Scan(_logger);
        var unrelated = FreshUnrelatedLog();

        var verification = FinalVerification.Verify(new FinalVerificationInput
        {
            Config = config,
            DetectedHost = pending.Host,
            DetectedJavaScriptLogEvent = pending.LogEvent,
            DetectedAt = pending.DetectedAt,
            Now = now,
            ForegroundIsExpectedClaudeWindow = foregroundOk,
            FinalUi = scan.Snapshot,
            LatestJavaScriptLogEvent = js,
            LatestUnrelatedLogEvent = unrelated,
            HiddenDialogMaxAgeMs = config.HiddenDialogMaxAgeMs,
            RequestHasBeenAnswered = requestId is not null && HasAnsweredJavaScriptRequest(requestId)
        });

        if (verification.Action != ApprovalAction.Approve)
        {
            _logger.Warn($"Approval cancelled: {verification.Reason} source={verification.Source} host={verification.Host ?? pending.Host ?? "-"}");
            return;
        }

        if (IsHandled(pending.Key) || RecentlyApproved())
        {
            _logger.Info("Final verification passed, but this prompt was already handled.");
            return;
        }

        _logger.Info($"Final verification passed: {verification.Reason}");

        var invoked = false;
        if (verification.Method == ApprovalMethod.InvokeAllowOnceButton)
        {
            invoked = _scanner.TryInvokeAllowOnce(scan.AllowOnceButton, _logger);
            if (!invoked && KeyboardApprover.IsExpectedForeground(expectedWindow))
            {
                _logger.Info("Allow once invoke failed; sending Ctrl+Enter only because the accessible JavaScript dialog was re-verified.");
                invoked = KeyboardApprover.SendAllowOnce(expectedWindow, _logger);
            }
        }
        else if (verification.Method == ApprovalMethod.SendCtrlEnter)
        {
            invoked = KeyboardApprover.SendAllowOnce(expectedWindow, _logger);
        }
        else
        {
            _logger.Warn("Final verification approved but no safe approval method was selected. Ctrl+Enter was not sent.");
            return;
        }

        if (!invoked)
        {
            _logger.Warn("Final verification passed, but the approval action failed. Ctrl+Enter was not retried.");
            return;
        }

        Remember(pending.Key);
        _lastApproveAt = DateTimeOffset.UtcNow;
        _pendingJsLog = null;
        _postCheck = new PendingPostCheck
        {
            Host = verification.Host ?? pending.Host ?? "",
            RequestId = pending.LogEvent?.RequestId ?? js?.RequestId,
            StartedAt = DateTimeOffset.UtcNow
        };
    }

    private void RunPostCheck()
    {
        var pending = _postCheck;
        if (pending is null)
        {
            return;
        }

        DrainLog();
        var scan = _scanner.Scan(_logger);
        var response = _recentResponses.LastOrDefault(item =>
            string.Equals(item.ToolName, ApprovalEngine.JavaScriptToolName, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(pending.RequestId) ||
             string.Equals(item.RequestId, pending.RequestId, StringComparison.OrdinalIgnoreCase)));

        var result = PostApprovalCheck.Inspect(scan.Snapshot, response, pending.Host, pending.RequestId);
        if (result.Action == ApprovalAction.Approve)
        {
            _logger.Info(result.Reason);
            _postCheck = null;
            return;
        }

        if (DateTimeOffset.UtcNow - pending.StartedAt > TimeSpan.FromSeconds(2))
        {
            _logger.Warn(result.Reason);
            _postCheck = null;
        }
    }

    private void DrainLog()
    {
        if (_tailer is null)
        {
            if (_logPath is null)
            {
                _logPath = ClaudeLogLocator.FindMainLog();
                if (_logPath is not null)
                {
                    _logger.Info("Found Claude log: " + _logPath);
                    _tailer = new FileTailer(_logPath);
                    _ = _tailer.ReadNewText();
                }
            }

            return;
        }

        var chunk = _tailer.ReadNewText();
        if (chunk is null)
        {
            return;
        }

        var batch = _logParser.Push(chunk, DateTimeOffset.UtcNow);
        foreach (var evt in batch.Permissions)
        {
            _logger.Info($"Claude permission log: tool={evt.ToolName} origin={evt.Origin} id={evt.RequestId ?? "-"}");
            if (evt.IsJavaScriptTool)
            {
                _pendingJsLog = evt;
            }
            else
            {
                _pendingUnrelatedLog = evt;
            }
        }

        foreach (var response in batch.Responses)
        {
            _recentResponses.Add(response);
            if (_recentResponses.Count > 20)
            {
                _recentResponses.RemoveAt(0);
            }

            _logger.Info($"Claude permission response: {response.Decision} tool={response.ToolName} id={response.RequestId}");
            InvalidateAnsweredJavaScriptRequest(response);
        }
    }

    private void InvalidateAnsweredJavaScriptRequest(ClaudePermissionResponseEvent response)
    {
        if (!string.Equals(response.ToolName, ApprovalEngine.JavaScriptToolName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Remember(response.RequestId);
        if (_pendingJsLog?.RequestId is { } pendingId &&
            string.Equals(pendingId, response.RequestId, StringComparison.OrdinalIgnoreCase))
        {
            _pendingJsLog = null;
        }

        if (_pendingApproval?.LogEvent?.RequestId is { } candidateId &&
            string.Equals(candidateId, response.RequestId, StringComparison.OrdinalIgnoreCase))
        {
            _pendingApproval = null;
        }
    }

    private bool HasAnsweredJavaScriptRequest(string requestId) =>
        _recentResponses.Any(item =>
            string.Equals(item.RequestId, requestId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ToolName, ApprovalEngine.JavaScriptToolName, StringComparison.OrdinalIgnoreCase));

    private ClaudePermissionLogEvent? FreshJavaScriptLog()
    {
        if (_pendingJsLog is null)
        {
            return null;
        }

        if (_pendingJsLog.RequestId is not null &&
            (IsHandled(_pendingJsLog.RequestId) || HasAnsweredJavaScriptRequest(_pendingJsLog.RequestId)))
        {
            return null;
        }

        return _pendingJsLog;
    }

    private ClaudePermissionLogEvent? FreshUnrelatedLog() => _pendingUnrelatedLog;

    private void LogIgnore(ApprovalResult decision, UiPermissionSnapshot snapshot, ClaudePermissionLogEvent? logEvent)
    {
        var interesting = snapshot.LooksLikeJavaScriptPermission ||
                          snapshot.LooksLikeUnrelatedPermission ||
                          logEvent is not null;
        if (!interesting)
        {
            return;
        }

        var signature = $"{decision.Reason}|{decision.Host}|{logEvent?.RequestId}";
        if (!string.Equals(signature, _lastIgnoreSignature, StringComparison.Ordinal))
        {
            _logger.Info($"Ignored: {decision.Reason} source={decision.Source} host={decision.Host ?? "-"} ui={snapshot.Summary}");
            _lastIgnoreSignature = signature;
        }

        if (logEvent is not null &&
            (decision.Reason.Contains("not in the allowlist", StringComparison.OrdinalIgnoreCase) ||
             decision.Reason.Contains("not JavaScript", StringComparison.OrdinalIgnoreCase) ||
             decision.Reason.Contains("Unrelated", StringComparison.OrdinalIgnoreCase)))
        {
            Remember(logEvent.RequestId ?? logEvent.Raw);
        }
    }

    private bool RecentlyApproved() => DateTimeOffset.UtcNow - _lastApproveAt < TimeSpan.FromMilliseconds(700);

    private bool IsHandled(string key) => _handledKeys.Contains(key);

    private void Remember(string key)
    {
        if (!_handledKeys.Add(key))
        {
            return;
        }

        _handledOrder.Enqueue(key);
        while (_handledOrder.Count > 80)
        {
            _handledKeys.Remove(_handledOrder.Dequeue());
        }
    }

    private sealed class PendingApproval
    {
        public string? Host { get; init; }
        public ClaudePermissionLogEvent? LogEvent { get; init; }
        public DateTimeOffset DetectedAt { get; init; }
        public IntPtr WindowHandle { get; init; }
        public DateTimeOffset ReadyAt { get; init; }
        public required string Key { get; init; }
    }

    private sealed class PendingPostCheck
    {
        public required string Host { get; init; }
        public string? RequestId { get; init; }
        public DateTimeOffset StartedAt { get; init; }
    }
}

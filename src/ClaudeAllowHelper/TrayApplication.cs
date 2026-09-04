using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper;

internal sealed class TrayApplication : ApplicationContext
{
    private const int EmergencyHotkeyId = 1;

    private readonly ConfigStore _store;
    private readonly FileLogger _logger;
    private readonly PermissionWatcher _watcher;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly HotKeyWindow _hotKeys = new();
    private Icon? _onIcon;
    private Icon? _offIcon;
    private AllowedSitesForm? _sitesForm;
    private LogViewerForm? _logForm;

    public TrayApplication(ConfigStore store, FileLogger logger, PermissionWatcher watcher)
    {
        _store = store;
        _logger = logger;
        _watcher = watcher;

        _onIcon = CreateIcon(true);
        _offIcon = CreateIcon(false);

        _enabledItem = new ToolStripMenuItem("Enabled") { CheckOnClick = true };
        _enabledItem.Click += (_, _) => _watcher.Enabled = _enabledItem.Checked;

        var sitesItem = new ToolStripMenuItem("Allowed Sites…");
        sitesItem.Click += (_, _) => ShowSites();

        var logItem = new ToolStripMenuItem("View Log…");
        logItem.Click += (_, _) => ShowLog();

        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startupItem.Click += (_, _) =>
        {
            StartupManager.SetEnabled(_startupItem.Checked);
            var config = _watcher.CurrentConfig();
            config.StartWithWindows = _startupItem.Checked;
            _store.Save(config);
            _logger.Info("Start with Windows: " + _startupItem.Checked);
        };

        var offItem = new ToolStripMenuItem("Emergency Off");
        offItem.Click += (_, _) => EmergencyOff("tray menu");

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(sitesItem);
        menu.Items.Add(logItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(offItem);
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = _offIcon,
            Visible = true,
            ContextMenuStrip = menu,
            Text = "Claude Allow Helper"
        };
        _tray.DoubleClick += (_, _) => _watcher.Enabled = !_watcher.Enabled;

        _watcher.EnabledChanged += (_, enabled) => BeginInvokeUi(() => SyncMenu(enabled));
        _watcher.Start();
        SyncMenu(_watcher.Enabled);
        _startupItem.Checked = StartupManager.IsEnabled();

        _hotKeys.Create();
        _hotKeys.EmergencyOff += () => EmergencyOff("Ctrl+Alt+Shift+Q");
        if (!NativeMethods.RegisterHotKey(_hotKeys.Handle, EmergencyHotkeyId,
                NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift, NativeMethods.KeyQ))
        {
            _logger.Warn("Could not register emergency hotkey Ctrl+Alt+Shift+Q.");
        }
        else
        {
            _logger.Info("Emergency off hotkey: Ctrl+Alt+Shift+Q");
        }

        _tray.BalloonTipTitle = "Claude Allow Helper";
        _tray.BalloonTipText = _watcher.Enabled
            ? "Enabled. JavaScript prompts for allowlisted local sites will be approved. Emergency off: Ctrl+Alt+Shift+Q"
            : "Running, currently OFF. Right-click the tray icon and enable it when you want auto-approve.";
        _tray.ShowBalloonTip(4000);
    }

    protected override void ExitThreadCore()
    {
        NativeMethods.UnregisterHotKey(_hotKeys.Handle, EmergencyHotkeyId);
        _hotKeys.DestroyHandle();
        _watcher.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _onIcon?.Dispose();
        _offIcon?.Dispose();
        base.ExitThreadCore();
    }

    private void EmergencyOff(string source)
    {
        _logger.Warn("Emergency OFF from " + source);
        _watcher.Enabled = false;
        BeginInvokeUi(() =>
        {
            _tray.BalloonTipTitle = "Claude Allow Helper";
            _tray.BalloonTipText = "Emergency off. Auto-approve is disabled.";
            _tray.ShowBalloonTip(3000);
        });
    }

    private void ShowSites()
    {
        if (_sitesForm is { Visible: true })
        {
            _sitesForm.Activate();
            return;
        }

        _sitesForm = new AllowedSitesForm(_store, _logger);
        _sitesForm.FormClosed += (_, _) => _sitesForm = null;
        _sitesForm.Show();
    }

    private void ShowLog()
    {
        if (_logForm is { Visible: true })
        {
            _logForm.Activate();
            return;
        }

        _logForm = new LogViewerForm(_logger);
        _logForm.FormClosed += (_, _) => _logForm = null;
        _logForm.Show();
    }

    private void SyncMenu(bool enabled)
    {
        _enabledItem.Checked = enabled;
        _tray.Icon = enabled ? _onIcon : _offIcon;
        _tray.Text = enabled ? "Claude Allow Helper (ON)" : "Claude Allow Helper (OFF)";
    }

    private void BeginInvokeUi(Action action)
    {
        if (_tray.ContextMenuStrip?.IsHandleCreated == true)
        {
            _tray.ContextMenuStrip.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private static Icon CreateIcon(bool enabled)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(enabled ? Color.FromArgb(46, 125, 50) : Color.FromArgb(97, 97, 97));
        graphics.FillEllipse(brush, 1, 1, 14, 14);
        using var font = new Font("Segoe UI", 7, FontStyle.Bold);
        var text = enabled ? "A" : "X";
        var size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, Brushes.White, (16 - size.Width) / 2, (16 - size.Height) / 2);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private sealed class HotKeyWindow : NativeWindow
    {
        public event Action? EmergencyOff;

        public void Create()
        {
            CreateHandle(new CreateParams
            {
                Caption = "ClaudeAllowHelperHotkeys"
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmHotkey)
            {
                EmergencyOff?.Invoke();
            }

            base.WndProc(ref m);
        }
    }
}

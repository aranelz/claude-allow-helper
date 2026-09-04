using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper;

internal sealed class LogViewerForm : Form
{
    private readonly FileLogger _logger;
    private readonly TextBox _text = new();

    public LogViewerForm(FileLogger logger)
    {
        _logger = logger;
        Text = "Claude Allow Helper Log";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 820;
        Height = 520;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Consolas", 9F);

        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.ScrollBars = ScrollBars.Both;
        _text.Dock = DockStyle.Fill;
        _text.WordWrap = false;

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => LoadLog();
        var open = new Button { Text = "Open log folder", AutoSize = true };
        open.Click += (_, _) =>
        {
            var dir = Path.GetDirectoryName(_logger.LogPath);
            if (dir is not null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        };

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Padding = new Padding(8),
            WrapContents = false
        };
        bar.Controls.Add(refresh);
        bar.Controls.Add(open);

        Controls.Add(_text);
        Controls.Add(bar);
        LoadLog();
    }

    private void LoadLog()
    {
        _text.Text = _logger.ReadTail();
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }
}

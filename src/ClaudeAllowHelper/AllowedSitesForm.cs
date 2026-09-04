using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper;

internal sealed class AllowedSitesForm : Form
{
    private readonly ConfigStore _store;
    private readonly FileLogger _logger;
    private readonly ListBox _list = new();
    private readonly TextBox _input = new();
    private readonly Label _hint = new();

    public AllowedSitesForm(ConfigStore store, FileLogger logger)
    {
        _store = store;
        _logger = logger;
        Text = "Allowed Sites";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 360);
        Font = new Font("Segoe UI", 9F);

        var intro = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 10, 12, 4),
            Text = "Only “Allow Claude to execute JavaScript on …” prompts for these hosts are auto-approved. Other Claude permission dialogs are never clicked."
        };

        _list.IntegralHeight = false;
        _list.Dock = DockStyle.Fill;

        _input.PlaceholderText = "analitikcms.test";
        _input.Dock = DockStyle.Fill;
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddSite();
            }
        };

        var add = new Button { Text = "Add", Width = 80, Height = 28 };
        add.Click += (_, _) => AddSite();
        var remove = new Button { Text = "Remove", Width = 80, Height = 28 };
        remove.Click += (_, _) => RemoveSelected();

        _hint.AutoSize = false;
        _hint.Dock = DockStyle.Top;
        _hint.Height = 36;
        _hint.Padding = new Padding(12, 4, 12, 4);
        _hint.ForeColor = Color.DimGray;
        _hint.Text = "Examples: analitikcms.test, localhost, 127.0.0.1, *.mysite.test";

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 100,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(8),
            WrapContents = false
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(8, 4, 8, 8) };
        _input.Dock = DockStyle.Fill;
        bottom.Controls.Add(_input);

        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 0, 4) };
        listHost.Controls.Add(_list);

        Controls.Add(listHost);
        Controls.Add(buttons);
        Controls.Add(bottom);
        Controls.Add(_hint);
        Controls.Add(intro);

        LoadSites();
    }

    private void LoadSites()
    {
        var config = _store.Load().Config;
        _list.Items.Clear();
        foreach (var site in config.AllowedSites)
        {
            _list.Items.Add(site);
        }
    }

    private void AddSite()
    {
        var raw = _input.Text.Trim();
        if (raw.Length == 0)
        {
            return;
        }

        if (!SiteAllowList.TryParseHost(raw, out var host, out var port))
        {
            MessageBox.Show(this, "That does not look like a host or URL.", "Allowed Sites",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var stored = port is null ? host : $"{host}:{port}";
        if (!SiteAllowList.LooksLikeLocalDevelopmentHost(stored))
        {
            var confirm = MessageBox.Show(
                this,
                $"“{stored}” does not look like a local development host.\n\nAdd it anyway? Only do this for sites you fully trust.",
                "Non-local site",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        var loaded = _store.Load();
        var config = loaded.Config;
        if (config.AllowedSites.Any(site => site.Equals(stored, StringComparison.OrdinalIgnoreCase)))
        {
            _input.Clear();
            return;
        }

        config.AllowedSites.Add(stored);
        _store.Save(config);
        _logger.Info("Allowlist added: " + stored);
        _input.Clear();
        LoadSites();
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItem is not string selected)
        {
            return;
        }

        var config = _store.Load().Config;
        config.AllowedSites = config.AllowedSites
            .Where(site => !site.Equals(selected, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _store.Save(config);
        _logger.Info("Allowlist removed: " + selected);
        LoadSites();
    }
}

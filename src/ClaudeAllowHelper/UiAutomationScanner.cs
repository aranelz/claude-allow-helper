using System.Diagnostics;
using ClaudeAllowHelper.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace ClaudeAllowHelper;

internal sealed class UiAutomationScanner : IDisposable
{
    private readonly UIA3Automation _automation = new();

    public UiScanResult Scan(FileLogger logger)
    {
        try
        {
            var desktop = _automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
            foreach (var window in windows)
            {
                if (!IsClaudeWindow(window))
                {
                    continue;
                }

                var texts = new List<string>();
                var buttons = new List<string>();
                AutomationElement? allowOnce = null;
                Collect(window, texts, buttons, ref allowOnce, 0, new Counter());

                var snapshot = PermissionDialogParser.Parse(texts, buttons);
                return new UiScanResult
                {
                    Snapshot = snapshot,
                    AllowOnceButton = allowOnce,
                    WindowHandle = window.Properties.NativeWindowHandle.TryGetValue(out var handle)
                        ? handle
                        : IntPtr.Zero,
                    NamedElementCount = texts.Count + buttons.Count
                };
            }
        }
        catch (Exception ex)
        {
            logger.Warn("UI Automation scan failed: " + ex.Message);
        }

        return new UiScanResult
        {
            Snapshot = PermissionDialogParser.Parse([], []),
            WindowHandle = KeyboardApprover.FindClaudeMainWindow()
        };
    }

    public bool TryInvokeAllowOnce(AutomationElement? button, FileLogger logger)
    {
        if (button is null)
        {
            return false;
        }

        try
        {
            if (!string.Equals(button.Name, "Allow once", StringComparison.OrdinalIgnoreCase))
            {
                logger.Warn($"Refused to invoke button named '{button.Name}'.");
                return false;
            }

            if (button.ControlType != ControlType.Button && button.ControlType != ControlType.SplitButton)
            {
                logger.Warn($"Refused to invoke non-button control '{button.ControlType}'.");
                return false;
            }

            button.Patterns.Invoke.Pattern.Invoke();
            logger.Info("Invoked the 'Allow once' button through UI Automation.");
            return true;
        }
        catch (Exception ex)
        {
            logger.Warn("UI Automation invoke failed: " + ex.Message);
            return false;
        }
    }

    public string DumpTree(int maxNodes = 250)
    {
        var writer = new StringWriter();
        try
        {
            var desktop = _automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window));
            foreach (var window in windows)
            {
                if (!IsClaudeWindow(window))
                {
                    continue;
                }

                Dump(window, writer, 0, maxNodes, new Counter());
            }
        }
        catch (Exception ex)
        {
            writer.WriteLine("Dump failed: " + ex.Message);
        }

        return writer.ToString();
    }

    public void Dispose() => _automation.Dispose();

    private static bool IsClaudeWindow(AutomationElement window)
    {
        try
        {
            if (!window.Properties.ProcessId.TryGetValue(out var pid) || pid == 0)
            {
                return false;
            }

            using var process = Process.GetProcessById(pid);
            if (!string.Equals(process.ProcessName, "claude", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var name = window.Name ?? "";
            return name.Contains("Claude", StringComparison.OrdinalIgnoreCase) || name.Length == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void Collect(
        AutomationElement element,
        List<string> texts,
        List<string> buttons,
        ref AutomationElement? allowOnce,
        int depth,
        Counter counter)
    {
        if (counter.Value > 400 || depth > 40)
        {
            return;
        }

        counter.Value++;
        string name;
        ControlType type;
        try
        {
            name = element.Name ?? "";
            type = element.ControlType;
        }
        catch
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(name) && name.Length <= 280)
        {
            if (type is ControlType.Button or ControlType.SplitButton or ControlType.Hyperlink)
            {
                buttons.Add(name);
                if (name.Equals("Allow once", StringComparison.OrdinalIgnoreCase))
                {
                    allowOnce = element;
                }
            }
            else
            {
                texts.Add(name);
            }
        }

        AutomationElement[] children;
        try
        {
            children = element.FindAllChildren();
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            Collect(child, texts, buttons, ref allowOnce, depth + 1, counter);
        }
    }

    private static void Dump(AutomationElement element, TextWriter writer, int depth, int maxNodes, Counter counter)
    {
        if (counter.Value >= maxNodes || depth > 20)
        {
            return;
        }

        counter.Value++;
        try
        {
            var name = element.Name ?? "";
            if (name.Length > 120)
            {
                name = name[..120] + "...";
            }

            writer.WriteLine($"{new string(' ', depth * 2)}[{element.ControlType}] {name}");
        }
        catch
        {
            return;
        }

        AutomationElement[] children;
        try
        {
            children = element.FindAllChildren();
        }
        catch
        {
            return;
        }

        foreach (var child in children)
        {
            Dump(child, writer, depth + 1, maxNodes, counter);
        }
    }

    private sealed class Counter
    {
        public int Value;
    }
}

internal sealed class UiScanResult
{
    public required UiPermissionSnapshot Snapshot { get; init; }

    public AutomationElement? AllowOnceButton { get; init; }

    public IntPtr WindowHandle { get; init; }

    public int NamedElementCount { get; init; }
}

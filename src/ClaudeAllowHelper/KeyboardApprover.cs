using System.Diagnostics;
using ClaudeAllowHelper.Core;

namespace ClaudeAllowHelper;

internal static class KeyboardApprover
{
    public static bool SendAllowOnce(IntPtr claudeWindow, FileLogger logger)
    {
        if (claudeWindow == IntPtr.Zero)
        {
            logger.Warn("No Claude window handle was available for Ctrl+Enter.");
            return false;
        }

        if (!IsClaudeWindow(claudeWindow, logger))
        {
            logger.Warn("Refused to send Ctrl+Enter because the target window is not Claude.");
            return false;
        }

        if (!IsExpectedForeground(claudeWindow))
        {
            logger.Warn("Refused to send Ctrl+Enter because the expected Claude window is not in the foreground.");
            return false;
        }

        var inputs = new NativeMethods.INPUT[]
        {
            Key(NativeMethods.VkControl, down: true),
            Key(NativeMethods.VkReturn, down: true),
            Key(NativeMethods.VkReturn, down: false),
            Key(NativeMethods.VkControl, down: false)
        };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, MarshalSize());
        if (sent != inputs.Length)
        {
            logger.Warn($"SendInput only delivered {sent} of {inputs.Length} key events.");
            return false;
        }

        if (!IsExpectedForeground(claudeWindow))
        {
            logger.Warn("Foreground changed while sending Ctrl+Enter; treating the send as failed.");
            return false;
        }

        logger.Info("Sent Ctrl+Enter to the already-focused Claude window after final verification passed.");
        return true;
    }

    public static bool IsExpectedForeground(IntPtr expectedWindow)
    {
        if (expectedWindow == IntPtr.Zero)
        {
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == expectedWindow && IsClaudeWindow(foreground);
    }

    public static IntPtr FindClaudeMainWindow()
    {
        foreach (var process in Process.GetProcessesByName("claude"))
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero &&
                    !string.IsNullOrWhiteSpace(process.MainWindowTitle) &&
                    process.MainWindowTitle.Contains("Claude", StringComparison.OrdinalIgnoreCase) &&
                    IsClaudePath(process))
                {
                    return process.MainWindowHandle;
                }
            }
            catch
            {
                // Process may exit while enumerating.
            }
        }

        return IntPtr.Zero;
    }

    public static bool IsClaudeWindow(IntPtr hwnd, FileLogger? logger = null)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (!string.Equals(process.ProcessName, "claude", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return IsClaudePath(process);
        }
        catch (Exception ex)
        {
            logger?.Warn("Could not verify Claude process: " + ex.Message);
            return false;
        }
    }

    private static bool IsClaudePath(Process process)
    {
        string? path = null;
        try
        {
            path = process.MainModule?.FileName;
        }
        catch
        {
            // Access denied on some child processes is fine; name was already claude.
            return true;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        return path.Contains(@"\Claude_", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Claude\", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith("claude.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static NativeMethods.INPUT Key(ushort vk, bool down) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KEYBDINPUT
            {
                WVk = vk,
                DwFlags = down ? 0 : NativeMethods.KeyeventfKeyup
            }
        }
    };

    private static int MarshalSize() => System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>();
}

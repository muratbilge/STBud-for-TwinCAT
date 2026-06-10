using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using STFormatter.Core.Configuration;

namespace STFormatter.Host;

internal sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_F = 0x46;
    private const int VK_D = 0x44;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _hookCallback;
    private bool _disposed;
    private volatile bool _processing;
    private SynchronizationContext? _syncContext;
    private int _lastHotkeyVkCode;
    private uint _lastHotkeyTime;

    public event Action<int>? FormatDocumentHotkey;
    public event Action<int>? FormatSelectionHotkey;

    public KeyboardHook()
    {
        _hookCallback = HookCallback;
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        _syncContext = SynchronizationContext.Current;
        if (_syncContext == null)
        {
            HostLog.Append("KeyboardHook", "No SynchronizationContext available — keyboard shortcuts will not work");
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        IntPtr hModule = GetModuleHandle(currentModule?.ModuleName ?? string.Empty);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, hModule, 0);

        if (_hookId == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            HostLog.Append("KeyboardHook", $"SetWindowsHookEx failed with error {error}");
        }
        else
        {
            HostLog.Append("KeyboardHook", "Low-level keyboard hook installed (Ctrl+Shift+F = Format Document, Ctrl+Shift+D = Format Selection)");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int scanCode = Marshal.ReadInt32(lParam, 4);
            uint time = (uint)Marshal.ReadInt32(lParam, 12);

            if (vkCode == _lastHotkeyVkCode && time == _lastHotkeyTime)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            if ((vkCode == VK_F || vkCode == VK_D) && IsCtrlShiftPressed())
            {
                // Low-level hook callbacks must return fast (Windows silently
                // unhooks slow hooks) - keep this path to cheap Win32 calls plus
                // a cached process-name lookup; logging happens in the posted
                // continuation on the UI thread.
                int? pid = FindTcXaeShellPid();
                if (pid.HasValue)
                {
                    _lastHotkeyVkCode = vkCode;
                    _lastHotkeyTime = time;
                    var capturedPid = pid.Value;
                    var handler = vkCode == VK_F ? FormatDocumentHotkey : FormatSelectionHotkey;
                    var keyName = vkCode == VK_F ? "Ctrl+Shift+F" : "Ctrl+Shift+D";
                    _syncContext?.Post(_ =>
                    {
                        if (!_processing)
                        {
                            _processing = true;
                            try
                            {
                                HostLog.Append("KeyboardHook", $"{keyName} detected, TcXaeShell PID {capturedPid}");
                                handler?.Invoke(capturedPid);
                            }
                            finally { _processing = false; }
                        }
                    }, null);
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    // PID -> is-TcXaeShell, cached briefly so the hook callback avoids the
    // comparatively slow Process.GetProcessById on every keystroke.
    private static readonly System.Collections.Generic.Dictionary<uint, (bool IsTcXae, DateTime CheckedAt)> _pidCache = new();
    private static readonly TimeSpan PidCacheValidity = TimeSpan.FromSeconds(5);

    private static int? FindTcXaeShellPid()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;

        GetWindowThreadProcessId(foreground, out uint pid);
        if (pid == 0) return null;

        if (_pidCache.TryGetValue(pid, out var cached) &&
            DateTime.UtcNow - cached.CheckedAt < PidCacheValidity)
        {
            return cached.IsTcXae ? (int)pid : (int?)null;
        }

        bool isTcXae = false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            isTcXae = process.ProcessName.Equals("TcXaeShell", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
        }

        if (_pidCache.Count > 64) _pidCache.Clear();
        _pidCache[pid] = (isTcXae, DateTime.UtcNow);
        return isTcXae ? (int)pid : (int?)null;
    }

    private static bool IsCtrlShiftPressed()
    {
        return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
            && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            HostLog.Append("KeyboardHook", "Low-level keyboard hook removed");
        }
    }
}
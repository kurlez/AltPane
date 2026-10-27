using System.Runtime.InteropServices;
using AltPane.Core.Switcher;

namespace AltPane;

/// <summary>
/// Records foreground changes. The callback only stores a handle. Switcher and
/// settings windows belong to this process and are ignored, so they do not push
/// real windows down the list. The order is read when the switcher opens.
/// </summary>
internal sealed class ForegroundTracker : IDisposable
{
    const uint EventSystemForeground = 0x0003;
    const uint WinEventOutOfContext = 0x0000;
    readonly ActivationMru _history;
    readonly WinEventProc _callback;
    readonly uint _selfPid;
    nint _hook;
    bool _disposed;

    public ForegroundTracker(ActivationMru history)
    {
        _history = history;
        _selfPid = (uint)Environment.ProcessId;
        _callback = OnForeground;
        _hook = NativeMethods.SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            0,
            _callback,
            0,
            0,
            WinEventOutOfContext);
        if (_hook == 0)
            AppLog.Info("Foreground hook was not installed. Stable grouped order falls back to Z-order until activations are recorded.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook == 0) return;
        NativeMethods.UnhookWinEvent(_hook);
        _hook = 0;
    }

    void OnForeground(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint threadId, uint time)
    {
        if (hwnd == 0) return;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == _selfPid) return;
        _history.Note((long)hwnd);
    }
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void WinEventProc(
    nint hWinEventHook,
    uint eventType,
    nint hwnd,
    int idObject,
    int idChild,
    uint idEventThread,
    uint dwmsEventTime);

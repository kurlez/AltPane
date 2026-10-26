namespace AltPane;

internal enum KillAttempt
{
    Success,
    AccessDenied,
    Failed
}

internal static class WindowActions
{
    public static bool TryActivate(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return false;
        try
        {
            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SwRestore);
            else
                NativeMethods.ShowWindow(hwnd, NativeMethods.SwShow);

            var foreground = NativeMethods.GetForegroundWindow();
            var targetThread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
            var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var thisThread = NativeMethods.GetCurrentThreadId();

            var attachedForeground = false;
            var attachedTarget = false;
            if (foregroundThread != 0 && foregroundThread != thisThread)
                attachedForeground = NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
            if (targetThread != 0 && targetThread != thisThread && targetThread != foregroundThread)
                attachedTarget = NativeMethods.AttachThreadInput(thisThread, targetThread, true);

            NativeMethods.LockSetForegroundWindow(2);
            NativeMethods.AllowSetForegroundWindow(-1);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.BringWindowToTop(hwnd);
            if (NativeMethods.GetForegroundWindow() != hwnd)
                NativeMethods.SwitchToThisWindow(hwnd, true);

            if (attachedTarget)
                NativeMethods.AttachThreadInput(thisThread, targetThread, false);
            if (attachedForeground)
                NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);

            var active = NativeMethods.GetForegroundWindow() == hwnd;
            if (!active)
                AppLog.Info($"Activate failed for 0x{hwnd:X}.");
            return active;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }

    public static bool RequestClose(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd)) return false;
        return NativeMethods.PostMessage(hwnd, NativeMethods.WmClose, 0, 0);
    }

    public static void Minimize(nint hwnd) =>
        NativeMethods.PostMessage(hwnd, NativeMethods.WmSysCommand, NativeMethods.ScMinimize, 0);

    public static void Maximize(nint hwnd) =>
        NativeMethods.PostMessage(hwnd, NativeMethods.WmSysCommand, NativeMethods.ScMaximize, 0);

    public static void Restore(nint hwnd) =>
        NativeMethods.PostMessage(hwnd, NativeMethods.WmSysCommand, NativeMethods.ScRestore, 0);

    public static KillAttempt Kill(int processId)
    {
        if (processId <= 4) return KillAttempt.Failed;
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessTerminate, false, processId);
        if (handle == 0)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            AppLog.Info($"OpenProcess terminate failed for pid {processId}: {error}");
            return error == 5 ? KillAttempt.AccessDenied : KillAttempt.Failed;
        }

        try
        {
            return NativeMethods.TerminateProcess(handle, 1) ? KillAttempt.Success : KillAttempt.Failed;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}

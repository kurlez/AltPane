namespace AltPane;

internal enum KillAttempt
{
    Success,
    AccessDenied,
    Failed
}

internal static class WindowActions
{
    /// <summary>
    /// Brings <paramref name="other"/> forward without focusing it, then activates
    /// <paramref name="focus"/>. Neither call moves or resizes a restored window.
    /// </summary>
    public static bool TryActivatePair(nint other, nint focus)
    {
        if (other != 0 && other != focus)
            RaiseInPlace(other);
        return TryActivate(focus);
    }

    public static void RaiseInPlace(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd)) return;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

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

    public static KillAttempt KillAll(IReadOnlyList<int> processIds)
    {
        if (processIds.Count == 0) return KillAttempt.Failed;
        var root = processIds[^1];
        var rootResult = KillAttempt.Failed;
        var childEnded = false;
        foreach (var pid in processIds)
        {
            var result = Kill(pid);
            if (pid == root) rootResult = result;
            else if (result == KillAttempt.Success) childEnded = true;
        }
        if (rootResult == KillAttempt.Success) return KillAttempt.Success;
        if (rootResult == KillAttempt.AccessDenied) return KillAttempt.AccessDenied;
        return childEnded ? KillAttempt.Success : rootResult;
    }

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

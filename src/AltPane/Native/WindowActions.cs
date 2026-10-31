using System.Runtime.InteropServices;
using AltPane.Core.Switcher;

namespace AltPane;

internal enum KillAttempt
{
    Success,
    AccessDenied,
    Failed
}

internal readonly record struct PairActivateResult(bool FocusActive, bool PartnerRaised, string Detail);

internal static class WindowActions
{
    /// <summary>
    /// Raises both windows above every other window on that monitor and leaves
    /// the keyboard on <paramref name="focus"/>. Positions and sizes are not changed.
    /// <c>SetWindowPos(HWND_TOP)</c> alone is not enough: after the switcher hides,
    /// this process is no longer foreground, and Windows ignores a plain top-of-z-order
    /// request for another process's window. The partner is therefore inserted directly
    /// under the focused window while the threads are attached, and if that is still
    /// buried it is lifted with a temporary topmost band.
    /// </summary>
    public static PairActivateResult TryActivatePair(nint other, nint focus)
    {
        if (focus == 0 || !NativeMethods.IsWindow(focus))
        {
            AppLog.Info($"Pair activate missing focus 0x{focus:X} other 0x{other:X}.");
            return new PairActivateResult(false, false, "missing-focus");
        }

        var partner = other != 0 && other != focus && NativeMethods.IsWindow(other);
        if (partner && NativeMethods.IsIconic(other))
        {
            AppLog.Info($"Pair activate other 0x{other:X} is minimized; leaving its position alone.");
            partner = false;
        }
        if (NativeMethods.IsIconic(focus))
            AppLog.Info($"Pair activate focus 0x{focus:X} is minimized.");

        var foreground = NativeMethods.GetForegroundWindow();
        AppLog.Info($"Pair activate begin focus 0x{focus:X} other 0x{(partner ? other : 0):X} fg 0x{foreground:X} focusTop={IsTopMost(focus)} otherTop={(partner ? IsTopMost(other) : false)}.");

        var attached = Attach(foreground, focus, partner ? other : 0);
        try
        {
            NativeMethods.LockSetForegroundWindow(2);
            NativeMethods.AllowSetForegroundWindow(-1);
            if (partner)
                Place(other, NativeMethods.HwndTop, "top-other");
            Place(focus, NativeMethods.HwndTop, "top-focus");
            ActivateFocus(focus);
            if (partner)
                Place(other, focus, "under-focus");
            ActivateFocus(focus);

            var order = SignificantZOrder(focus, partner ? other : 0);
            var raised = !partner || PairZOrder.IsRaised(order, focus, other);
            if (partner && !raised)
            {
                AppLog.Info($"Pair activate HWND_TOP left the partner behind ({FormatOrder(order)}). Lifting with a temporary topmost band.");
                LiftTopMost(other, focus);
                ActivateFocus(focus);
                order = SignificantZOrder(focus, other);
                raised = PairZOrder.IsRaised(order, focus, other);
            }

            var now = NativeMethods.GetForegroundWindow();
            var focusActive = now == focus;
            var detail = $"fg 0x{now:X} focusOk={focusActive} partner={raised} z={FormatOrder(order)}";
            AppLog.Info("Pair activate done " + detail + ".");
            return new PairActivateResult(focusActive, raised || !partner, detail);
        }
        finally
        {
            attached.Dispose();
        }
    }

    /// <summary>True when the partner is already above every other window on the focus window's monitor.</summary>
    public static bool PairIsRaised(nint other, nint focus)
    {
        if (focus == 0 || other == 0 || other == focus) return false;
        if (!NativeMethods.IsWindow(focus) || !NativeMethods.IsWindow(other)) return false;
        return PairZOrder.IsRaised(SignificantZOrder(focus, other), focus, other)
            && NativeMethods.GetForegroundWindow() == focus;
    }

    static bool LiftTopMost(nint other, nint focus)
    {
        var otherWas = IsTopMost(other);
        var focusWas = IsTopMost(focus);
        var flags = PlaceFlags;
        if (!Defer(stack =>
            {
                stack = DeferOne(stack, other, NativeMethods.HwndTopMost, flags);
                return DeferOne(stack, focus, NativeMethods.HwndTopMost, flags);
            }))
        {
            Place(other, NativeMethods.HwndTopMost, "topmost-other");
            Place(focus, NativeMethods.HwndTopMost, "topmost-focus");
        }

        ActivateFocus(focus);

        if (!Defer(stack =>
            {
                if (!otherWas)
                    stack = DeferOne(stack, other, NativeMethods.HwndNoTopMost, flags);
                if (!focusWas)
                    stack = DeferOne(stack, focus, NativeMethods.HwndNoTopMost, flags);
                return stack;
            }))
        {
            if (!otherWas) Place(other, NativeMethods.HwndNoTopMost, "notop-other");
            if (!focusWas) Place(focus, NativeMethods.HwndNoTopMost, "notop-focus");
        }

        Place(other, focus, "under-focus-after-topmost");
        return true;
    }

    static bool ActivateFocus(nint focus)
    {
        NativeMethods.SetForegroundWindow(focus);
        NativeMethods.BringWindowToTop(focus);
        if (NativeMethods.GetForegroundWindow() == focus) return true;
        NativeMethods.SwitchToThisWindow(focus, true);
        return NativeMethods.GetForegroundWindow() == focus;
    }

    const uint PlaceFlags =
        NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate
        | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpNoSendChanging;

    static bool Place(nint hwnd, nint after, string label)
    {
        if (hwnd == 0) return false;
        var ok = NativeMethods.SetWindowPos(hwnd, after, 0, 0, 0, 0, PlaceFlags);
        if (!ok)
            AppLog.Info($"Pair SetWindowPos {label} 0x{hwnd:X} after 0x{after:X} failed {Marshal.GetLastWin32Error()}.");
        return ok;
    }

    static bool Defer(Func<nint, nint> fill)
    {
        var stack = NativeMethods.BeginDeferWindowPos(2);
        if (stack == 0)
        {
            AppLog.Info($"Pair BeginDeferWindowPos failed {Marshal.GetLastWin32Error()}.");
            return false;
        }
        stack = fill(stack);
        if (stack == 0) return false;
        var ok = NativeMethods.EndDeferWindowPos(stack);
        if (!ok)
            AppLog.Info($"Pair EndDeferWindowPos failed {Marshal.GetLastWin32Error()}.");
        return ok;
    }

    static nint DeferOne(nint stack, nint hwnd, nint after, uint flags)
    {
        if (stack == 0 || hwnd == 0) return stack;
        var next = NativeMethods.DeferWindowPos(stack, hwnd, after, 0, 0, 0, 0, flags);
        if (next == 0)
            AppLog.Info($"Pair DeferWindowPos 0x{hwnd:X} after 0x{after:X} failed {Marshal.GetLastWin32Error()}.");
        return next;
    }

    static bool IsTopMost(nint hwnd) =>
        hwnd != 0 && (NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle) & (nint)NativeMethods.WsExTopMost) != 0;

    static List<long> SignificantZOrder(nint focus, nint other)
    {
        var list = new List<long>(16);
        var monitor = NativeMethods.MonitorFromWindow(focus, NativeMethods.MonitorDefaultToNearest);
        var self = Environment.ProcessId;
        var seen = 0;
        for (var hwnd = NativeMethods.GetTopWindow(0); hwnd != 0 && seen < 80; hwnd = NativeMethods.GetWindow(hwnd, (uint)NativeMethods.GwHwndNext))
        {
            seen++;
            if (!CountsInFront(hwnd, focus, other, monitor, self)) continue;
            list.Add(hwnd);
            if (list.Count >= 8) break;
        }
        return list;
    }

    static bool CountsInFront(nint hwnd, nint focus, nint other, nint monitor, int self)
    {
        if (hwnd == 0 || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return false;
        if (IsCloaked(hwnd)) return false;
        var member = hwnd == focus || hwnd == other;
        if (!member && monitor != 0 && NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest) != monitor)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (!member && (int)pid == self) return false;
        if (member) return true;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        if ((style & (nint)NativeMethods.WsExToolWindow) != 0) return false;
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRootOwner);
        if (root == 0) root = hwnd;
        return root == hwnd;
    }

    static bool IsCloaked(nint hwnd)
    {
        var hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var value, sizeof(int));
        return hr == 0 && value != 0;
    }

    static string FormatOrder(IReadOnlyList<long> order)
    {
        if (order.Count == 0) return "[]";
        var text = new System.Text.StringBuilder();
        text.Append('[');
        for (var i = 0; i < order.Count; i++)
        {
            if (i > 0) text.Append(' ');
            text.Append(Tag((nint)order[i]));
        }
        text.Append(']');
        return text.ToString();
    }

    static string Tag(nint hwnd)
    {
        var buffer = new System.Text.StringBuilder(80);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        var title = buffer.ToString().Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (title.Length > 40) title = title[..40];
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return $"0x{hwnd:X}/{pid}:{title}";
    }

    static ThreadAttachments Attach(nint foreground, nint focus, nint other)
    {
        var current = NativeMethods.GetCurrentThreadId();
        var threads = new List<uint>(3);
        AddThread(threads, current, foreground);
        AddThread(threads, current, focus);
        AddThread(threads, current, other);
        var attached = new uint[threads.Count];
        var count = 0;
        foreach (var thread in threads)
        {
            if (NativeMethods.AttachThreadInput(current, thread, true))
                attached[count++] = thread;
        }
        return new ThreadAttachments(current, attached, count);
    }

    static void AddThread(List<uint> threads, uint current, nint hwnd)
    {
        if (hwnd == 0) return;
        var thread = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        if (thread == 0 || thread == current) return;
        if (!threads.Contains(thread)) threads.Add(thread);
    }

    readonly struct ThreadAttachments : IDisposable
    {
        readonly uint _current;
        readonly uint[] _threads;
        readonly int _count;

        public ThreadAttachments(uint current, uint[] threads, int count)
        {
            _current = current;
            _threads = threads;
            _count = count;
        }

        public void Dispose()
        {
            for (var i = _count - 1; i >= 0; i--)
                NativeMethods.AttachThreadInput(_current, _threads[i], false);
        }
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

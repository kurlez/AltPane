using System.Runtime.InteropServices;
using System.Text;
using AltPane.Core.Actions;
using AltPane.Core.Filtering;
using AltPane.Core.Models;

namespace AltPane;

[ComImport]
[Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManager
{
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

    [PreserveSig]
    int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

    [PreserveSig]
    int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
}

internal static class WindowEnumerator
{
    public static List<WindowEntry> Enumerate(AppSettings settings)
    {
        var context = new FilterContext
        {
            CurrentProcessId = Environment.ProcessId,
            IncludeOtherVirtualDesktops = settings.IncludeOtherVirtualDesktops,
            ExcludedProcessNames = settings.ExcludedProcesses
        };

        IVirtualDesktopManager? desktops = null;
        try
        {
            desktops = TryDesktopManager();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }

        IReadOnlyList<LiveMonitor> monitors;
        try
        {
            monitors = MonitorUtil.Live();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            monitors = Array.Empty<LiveMonitor>();
        }

        var found = new List<WindowEntry>();
        var callback = new EnumWindowsProc((hwnd, _) =>
        {
            try
            {
                var candidate = ReadCandidate(hwnd, desktops);
                if (!WindowFilter.ShouldInclude(candidate, context)) return true;
                found.Add(ToEntry(candidate, found.Count, monitors));
            }
            catch (Exception ex)
            {
                AppLog.Error(ex);
            }
            return true;
        });

        try
        {
            NativeMethods.EnumWindows(callback, 0);
        }
        finally
        {
            if (desktops is not null && Marshal.IsComObject(desktops))
                Marshal.ReleaseComObject(desktops);
        }

        AppLog.Info($"Enumerated {found.Count} switchable windows.");
        return found;
    }

    static WindowCandidate ReadCandidate(nint hwnd, IVirtualDesktopManager? desktops)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        var path = QueryProcessPath((int)pid);
        var process = string.IsNullOrEmpty(path) ? "" : Path.GetFileNameWithoutExtension(path);
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRootOwner);
        if (root == 0) root = hwnd;
        return new WindowCandidate
        {
            Handle = hwnd,
            Title = ReadText(hwnd),
            ClassName = ReadClass(hwnd),
            ProcessName = process,
            ProcessId = (int)pid,
            OwnerHandle = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner),
            RootOwnerHandle = root,
            ExStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle),
            IsVisible = NativeMethods.IsWindowVisible(hwnd),
            CloakFlags = ReadCloak(hwnd),
            VirtualDesktopSame = DesktopState(hwnd, desktops)
        };
    }

    static WindowEntry ToEntry(WindowCandidate candidate, int index, IReadOnlyList<LiveMonitor> monitors)
    {
        var iconPath = "";
        string? hostedName = null;
        var hostedPid = 0;
        if (candidate.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            var child = FindCoreWindow((nint)candidate.Handle);
            if (child != 0)
            {
                NativeMethods.GetWindowThreadProcessId(child, out var cpid);
                hostedPid = (int)cpid;
                var hostedPath = QueryProcessPath(hostedPid);
                if (!string.IsNullOrEmpty(hostedPath))
                {
                    hostedName = Path.GetFileNameWithoutExtension(hostedPath);
                    iconPath = hostedPath;
                }
            }
        }

        if (iconPath.Length == 0)
            iconPath = QueryProcessPath(candidate.ProcessId);

        var hwnd = (nint)candidate.Handle;
        var hit = MonitorLocator.Locate(hwnd, monitors);
        return new WindowEntry
        {
            Handle = candidate.Handle,
            Title = candidate.Title.Trim(),
            ClassName = candidate.ClassName,
            ProcessName = candidate.ProcessName,
            HostedProcessName = hostedName,
            DisplayProcessName = ProcessNames.DisplayName(candidate.ProcessName, hostedName),
            ProcessId = candidate.ProcessId,
            HostedProcessId = hostedPid,
            KillProcessId = ProcessActionPolicy.ResolveKillProcessId(candidate.ProcessName, candidate.ProcessId, hostedPid),
            KillProcessName = ProcessActionPolicy.ResolveKillProcessName(candidate.ProcessName, hostedName),
            IconPath = iconPath,
            IsIconic = NativeMethods.IsIconic(hwnd),
            IsZoomed = NativeMethods.IsZoomed(hwnd),
            OriginalIndex = index,
            MonitorNumber = hit.Number,
            MonitorWidth = hit.Width,
            MonitorHeight = hit.Height,
            MonitorPrimary = hit.Primary,
            MonitorFriendlyName = hit.FriendlyName ?? ""
        };
    }

    static nint FindCoreWindow(nint frame)
    {
        nint found = 0;
        var callback = new EnumWindowsProc((hwnd, _) =>
        {
            if (ReadClass(hwnd) == "Windows.UI.Core.CoreWindow")
            {
                found = hwnd;
                return false;
            }
            return true;
        });
        NativeMethods.EnumChildWindows(frame, callback, 0);
        return found;
    }

    static int DesktopState(nint hwnd, IVirtualDesktopManager? desktops)
    {
        if (desktops is null) return -1;
        try
        {
            var hr = desktops.IsWindowOnCurrentVirtualDesktop(hwnd, out var current);
            if (hr != 0) return -1;
            return current ? 1 : 0;
        }
        catch
        {
            return -1;
        }
    }

    static int ReadCloak(nint hwnd)
    {
        var hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var value, sizeof(int));
        return hr == 0 ? value : 0;
    }

    static string ReadText(nint hwnd)
    {
        var buffer = new StringBuilder(1024);
        NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    static string ReadClass(nint hwnd)
    {
        var buffer = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string QueryProcessPath(int pid)
    {
        if (pid <= 0) return "";
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, pid);
        if (handle == 0) return "";
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : "";
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    static IVirtualDesktopManager? TryDesktopManager()
    {
        var clsid = new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a");
        var iid = typeof(IVirtualDesktopManager).GUID;
        var hr = NativeMethods.CoCreateInstance(ref clsid, 0, 1, ref iid, out var unknown);
        if (hr != 0 || unknown is null) return null;
        return (IVirtualDesktopManager)unknown;
    }
}

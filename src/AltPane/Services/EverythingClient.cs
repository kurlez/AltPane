using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using AltPane.Core.Search;
using Microsoft.Win32;

namespace AltPane;

internal readonly record struct EverythingStatus(
    bool Running,
    bool Installed,
    string? Executable,
    EverythingReach Reach = EverythingReach.NotRunning,
    EverythingLink Link = EverythingLink.None,
    int? HttpPort = null);

/// <summary>
/// Asks a running Everything instance for files. The DLL is used when it sits next to
/// AltPane or in Everything's install directory. IPC is sent to
/// <c>EVERYTHING_IPC_WNDCLASS</c>, then to <c>EVERYTHING_TASKBAR_NOTIFICATION</c> and
/// the main <c>EVERYTHING</c> window when that class is missing or does not answer.
/// <c>es.exe</c> is next. If IPC still has no reply, the local HTTP server is queried.
/// A missing Everything returns no rows. Call <see cref="Query"/> off the UI thread.
/// </summary>
internal sealed class EverythingClient
{
    const int WmCopyData = 0x004A;
    const uint SmtoAbortIfHung = 0x0002;
    const uint PmRemove = 0x0001;
    const uint QsAllInput = 0x04FF;
    const uint WaitTimeout = 258;
    static readonly nint HwndMessage = new(-3);
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly object _gate = new();
    EverythingQuery.TransportPick _pick;

    public EverythingStatus Probe()
    {
        var ipc = FindClass(EverythingQuery.IpcClassName);
        var taskbar = FindClass(EverythingQuery.TaskbarClassName);
        var main = FindClass(EverythingQuery.MainClassName);
        var running = ipc != 0 || taskbar != 0 || main != 0 || IsEverythingProcessRunning();
        var exe = FindInstalledExe();
        return new EverythingStatus(running, exe is not null, exe);
    }

    /// <summary>IPC ping, then HTTP. Must run off the UI thread.</summary>
    public EverythingStatus Describe(int httpPort, bool httpFallback)
    {
        var status = Probe();
        if (!status.Running)
            return status with { Reach = EverythingReach.NotRunning };
        var result = Query("ext:altpaneprobe", 1, 350, httpPort, httpFallback);
        return status with
        {
            Reach = result.Reach,
            Link = result.Link,
            HttpPort = result.Port,
            Running = true
        };
    }

    public bool TryStart()
    {
        var exe = FindInstalledExe();
        if (exe is null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return false;
        }
    }

    public EverythingQuery.Result Query(string search, int max, int timeoutMs, int httpPort, bool httpFallback)
    {
        if (string.IsNullOrWhiteSpace(search) || max <= 0)
        {
            return new EverythingQuery.Result(
                Array.Empty<string>(),
                EverythingLink.None,
                null,
                EverythingReach.NotRunning,
                Array.Empty<EverythingAttempt>());
        }
        lock (_gate)
        {
            var ipc = FindClass(EverythingQuery.IpcClassName);
            var taskbar = FindClass(EverythingQuery.TaskbarClassName);
            var main = FindClass(EverythingQuery.MainClassName);
            var running = ipc != 0 || taskbar != 0 || main != 0 || IsEverythingProcessRunning();
            var ipcBudget = Math.Clamp(timeoutMs, 50, 700);
            var httpBudget = Math.Clamp(Math.Min(timeoutMs, 400), 50, 400);
            var result = EverythingQuery.Run(
                running,
                link => link switch
                {
                    EverythingLink.Sdk => SdkPresent(),
                    EverythingLink.Ipc => ipc != 0,
                    EverythingLink.IpcTaskbar => taskbar != 0,
                    EverythingLink.IpcWindow => main != 0,
                    EverythingLink.Es => FindEs() is not null,
                    _ => false
                },
                (link, port) => link switch
                {
                    EverythingLink.Sdk => TryDll(search, max),
                    EverythingLink.Ipc => TryIpc(ipc, search, max, ipcBudget),
                    EverythingLink.IpcTaskbar => TryIpc(taskbar, search, max, ipcBudget),
                    EverythingLink.IpcWindow => TryIpc(main, search, max, ipcBudget),
                    EverythingLink.Es => TryEs(search, max, Math.Max(ipcBudget, 400)),
                    EverythingLink.Http => TryHttp(search, max, port ?? httpPort, httpBudget),
                    _ => null
                },
                httpPort,
                httpFallback,
                _pick.Link,
                _pick.Link == EverythingLink.Http ? _pick.Port : null,
                EverythingQuery.IsNonTrivial(search),
                _pick.Link,
                _pick.Link == EverythingLink.Http ? _pick.Port : null);
            _pick = EverythingQuery.Remember(_pick, result.Link, result.Port, result.Paths.Count);
            var tried = string.Join(",", result.Attempted.Select(item => EverythingQuery.Name(item.Link, item.Port)));
            AppLog.Info($"Everything transport {EverythingQuery.Name(result.Link, result.Port)}, rows {result.Paths.Count}, reach {result.Reach}, tried {tried}");
            return result;
        }
    }

    public static string? FindInstalledExe()
    {
        foreach (var root in new[]
                 {
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\voidtools\Everything",
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\voidtools\Everything",
                     @"HKEY_CURRENT_USER\SOFTWARE\voidtools\Everything"
                 })
        {
            foreach (var value in new[] { "InstallLocation", "Location" })
            {
                var raw = Registry.GetValue(root, value, null) as string;
                var exe = ExeIn(raw);
                if (exe is not null) return exe;
            }
        }

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var exe = ExeIn(Path.Combine(folder, "Everything"));
            if (exe is not null) return exe;
        }
        return null;
    }

    IReadOnlyList<string>? TryDll(string search, int max)
    {
        var module = LoadSdk();
        if (module == 0) return null;
        try
        {
            var setSearch = Marshal.GetDelegateForFunctionPointer<SetSearchW>(Required(module, "Everything_SetSearchW"));
            var setMax = Marshal.GetDelegateForFunctionPointer<SetDword>(Required(module, "Everything_SetMax"));
            var setRegex = Marshal.GetDelegateForFunctionPointer<SetBool>(Required(module, "Everything_SetRegex"));
            var setPath = Marshal.GetDelegateForFunctionPointer<SetBool>(Required(module, "Everything_SetMatchPath"));
            var setCase = Marshal.GetDelegateForFunctionPointer<SetBool>(Required(module, "Everything_SetMatchCase"));
            var query = Marshal.GetDelegateForFunctionPointer<QueryW>(Required(module, "Everything_QueryW"));
            var count = Marshal.GetDelegateForFunctionPointer<GetDword>(Required(module, "Everything_GetNumResults"));
            var path = Marshal.GetDelegateForFunctionPointer<GetPath>(Required(module, "Everything_GetResultFullPathNameW"));
            setRegex(false);
            setPath(false);
            setCase(false);
            setMax((uint)max);
            setSearch(search);
            if (!query(true)) return null;
            var n = (int)Math.Min(count(), (uint)max);
            var list = new List<string>(n);
            var buffer = new StringBuilder(1024);
            for (var i = 0; i < n; i++)
            {
                buffer.Clear();
                path((uint)i, buffer, (uint)buffer.Capacity);
                if (buffer.Length > 0) list.Add(buffer.ToString());
            }
            return list;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static nint LoadSdk()
    {
        foreach (var directory in SdkDirectories())
        {
            var file = Path.Combine(directory, "Everything64.dll");
            if (!File.Exists(file)) continue;
            var module = LoadLibrary(file);
            if (module != 0) return module;
        }
        return 0;
    }

    static IEnumerable<string> SdkDirectories()
    {
        var exe = AltPane.Core.Settings.AppPaths.ResolveExecutable(Environment.ProcessPath);
        var dir = Path.GetDirectoryName(exe);
        if (!string.IsNullOrEmpty(dir)) yield return dir;
        var installed = FindInstalledExe();
        var home = Path.GetDirectoryName(installed);
        if (!string.IsNullOrEmpty(home)) yield return home;
    }

    static nint Required(nint module, string name)
    {
        var ptr = GetProcAddress(module, name);
        if (ptr == 0) throw new EntryPointNotFoundException(name);
        return ptr;
    }

    IReadOnlyList<string>? TryIpc(nint everything, string search, int max, int timeoutMs)
    {
        if (everything == 0) return null;
        byte[]? reply = null;
        nint hwnd = 0;
        var proc = new WndProc((h, msg, w, l) =>
        {
            if (msg == WmCopyData)
            {
                var data = Marshal.PtrToStructure<CopyDataStruct>(l);
                if (data.dwData != new nint(EverythingIpc.ReplyMessage))
                    return 1;
                if (data.cbData <= 0 || data.lpData == 0)
                    return 1;
                var bytes = new byte[data.cbData];
                Marshal.Copy(data.lpData, bytes, 0, bytes.Length);
                reply = bytes;
                return 1;
            }
            return DefWindowProc(h, msg, w, l);
        });
        var className = "AltPaneEv" + Guid.NewGuid().ToString("N");
        var wc = new WndClassEx
        {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = proc,
            lpszClassName = className,
            hInstance = GetModuleHandle(null)
        };
        RegisterClassEx(ref wc);
        try
        {
            hwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, new nint(-3), 0, wc.hInstance, 0);
            if (hwnd == 0) return null;
            var payload = EverythingIpc.BuildQuery(unchecked((uint)hwnd.ToInt64()), search, max);
            var buffer = Marshal.AllocHGlobal(payload.Length);
            try
            {
                Marshal.Copy(payload, 0, buffer, payload.Length);
                var copy = new CopyDataStruct { dwData = new nint(EverythingIpc.QueryW), cbData = payload.Length, lpData = buffer };
                var copyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStruct>());
                try
                {
                    Marshal.StructureToPtr(copy, copyPtr, false);
                    var budget = Math.Max(50, timeoutMs);
                    var started = Environment.TickCount64;
                    var accepted = SendMessageTimeout(everything, WmCopyData, hwnd, copyPtr, SmtoAbortIfHung, (uint)budget, out _) != 0;
                    if (accepted || reply is not null)
                        PumpUntil(hwnd, started, budget, () => reply is not null);
                    else
                        Drain(hwnd, () => reply is not null);
                }
                finally
                {
                    Marshal.FreeHGlobal(copyPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            if (reply is null) return null;
            return EverythingIpc.Parse(reply, max);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (hwnd != 0) DestroyWindow(hwnd);
            UnregisterClass(className, wc.hInstance);
            GC.KeepAlive(proc);
        }
    }

    static void Drain(nint hwnd, Func<bool> done)
    {
        while (!done() && PeekMessage(out var msg, hwnd, 0, 0, PmRemove))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    static void PumpUntil(nint hwnd, long started, int budget, Func<bool> done)
    {
        while (!done())
        {
            while (PeekMessage(out var msg, hwnd, 0, 0, PmRemove))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
                if (done()) return;
            }
            var left = budget - (Environment.TickCount64 - started);
            if (left <= 0)
            {
                Drain(hwnd, done);
                return;
            }
            var wake = MsgWaitForMultipleObjects(0, null, false, (uint)left, QsAllInput);
            if (wake == WaitTimeout)
            {
                Drain(hwnd, done);
                return;
            }
            if (!PeekMessage(out _, hwnd, 0, 0, 0))
                Thread.Sleep(15);
        }
    }

    static IReadOnlyList<string>? TryHttp(string search, int max, int port, int timeoutMs)
    {
        var budget = Math.Clamp(timeoutMs, 50, 2000);
        var url = EverythingHttp.BuildUrl(port, search, max);
        using var cancel = new CancellationTokenSource(budget);
        try
        {
            var task = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
            if (!task.Wait(budget + 50) || !task.IsCompletedSuccessfully) return null;
            using var response = task.Result;
            if (!response.IsSuccessStatusCode) return null;
            var read = response.Content.ReadAsByteArrayAsync(cancel.Token);
            if (!read.Wait(budget + 50) || !read.IsCompletedSuccessfully) return null;
            var charset = response.Content.Headers.ContentType?.CharSet;
            return EverythingHttp.Parse(EverythingHttp.Decode(read.Result, charset), max);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static IReadOnlyList<string>? TryEs(string search, int max, int timeoutMs)
    {
        var es = FindEs();
        if (es is null) return null;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = es,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-n");
            process.StartInfo.ArgumentList.Add(max.ToString());
            process.StartInfo.ArgumentList.Add(search);
            if (!process.Start()) return null;
            var read = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            if (!read.Wait(50) || process.ExitCode != 0) return null;
            var list = new List<string>();
            foreach (var line in read.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                list.Add(line.Trim());
                if (list.Count >= max) break;
            }
            return list;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string? FindEs()
    {
        foreach (var directory in SdkDirectories())
        {
            var file = Path.Combine(directory, "es.exe");
            if (File.Exists(file)) return file;
        }
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var raw in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var file = Path.Combine(raw.Trim().Trim('"'), "es.exe");
            if (File.Exists(file)) return file;
        }
        return null;
    }

    static string? ExeIn(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var file = Path.Combine(directory.Trim().Trim('"'), "Everything.exe");
        return File.Exists(file) ? file : null;
    }

    bool SdkPresent()
    {
        foreach (var directory in SdkDirectories())
        {
            if (File.Exists(Path.Combine(directory, "Everything64.dll"))) return true;
        }
        return false;
    }

    static bool IsEverythingProcessRunning()
    {
        Process[] list;
        try
        {
            list = Process.GetProcessesByName("Everything");
        }
        catch (Exception)
        {
            return false;
        }
        try
        {
            return list.Length > 0;
        }
        finally
        {
            foreach (var process in list) process.Dispose();
        }
    }

    static nint FindClass(string className)
    {
        var hwnd = FindWindow(className, null);
        if (hwnd != 0) return hwnd;
        return FindWindowEx(HwndMessage, 0, className, null);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate void SetSearchW(string text);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void SetDword(uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate void SetBool(bool value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate bool QueryW(bool wait);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint GetDword();
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    delegate uint GetPath(uint index, StringBuilder buffer, uint capacity);
    delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CopyDataStruct
    {
        public nint dwData;
        public int cbData;
        public nint lpData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint FindWindowEx(nint parent, nint child, string className, string? windowName);

    [DllImport("user32.dll")]
    static extern bool PeekMessage(out Msg msg, nint hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    static extern nint DispatchMessage(ref Msg msg);

    [DllImport("user32.dll")]
    static extern uint MsgWaitForMultipleObjects(uint count, nint[]? handles, bool waitAll, uint milliseconds, uint wakeMask);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(nint hwnd);

    [DllImport("user32.dll")]
    static extern nint DefWindowProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint SendMessageTimeout(nint hwnd, int msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern nint LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern nint GetProcAddress(nint module, string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern nint GetModuleHandle(string? name);
}

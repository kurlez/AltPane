using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AltPane;

internal readonly record struct EverythingStatus(bool Running, bool Installed, string? Executable);

/// <summary>
/// Asks a running Everything instance for files. The DLL is used when it sits next to
/// AltPane or in Everything's install directory. Otherwise the documented IPC query is
/// used, then es.exe. A missing or hung Everything returns no rows and never blocks startup.
/// </summary>
internal sealed class EverythingClient
{
    const int CopyDataQueryW = 2;
    const int WmCopyData = 0x004A;
    const uint SmtoAbortIfHung = 0x0002;
    readonly object _gate = new();

    public EverythingStatus Probe()
    {
        var running = FindEverythingWindow() != 0;
        var exe = FindInstalledExe();
        return new EverythingStatus(running, exe is not null, exe);
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

    public IReadOnlyList<string> Query(string search, int max, int timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(search) || max <= 0) return Array.Empty<string>();
        lock (_gate)
        {
            var fromDll = TryDll(search, max);
            if (fromDll is not null) return fromDll;
            var fromIpc = TryIpc(search, max, timeoutMs);
            if (fromIpc is not null) return fromIpc;
            return TryEs(search, max, Math.Max(timeoutMs, 400));
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
        catch (Exception ex)
        {
            AppLog.Error(ex);
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

    IReadOnlyList<string>? TryIpc(string search, int max, int timeoutMs)
    {
        var everything = FindEverythingWindow();
        if (everything == 0) return null;
        byte[]? reply = null;
        nint hwnd = 0;
        var proc = new WndProc((h, msg, w, l) =>
        {
            if (msg == WmCopyData)
            {
                var data = Marshal.PtrToStructure<CopyDataStruct>(l);
                if (data.cbData > 0 && data.lpData != 0)
                {
                    var bytes = new byte[data.cbData];
                    Marshal.Copy(data.lpData, bytes, 0, bytes.Length);
                    reply = bytes;
                }
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
            var payload = BuildQuery(hwnd, search, max);
            var buffer = Marshal.AllocHGlobal(payload.Length);
            try
            {
                Marshal.Copy(payload, 0, buffer, payload.Length);
                var copy = new CopyDataStruct { dwData = new nint(CopyDataQueryW), cbData = payload.Length, lpData = buffer };
                var copyPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CopyDataStruct>());
                try
                {
                    Marshal.StructureToPtr(copy, copyPtr, false);
                    SendMessageTimeout(everything, WmCopyData, hwnd, copyPtr, SmtoAbortIfHung, (uint)Math.Max(50, timeoutMs), out _);
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
            return reply is null ? null : ParseList(reply, max);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return null;
        }
        finally
        {
            if (hwnd != 0) DestroyWindow(hwnd);
            GC.KeepAlive(proc);
        }
    }

    static byte[] BuildQuery(nint replyWindow, string search, int max)
    {
        var chars = Encoding.Unicode.GetBytes(search + "\0");
        var payload = new byte[20 + chars.Length];
        BitConverter.GetBytes(unchecked((uint)replyWindow.ToInt64())).CopyTo(payload, 0);
        BitConverter.GetBytes(0u).CopyTo(payload, 4);
        BitConverter.GetBytes(0u).CopyTo(payload, 8);
        BitConverter.GetBytes(0u).CopyTo(payload, 12);
        BitConverter.GetBytes((uint)max).CopyTo(payload, 16);
        chars.CopyTo(payload, 20);
        return payload;
    }

    static List<string>? ParseList(byte[] blob, int max)
    {
        return TryItems(blob, 16, max) ?? TryItems(blob, 8, max);
    }

    static List<string>? TryItems(byte[] blob, int header, int max)
    {
        if (blob.Length < header + 12) return null;
        var count = BitConverter.ToInt32(blob, 4);
        if (count < 0 || count > 10_000) return null;
        if (header + count * 12 > blob.Length) return null;
        var list = new List<string>(Math.Min(count, max));
        for (var i = 0; i < count && list.Count < max; i++)
        {
            var at = header + i * 12;
            var nameOffset = BitConverter.ToInt32(blob, at + 4);
            var pathOffset = BitConverter.ToInt32(blob, at + 8);
            if (nameOffset < 0 || pathOffset < 0 || nameOffset >= blob.Length || pathOffset >= blob.Length)
                return null;
            var name = ReadWide(blob, nameOffset);
            var dir = ReadWide(blob, pathOffset);
            if (name.Length == 0) continue;
            list.Add(dir.Length == 0 ? name : Path.Combine(dir, name));
        }
        return list;
    }

    static string ReadWide(byte[] blob, int offset)
    {
        var end = offset;
        while (end + 1 < blob.Length && (blob[end] != 0 || blob[end + 1] != 0))
            end += 2;
        if (end <= offset) return "";
        return Encoding.Unicode.GetString(blob, offset, end - offset);
    }

    static IReadOnlyList<string> TryEs(string search, int max, int timeoutMs)
    {
        var es = FindEs();
        if (es is null) return Array.Empty<string>();
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
            if (!process.Start()) return Array.Empty<string>();
            var read = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            if (!read.Wait(50)) return Array.Empty<string>();
            var list = new List<string>();
            foreach (var line in read.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                list.Add(line.Trim());
                if (list.Count >= max) break;
            }
            return list;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return Array.Empty<string>();
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

    static nint FindEverythingWindow() => FindWindow("EVERYTHING_IPC_WNDCLASS", null);

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

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

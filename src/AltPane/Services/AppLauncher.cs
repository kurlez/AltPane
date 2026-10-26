using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AltPane.Core.Apps;

namespace AltPane;

internal static class AppLauncher
{
    public static bool TryLaunch(AppEntry app, bool admin, out string? error)
    {
        error = null;
        try
        {
            if (app.Kind == AppKind.Store)
            {
                if (admin)
                {
                    error = "launch.adminStore";
                    return false;
                }
                return TryActivateStore(app.LaunchPath, app.Name, out error);
            }

            var useShortcut = !admin && app.Kind == AppKind.Shortcut;
            var target = useShortcut || string.IsNullOrWhiteSpace(app.ResolvedPath) ? app.LaunchPath : app.ResolvedPath;
            if (string.IsNullOrWhiteSpace(target))
            {
                error = "launch.failed";
                return false;
            }

            var info = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            };
            if (!useShortcut)
            {
                if (!string.IsNullOrWhiteSpace(app.Arguments)) info.Arguments = app.Arguments;
                if (!string.IsNullOrWhiteSpace(app.WorkingDirectory)) info.WorkingDirectory = app.WorkingDirectory;
            }
            if (admin) info.Verb = "runas";
            Process.Start(info);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "launch.cancelled";
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            error = admin ? "launch.adminFailed" : "launch.failed";
            return false;
        }
    }

    public static bool TryReveal(AppEntry app, out string? error)
    {
        error = null;
        var path = !string.IsNullOrWhiteSpace(app.ResolvedPath) ? app.ResolvedPath : app.LaunchPath;
        if (app.Kind == AppKind.Store || string.IsNullOrWhiteSpace(path) || path.Contains('!') && !path.Contains('\\') && !path.Contains(':'))
        {
            error = "launch.noLocation";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            error = "launch.noLocation";
            return false;
        }
    }

    static bool TryActivateStore(string aumid, string name, out string? error)
    {
        error = null;
        var id = aumid;
        const string prefix = "shell:AppsFolder\\";
        if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            id = id[prefix.Length..];
        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            manager.ActivateApplication(id, "", 0, out _);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:AppsFolder\\" + id,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception fallback)
            {
                AppLog.Error(fallback);
                error = "launch.failed";
                _ = name;
                return false;
            }
        }
    }
}

[ComImport]
[Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager
{
    void ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        uint options,
        out uint processId);
}

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
class ApplicationActivationManager
{
}

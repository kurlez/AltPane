using System.Runtime.InteropServices;
using AltPane.Core.Dialogs;

namespace AltPane;

/// <summary>Folders open in File Explorer. Called only while a file dialog panel is showing.</summary>
internal static class ExplorerFolders
{
    static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    public static IReadOnlyList<string> Open()
    {
        var result = new List<string>();
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ShellWindowsClsid);
            if (type is null) return result;
            shell = Activator.CreateInstance(type);
            if (shell is null) return result;
            dynamic windows = shell;
            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                object? item = null;
                try
                {
                    item = windows.Item(i);
                    if (item is null) continue;
                    dynamic window = item;
                    var url = "";
                    try { url = (string?)window.LocationURL ?? ""; }
                    catch { url = ""; }
                    if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        && uri.IsFile)
                    {
                        var path = FolderPath.Normalize(uri.LocalPath);
                        if (FolderPath.IsAbsolute(path)) result.Add(path);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }
                finally
                {
                    if (item is not null && Marshal.IsComObject(item))
                        Marshal.ReleaseComObject(item);
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.ReleaseComObject(shell);
        }
        return result;
    }
}

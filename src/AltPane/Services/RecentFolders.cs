using AltPane.Core.Apps;
using AltPane.Core.Dialogs;

namespace AltPane;

/// <summary>
/// AltPane's own folder MRU, plus directory targets from the Windows Recent folder.
/// Jump-list OLE files are not parsed.
/// </summary>
internal static class RecentFolders
{
    public static IReadOnlyList<string> Read(IReadOnlyList<string> remembered, int limit = 12)
    {
        var fromWindows = new List<string>();
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var recent = Path.Combine(root, "Microsoft", "Windows", "Recent");
            if (Directory.Exists(recent))
            {
                foreach (var file in new DirectoryInfo(recent).EnumerateFiles("*.lnk").OrderByDescending(item => item.LastWriteTimeUtc).Take(32))
                {
                    try
                    {
                        if (!ShellLinkReader.TryRead(File.ReadAllBytes(file.FullName), out var link)) continue;
                        var target = ShellLinkReader.ResolveTarget(link);
                        var path = FolderPath.Normalize(target);
                        if (!FolderPath.IsAbsolute(path) || !Directory.Exists(path)) continue;
                        fromWindows.Add(path);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error(ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        return FolderMatch.Merge(remembered, fromWindows, limit);
    }
}

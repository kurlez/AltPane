namespace AltPane.Core.Search;

public enum FileSearchSource
{
    None = 0,
    Everything,
    Builtin
}

/// <summary>
/// Everything wins when a query actually connects. Otherwise the built-in index
/// is used when that setting is on. An empty Everything reply is still a connection.
/// </summary>
public static class FileSearchRouter
{
    public static FileSearchSource Choose(bool everythingEnabled, EverythingReach reach, bool builtinEnabled)
    {
        if (everythingEnabled && reach is EverythingReach.Connected or EverythingReach.ConnectedHttp)
            return FileSearchSource.Everything;
        if (builtinEnabled) return FileSearchSource.Builtin;
        return FileSearchSource.None;
    }

    public static FileSearchHint HintFor(FileSearchSource source, EverythingReach reach) => source switch
    {
        FileSearchSource.Builtin => FileSearchHint.Builtin,
        FileSearchSource.Everything when reach == EverythingReach.ConnectedHttp => FileSearchHint.Http,
        FileSearchSource.Everything => FileSearchHint.Everything,
        _ => reach switch
        {
            EverythingReach.IpcFailed => FileSearchHint.IpcFailed,
            EverythingReach.ConnectedHttp => FileSearchHint.Http,
            _ => FileSearchHint.NotRunning
        }
    };

    public static string SectionKey(FileSearchHint hint) => hint switch
    {
        FileSearchHint.Builtin => "file.builtin",
        FileSearchHint.Everything or FileSearchHint.Http => "file.everything",
        _ => "file.section"
    };
}

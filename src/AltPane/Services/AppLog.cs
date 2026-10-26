namespace AltPane;

internal static class AppLog
{
    static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(Exception exception) => Write("ERROR", exception.ToString());

    static void Write(string level, string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AltPane");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "altpane.log");
            lock (Gate)
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 1_000_000)
                    File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take down the hook.
        }
    }
}

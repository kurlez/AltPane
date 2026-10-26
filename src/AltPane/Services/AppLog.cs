namespace AltPane;

internal static class AppLog
{
    static readonly object Gate = new();

    public static bool Verbose { get; set; }

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AltPane");

    public static string LogFilePath => Path.Combine(DirectoryPath, "altpane.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);
    public static void Error(Exception exception) => Write("ERROR", exception.ToString());

    static void Write(string level, string message)
    {
        try
        {
            var directory = DirectoryPath;
            Directory.CreateDirectory(directory);
            var path = LogFilePath;
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

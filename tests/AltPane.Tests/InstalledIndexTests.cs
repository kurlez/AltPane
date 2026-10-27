using System.Text;
using AltPane.Core.Apps;
using Xunit;

namespace AltPane.Tests;

public class InstalledIndexTests
{
    const string UserMenu = @"C:\Users\liuwenfeng\AppData\Roaming\Microsoft\Windows\Start Menu";
    const string CommonMenu = @"C:\ProgramData\Microsoft\Windows\Start Menu";
    const string Junction = UserMenu + @"\程序";
    const string Denied = UserMenu + @"\Secret";
    const string UserPrograms = UserMenu + @"\Programs";
    const string CommonPrograms = CommonMenu + @"\Programs";

    [Fact]
    public void An_inaccessible_directory_and_a_junction_do_not_drop_the_other_shortcuts()
    {
        var files = new MemoryDirectory();
        files.AddDirectory(UserMenu,
            Dir(UserPrograms, "Programs"),
            Dir(Junction, "程序", reparse: true),
            Dir(Denied, "Secret"));
        files.AddDirectory(UserPrograms, files.File(UserPrograms + @"\Word.lnk", "Word.lnk", WordLink()));
        files.Deny(Denied);
        files.AddDirectory(Junction, files.File(Junction + @"\Decoy.lnk", "Decoy.lnk", TargetLink(@"C:\Decoy\ShouldNot.exe", "Decoy")));
        files.AddDirectory(CommonMenu, Dir(CommonPrograms, "Programs"));
        files.AddDirectory(CommonPrograms, files.File(CommonPrograms + @"\文字处理.lnk", "文字处理.lnk", WordLink()));

        var result = InstalledIndex.Scan(new InstalledIndexRequest
        {
            StartMenu = true,
            StartMenuRoots = new[] { UserMenu, CommonMenu }
        }, files);

        var word = Assert.Single(result.Apps);
        Assert.Equal("WINWORD", word.ProcessName);
        Assert.Contains(files.Listed, path => path.Equals(CommonPrograms, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Apps, app => app.Name == "Decoy");
        Assert.DoesNotContain(files.Listed, path => path.Equals(Junction, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Denied, files.Listed);
        Assert.Equal(1, result.SkippedFolders);
        Assert.Equal(2, result.StartMenuCount);
        Assert.Equal(0, result.DesktopCount);
        Assert.Equal(0, result.PathCount);
        Assert.Equal(0, result.StoreCount);
    }

    [Fact]
    public void One_source_failing_keeps_the_apps_from_the_others()
    {
        var files = new MemoryDirectory();
        files.Deny(UserMenu);
        var desktop = @"C:\Users\liuwenfeng\Desktop";
        files.AddDirectory(desktop, files.File(desktop + @"\Notepad.lnk", "Notepad.lnk", TargetLink(@"C:\Windows\notepad.exe", "Notepad")));
        var store = AppCatalog.Create(
            "Microsoft Word",
            "Microsoft.Office.WINWORD_8wekyb3d8bbwe!Word",
            AppKind.Store,
            AppSource.Store,
            "",
            "",
            "",
            "",
            0);
        string? failed = null;

        var result = InstalledIndex.Scan(new InstalledIndexRequest
        {
            StartMenu = true,
            Desktop = true,
            Store = true,
            StartMenuRoots = new[] { UserMenu },
            DesktopRoots = new[] { desktop },
            StoreApps = () => new[] { store },
            OnSourceFailed = (name, _) => failed = name
        }, files);

        Assert.Null(failed);
        Assert.Contains(result.Apps, app => app.Source == AppSource.Desktop && app.ProcessName == "notepad");
        Assert.Contains(result.Apps, app => app.Source == AppSource.Store && app.Name == "Microsoft Word");
        Assert.True(result.SkippedFolders >= 1);
        Assert.Equal(0, result.StartMenuCount);
        Assert.Equal(1, result.DesktopCount);
        Assert.Equal(1, result.StoreCount);
    }

    [Fact]
    public void Word_matches_a_localized_name_and_the_english_executable()
    {
        var files = new MemoryDirectory();
        files.AddDirectory(CommonMenu, Dir(CommonPrograms, "Programs"));
        files.AddDirectory(CommonPrograms,
            files.File(CommonPrograms + @"\Microsoft Word.lnk", "Microsoft Word.lnk", WordLink()),
            files.File(CommonPrograms + @"\文字处理.lnk", "文字处理.lnk", WordLink()));

        var found = ShortcutWalk.Collect(new[] { CommonMenu }, AppSource.StartMenu, files, recursive: true, extension: ".lnk").Apps;
        Assert.Equal(2, found.Count);

        var word = Rank(found, "word");
        Assert.Contains(word, hit => hit.App.Name == "Microsoft Word");
        Assert.Contains(word, hit => hit.App.Name == "文字处理" && hit.App.ProcessName == "WINWORD");

        var exe = Rank(found, "winword");
        Assert.Equal(2, exe.Count);
        Assert.All(exe, hit => Assert.Equal("WINWORD", hit.App.ProcessName));

        var localized = Rank(found, "文字");
        Assert.Single(localized);
        Assert.Equal("文字处理", localized[0].App.Name);

        var merged = InstalledIndex.Scan(new InstalledIndexRequest
        {
            StartMenu = true,
            StartMenuRoots = new[] { CommonMenu }
        }, files);
        Assert.Contains(Rank(merged.Apps, "word"), hit => hit.App.ProcessName == "WINWORD");
        Assert.Contains(Rank(merged.Apps, "winword"), hit => hit.App.ProcessName == "WINWORD");
    }

    [Fact]
    public void A_real_directory_skips_a_symlink_and_an_unreadable_child()
    {
        var root = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        var outside = root + "-outside";
        var programs = Path.Combine(root, "Programs");
        var junction = Path.Combine(root, "程序");
        var denied = Path.Combine(root, "denied");
        Directory.CreateDirectory(programs);
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(denied);
        File.WriteAllBytes(Path.Combine(programs, "Microsoft Word.lnk"), WordLink());
        File.WriteAllBytes(Path.Combine(outside, "Decoy.lnk"), TargetLink(@"C:\Decoy\ShouldNot.exe", "Decoy"));
        var symlink = TryLink(junction, outside);
        TryLock(denied);
        try
        {
            var result = InstalledIndex.Scan(new InstalledIndexRequest
            {
                StartMenu = true,
                StartMenuRoots = new[] { root }
            }, new PhysicalAppDirectory());

            Assert.Contains(result.Apps, app => app.Name == "Microsoft Word" && app.ProcessName == "WINWORD");
            if (symlink)
                Assert.DoesNotContain(result.Apps, app => app.Name == "Decoy");
            Assert.DoesNotContain(result.Apps, app => app.LaunchPath.Contains("denied", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryUnlock(denied);
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    static List<AppHit> Rank(IReadOnlyList<AppEntry> apps, string query) =>
        AppCatalog.Rank(apps, query, null, Array.Empty<AltPane.Core.Models.WindowEntry>(), Array.Empty<AltPane.Core.Models.WindowEntry>(), null, DateTime.UnixEpoch);

    static AppDirEntry Dir(string path, string name, bool reparse = false) =>
        new(path, name, true, reparse, false);

    static byte[] WordLink() => TargetLink(@"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", "WINWORD");

    static byte[] TargetLink(string target, string name)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(0x4C);
        writer.Write(new byte[16]);
        writer.Write(0x2u | 0x4u | 0x80u);
        writer.Write(0);
        writer.Write(new byte[24]);
        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0);
        writer.Write(0);
        var pathBytes = Encoding.ASCII.GetBytes(target + "\0");
        var infoSize = 0x1C + pathBytes.Length + 1;
        writer.Write(infoSize);
        writer.Write(0x1C);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0x1Cu);
        writer.Write(0u);
        writer.Write((uint)(0x1C + pathBytes.Length));
        writer.Write(pathBytes);
        writer.Write((byte)0);
        var chars = name + "\0";
        writer.Write((ushort)chars.Length);
        writer.Write(Encoding.Unicode.GetBytes(chars));
        writer.Flush();
        return stream.ToArray();
    }

    static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    static bool TryLock(string directory)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return false;
            File.SetUnixFileMode(directory, UnixFileMode.None);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void TryUnlock(string directory)
    {
        try
        {
            if (!OperatingSystem.IsWindows() && Directory.Exists(directory))
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // The temp directory is best-effort.
        }
    }

    sealed class MemoryDirectory : IAppDirectory
    {
        readonly Dictionary<string, IReadOnlyList<AppDirEntry>> _dirs = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Listed { get; } = new();

        public void AddDirectory(string path, params AppDirEntry[] children) => _dirs[path] = children;

        public void Deny(string path) => _denied.Add(path);

        public IReadOnlyList<AppDirEntry> List(string directory)
        {
            Listed.Add(directory);
            if (_denied.Contains(directory))
                throw new UnauthorizedAccessException(directory);
            if (!_dirs.TryGetValue(directory, out var children))
                throw new DirectoryNotFoundException(directory);
            return children;
        }

        public byte[] ReadAllBytes(string file)
        {
            if (!_files.TryGetValue(file, out var bytes))
                throw new UnauthorizedAccessException(file);
            return bytes;
        }

        public AppDirEntry File(string path, string name, byte[] bytes)
        {
            _files[path] = bytes;
            return new AppDirEntry(path, name, false, false, false);
        }
    }
}

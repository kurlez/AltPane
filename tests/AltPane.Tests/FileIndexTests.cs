using AltPane.Core.Apps;
using AltPane.Core.Models;
using AltPane.Core.Search;
using AltPane.Core.Settings;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class FileIndexTests
{
    [Fact]
    public void Search_is_a_case_insensitive_substring_and_and_combines_terms()
    {
        var index = Index(
            (@"C:\Users\a\Documents", "Annual Report.pdf"),
            (@"C:\Users\a\Documents", "annual.pdf"),
            (@"C:\Users\a\Documents", "NOTES.TXT"));

        var both = FileIndexQuery.SearchQuery(index, "annual report", null, null);
        var hit = Assert.Single(both);
        Assert.Equal("Annual Report.pdf", hit.Name);
        Assert.Equal(@"C:\Users\a\Documents\Annual Report.pdf", hit.FullPath);

        var notes = FileIndexQuery.SearchQuery(index, "notes", null, null);
        Assert.Equal("NOTES.TXT", Assert.Single(notes).Name);
        Assert.Empty(FileIndexQuery.SearchQuery(index, "annual missing", null, null));
    }

    [Fact]
    public void Document_filter_stays_unless_the_f_prefix_asks_for_every_type()
    {
        var index = Index(
            (@"C:\Docs", "notes.pdf"),
            (@"C:\Docs", "notes.exe"),
            (@"C:\Docs", "photo.png"));

        var documents = FileIndexQuery.SearchQuery(index, "notes", "pdf,txt", null);
        Assert.Equal("notes.pdf", Assert.Single(documents).Name);

        var all = FileIndexQuery.SearchQuery(index, "f notes", "pdf,txt", null);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, hit => hit.Name == "notes.exe");

        var filePrefix = FileIndexQuery.SearchQuery(index, "file:photo", "pdf", null);
        Assert.Equal("photo.png", Assert.Single(filePrefix).Name);
    }

    [Fact]
    public void Pinyin_initials_match_a_chinese_file_name()
    {
        var index = Index((@"C:\Docs", "音乐.pdf"), (@"C:\Docs", "报告.docx"));
        var musicInitials = Pinyin.PrimaryInitials("音乐");
        Assert.True(musicInitials.Length >= 2);
        Assert.Contains(FileIndexQuery.SearchQuery(index, musicInitials, null, null), hit => hit.Name == "音乐.pdf");
        Assert.Contains(FileIndexQuery.SearchQuery(index, "yy", null, null), hit => hit.Name == "音乐.pdf");
        var reportInitials = Pinyin.PrimaryInitials("报告");
        Assert.Contains(FileIndexQuery.SearchQuery(index, reportInitials, null, null), hit => hit.Name == "报告.docx");
        Assert.True(FileIndexQuery.MatchesName("年度报告.docx", reportInitials));
    }

    [Fact]
    public void Scan_skips_hidden_system_appdata_and_agent_directories()
    {
        var tree = new MapTree();
        tree.Dir(@"C:\Users\a",
            Dir("Documents"),
            Dir("AppData", hidden: true),
            Dir(".cursor"),
            Dir("node_modules"),
            Dir(".hidden", hidden: true),
            Dir("System", system: true),
            Leaf("readme.txt"));
        tree.Dir(@"C:\Users\a\Documents", Leaf("report.pdf"), Dir("nested"));
        tree.Dir(@"C:\Users\a\Documents\nested", Leaf("deep.md"));
        tree.Dir(@"C:\Users\a\AppData", Dir("Roaming"));
        tree.Dir(@"C:\Users\a\AppData\Roaming", Leaf("secret.txt"));
        tree.Dir(@"C:\Users\a\.cursor", Leaf("chat.md"));
        tree.Dir(@"C:\Users\a\node_modules", Leaf("index.js"));
        tree.Dir(@"C:\Users\a\.hidden", Leaf("nope.txt"));
        tree.Dir(@"C:\Users\a\System", Leaf("sys.dat"));
        tree.Dir(@"C:\Users\a\Start Menu", Leaf("Code.lnk"));

        var result = FileIndexScan.Run(new[] { @"C:\Users\a", @"C:\Users\a\Start Menu" }, tree, new[] { @"D:\Secret" });
        var names = result.Index.Files.Select(slot => slot.Name).ToArray();
        Assert.Contains("report.pdf", names);
        Assert.Contains("deep.md", names);
        Assert.Contains("readme.txt", names);
        Assert.Contains("Code.lnk", names);
        Assert.DoesNotContain("secret.txt", names);
        Assert.DoesNotContain("chat.md", names);
        Assert.DoesNotContain("index.js", names);
        Assert.DoesNotContain("nope.txt", names);
        Assert.DoesNotContain("sys.dat", names);
        Assert.True(result.SkippedDirectories >= 4);
    }

    [Fact]
    public void User_exclusions_drop_indexed_hits_and_skipped_folders()
    {
        var tree = new MapTree();
        tree.Dir(@"C:\Work", Dir("Secret"), Dir("Public"));
        tree.Dir(@"C:\Work\Secret", Leaf("plan.pdf"));
        tree.Dir(@"C:\Work\Public", Leaf("plan.pdf"));
        var result = FileIndexScan.Run(new[] { @"C:\Work" }, tree, new[] { @"C:\Work\Secret" });
        var hits = FileIndexQuery.Search(result.Index, "plan", new[] { "pdf" }, new[] { @"C:\Work\Secret" });
        var hit = Assert.Single(hits);
        Assert.Equal(@"C:\Work\Public\plan.pdf", hit.FullPath);
        Assert.True(FileIndexPaths.IsExcluded(@"C:\Work\Secret\plan.pdf", new[] { @"C:\Work\Secret" }));
        Assert.True(FileSearch.IsExcluded(@"C:\Users\a\.claude\notes.md"));
    }

    [Fact]
    public void The_index_shares_one_directory_slot_and_round_trips_the_snapshot()
    {
        var index = Index(
            (@"C:\Docs", "a.pdf"),
            (@"C:\Docs", "b.pdf"));
        Assert.Equal(2, index.Count);
        Assert.Equal(1, index.DirectoryCount);
        Assert.Equal(index.Files[0].DirectoryId, index.Files[1].DirectoryId);
        var full = (@"C:\Docs\a.pdf".Length + @"C:\Docs\b.pdf".Length) * 2;
        Assert.True(index.EstimatedBytes < full + 400);

        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "file-index.bin");
        FileIndexStore.Save(path, index);
        var loaded = FileIndexStore.Load(path);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(@"C:\Docs\b.pdf", loaded.FullPath(loaded.Files[1]));
        Assert.Equal(FileIndex.Empty.Count, FileIndexStore.Load(path + ".missing").Count);
    }

    [Fact]
    public void Roots_keep_start_menu_under_appdata_and_drop_folders_the_profile_already_walks()
    {
        bool Skip(string path) =>
            path.Contains(@"\AppData", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\.cursor", StringComparison.OrdinalIgnoreCase);

        var roots = FileIndexRoots.Collect(
            @"C:\Users\a",
            new[] { @"C:\Users\a\Desktop", @"D:\Documents" },
            new[] { @"C:\Users\a\OneDrive", @"E:\OneDrive" },
            new[] { @"C:\Users\a\AppData\Roaming\Microsoft\Windows\Start Menu" },
            new[] { @"D:\Extra" },
            Skip);

        Assert.Contains(@"C:\Users\a", roots);
        Assert.Contains(@"D:\Documents", roots);
        Assert.Contains(@"E:\OneDrive", roots);
        Assert.Contains(@"D:\Extra", roots);
        Assert.Contains(@"C:\Users\a\AppData\Roaming\Microsoft\Windows\Start Menu", roots);
        Assert.DoesNotContain(@"C:\Users\a\Desktop", roots);
        Assert.DoesNotContain(@"C:\Users\a\OneDrive", roots);
    }

    [Fact]
    public void Watcher_changes_debounce_and_overflow_rescans_while_appdata_is_ignored()
    {
        Assert.False(FileIndexWatch.IsOverflow(null));
        Assert.False(FileIndexWatch.IsOverflow(new IOException("disk")));
        Assert.True(FileIndexWatch.IsOverflow(new InternalBufferOverflowException()));
        Assert.Equal(400, FileIndexWatch.DebounceMilliseconds);

        var roots = new[] { @"C:\Users\a\AppData\Roaming\Microsoft\Windows\Start Menu" };
        Assert.True(FileIndexWatch.Ignore(@"C:\Users\a\AppData\Local\Temp\x.tmp", roots, null));
        Assert.False(FileIndexWatch.Ignore(@"C:\Users\a\AppData\Roaming\Microsoft\Windows\Start Menu\Code.lnk", roots, null));
        Assert.False(FileIndexWatch.Ignore(@"C:\Users\a\Documents\a.pdf", roots, null));
        Assert.True(FileIndexWatch.Ignore(@"C:\Users\a\Documents\.cursor\a.md", roots, null));
        Assert.True(FileIndexWatch.Ignore(@"D:\Secret\a.pdf", roots, new[] { @"D:\Secret" }));
    }

    [Fact]
    public void Routing_uses_everything_only_when_it_answers()
    {
        Assert.Equal(FileSearchSource.Everything, FileSearchRouter.Choose(true, EverythingReach.Connected, true));
        Assert.Equal(FileSearchSource.Everything, FileSearchRouter.Choose(true, EverythingReach.ConnectedHttp, true));
        Assert.Equal(FileSearchSource.Builtin, FileSearchRouter.Choose(true, EverythingReach.NotRunning, true));
        Assert.Equal(FileSearchSource.Builtin, FileSearchRouter.Choose(true, EverythingReach.IpcFailed, true));
        Assert.Equal(FileSearchSource.Builtin, FileSearchRouter.Choose(false, EverythingReach.Connected, true));
        Assert.Equal(FileSearchSource.None, FileSearchRouter.Choose(true, EverythingReach.NotRunning, false));
        Assert.Equal(FileSearchSource.None, FileSearchRouter.Choose(false, EverythingReach.IpcFailed, false));

        Assert.Equal(FileSearchHint.Everything, FileSearchRouter.HintFor(FileSearchSource.Everything, EverythingReach.Connected));
        Assert.Equal(FileSearchHint.Http, FileSearchRouter.HintFor(FileSearchSource.Everything, EverythingReach.ConnectedHttp));
        Assert.Equal(FileSearchHint.Builtin, FileSearchRouter.HintFor(FileSearchSource.Builtin, EverythingReach.NotRunning));
        Assert.Equal(FileSearchHint.IpcFailed, FileSearchRouter.HintFor(FileSearchSource.None, EverythingReach.IpcFailed));
        Assert.Equal("file.builtin", FileSearchRouter.SectionKey(FileSearchHint.Builtin));
        Assert.Equal("file.everything", FileSearchRouter.SectionKey(FileSearchHint.Everything));
        Assert.Equal("file.everything", FileSearchRouter.SectionKey(FileSearchHint.Http));
        Assert.Equal("内置索引", AltPane.Core.Localization.StringTables.Chinese["file.builtin"]);
        Assert.Equal("Everything", AltPane.Core.Localization.StringTables.Chinese["file.everything"]);
    }

    [Fact]
    public void Builtin_hits_stay_in_the_session_and_excluded_rows_do_not()
    {
        var session = new SwitcherSession(Array.Empty<WindowEntry>());
        session.SetFiles(new[]
        {
            new FileHit(@"C:\Docs\a.pdf", "a.pdf", @"C:\Docs"),
            new FileHit(@"C:\Users\a\.cursor\secret.pdf", "secret.pdf", @"C:\Users\a\.cursor")
        }, FileSearchHint.Builtin);
        var hit = Assert.Single(session.Files);
        Assert.Equal("a.pdf", hit.Name);
        Assert.False(session.FileSearchUnavailable);
        Assert.Equal(FileSearchHint.Builtin, session.FileHint);

        session.SetFiles(Array.Empty<FileHit>(), FileSearchHint.NotRunning);
        Assert.True(session.FileSearchUnavailable);
    }

    [Fact]
    public void Builtin_index_defaults_on_and_extra_folders_round_trip()
    {
        var fresh = AppSettings.CreateDefault();
        Assert.True(fresh.UseBuiltinFileIndex);
        Assert.False(fresh.OpenOnWinKey);
        Assert.Empty(fresh.BuiltinIndexFolders);

        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "version": 7 }""");
        var old = SettingsStore.Load(path);
        Assert.True(old.Settings.UseBuiltinFileIndex);
        Assert.False(old.Settings.OpenOnWinKey);

        fresh.UseBuiltinFileIndex = false;
        fresh.BuiltinIndexFolders = new List<string> { @"D:\Notes", @"d:\notes", "  " };
        fresh.OpenOnWinKey = true;
        SettingsStore.Save(path, fresh);
        var loaded = SettingsStore.Load(path);
        Assert.False(loaded.Settings.UseBuiltinFileIndex);
        Assert.True(loaded.Settings.OpenOnWinKey);
        Assert.Equal(new[] { @"D:\Notes" }, loaded.Settings.BuiltinIndexFolders);
    }

    static FileIndex Index(params (string Directory, string Name)[] files) =>
        FileIndexScan.Run(
            files.Select(file => file.Directory).Distinct(StringComparer.OrdinalIgnoreCase),
            MapFrom(files),
            null).Index;

    static MapTree MapFrom(IEnumerable<(string Directory, string Name)> files)
    {
        var tree = new MapTree();
        foreach (var group in files.GroupBy(file => file.Directory, StringComparer.OrdinalIgnoreCase))
            tree.Dir(group.Key, group.Select(file => Leaf(file.Name)).ToArray());
        return tree;
    }

    static FileNode Dir(string name, bool hidden = false, bool system = false) =>
        new(name, true, hidden, system, false);

    static FileNode Leaf(string name) => new(name, false, false, false, false);

    sealed class MapTree : IFileTree
    {
        readonly Dictionary<string, List<FileNode>> _dirs = new(StringComparer.OrdinalIgnoreCase);

        public void Dir(string path, params FileNode[] children)
        {
            var key = FileIndexPaths.Normalize(path);
            if (!_dirs.TryGetValue(key, out var list))
            {
                list = new List<FileNode>();
                _dirs[key] = list;
            }
            list.AddRange(children);
        }

        public bool Exists(string directory) => _dirs.ContainsKey(FileIndexPaths.Normalize(directory));

        public IReadOnlyList<FileNode> List(string directory)
        {
            if (!_dirs.TryGetValue(FileIndexPaths.Normalize(directory), out var list))
                throw new DirectoryNotFoundException(directory);
            return list;
        }
    }
}

public class AppsModeListTests
{
    [Fact]
    public void Apps_mode_lists_start_menu_and_store_apps_and_filters_pinyin()
    {
        var apps = new[]
        {
            App("Visual Studio Code", AppSource.StartMenu),
            App("微信", AppSource.Store),
            App("Notepad", AppSource.Desktop),
            App("cmd", AppSource.Path)
        };
        var all = AppsModeList.List(apps, "", Array.Empty<WindowEntry>(), null);
        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { "Visual Studio Code", "微信" }, all.Select(hit => hit.App.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray());

        var code = AppsModeList.List(apps, "vsc", Array.Empty<WindowEntry>(), null);
        Assert.Equal("Visual Studio Code", Assert.Single(code).App.Name);
        var wechat = AppsModeList.List(apps, "wx", Array.Empty<WindowEntry>(), null);
        Assert.Equal("微信", Assert.Single(wechat).App.Name);

        var session = new SwitcherSession(Array.Empty<WindowEntry>());
        session.SetAppsMode(true);
        session.SetCatalog(apps, null, null, enabled: true);
        Assert.True(session.AppsMode);
        Assert.Empty(session.Filtered);
        Assert.Equal(2, session.AppHits.Count);
        session.SetQuery("weixin");
        Assert.Equal("微信", Assert.Single(session.AppHits).App.Name);
    }

    static AppEntry App(string name, AppSource source) => new()
    {
        Id = source + ":" + name,
        Name = name,
        LaunchPath = name + ".lnk",
        Kind = source == AppSource.Store ? AppKind.Store : AppKind.Shortcut,
        Source = source,
        ProcessName = name
    };
}

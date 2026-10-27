using System.Text;
using AltPane.Core.Apps;
using AltPane.Core.Models;
using AltPane.Core.Search;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class PinyinTests
{
    [Fact]
    public void Chinese_names_match_full_pinyin_and_initials()
    {
        Assert.Equal("wei", Pinyin.PrimarySpell("微"));
        Assert.Equal("xin", Pinyin.PrimarySpell("信"));
        Assert.True(Pinyin.SpellHit("微信", "weixin"));
        Assert.True(Pinyin.SpellHit("微信", "wei"));
        Assert.True(Pinyin.InitialsHit("微信", "wx"));
        Assert.True(Pinyin.Matches("微信", "wx"));
        Assert.True(Pinyin.SpellHit("音乐", "yinyue"));
        Assert.True(Pinyin.InitialsHit("音乐", "yy"));
        Assert.True(Pinyin.SpellHit("女", "nv"));
        Assert.True(Pinyin.SpellHit("女", "nu"));
        Assert.False(Pinyin.InitialsHit("微信", "w"));
        Assert.False(Pinyin.Matches("记事本", "zz"));
    }

    [Fact]
    public void Latin_initials_follow_word_starts()
    {
        Assert.True(Pinyin.InitialsHit("Visual Studio Code", "vsc"));
        Assert.Equal("vsc", Pinyin.PrimaryInitials("Visual Studio Code"));
        Assert.False(Pinyin.InitialsHit("Visual Studio Code", "zzz"));
    }
}

public class AppMatchTests
{
    static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    static AppEntry App(string name, string process, AppSource source = AppSource.StartMenu, string? id = null) =>
        new()
        {
            Id = id ?? "exe:" + process.ToLowerInvariant(),
            Name = name,
            LaunchPath = @"C:\Apps\" + process + ".lnk",
            Kind = AppKind.Shortcut,
            Source = source,
            ResolvedPath = @"C:\Apps\" + process + ".exe",
            ProcessName = process
        };

    static WindowEntry Win(int index, string title, string process) => new()
    {
        Handle = index,
        Title = title,
        ClassName = "C",
        ProcessName = process,
        DisplayProcessName = process,
        ProcessId = index,
        KillProcessId = index,
        KillProcessName = process,
        OriginalIndex = index
    };

    [Fact]
    public void Acronym_pinyin_and_fuzzy_names_rank()
    {
        var apps = new[]
        {
            App("Visual Studio Code", "Code"),
            App("微信", "WeChat"),
            App("Notepad", "notepad")
        };
        var code = AppCatalog.Rank(apps, "vsc", null, Array.Empty<WindowEntry>(), Array.Empty<WindowEntry>(), null, Now);
        Assert.Equal("Visual Studio Code", Assert.Single(code).App.Name);

        var vscode = AppCatalog.Rank(apps, "vscode", null, Array.Empty<WindowEntry>(), Array.Empty<WindowEntry>(), null, Now);
        Assert.Equal("Visual Studio Code", Assert.Single(vscode).App.Name);

        var weixin = AppCatalog.Rank(apps, "weixin", null, Array.Empty<WindowEntry>(), Array.Empty<WindowEntry>(), null, Now);
        Assert.Equal("微信", Assert.Single(weixin).App.Name);
        var initials = AppCatalog.Rank(apps, "wx", null, Array.Empty<WindowEntry>(), Array.Empty<WindowEntry>(), null, Now);
        Assert.Equal("微信", Assert.Single(initials).App.Name);

        Assert.True(SearchMatcher.Matches("wx", "微信", "WeChat"));
        Assert.True(SearchMatcher.Matches("weixin", "微信", "WeChat"));
    }

    [Fact]
    public void Frequency_breaks_ties_but_does_not_beat_a_much_better_match()
    {
        var rare = App("Alpha Tool", "alpha");
        var often = App("Alpine Notes", "alpine", id: "exe:alpine");
        var usage = new Dictionary<string, LaunchMemory>
        {
            ["exe:alpine"] = new() { Count = 12, LastUtcTicks = Now.AddHours(-1).Ticks }
        };
        var tied = AppCatalog.Rank(new[] { rare, often }, "alp", usage, Array.Empty<WindowEntry>(), Array.Empty<WindowEntry>(), null, Now);
        Assert.Equal("Alpine Notes", tied[0].App.Name);

        var exact = App("Note", "note", id: "exe:note");
        var fuzzy = App("Noodle Top Extra", "noodle", id: "exe:noodle");
        var heavy = new Dictionary<string, LaunchMemory>
        {
            ["exe:noodle"] = new() { Count = 40, LastUtcTicks = Now.Ticks }
        };
        var ranked = AppCatalog.Rank(new[] { fuzzy, exact }, "note", heavy, Array.Empty<WindowEntry>(), Array.Empty<WindowEntry>(), null, Now);
        Assert.Equal("Note", ranked[0].App.Name);
    }

    [Fact]
    public void Open_windows_are_preferred_and_hidden_when_already_listed()
    {
        var app = App("Visual Studio Code", "Code");
        var windows = new[] { Win(1, "README.md", "Code"), Win(2, "Other", "notepad") };
        var listed = AppCatalog.Rank(new[] { app }, "vsc", null, windows, Array.Empty<WindowEntry>(), null, Now);
        var hit = Assert.Single(listed);
        Assert.Equal(1, hit.SwitchHandle);

        var hidden = AppCatalog.Rank(new[] { app }, "vsc", null, windows, windows, null, Now);
        Assert.Empty(hidden);
    }

    [Fact]
    public void Exclusions_paths_and_merge_prefer_start_menu()
    {
        var menu = App("Visual Studio Code", "Code", AppSource.StartMenu);
        var desktop = App("Code", "Code", AppSource.Desktop, id: menu.Id);
        desktop.LaunchPath = @"C:\Users\Public\Desktop\Code.lnk";
        var merged = AppCatalog.Merge(new[] { desktop, menu });
        var only = Assert.Single(merged);
        Assert.Equal(AppSource.StartMenu, only.Source);
        Assert.Equal("Visual Studio Code", only.Name);

        Assert.True(AppCatalog.IsExcluded(menu, new[] { "code" }));
        Assert.True(AppCatalog.IsExcluded(menu, new[] { @"C:\Apps" }));
        Assert.False(AppCatalog.IsExcluded(menu, new[] { "chrome" }));
        Assert.True(AppCatalog.IsNoisyPathExecutable(@"C:\Windows\System32\notepad.exe"));
        Assert.False(AppCatalog.IsNoisyPathExecutable(@"C:\Tools\rg.exe"));
    }

    [Fact]
    public void Incremental_upsert_replaces_one_shortcut_and_history_round_trips()
    {
        var first = App("Old", "old", id: "path:old");
        first.Kind = AppKind.Shortcut;
        first.ResolvedPath = "";
        var second = App("New", "new", id: "path:new");
        second.ResolvedPath = "";
        var updated = App("Old Renamed", "old", id: "path:old");
        updated.LaunchPath = first.LaunchPath;
        updated.ResolvedPath = "";
        var list = AppCatalog.Upsert(AppCatalog.Upsert(Array.Empty<AppEntry>(), first), second);
        list = AppCatalog.Upsert(list, updated);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, app => app.Name == "Old Renamed");
        list = AppCatalog.RemoveLaunchPath(list, first.LaunchPath);
        Assert.Equal("New", Assert.Single(list).Name);

        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "apps.json");
        AppCatalog.SaveIndex(path, list);
        var loaded = AppCatalog.LoadIndex(path);
        Assert.Equal("New", Assert.Single(loaded).Name);

        var historyPath = Path.Combine(Path.GetDirectoryName(path)!, "launches.json");
        var usage = AppCatalog.Remember(new Dictionary<string, LaunchMemory>(), loaded[0].Id, Now);
        AppCatalog.SaveHistory(historyPath, usage);
        var again = AppCatalog.LoadHistory(historyPath);
        Assert.Equal(1, again[loaded[0].Id].Count);
        Assert.Empty(AppCatalog.LoadIndex(path + ".missing"));
    }

    [Fact]
    public void Empty_query_does_not_list_the_whole_catalog()
    {
        var hits = AppCatalog.Rank(new[] { App("Notepad", "notepad") }, "  ", null, Array.Empty<WindowEntry>(), Array.Empty<WindowEntry>(), null, Now);
        Assert.Empty(hits);
    }
}

public class ShellLinkTests
{
    [Fact]
    public void Shortcut_blob_exposes_target_arguments_and_icon()
    {
        var bytes = Link(
            flags: 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x20 | 0x40 | 0x80,
            iconIndex: 3,
            idList: new byte[] { 0, 0 },
            localPath: @"C:\Apps\Code.exe",
            name: "Visual Studio Code",
            relative: "Code.exe",
            working: @"C:\Apps",
            arguments: "--reuse-window",
            icon: @"C:\Apps\Code.exe");
        Assert.True(ShellLinkReader.TryRead(bytes, out var link));
        Assert.Equal("Visual Studio Code", link.Name);
        Assert.Equal(@"C:\Apps\Code.exe", link.LocalBasePath);
        Assert.Equal("Code.exe", link.RelativePath);
        Assert.Equal(@"C:\Apps", link.WorkingDirectory);
        Assert.Equal("--reuse-window", link.Arguments);
        Assert.Equal(@"C:\Apps\Code.exe", link.IconLocation);
        Assert.Equal(3, link.IconIndex);
        Assert.Equal(@"C:\Apps\Code.exe", ShellLinkReader.ResolveTarget(link));

        var entry = AppCatalog.FromShortcut(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Visual Studio Code.lnk", link, AppSource.StartMenu);
        Assert.NotNull(entry);
        Assert.Equal("Visual Studio Code", entry!.Name);
        Assert.Equal("Code", entry.ProcessName);
        Assert.Equal(AppKind.Shortcut, entry.Kind);
        Assert.StartsWith("exe:", entry.Id);
    }

    [Fact]
    public void Environment_block_supplies_a_target_when_link_info_is_absent()
    {
        var bytes = Link(
            flags: 0x80,
            iconIndex: 0,
            idList: null,
            localPath: null,
            name: null,
            relative: null,
            working: null,
            arguments: null,
            icon: null,
            environment: @"D:\Tools\app.exe");
        Assert.True(ShellLinkReader.TryRead(bytes, out var link));
        Assert.Equal(@"D:\Tools\app.exe", link.EnvironmentTarget);
        Assert.Equal(@"D:\Tools\app.exe", ShellLinkReader.ResolveTarget(link));
        Assert.False(ShellLinkReader.TryRead(new byte[] { 1, 2, 3 }, out _));
    }

    static byte[] Link(
        uint flags,
        int iconIndex,
        byte[]? idList,
        string? localPath,
        string? name,
        string? relative,
        string? working,
        string? arguments,
        string? icon,
        string? environment = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true);
        writer.Write(0x4C);
        writer.Write(new byte[16]);
        writer.Write(flags);
        writer.Write(0);
        writer.Write(new byte[24]);
        writer.Write(0);
        writer.Write(iconIndex);
        writer.Write(1);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0);
        writer.Write(0);
        if (idList is not null)
        {
            writer.Write((ushort)idList.Length);
            writer.Write(idList);
        }
        if (localPath is not null)
        {
            var pathBytes = Encoding.ASCII.GetBytes(localPath + "\0");
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
        }
        WriteString(writer, name);
        WriteString(writer, relative);
        WriteString(writer, working);
        WriteString(writer, arguments);
        WriteString(writer, icon);
        if (environment is not null)
        {
            writer.Write(0x314);
            writer.Write(0xA0000001);
            writer.Write(new byte[260]);
            var chars = Encoding.Unicode.GetBytes(environment);
            var unicode = new byte[520];
            Buffer.BlockCopy(chars, 0, unicode, 0, Math.Min(chars.Length, 518));
            writer.Write(unicode);
        }
        writer.Flush();
        return stream.ToArray();
    }

    static void WriteString(BinaryWriter writer, string? value)
    {
        if (value is null) return;
        var chars = value + "\0";
        writer.Write((ushort)chars.Length);
        writer.Write(Encoding.Unicode.GetBytes(chars));
    }
}

public class AppSessionTests
{
    static WindowEntry Win(int index, string title, string process) => new()
    {
        Handle = index,
        Title = title,
        ClassName = "C",
        ProcessName = process,
        DisplayProcessName = process,
        ProcessId = index,
        KillProcessId = index,
        KillProcessName = process,
        OriginalIndex = index
    };

    [Fact]
    public void No_window_match_selects_the_first_app_and_navigation_stops_at_the_ends()
    {
        var session = new SwitcherSession(new[]
        {
            Win(1, "Current", "current"),
            Win(2, "Notepad", "notepad")
        });
        var code = new AppEntry
        {
            Id = "exe:code",
            Name = "Visual Studio Code",
            LaunchPath = @"C:\Apps\Code.lnk",
            Kind = AppKind.Shortcut,
            Source = AppSource.StartMenu,
            ResolvedPath = @"C:\Apps\Code.exe",
            ProcessName = "Code"
        };
        var wechat = new AppEntry
        {
            Id = "exe:wechat",
            Name = "微信",
            LaunchPath = @"C:\Apps\WeChat.lnk",
            Kind = AppKind.Shortcut,
            Source = AppSource.StartMenu,
            ResolvedPath = @"C:\Apps\WeChat.exe",
            ProcessName = "WeChat"
        };
        session.SetCatalog(new[] { code, wechat }, null, null, enabled: true, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
        session.SetQuery("vsc");
        Assert.Empty(session.Filtered);
        Assert.Equal("Visual Studio Code", session.SelectedApp!.Value.App.Name);
        Assert.Null(session.Selected);

        session.SetQuery("");
        Assert.Empty(session.AppHits);
        Assert.Equal("Current", session.Selected!.Title);
        session.SelectLast();
        var last = session.SelectedIndex;
        session.Move(1);
        Assert.Equal(last, session.SelectedIndex);

        session.SetQuery("note");
        Assert.Equal("Notepad", session.Selected!.Title);
        Assert.Empty(session.AppHits);

        session.SetCatalog(new[] { code }, null, null, enabled: true);
        session.SetQuery("c");
        Assert.Equal("Current", Assert.Single(session.Filtered).Title);
        Assert.Single(session.AppHits);
        Assert.Equal("c", session.IndexForHandle(1));
        Assert.Equal("Current", session.Selected!.Title);
        session.Move(1);
        Assert.Equal("Visual Studio Code", session.SelectedApp!.Value.App.Name);
        session.Move(1);
        Assert.Equal("Visual Studio Code", session.SelectedApp!.Value.App.Name);
        session.Move(-1);
        Assert.Equal("Current", session.Selected!.Title);
    }

    [Fact]
    public void Running_app_that_the_window_list_missed_offers_a_switch()
    {
        var session = new SwitcherSession(new[] { Win(4, "README.md", "Code") });
        var code = new AppEntry
        {
            Id = "exe:code",
            Name = "Visual Studio Code",
            LaunchPath = @"C:\Apps\Code.lnk",
            Kind = AppKind.Shortcut,
            Source = AppSource.StartMenu,
            ResolvedPath = @"C:\Apps\Code.exe",
            ProcessName = "Code"
        };
        session.SetCatalog(new[] { code }, null, null, true, DateTime.UnixEpoch);
        session.SetQuery("vsc");
        Assert.Empty(session.Filtered);
        Assert.Equal(4, session.SelectedApp!.Value.SwitchHandle);
    }
}

using AltPane.Core.Apps;
using Xunit;

namespace AltPane.Tests;

public class EverythingIndexTests
{
    [Fact]
    public void Query_quotes_operators_and_adds_extensions_and_noise_paths()
    {
        var query = EverythingIndex.Build("code !test", "exe, lnk", new[] { @"D:\Temp\Noise" });
        Assert.NotNull(query);
        Assert.Contains("code", query);
        Assert.Contains("\"!test\"", query);
        Assert.Contains("ext:exe;lnk", query);
        Assert.Contains("!path:windows\\winsxs", query);
        Assert.Contains("!path:node_modules", query);
        Assert.Contains("!path:\"D:\\Temp\\Noise\"", query);
        Assert.Null(EverythingIndex.Build("   ", "exe", null));
        Assert.Equal(EverythingIndex.DefaultExtensions, EverythingIndex.SplitExtensions(null));
        Assert.Equal(EverythingIndex.DefaultExtensions, EverythingIndex.SplitExtensions(""));
    }

    [Fact]
    public void Blank_extensions_do_not_build_a_query()
    {
        Assert.Null(EverythingIndex.Build("code", Array.Empty<string>(), null));
    }

    [Fact]
    public void Noise_is_a_path_segment_or_an_installer_stem_not_a_substring()
    {
        Assert.True(EverythingIndex.IsNoise(@"C:\Windows\WinSxS\amd64\notepad.exe"));
        Assert.True(EverythingIndex.IsNoise(@"C:\Windows\System32\notepad.exe"));
        Assert.True(EverythingIndex.IsNoise(@"C:\src\app\node_modules\tool\tool.exe"));
        Assert.True(EverythingIndex.IsNoise(@"C:\Users\a\AppData\Local\Temp\setup.exe"));
        Assert.True(EverythingIndex.IsNoise(@"D:\Apps\Chrome\chrome_updater.exe"));
        Assert.True(EverythingIndex.IsNoise(@"D:\Apps\Foo\unins000.exe"));
        Assert.False(EverythingIndex.IsNoise(@"C:\Program Files\Microsoft VS Code\Code.exe"));
        Assert.False(EverythingIndex.IsNoise(@"C:\Windows\notepad.exe"));
        Assert.False(EverythingIndex.IsNoise(@"C:\Program Files\UpdateManager\UpdateManager.exe"));
    }

    [Fact]
    public void Paths_are_classified_and_noisy_files_are_dropped()
    {
        Assert.Equal(AppSource.StartMenu, EverythingIndex.ClassifyPath(@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Code.lnk"));
        Assert.Equal(AppSource.Desktop, EverythingIndex.ClassifyPath(@"C:\Users\a\Desktop\Code.lnk"));
        Assert.Equal(AppSource.Installed, EverythingIndex.ClassifyPath(@"C:\Program Files\Mozilla Firefox\firefox.exe"));
        Assert.Equal(AppSource.Installed, EverythingIndex.ClassifyPath(@"C:\Users\a\AppData\Local\Programs\app\app.exe"));
        Assert.Equal(AppSource.Everything, EverythingIndex.ClassifyPath(@"D:\tools\ripgrep\rg.exe"));

        var code = EverythingIndex.FromFile(@"C:\Program Files\Microsoft VS Code\Code.exe");
        Assert.NotNull(code);
        Assert.Equal("Code", code!.Name);
        Assert.Equal(AppKind.Executable, code.Kind);
        Assert.Equal(AppSource.Installed, code.Source);
        Assert.Equal(2, AppCatalog.SourceRank(code.Source));
        Assert.Null(EverythingIndex.FromFile(@"C:\Windows\System32\notepad.exe"));
        var link = EverythingIndex.FromFile(@"C:\Users\a\Desktop\Notes.lnk");
        Assert.Equal(AppKind.Shortcut, link!.Kind);
    }

    [Fact]
    public void Catalog_names_win_and_duplicate_targets_collapse()
    {
        var menu = AppCatalog.Create(
            "Visual Studio Code",
            @"C:\ProgramData\Start Menu\Programs\Code.lnk",
            AppKind.Shortcut,
            AppSource.StartMenu,
            @"C:\Program Files\Microsoft VS Code\Code.exe",
            "",
            "",
            @"C:\Program Files\Microsoft VS Code\Code.exe",
            0);
        var raw = EverythingIndex.FromFile(@"C:\Program Files\Microsoft VS Code\Code.exe")!;
        var other = EverythingIndex.FromFile(@"D:\tools\rg.exe")!;
        var shortcut = AppCatalog.Create(
            "ripgrep",
            @"D:\tools\rg.lnk",
            AppKind.Shortcut,
            AppSource.Everything,
            @"D:\tools\rg.exe",
            "--smart-case",
            @"D:\tools",
            @"D:\tools\rg.exe",
            0);
        var combined = EverythingIndex.Combine(new[] { menu }, new[] { raw, other, shortcut });
        Assert.Contains(combined, app => app.Name == "Visual Studio Code");
        Assert.DoesNotContain(combined, app => app.Name == "Code" && app.Source == AppSource.Installed);
        var rg = Assert.Single(combined, app => app.ResolvedPath.EndsWith("rg.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AppKind.Shortcut, rg.Kind);
        Assert.Equal("--smart-case", rg.Arguments);
    }

    [Fact]
    public void A_start_menu_name_outranks_an_unused_program_files_hit_with_the_same_id()
    {
        var menu = AppCatalog.Create("微信", @"C:\Start Menu\微信.lnk", AppKind.Shortcut, AppSource.StartMenu, @"C:\Program Files\Tencent\Weixin\Weixin.exe", "", "", "", 0);
        var installed = AppCatalog.Create("Weixin", @"C:\Program Files\Tencent\Weixin\Weixin.exe", AppKind.Executable, AppSource.Installed, @"C:\Program Files\Tencent\Weixin\Weixin.exe", "", "", "", 0);
        var merged = AppCatalog.Merge(new[] { menu, installed });
        var kept = Assert.Single(merged);
        Assert.Equal("微信", kept.Name);
        Assert.Equal(AppSource.StartMenu, kept.Source);
    }
}

using AltPane.Core.Apps;
using AltPane.Core.Icons;
using Xunit;

namespace AltPane.Tests;

public class IconCacheKeyTests
{
    [Fact]
    public void Ordinary_files_share_an_extension_key()
    {
        var first = IconCacheKey.For(@"C:\Docs\季度.pptx");
        var second = IconCacheKey.For(@"D:\Other\DECK.PPTX");
        Assert.Equal(first, second);
        Assert.Equal("ext:.pptx", first);
        Assert.Equal(IconCacheKey.For(@"C:\a.txt"), IconCacheKey.For(@"C:\b.txt"));
    }

    [Fact]
    public void Executables_links_icons_and_urls_keep_the_full_path()
    {
        Assert.NotEqual(IconCacheKey.For(@"C:\Apps\a.exe"), IconCacheKey.For(@"C:\Apps\b.exe"));
        Assert.NotEqual(IconCacheKey.For(@"C:\a.lnk"), IconCacheKey.For(@"D:\a.lnk"));
        Assert.NotEqual(IconCacheKey.For(@"C:\a.ico"), IconCacheKey.For(@"C:\b.ico"));
        Assert.NotEqual(IconCacheKey.For(@"C:\a.url"), IconCacheKey.For(@"C:\b.url"));
        Assert.StartsWith("path:", IconCacheKey.For(@"C:\Apps\a.exe"));
    }

    [Fact]
    public void A_non_zero_icon_index_is_part_of_the_path_key()
    {
        Assert.NotEqual(IconCacheKey.For(@"C:\Windows\System32\shell32.dll", 0), IconCacheKey.For(@"C:\Windows\System32\shell32.dll", 3));
        Assert.NotEqual(IconCacheKey.For(@"C:\Apps\a.exe", 0), IconCacheKey.For(@"C:\Apps\a.exe", 2));
    }

    [Fact]
    public void Store_and_shell_paths_are_not_folded_by_extension()
    {
        var one = IconCacheKey.For(@"shell:AppsFolder\Contoso.App_123!App");
        var two = IconCacheKey.For(@"shell:AppsFolder\Fabrikam.App_123!App");
        Assert.NotEqual(one, two);
        Assert.NotEqual(IconCacheKey.For("Contoso.App_123!App"), IconCacheKey.For("Fabrikam.App_123!App"));
    }

    [Fact]
    public void The_cache_drops_the_oldest_entry_and_a_read_keeps_it()
    {
        var cache = new LruCache<int>(2);
        Assert.Null(cache.Set("a", 1));
        Assert.Null(cache.Set("b", 2));
        Assert.True(cache.TryGet("a", out var kept));
        Assert.Equal(1, kept);
        var evicted = cache.Set("c", 3);
        Assert.Equal("b", evicted);
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Rebinding_an_app_returns_the_previous_executable()
    {
        var bindings = new IconPathBindings();
        Assert.Null(bindings.Rebind("chrome", @"C:\Old\chrome.exe"));
        Assert.Null(bindings.Rebind("chrome", @"C:\Old\chrome.exe"));
        Assert.Equal(@"C:\Old\chrome.exe", bindings.Rebind("chrome", @"C:\New\chrome.exe"));
        Assert.Null(bindings.Rebind("code", @"C:\Apps\Code.exe"));
        Assert.True(IconCacheKey.SamePath(IconCacheKey.For(@"C:\Old\chrome.exe"), @"C:\Old\chrome.exe"));
        Assert.True(IconCacheKey.SamePath(IconCacheKey.For(@"C:\Old\chrome.exe", 2), @"C:\Old\chrome.exe"));
        Assert.False(IconCacheKey.SamePath(IconCacheKey.For(@"C:\Old\chrome.exe.bak"), @"C:\Old\chrome.exe"));
    }
}

public class IconPixelSizeTests
{
    [Theory]
    [InlineData(96, 32)]
    [InlineData(120, 40)]
    [InlineData(144, 48)]
    [InlineData(192, 64)]
    [InlineData(72, 32)]
    public void Requested_edge_is_at_least_32_times_the_scale(int dpi, int edge)
    {
        Assert.Equal(edge, IconPixelSize.ForDpi(dpi));
    }

    [Fact]
    public void Jumbo_is_used_above_the_48px_image_list()
    {
        Assert.False(IconPixelSize.UseJumbo(32));
        Assert.False(IconPixelSize.UseJumbo(48));
        Assert.True(IconPixelSize.UseJumbo(64));
        Assert.Equal(256, IconPixelSize.ForDpi(2000));
    }
}

public class WindowIconPlanTests
{
    [Fact]
    public void A_normal_window_prefers_its_own_icon_then_the_executable_then_the_shortcut()
    {
        var request = new WindowIconRequest(Packaged: false, SharedHost: false);
        Assert.Equal(
            new[] { IconOrigin.WindowBig, IconOrigin.ClassIcon, IconOrigin.ExeImage, IconOrigin.Shortcut },
            WindowIconPlan.Candidates(request));

        Assert.Equal(IconOrigin.WindowBig, WindowIconPlan.Select(request, origin => origin is IconOrigin.WindowBig or IconOrigin.ExeImage));
        Assert.Equal(IconOrigin.ClassIcon, WindowIconPlan.Select(request, origin => origin is IconOrigin.ClassIcon or IconOrigin.ExeImage));
        Assert.Equal(IconOrigin.ExeImage, WindowIconPlan.Select(request, origin => origin == IconOrigin.ExeImage));
        Assert.Equal(IconOrigin.Shortcut, WindowIconPlan.Select(request, origin => origin == IconOrigin.Shortcut));
        Assert.Null(WindowIconPlan.Select(request, _ => false));
    }

    [Fact]
    public void A_normal_app_skips_a_small_window_icon_when_the_executable_is_sharp()
    {
        var edges = new[] { 16, 32, 64, 0 };
        Assert.Equal(2, WindowIconPlan.Pick(edges, requested: 64, preferFirstAdequate: false));
        Assert.Equal(0, WindowIconPlan.Pick(new[] { 64, 256, 256, 0 }, requested: 64, preferFirstAdequate: false));
        Assert.Equal(1, WindowIconPlan.Pick(new[] { 16, 32, 0, 0 }, requested: 64, preferFirstAdequate: false));
    }

    [Fact]
    public void A_packaged_app_uses_the_logo_before_the_host_executable()
    {
        var request = new WindowIconRequest(Packaged: true, SharedHost: false);
        var seen = new List<IconOrigin>();
        var chosen = WindowIconPlan.Select(request, origin =>
        {
            seen.Add(origin);
            return origin == IconOrigin.PackageLogo;
        });
        Assert.Equal(IconOrigin.PackageLogo, chosen);
        Assert.Equal(IconOrigin.PackageLogo, Assert.Single(seen));

        Assert.Equal(IconOrigin.WindowBig, WindowIconPlan.Select(request, origin => origin == IconOrigin.WindowBig));
        Assert.Equal(IconOrigin.ExeImage, WindowIconPlan.Select(request, origin => origin == IconOrigin.ExeImage));
        Assert.Equal(IconOrigin.PackageLogo, WindowIconPlan.Candidates(request)[0]);
        var order = WindowIconPlan.Candidates(request);
        Assert.True(IndexOf(order, IconOrigin.PackageLogo) < IndexOf(order, IconOrigin.ExeImage));
    }

    [Fact]
    public void A_shared_host_prefers_the_window_or_relaunch_icon_over_the_executable()
    {
        var request = new WindowIconRequest(Packaged: false, SharedHost: true);
        var order = WindowIconPlan.Candidates(request);
        Assert.Equal(IconOrigin.WindowBig, order[0]);
        Assert.True(IndexOf(order, IconOrigin.Relaunch) < IndexOf(order, IconOrigin.ExeImage));
        Assert.True(IndexOf(order, IconOrigin.WindowBig) < IndexOf(order, IconOrigin.ExeImage));
        Assert.Equal(IconOrigin.Relaunch, WindowIconPlan.Select(request, origin => origin is IconOrigin.Relaunch or IconOrigin.ExeImage));
        Assert.Equal(IconOrigin.WindowBig, WindowIconPlan.Select(request, origin => origin is IconOrigin.WindowBig or IconOrigin.ExeImage));
        Assert.Equal(0, WindowIconPlan.Pick(new[] { 32, 0, 48, 0, 256 }, requested: 64, preferFirstAdequate: true));
        Assert.Equal(2, WindowIconPlan.Pick(new[] { 16, 16, 48, 0, 256 }, requested: 64, preferFirstAdequate: true));
    }

    [Fact]
    public void Host_executables_are_recognized_without_treating_every_electron_app_as_shared()
    {
        Assert.True(HostExecutables.SharesHost("chrome"));
        Assert.True(HostExecutables.SharesHost("msedge.exe"));
        Assert.True(HostExecutables.SharesHost("msedgewebview2"));
        Assert.False(HostExecutables.SharesHost("Code"));
        Assert.False(HostExecutables.SharesHost("POWERPNT"));
        Assert.True(HostExecutables.IsFrameHost("ApplicationFrameHost.exe"));
        Assert.True(HostExecutables.IsFramePath(@"C:\Windows\System32\ApplicationFrameHost.exe"));
        Assert.False(HostExecutables.SharesHost("ApplicationFrameHost"));
    }

    static int IndexOf(IReadOnlyList<IconOrigin> order, IconOrigin origin)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == origin) return i;
        }
        return -1;
    }
}

public class PackageLogoTests
{
    const string Manifest = """
        <Package xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
          <Applications>
            <Application Id="App">
              <uap:VisualElements Square44x44Logo="Assets\Square44x44Logo.png" BackgroundColor="#FF0078D7" />
            </Application>
            <Application Id="Other">
              <uap:VisualElements Square44x44Logo="Assets\Other.png" BackgroundColor="transparent" />
            </Application>
          </Applications>
        </Package>
        """;

    [Fact]
    public void The_manifest_selects_the_aumid_application_and_its_logo()
    {
        var visuals = PackageManifest.Read(Manifest);
        Assert.Equal(2, visuals.Count);
        Assert.Equal("App", PackageManifest.ApplicationId(@"shell:AppsFolder\Contoso.App_1.0.0.0_neutral__8wekyb3d8bbwe!App"));
        var selected = PackageManifest.Select(visuals, "App");
        Assert.Equal(@"Assets\Square44x44Logo.png", selected?.Square44);
        Assert.Equal("#FF0078D7", selected?.Background);
        Assert.Equal(@"Assets\Other.png", PackageManifest.Select(visuals, "Other")?.Square44);
        Assert.Equal(@"Assets\Square44x44Logo.png", PackageManifest.Select(visuals, "missing")?.Square44);
    }

    [Fact]
    public void Unplated_targetsize_wins_and_a_color_background_is_plated()
    {
        var files = new[]
        {
            @"Assets/Square44x44Logo.png",
            @"Assets/Square44x44Logo.targetsize-16_altform-unplated.png",
            @"Assets/Square44x44Logo.targetsize-32_altform-unplated.png",
            @"Assets/Square44x44Logo.targetsize-48_altform-unplated.png",
            @"Assets/Square44x44Logo.scale-200.png",
            @"Assets/Square44x44Logo.targetsize-24_contrast-white.png"
        };
        var choice = PackageLogoPicker.Choose(@"Assets\Square44x44Logo.png", "#FF0078D7", files, 40);
        Assert.NotNull(choice);
        Assert.Equal("Assets/Square44x44Logo.targetsize-48_altform-unplated.png", choice.Value.RelativePath);
        Assert.True(choice.Value.NeedsPlate);
        Assert.True(IconColor.TryParse(choice.Value.Background, out var r, out var g, out var b));
        Assert.Equal((byte)0x00, r);
        Assert.Equal((byte)0x78, g);
        Assert.Equal((byte)0xD7, b);

        var clear = PackageLogoPicker.Choose(@"Assets\Square44x44Logo.png", "transparent", files, 32);
        Assert.Equal("Assets/Square44x44Logo.targetsize-32_altform-unplated.png", clear?.RelativePath);
        Assert.False(clear?.NeedsPlate);
    }

    [Fact]
    public void An_unplated_logo_is_drawn_over_its_background()
    {
        var src = new byte[] { 0, 0, 0, 0, 255, 0, 0, 255 };
        var plated = IconPlate.Over(src, 2, 1, b: 0xD7, g: 0x78, r: 0x00, premultiplied: false);
        Assert.Equal(new byte[] { 0xD7, 0x78, 0x00, 255, 255, 0, 0, 255 }, plated);
    }
}

public class RelaunchIconTests
{
    [Fact]
    public void Relaunch_resources_keep_the_path_and_the_index()
    {
        Assert.True(RelaunchIcon.TryParse(@"C:\Program Files\Google\Chrome\Application\chrome.exe,0", out var plain));
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", plain.Path);
        Assert.Equal(0, plain.Index);

        Assert.True(RelaunchIcon.TryParse(@"%SystemRoot%\System32\imageres.dll,-102", out var negative));
        Assert.Equal(@"%SystemRoot%\System32\imageres.dll", negative.Path);
        Assert.Equal(-102, negative.Index);

        Assert.True(RelaunchIcon.TryParse("\"C:\\Program Files\\App\\icon.dll\",5", out var quoted));
        Assert.Equal(@"C:\Program Files\App\icon.dll", quoted.Path);
        Assert.Equal(5, quoted.Index);

        Assert.True(RelaunchIcon.TryParse(@"C:\Icons\app.ico", out var file));
        Assert.Equal(@"C:\Icons\app.ico", file.Path);
        Assert.Equal(0, file.Index);
        Assert.False(RelaunchIcon.TryParse("  ", out _));
    }

    [Fact]
    public void A_shortcut_custom_icon_wins_over_the_link_file()
    {
        var apps = new[]
        {
            new AppEntry
            {
                Kind = AppKind.Shortcut,
                LaunchPath = @"C:\Start\Code.lnk",
                ResolvedPath = @"C:\Apps\Code.exe",
                IconLocation = @"C:\Apps\custom.dll",
                IconIndex = 4
            }
        };
        var match = ShortcutIcon.Match(@"C:\Apps\Code.exe", apps);
        Assert.Equal(@"C:\Apps\custom.dll", match?.Path);
        Assert.Equal(4, match?.Index);

        apps[0].IconLocation = @"C:\Apps\Code.exe";
        match = ShortcutIcon.Match(@"C:\Apps\Code.exe", apps);
        Assert.Equal(@"C:\Start\Code.lnk", match?.Path);
        Assert.Equal(0, match?.Index);
    }
}

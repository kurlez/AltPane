using AltPane.Core.Dialogs;
using AltPane.Core.Input;
using AltPane.Core.Layout;
using AltPane.Core.Models;
using AltPane.Core.Search;
using AltPane.Core.Settings;
using AltPane.Core.Versioning;
using Xunit;

namespace AltPane.Tests;

public class FileDialogClassifierTests
{
    [Fact]
    public void Common_item_dialog_needs_dui_and_an_address_bar()
    {
        Assert.Equal(1148, FileDialogClassifier.FileNameControlId);
        var common = FileDialogClassifier.Classify(new DialogSnapshot
        {
            ClassName = "#32770",
            Title = "打开",
            Descendants = new[]
            {
                new DialogControl("DUIViewWndClassName", 0),
                new DialogControl("DirectUIHWND", 0),
                new DialogControl("Breadcrumb Parent", 0),
                new DialogControl("ComboBox", FileDialogClassifier.FileNameControlId),
                new DialogControl("Edit", 0)
            }
        });
        Assert.Equal(FileDialogKind.CommonItem, common);

        var toolbar = FileDialogClassifier.Classify(new DialogSnapshot
        {
            ClassName = "#32770",
            Title = "Open",
            Descendants = new[]
            {
                new DialogControl("DUIViewWndClassName", 0),
                new DialogControl("DirectUIHWND", 0),
                new DialogControl("ToolbarWindow32", 0),
                new DialogControl("ComboBox", 0x47C)
            }
        });
        Assert.Equal(FileDialogKind.CommonItem, toolbar);
    }

    [Fact]
    public void Legacy_get_open_file_name_is_recognized_and_message_boxes_are_not()
    {
        var legacy = FileDialogClassifier.Classify(new DialogSnapshot
        {
            ClassName = "#32770",
            Title = "Open",
            Descendants = new[]
            {
                new DialogControl("Edit", FileDialogClassifier.LegacyFileNameEditId),
                new DialogControl("ListBox", FileDialogClassifier.FileListId),
                new DialogControl("ComboBox", 0x470)
            }
        });
        Assert.Equal(FileDialogKind.Legacy, legacy);

        var explorerStyle = FileDialogClassifier.Classify(new DialogSnapshot
        {
            ClassName = "#32770",
            Title = "Open",
            Descendants = new[]
            {
                new DialogControl("ComboBox", 0x47C),
                new DialogControl("SysListView32", 0),
                new DialogControl("Static", 0x441)
            }
        });
        Assert.Equal(FileDialogKind.Legacy, explorerStyle);

        var message = FileDialogClassifier.Classify(new DialogSnapshot
        {
            ClassName = "#32770",
            Title = "AltPane",
            Descendants = new[]
            {
                new DialogControl("Static", 0xFFFF),
                new DialogControl("Button", 1),
                new DialogControl("Button", 2)
            }
        });
        Assert.Equal(FileDialogKind.None, message);

        var duiWithoutAddress = FileDialogClassifier.Classify(new DialogSnapshot
        {
            ClassName = "#32770",
            Title = "Options",
            Descendants = new[]
            {
                new DialogControl("DUIViewWndClassName", 0),
                new DialogControl("DirectUIHWND", 0),
                new DialogControl("Button", 1)
            }
        });
        Assert.Equal(FileDialogKind.None, duiWithoutAddress);
        Assert.Equal(FileDialogKind.None, FileDialogClassifier.Classify(new DialogSnapshot { ClassName = "Notepad", Title = "Open" }));
    }

    [Fact]
    public void Save_titles_are_detected_without_matching_unsaved()
    {
        Assert.True(FileDialogClassifier.IsSaveDialog("另存为"));
        Assert.True(FileDialogClassifier.IsSaveDialog("保存"));
        Assert.True(FileDialogClassifier.IsSaveDialog("Save As"));
        Assert.True(FileDialogClassifier.IsSaveDialog("SaveAs"));
        Assert.False(FileDialogClassifier.IsSaveDialog("打开"));
        Assert.False(FileDialogClassifier.IsSaveDialog("Open"));
        Assert.False(FileDialogClassifier.IsSaveDialog("Unsaved changes"));
        Assert.False(FileDialogClassifier.IsSaveDialog(null));
    }
}

public class FolderMruTests
{
    [Fact]
    public void Remember_moves_a_folder_to_the_front_and_drops_the_oldest()
    {
        var mru = new FolderMru(2);
        mru.Remember(@"C:\Work");
        mru.Remember(@"D:\Notes\");
        mru.Remember(@"c:\work");
        Assert.Equal(new[] { @"c:\work", @"D:\Notes" }, mru.Entries);
        mru.Remember(@"E:\Third");
        Assert.Equal(new[] { @"E:\Third", @"c:\work" }, mru.Entries);
        mru.Remember("Documents");
        mru.Remember("  ");
        Assert.Equal(2, mru.Entries.Count);
    }

    [Fact]
    public void The_list_round_trips_newest_first()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "folder-mru.json");
        var mru = new FolderMru();
        mru.Remember(@"C:\Older");
        mru.Remember(@"C:\Newer");
        FolderMruStore.Save(path, mru);
        var loaded = FolderMruStore.Load(path);
        Assert.Equal(new[] { @"C:\Newer", @"C:\Older" }, loaded.Entries);
    }
}

public class FolderMatchTests
{
    [Fact]
    public void Terms_are_and_and_pinyin_initials_match_the_folder_name()
    {
        Assert.True(FolderMatch.Matches("doc", @"C:\Users\me\Documents"));
        Assert.True(FolderMatch.Matches("my doc", @"C:\Users\me\My Documents"));
        Assert.False(FolderMatch.Matches("my zz", @"C:\Users\me\My Documents"));
        Assert.True(FolderMatch.Matches("yy", @"D:\音乐"));
        Assert.True(FolderMatch.Matches("yinyue", @"D:\音乐"));
        Assert.True(FolderMatch.Matches("wd", @"C:\Users\me\文档"));
        Assert.Equal("folder:yy", FolderMatch.EverythingQuery(" yy "));
        Assert.Equal("", FolderMatch.EverythingQuery("  "));
    }

    [Fact]
    public void The_built_in_index_returns_matching_directories()
    {
        var index = new FileIndex(
            new[] { @"C:\Users\me\文档", @"C:\Users\me\Documents", @"C:\Windows" },
            new[] { new FileSlot(0, "a.txt"), new FileSlot(1, "b.txt"), new FileSlot(2, "c.txt") },
            0);
        var hits = FolderMatch.SearchIndex(index, "doc", null, 10);
        Assert.Equal(new[] { @"C:\Users\me\Documents" }, hits);
        var pinyin = FolderMatch.SearchIndex(index, "wd", null, 10);
        Assert.Equal(new[] { @"C:\Users\me\文档" }, pinyin);
    }

    [Fact]
    public void An_empty_query_lists_explorer_then_recent_and_a_query_adds_search()
    {
        var idle = FolderMatch.Compose(
            "",
            new[] { @"C:\Explorer", @"D:\Shared" },
            new[] { @"D:\Shared", @"E:\Recent" },
            new[] { @"F:\Search" });
        Assert.Equal(new[] { @"C:\Explorer", @"D:\Shared", @"E:\Recent" }, idle.Select(row => row.Path));
        Assert.Equal(FolderOrigin.Explorer, idle[0].Origin);
        Assert.Equal(FolderOrigin.Recent, idle[2].Origin);

        var filtered = FolderMatch.Compose(
            "recent",
            new[] { @"C:\Explorer" },
            new[] { @"E:\Recent" },
            new[] { @"E:\Recent Notes", @"C:\Other" });
        Assert.Equal(new[] { @"E:\Recent", @"E:\Recent Notes" }, filtered.Select(row => row.Path));
        Assert.Equal(FolderOrigin.Recent, filtered[0].Origin);
        Assert.Equal(FolderOrigin.Search, filtered[1].Origin);
    }
}

public class DialogNavigationTests
{
    [Fact]
    public void A_save_dialog_restores_the_typed_filename_after_the_folder_is_accepted()
    {
        var surface = new FakeDialog("notes.txt", @"C:\start");
        var result = DialogNavigation.Go(surface, @"D:\Work", saveDialog: true);
        Assert.True(result.Succeeded);
        Assert.False(result.UsedAddressBar);
        Assert.Equal("notes.txt", result.RestoredName);
        Assert.Equal("notes.txt", surface.FileName);
        Assert.Equal(@"D:\Work", surface.CurrentLocation);
        Assert.Equal(1, surface.Submits);
        Assert.Equal(0, surface.AddressCalls);
    }

    [Fact]
    public void An_open_dialog_does_not_put_the_filename_back()
    {
        var surface = new FakeDialog("notes.txt", @"C:\start");
        var result = DialogNavigation.Go(surface, @"D:\Work", saveDialog: false);
        Assert.True(result.Succeeded);
        Assert.Equal("", result.RestoredName);
        Assert.Equal("", surface.FileName);
    }

    [Fact]
    public void A_full_file_path_restores_only_the_file_name()
    {
        var surface = new FakeDialog(@"C:\old\report.docx", @"C:\old");
        var result = DialogNavigation.Go(surface, @"D:\Work\", saveDialog: true);
        Assert.True(result.Succeeded);
        Assert.Equal("report.docx", surface.FileName);
        Assert.Equal("", DialogNavigation.FileNameToRestore(@"D:\Work\", true));
    }

    [Fact]
    public void The_address_bar_is_used_when_the_filename_edit_does_not_navigate()
    {
        var surface = new FakeDialog("notes.txt", @"C:\start") { FileNameWorks = false };
        var result = DialogNavigation.Go(surface, @"D:\Work", saveDialog: true);
        Assert.True(result.Succeeded);
        Assert.True(result.UsedAddressBar);
        Assert.Equal(@"D:\Work", surface.CurrentLocation);
        Assert.Equal("notes.txt", surface.FileName);
        Assert.Equal(1, surface.Submits);
        Assert.Equal(1, surface.AddressCalls);
    }

    [Fact]
    public void A_failed_jump_puts_the_original_filename_back()
    {
        var surface = new FakeDialog("notes.txt", @"C:\start")
        {
            FileNameWorks = false,
            AddressWorks = false
        };
        var result = DialogNavigation.Go(surface, @"D:\Missing", saveDialog: true);
        Assert.False(result.Succeeded);
        Assert.False(result.UsedAddressBar);
        Assert.Equal(1, surface.AddressCalls);
        Assert.Equal("notes.txt", surface.FileName);
        Assert.Equal(@"C:\start", surface.CurrentLocation);
    }

    sealed class FakeDialog : IDialogSurface
    {
        public FakeDialog(string fileName, string location)
        {
            FileName = fileName;
            CurrentLocation = location;
        }

        public string FileName { get; set; }
        public string CurrentLocation { get; set; }
        public bool FileNameWorks { get; set; } = true;
        public bool AddressWorks { get; set; } = true;
        public int Submits { get; private set; }
        public int AddressCalls { get; private set; }

        public void SubmitFileName()
        {
            Submits++;
            if (!FileNameWorks) return;
            CurrentLocation = FolderPath.Normalize(FileName);
            FileName = "";
        }

        public bool TryNavigateAddress(string folder)
        {
            AddressCalls++;
            if (!AddressWorks) return false;
            CurrentLocation = folder;
            return true;
        }
    }
}

public class DialogPanelAndVersionTests
{
    [Fact]
    public void The_panel_docks_to_the_requested_edge_and_flips_at_the_work_area()
    {
        var work = new RectangleMath.RectI(0, 0, 1000, 800);
        var below = DialogPanelLayout.Dock(new RectangleMath.RectI(100, 100, 400, 300), work, FileDialogPanelEdge.Bottom, 200, 8);
        Assert.False(below.Flipped);
        Assert.Equal(new DialogPanelLayout.Slot(100, 408, 400, 200, false), below);

        var above = DialogPanelLayout.Dock(new RectangleMath.RectI(100, 700, 400, 80), work, FileDialogPanelEdge.Bottom, 200, 8);
        Assert.True(above.Flipped);
        Assert.Equal(492, above.Y);

        var right = DialogPanelLayout.Dock(new RectangleMath.RectI(100, 40, 300, 500), work, FileDialogPanelEdge.Right, 220, 8);
        Assert.False(right.Flipped);
        Assert.Equal(408, right.X);
        Assert.Equal(220, right.Width);

        var left = DialogPanelLayout.Dock(new RectangleMath.RectI(800, 40, 180, 500), work, FileDialogPanelEdge.Right, 220, 8);
        Assert.True(left.Flipped);
        Assert.Equal(800 - 8 - 220, left.X);
    }

    [Fact]
    public void A_two_digit_patch_sorts_after_0_4_9_and_formats_as_four_parts()
    {
        Assert.True(ProductVersion.Compare("0.4.9", "0.4.10") < 0);
        Assert.True(ProductVersion.Compare("v0.4.10", "0.4.11") < 0);
        Assert.Equal(0, ProductVersion.Compare("0.4.10", "0.4.10.0"));
        Assert.True(string.Compare("0.4.10", "0.4.9", StringComparison.Ordinal) < 0);
        Assert.Equal("0.4.10", ProductVersion.Display(new Version(0, 4, 10, 0)));
        Assert.Equal("0.4.10", ProductVersion.Display(null));
        Assert.Equal("0.4.10.0", ProductVersion.AssemblyVersion("v0.4.10"));
        Assert.Equal("0.4.10.0", ProductVersion.AssemblyVersion("0.4.10"));
        Assert.False(ProductVersion.TryParse("0.4", out _));
    }

    [Fact]
    public void Schema_9_turns_quick_jump_on_and_keeps_an_explicit_choice()
    {
        var directory = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oldPath = Path.Combine(directory, "old.json");
        File.WriteAllText(oldPath, """{ "version": 8, "showFrequentSidebar": false }""");
        var migrated = SettingsStore.Load(oldPath);
        Assert.True(migrated.Migrated);
        Assert.Equal(9, migrated.Settings.Version);
        Assert.True(migrated.Settings.FileDialogJump);
        Assert.Equal(FileDialogPanelEdge.Bottom, migrated.Settings.FileDialogPanelEdge);
        Assert.Equal(VirtualKeys.G, migrated.Settings.FileDialogHotkey!.VirtualKey);
        Assert.Equal(ModifierKeys.Control, migrated.Settings.FileDialogHotkey.Modifiers);
        Assert.False(migrated.Settings.ShowFrequentSidebar);

        var chosen = Path.Combine(directory, "chosen.json");
        File.WriteAllText(chosen, """
        { "version": 9, "fileDialogJump": false, "fileDialogPanelEdge": "Right", "fileDialogHotkey": { "modifiers": 6, "virtualKey": 71 } }
        """);
        var kept = SettingsStore.Load(chosen);
        Assert.False(kept.Migrated);
        Assert.False(kept.Settings.FileDialogJump);
        Assert.Equal(FileDialogPanelEdge.Right, kept.Settings.FileDialogPanelEdge);
        Assert.Equal(ModifierKeys.Control | ModifierKeys.Shift, kept.Settings.FileDialogHotkey!.Modifiers);

        var fresh = AppSettings.CreateDefault();
        SettingsStore.Save(oldPath, fresh);
        var round = SettingsStore.Load(oldPath);
        Assert.False(round.Migrated);
        Assert.True(round.Settings.FileDialogJump);
        Assert.True(FileDialogHotkey.Matches(round.Settings.FileDialogHotkey, VirtualKeys.G, new ModifierState(false, true, false, false)));
        Assert.False(FileDialogHotkey.Matches(round.Settings.FileDialogHotkey, VirtualKeys.G, new ModifierState(false, true, true, false)));
    }
}

using AltPane.Core.Actions;
using AltPane.Core.Load;
using AltPane.Core.Models;
using AltPane.Core.Settings;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class LoadMathTests
{
    const int Cores = 4;
    static readonly long OneSecond = TimeSpan.TicksPerSecond;

    [Fact]
    public void One_core_of_four_is_a_quarter_of_the_machine()
    {
        var percent = CpuMath.Percent(0, OneSecond, 0, OneSecond, Cores);
        Assert.NotNull(percent);
        Assert.Equal(25, percent!.Value, 3);
    }

    [Fact]
    public void Full_machine_clamps_at_100()
    {
        var percent = CpuMath.Percent(0, OneSecond * 8, 0, OneSecond, Cores);
        Assert.Equal(100, percent);
    }

    [Fact]
    public void Short_or_backwards_intervals_have_no_percent()
    {
        Assert.Null(CpuMath.Percent(0, 1_000, 0, CpuMath.MinIntervalTicks - 1, Cores));
        Assert.Null(CpuMath.Percent(500, 100, 0, OneSecond, Cores));
        Assert.Null(CpuMath.Percent(0, 1, 0, OneSecond, 0));
    }

    [Fact]
    public void System_percent_subtracts_idle_inside_kernel_time()
    {
        var percent = CpuMath.SystemPercent(
            previousIdle: 0, previousKernel: 0, previousUser: 0,
            idle: OneSecond, kernel: OneSecond * 2, user: OneSecond,
            previousWallTicks: 0, currentWallTicks: OneSecond, processorCount: Cores);
        Assert.NotNull(percent);
        Assert.Equal(50, percent!.Value, 3);
    }

    [Fact]
    public void Formats_cpu_memory_and_a_task_row()
    {
        Assert.Equal("…", LoadFormat.Cpu(null));
        Assert.Equal("8.2%", LoadFormat.Cpu(8.24));
        Assert.Equal("13%", LoadFormat.Cpu(12.5));
        Assert.Equal("0.0 MB", LoadFormat.Bytes(0));
        Assert.Equal("512 MB", LoadFormat.Bytes(512L * 1024 * 1024));
        Assert.Equal("1.5 GB", LoadFormat.Bytes((long)(1.5 * 1024 * 1024 * 1024)));
        Assert.Equal("8.2 GB / 16.0 GB", LoadFormat.MemoryPair((long)(8.2 * 1024 * 1024 * 1024), 16L * 1024 * 1024 * 1024));
        var row = LoadFormat.Row(new TaskLoad(12.5, 512L * 1024 * 1024, 400L * 1024 * 1024, 3));
        Assert.Equal("13% · 512 MB", row);
        Assert.Equal("—", LoadFormat.Row(default));
    }

    [Fact]
    public void Heat_tracks_cpu_or_memory()
    {
        Assert.Equal(LoadHeat.Calm, LoadHeatMap.Classify(1, 20L * 1024 * 1024));
        Assert.Equal(LoadHeat.Warm, LoadHeatMap.Classify(LoadHeatMap.WarmCpu, 0));
        Assert.Equal(LoadHeat.Warm, LoadHeatMap.Classify(0, LoadHeatMap.WarmMemory));
        Assert.Equal(LoadHeat.Hot, LoadHeatMap.Classify(LoadHeatMap.HotCpu, 0));
        Assert.Equal(LoadHeat.Hot, LoadHeatMap.Classify(0, LoadHeatMap.HotMemory));
    }
}

public class TaskLoadAggregatorTests
{
    const long Wall = 10_000_000_000;
    static readonly long Second = TimeSpan.TicksPerSecond;

    [Fact]
    public void Rolls_helper_processes_into_the_window_and_leaves_other_windows_alone()
    {
        var previous = new Dictionary<int, ProcessSample>
        {
            [10] = new(10, 1, 0, 0, 0, "chrome"),
            [11] = new(11, 10, 0, 0, 0, "chrome"),
            [20] = new(20, 1, 0, 0, 0, "notepad")
        };
        var current = new List<ProcessSample>
        {
            new(10, 1, Second, 200L * 1024 * 1024, 100L * 1024 * 1024, "chrome"),
            new(11, 10, Second, 300L * 1024 * 1024, 250L * 1024 * 1024, "chrome"),
            new(12, 11, Second, 50L * 1024 * 1024, 40L * 1024 * 1024, "chrome"),
            new(20, 1, Second / 2, 40L * 1024 * 1024, 20L * 1024 * 1024, "notepad")
        };

        var loads = TaskLoadAggregator.ForWindows(new[] { 10, 20 }, previous, current, Wall, Wall + Second, 4);

        Assert.Equal(3, loads[10].ProcessCount);
        Assert.Equal(50, loads[10].CpuPercent);
        Assert.Equal(550L * 1024 * 1024, loads[10].WorkingSetBytes);
        Assert.Equal(1, loads[20].ProcessCount);
        Assert.Equal(12.5, loads[20].CpuPercent);
    }

    [Fact]
    public void A_child_that_owns_a_window_is_not_counted_twice()
    {
        var previous = new Dictionary<int, ProcessSample>
        {
            [10] = new(10, 1, 0, 0, 0),
            [11] = new(11, 10, 0, 0, 0)
        };
        var current = new List<ProcessSample>
        {
            new(10, 1, Second, 100, 100),
            new(11, 10, Second, 100, 100)
        };
        var loads = TaskLoadAggregator.ForWindows(new[] { 10, 11 }, previous, current, Wall, Wall + Second, 4);
        Assert.Equal(1, loads[10].ProcessCount);
        Assert.Equal(1, loads[11].ProcessCount);
        Assert.Equal(25, loads[10].CpuPercent);
        Assert.Equal(25, loads[11].CpuPercent);
    }

    [Fact]
    public void Memory_shows_before_a_cpu_baseline_exists()
    {
        var current = new List<ProcessSample> { new(7, 1, 1000, 4096, 2048, "app") };
        var loads = TaskLoadAggregator.ForWindows(new[] { 7 }, new Dictionary<int, ProcessSample>(), current, 0, Second, 4);
        Assert.Null(loads[7].CpuPercent);
        Assert.Equal(4096, loads[7].WorkingSetBytes);
        Assert.Equal(1, loads[7].ProcessCount);
    }

    [Fact]
    public void Uwp_host_pid_is_the_key_the_caller_passes()
    {
        var current = new List<ProcessSample>
        {
            new(4, 1, 0, 10, 10, "ApplicationFrameHost"),
            new(90, 4, Second, 80, 70, "Paint")
        };
        var previous = new Dictionary<int, ProcessSample> { [90] = new(90, 4, 0, 0, 0, "Paint") };
        var loads = TaskLoadAggregator.ForWindows(new[] { 90 }, previous, current, Wall, Wall + Second, 4);
        Assert.False(loads.ContainsKey(4));
        Assert.Equal(1, loads[90].ProcessCount);
        Assert.Equal(25, loads[90].CpuPercent);
    }
}

public class LoadSortAndKillTests
{
    [Fact]
    public void Sort_puts_heavy_tasks_first_and_keeps_z_order_for_ties()
    {
        var items = new[]
        {
            new Row(1, 5, 10),
            new Row(0, 5, 99),
            new Row(2, null, 1)
        };
        var byCpu = LoadSorter.Sort(items, LoadSortMode.Cpu, r => r.Order, r => r.Cpu, r => r.Memory);
        Assert.Equal(new[] { 0, 1, 2 }, byCpu.Select(r => r.Order));
        var byMemory = LoadSorter.Sort(items, LoadSortMode.Memory, r => r.Order, r => r.Cpu, r => r.Memory);
        Assert.Equal(new[] { 0, 1, 2 }, byMemory.Select(r => r.Order));
        Assert.Equal(new[] { 1, 0, 2 }, LoadSorter.Sort(items, LoadSortMode.Recent, r => r.Order, r => r.Cpu, r => r.Memory).Select(r => r.Order));
    }

    [Fact]
    public void Session_sort_keeps_the_selected_window()
    {
        var session = new SwitcherSession(new[]
        {
            Entry(1, 0, "A"),
            Entry(2, 1, "B"),
            Entry(3, 2, "C")
        });
        session.UpdateLoads(new Dictionary<long, TaskLoad>
        {
            [1] = new(1, 10, 10, 1),
            [2] = new(40, 10, 10, 1),
            [3] = new(5, 900, 10, 1)
        });
        session.SelectHandle(1);
        session.SetSortMode(LoadSortMode.Cpu);
        Assert.Equal(2, session.Filtered[0].Handle);
        Assert.Equal(1, session.Selected?.Handle);
        session.SetQuery("c");
        Assert.Equal(3, session.Selected?.Handle);
    }

    [Fact]
    public void Kill_order_is_leaves_first_and_skips_protected_children()
    {
        var nodes = new List<ProcessNode>
        {
            new(10, 1, "chrome"),
            new(11, 10, "chrome"),
            new(12, 11, "chrome"),
            new(13, 10, "svchost"),
            new(14, 13, "chrome")
        };
        Assert.Equal(new[] { 12, 11, 14, 13, 10 }, ProcessTree.KillOrder(10, nodes));
        Assert.Equal(new[] { 12, 11, 10 }, ProcessTree.SelectTargets(10, "chrome", nodes, includeTree: true));
        Assert.Equal(new[] { 10 }, ProcessTree.SelectTargets(10, "chrome", nodes, includeTree: false));
        Assert.Empty(ProcessTree.SelectTargets(10, "explorer", nodes, includeTree: true));
    }

    [Fact]
    public void Kill_order_survives_a_parent_cycle()
    {
        var nodes = new[] { new ProcessNode(2, 3, "a"), new ProcessNode(3, 2, "b") };
        var order = ProcessTree.KillOrder(2, nodes);
        Assert.Equal(new[] { 3, 2 }, order);
    }

    [Fact]
    public void Confirmation_arms_once_then_accepts_the_same_target()
    {
        var arm = new KillArm();
        Assert.False(arm.Ready(20, includeTree: true, confirm: true));
        Assert.True(arm.IsArmed);
        Assert.False(arm.Ready(20, includeTree: false, confirm: true));
        Assert.True(arm.Ready(20, includeTree: false, confirm: true));
        Assert.False(arm.IsArmed);
        Assert.True(arm.Ready(20, includeTree: true, confirm: false));
    }

    [Fact]
    public void Load_settings_round_trip_and_old_files_keep_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var settings = AppSettings.CreateDefault();
        settings.ShowLoad = false;
        settings.SortMode = LoadSortMode.Memory;
        settings.ConfirmEndTask = false;
        settings.EndTaskKillsTree = false;
        settings.StickySwitcher = false;
        settings.QuickTapSwitches = true;
        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);
        Assert.False(loaded.Settings.ShowLoad);
        Assert.Equal(LoadSortMode.Memory, loaded.Settings.SortMode);
        Assert.False(loaded.Settings.ConfirmEndTask);
        Assert.False(loaded.Settings.EndTaskKillsTree);
        Assert.False(loaded.Settings.StickySwitcher);
        Assert.True(loaded.Settings.QuickTapSwitches);

        var legacy = Path.Combine(Path.GetTempPath(), "altpane-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, """{ "theme": "Dark" }""");
        var old = SettingsStore.Load(legacy);
        Assert.True(old.Settings.ShowLoad);
        Assert.Equal(LoadSortMode.Recent, old.Settings.SortMode);
        Assert.True(old.Settings.ConfirmEndTask);
        Assert.True(old.Settings.EndTaskKillsTree);
        Assert.True(old.Settings.StickySwitcher);
        Assert.False(old.Settings.QuickTapSwitches);
        Assert.True(old.Settings.LaunchApps);
        Assert.True(old.Settings.CaptureWinTab);
        Assert.True(old.Settings.ShowOtherMonitorHints);
        Assert.True(old.Settings.IndexStartMenu);
        Assert.False(old.Settings.IndexPath);
        Assert.Equal(AppTheme.Dark, old.Settings.Theme);
    }

    static WindowEntry Entry(long handle, int index, string title) => new()
    {
        Handle = handle,
        Title = title,
        ClassName = "C",
        ProcessName = "app",
        DisplayProcessName = "app",
        ProcessId = (int)handle,
        KillProcessId = (int)handle,
        KillProcessName = "app",
        OriginalIndex = index
    };

    sealed record Row(int Order, double? Cpu, long Memory);
}

using AltPane.Core.Load;
using AltPane.Core.Models;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class OrderStabilityTests
{
    [Fact]
    public void Load_updates_do_not_reorder_a_cpu_snapshot()
    {
        var session = Session();
        session.UpdateLoads(Loads(1, 10, 2, 80, 3, 30));
        session.SetSortMode(LoadSortMode.Cpu);
        Assert.Equal(new long[] { 2, 3, 1 }, Handles(session));
        session.SelectHandle(3);

        session.UpdateLoads(Loads(1, 99, 2, 1, 3, 5));
        Assert.Equal(new long[] { 2, 3, 1 }, Handles(session));
        Assert.Equal(3, session.Selected?.Handle);
        Assert.Equal(99, session.LoadFor(1).CpuPercent);
        Assert.Equal(1, session.LoadFor(2).CpuPercent);
    }

    [Fact]
    public void Changing_sort_mode_reorders_once_and_keeps_the_selection()
    {
        var session = Session();
        session.UpdateLoads(new Dictionary<long, TaskLoad>
        {
            [1] = new(1, 10, 10, 1),
            [2] = new(5, 500, 10, 1),
            [3] = new(9, 50, 10, 1)
        });
        session.SelectHandle(1);
        session.SetSortMode(LoadSortMode.Memory);
        Assert.Equal(new long[] { 2, 3, 1 }, Handles(session));
        Assert.Equal(1, session.Selected?.Handle);

        session.UpdateLoads(new Dictionary<long, TaskLoad>
        {
            [1] = new(1, 900, 10, 1),
            [2] = new(5, 1, 10, 1),
            [3] = new(9, 2, 10, 1)
        });
        Assert.Equal(new long[] { 2, 3, 1 }, Handles(session));
        Assert.Equal(1, session.Selected?.Handle);
    }

    [Fact]
    public void Removing_a_window_leaves_the_others_in_place()
    {
        var session = Session();
        session.SelectHandle(2);
        Assert.True(session.RemoveHandle(2));
        Assert.Equal(new long[] { 1, 3 }, Handles(session));
        Assert.Equal(3, session.Selected?.Handle);

        session.SelectHandle(1);
        Assert.True(session.RemoveHandle(1));
        Assert.Equal(new long[] { 3 }, Handles(session));
        Assert.Equal(3, session.Selected?.Handle);
    }

    [Fact]
    public void Search_filters_the_frozen_order_and_clearing_restores_it()
    {
        var session = new SwitcherSession(new[]
        {
            Window(1, "Alpha mail"),
            Window(2, "Bravo"),
            Window(3, "Mail draft"),
            Window(4, "Charlie")
        });
        session.UpdateLoads(Loads(1, 1, 2, 50, 3, 40, 4, 30));
        session.SetSortMode(LoadSortMode.Cpu);
        Assert.Equal(new long[] { 2, 3, 4, 1 }, Handles(session));

        session.SetQuery("mail");
        Assert.Equal(new long[] { 3, 1 }, Handles(session));
        session.SetQuery("");
        Assert.Equal(new long[] { 2, 3, 4, 1 }, Handles(session));

        session.UpdateLoads(Loads(1, 100, 2, 0, 3, 0, 4, 0));
        session.SetQuery("mail");
        Assert.Equal(new long[] { 3, 1 }, Handles(session));
    }

    [Fact]
    public void A_window_that_appears_while_open_is_appended()
    {
        var session = Session();
        session.UpdateLoads(Loads(1, 10, 2, 80, 3, 30));
        session.SetSortMode(LoadSortMode.Cpu);
        Assert.True(session.NoteArrived(Window(9, "Late")));
        Assert.Equal(new long[] { 2, 3, 1, 9 }, Handles(session));
        session.UpdateLoads(Loads(1, 1, 2, 1, 3, 1, 9, 100));
        Assert.Equal(new long[] { 2, 3, 1, 9 }, Handles(session));
        Assert.False(session.NoteArrived(Window(9, "Late")));
    }

    static SwitcherSession Session() => new(new[]
    {
        Window(1, "Alpha"),
        Window(2, "Bravo"),
        Window(3, "Charlie")
    });

    static long[] Handles(SwitcherSession session)
    {
        var handles = new long[session.Filtered.Count];
        for (var i = 0; i < handles.Length; i++)
            handles[i] = session.Filtered[i].Handle;
        return handles;
    }

    static Dictionary<long, TaskLoad> Loads(params long[] pairs)
    {
        var loads = new Dictionary<long, TaskLoad>();
        for (var i = 0; i < pairs.Length; i += 2)
            loads[pairs[i]] = new TaskLoad(pairs[i + 1], 10, 10, 1);
        return loads;
    }

    static WindowEntry Window(long handle, string title) => new()
    {
        Handle = handle,
        Title = title,
        ClassName = "C",
        ProcessName = title,
        DisplayProcessName = title,
        ProcessId = (int)handle,
        KillProcessId = (int)handle,
        KillProcessName = title,
        OriginalIndex = (int)handle
    };
}

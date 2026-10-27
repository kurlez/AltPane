using AltPane.Core.Input;
using AltPane.Core.Models;
using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class HoverGateTests
{
    [Fact]
    public void Opening_under_a_stationary_cursor_does_not_hover()
    {
        var gate = new HoverGate();
        gate.NoteOpened(8, 9);
        Assert.False(gate.NoteMouseMove(0, 8, 9));
        Assert.False(gate.NoteMouseMove(5_000, 8.4, 9));
        Assert.False(gate.HoverHighlight);
    }

    [Fact]
    public void A_real_move_after_open_enables_hover_without_waiting()
    {
        var gate = new HoverGate();
        gate.NoteOpened(1, 1);
        Assert.True(gate.NoteMouseMove(0, 12, 4));
        Assert.True(gate.HoverHighlight);
        Assert.True(gate.NoteMouseMove(50, 12, 4));
    }

    [Fact]
    public void An_unknown_open_position_ignores_the_first_report()
    {
        var gate = new HoverGate();
        gate.NoteOpened(double.NaN, double.NaN);
        Assert.False(gate.NoteMouseMove(0, 3, 3));
        Assert.True(gate.NoteMouseMove(0, 8, 3));
    }

    [Fact]
    public void Navigation_suppresses_hover_until_idle_and_a_later_move()
    {
        var gate = new HoverGate();
        gate.NoteOpened(10, 20);
        gate.NoteNavigation(1_000, 10, 20);
        Assert.False(gate.HoverHighlight);
        Assert.False(gate.NoteMouseMove(1_000 + HoverGate.IdleMilliseconds - 1, 30, 20));
        Assert.False(gate.NoteMouseMove(1_000 + HoverGate.IdleMilliseconds, 30, 20));
        Assert.True(gate.NoteMouseMove(1_000 + HoverGate.IdleMilliseconds, 36, 20));
        Assert.True(gate.HoverHighlight);
    }

    [Fact]
    public void A_stationary_cursor_after_navigation_never_reselects()
    {
        var gate = new HoverGate();
        gate.NoteOpened(4, 5);
        gate.NoteNavigation(100, 4, 5);
        Assert.False(gate.NoteMouseMove(100, 4, 5));
        Assert.False(gate.NoteMouseMove(100 + HoverGate.IdleMilliseconds + 50, 4, 5));
        Assert.False(gate.HoverHighlight);
    }

    [Fact]
    public void Idle_boundary_allows_a_move_and_rejects_the_same_point()
    {
        var gate = new HoverGate();
        gate.NoteOpened(0, 0);
        gate.NoteNavigation(0, 2, 2);
        Assert.False(gate.NoteMouseMove(HoverGate.IdleMilliseconds, 2, 2));
        Assert.True(gate.NoteMouseMove(HoverGate.IdleMilliseconds, 2 + HoverGate.MoveEpsilon, 2));
    }

    [Fact]
    public void The_next_navigation_suppresses_hover_again()
    {
        var gate = new HoverGate();
        gate.NoteOpened(0, 0);
        Assert.True(gate.NoteMouseMove(0, 5, 0));
        gate.NoteNavigation(20, 5, 0);
        Assert.False(gate.HoverHighlight);
        Assert.False(gate.NoteMouseMove(20, 5, 0));
    }
}

public class WheelNavigatorTests
{
    [Fact]
    public void One_notch_moves_one_row()
    {
        var wheel = new WheelNavigator();
        Assert.Equal(-1, wheel.Consume(WheelNavigator.Notch));
        Assert.Equal(1, wheel.Consume(-WheelNavigator.Notch));
    }

    [Fact]
    public void Several_notches_in_one_event_move_that_many_rows()
    {
        var wheel = new WheelNavigator();
        Assert.Equal(-3, wheel.Consume(WheelNavigator.Notch * 3));
        Assert.Equal(2, wheel.Consume(-WheelNavigator.Notch * 2));
    }

    [Fact]
    public void Precision_deltas_accumulate_to_one_row()
    {
        var wheel = new WheelNavigator();
        Assert.Equal(0, wheel.Consume(40));
        Assert.Equal(0, wheel.Consume(40));
        Assert.Equal(-1, wheel.Consume(40));
        Assert.Equal(0, wheel.Consume(40));
        Assert.Equal(-1, wheel.Consume(80));
    }

    [Fact]
    public void A_remainder_is_kept_and_a_direction_change_drops_it()
    {
        var wheel = new WheelNavigator();
        Assert.Equal(-1, wheel.Consume(200));
        Assert.Equal(0, wheel.Consume(30));
        Assert.Equal(-1, wheel.Consume(20));
        Assert.Equal(0, wheel.Consume(100));
        Assert.Equal(0, wheel.Consume(-10));
        Assert.Equal(1, wheel.Consume(-110));
    }

    [Fact]
    public void Zero_does_nothing_and_reset_drops_a_partial_notch()
    {
        var wheel = new WheelNavigator();
        Assert.Equal(0, wheel.Consume(0));
        Assert.Equal(0, wheel.Consume(100));
        wheel.Reset();
        Assert.Equal(0, wheel.Consume(20));
        Assert.Equal(-1, wheel.Consume(100));
    }

    [Fact]
    public void Wheel_steps_stop_on_the_first_and_last_row()
    {
        var session = new SwitcherSession(new[]
        {
            Row(1, "One"),
            Row(2, "Two"),
            Row(3, "Three")
        });
        var wheel = new WheelNavigator();
        session.Move(wheel.Consume(WheelNavigator.Notch));
        Assert.Equal(0, session.SelectedIndex);
        session.Move(wheel.Consume(-WheelNavigator.Notch * 10));
        Assert.Equal(2, session.SelectedIndex);
        session.Move(wheel.Consume(-WheelNavigator.Notch));
        Assert.Equal(2, session.SelectedIndex);
        session.Move(wheel.Consume(WheelNavigator.Notch));
        Assert.Equal(1, session.SelectedIndex);
    }

    static WindowEntry Row(int index, string title) => new()
    {
        Handle = index,
        Title = title,
        ClassName = "C",
        ProcessName = title,
        DisplayProcessName = title,
        ProcessId = index,
        KillProcessId = index,
        KillProcessName = title,
        OriginalIndex = index
    };
}

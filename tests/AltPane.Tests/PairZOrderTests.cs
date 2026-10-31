using AltPane.Core.Switcher;
using Xunit;

namespace AltPane.Tests;

public class PairZOrderTests
{
    [Fact]
    public void Both_windows_are_raised_when_nothing_else_covers_the_partner()
    {
        Assert.True(PairZOrder.IsRaised(new long[] { 10, 20, 30 }, focus: 10, other: 20));
        Assert.True(PairZOrder.IsRaised(new long[] { 10, 20 }, focus: 10, other: 20));
    }

    [Fact]
    public void The_partner_is_buried_when_another_window_sits_above_it()
    {
        Assert.False(PairZOrder.IsRaised(new long[] { 10, 30, 20 }, focus: 10, other: 20));
        Assert.False(PairZOrder.IsRaised(new long[] { 30, 10, 20 }, focus: 10, other: 20));
        Assert.False(PairZOrder.IsRaised(new long[] { 20, 10 }, focus: 10, other: 20));
        Assert.False(PairZOrder.IsRaised(new long[] { 10, 30 }, focus: 10, other: 20));
        Assert.False(PairZOrder.IsRaised(new long[] { 10 }, focus: 10, other: 20));
        Assert.False(PairZOrder.IsRaised(Array.Empty<long>(), focus: 10, other: 20));
    }
}

using GHelper.Ally;

namespace GHelper.Tests;

public sealed class StickDirectionTests
{
    [Theory]
    [InlineData(0, 0, 20000, 0, 0)]
    [InlineData(1, 0, -20000, 0, 0)]
    [InlineData(2, -20000, 0, 0, 0)]
    [InlineData(3, 20000, 0, 0, 0)]
    [InlineData(4, 0, 0, 0, 20000)]
    [InlineData(5, 0, 0, 0, -20000)]
    [InlineData(6, 0, 0, -20000, 0)]
    [InlineData(7, 0, 0, 20000, 0)]
    public void EachStickDirectionFiresIndependently(int expected, short lx, short ly, short rx, short ry)
    {
        var latch = new StickDirectionLatch();
        Assert.Empty(latch.Update(0, 0, 0, 0));
        Assert.Equal(new[] { expected }, latch.Update(lx, ly, rx, ry));
        Assert.Empty(latch.Update(lx, ly, rx, ry));
    }

    [Fact]
    public void JitterNearActivationDoesNotRepeatUntilStickReturnsTowardCenter()
    {
        var latch = new StickDirectionLatch();
        latch.Update(0, 0, 0, 0);
        Assert.Equal(new[] { 3 }, latch.Update(21000, 0, 0, 0));
        Assert.Empty(latch.Update(17000, 0, 0, 0));
        Assert.Empty(latch.Update(21000, 0, 0, 0));
        Assert.Empty(latch.Update(12000, 0, 0, 0));
        Assert.Equal(new[] { 3 }, latch.Update(21000, 0, 0, 0));
    }

    [Fact]
    public void StartingOrSwitchingPresetsWithHeldStickDoesNotLaunchAnAction()
    {
        var latch = new StickDirectionLatch();
        Assert.Empty(latch.Update(30000, 0, 0, 0));
        Assert.Empty(latch.Update(16000, 0, 0, 0));
        Assert.Empty(latch.Update(30000, 0, 0, 0));
        latch.Update(0, 0, 0, 0);
        Assert.Equal(new[] { 3 }, latch.Update(30000, 0, 0, 0));
        latch.Reset();
        Assert.Empty(latch.Update(30000, 0, 0, 0));
    }

    [Fact]
    public void DiagonalsTriggerBothDirectionsWithoutChangingTheInputAxes()
    {
        var latch = new StickDirectionLatch();
        latch.Update(0, 0, 0, 0);
        Assert.Equal(new[] { 0, 3, 5, 6 }, latch.Update(24000, 24000, -24000, -24000));
    }
}

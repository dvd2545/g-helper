using GHelper.UI;

namespace GHelper.Tests;

public sealed class TouchUiTests
{
    [Theory]
    [InlineData(40, 96, 40)]
    [InlineData(40, 144, 60)]
    [InlineData(40, 192, 80)]
    [InlineData(1, 72, 1)]
    public void ScaleDipProducesPhysicalTouchTarget(int dip, int dpi, int expected)
    {
        Assert.Equal(expected, TouchUi.ScaleDip(dip, dpi));
    }
}

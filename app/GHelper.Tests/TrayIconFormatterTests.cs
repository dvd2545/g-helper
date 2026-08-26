using GHelper.UI;

namespace GHelper.Tests;

public class TrayIconFormatterTests
{
    [Theory]
    [InlineData(null, "--")]
    [InlineData(69.4, "69")]
    [InlineData(70d, "70")]
    [InlineData(85d, "85")]
    public void TemperatureFormatsExpectedText(double? value, string text)
    {
        Assert.Equal(text, TrayIconFormatter.Temperature(value).Text);
    }

    [Fact]
    public void TemperatureThresholdsUseDistinctColorBuckets()
    {
        Assert.NotEqual(TrayIconFormatter.Temperature(69).Color, TrayIconFormatter.Temperature(70).Color);
        Assert.NotEqual(TrayIconFormatter.Temperature(84).Color, TrayIconFormatter.Temperature(85).Color);
    }

    [Theory]
    [InlineData("12.5", "+13")]
    [InlineData("-12.5", "-13")]
    [InlineData("0", "0")]
    public void BatteryPowerUsesSignedRoundedWatts(string raw, string expected)
    {
        Assert.Equal(expected, TrayIconFormatter.BatteryPower(decimal.Parse(raw)).Text);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(49.5, "50")]
    [InlineData(100, "100")]
    public void BatteryChargeIsClampedAndRounded(double value, string expected)
    {
        Assert.Equal(expected, TrayIconFormatter.BatteryCharge(value).Text);
    }
}

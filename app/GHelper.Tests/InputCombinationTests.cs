using GHelper.Ally;

namespace GHelper.Tests;

public sealed class InputCombinationTests
{
    [Theory]
    [InlineData(0xA3, true)]
    [InlineData(0xA2, false)]
    [InlineData(0x25, true)]
    [InlineData(0x41, false)]
    public void SoftwarePlaybackUsesExtendedFlagsForNavigationAndRightModifiers(int key, bool extended)
    {
        Assert.Equal(extended, InputCombinationPlayer.IsExtendedKey(key));
    }
    [Fact]
    public void KeyboardChordUsesFirmwareWithoutCarrierHotkey()
    {
        var chord = new InputCombination { Keys = [0xA2, 0xA0, 0x41] };
        Assert.Equal("04-03-8C-88-1C", InputCombinationPlayer.FirmwareBinding(chord));
        Assert.Equal(new byte[] { 4, 0, 0, 0, 0, 3, 0x8C, 0x88, 0x1C, 0, 0 }, AllyControl.DecodeBinding(InputCombinationPlayer.FirmwareBinding(chord)!));
    }

    [Fact]
    public void FiveKeyChordFitsWholeFirmwareBlock()
    {
        var chord = new InputCombination { Keys = [17, 16, 18, 65, 66] };
        byte[] block = AllyControl.DecodeBinding(InputCombinationPlayer.FirmwareBinding(chord)!);
        Assert.Equal(11, block.Length);
        Assert.Equal(5, block[5]);
        Assert.Equal(0x32, block[10]);
    }

    [Fact]
    public void MouseAndProgramActionsUseSoftwarePlayback()
    {
        Assert.Null(InputCombinationPlayer.FirmwareBinding(new InputCombination { Keys = [65], MouseButton = CombinationMouseButton.Left }));
        Assert.Null(InputCombinationPlayer.FirmwareBinding(new InputCombination { Keys = [65], ExecutablePath = @"C:\Apps\Game.exe" }));
        Assert.Null(InputCombinationPlayer.FirmwareBinding(new InputCombination { Keys = [65, 66, 67, 68, 69, 70] }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("04-06-01-02-03-04-05-06")]
    [InlineData("04-03-01")]
    [InlineData("invalid")]
    public void InvalidFirmwareBindingIsDisabled(string binding)
    {
        Assert.Equal(new byte[11], AllyControl.DecodeBinding(binding));
    }
}

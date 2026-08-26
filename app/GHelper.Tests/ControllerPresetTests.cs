using GHelper.Ally;
using System.Text.Json;

namespace GHelper.Tests;

public class ControllerPresetTests
{
    [Fact]
    public void ForegroundRuleWinsOverEarlierRunningRule()
    {
        var config = Config(
            Preset("running", Rule("game.exe", ExecutableMatchMode.Running)),
            Preset("foreground", Rule("editor.exe", ExecutableMatchMode.Foreground)));

        string result = ControllerPresetManager.MatchPreset(
            config,
            new ProcessChoice(@"C:\Apps\editor.exe", "editor"),
            [new ProcessChoice(@"C:\Games\game.exe", "game")]);

        Assert.Equal("foreground", result);
    }

    [Fact]
    public void RunningRulesUsePresetListOrderAndFallBackToDefault()
    {
        var config = Config(
            Preset("first", Rule("game.exe", ExecutableMatchMode.Running)),
            Preset("second", Rule("game.exe", ExecutableMatchMode.Running)));

        Assert.Equal("first", ControllerPresetManager.MatchPreset(
            config, null, [new ProcessChoice(@"C:\Apps\game.exe", "game")]));
        Assert.Equal("default", ControllerPresetManager.MatchPreset(config, null, []));
    }

    [Fact]
    public void LegacyMigrationCopiesBindingsAndTurboWithoutInventingOthers()
    {
        var values = new Dictionary<string, object>
        {
            ["bind_a"] = "02-29",
            ["bind2_a"] = "00-00",
            ["turbo_a"] = 100
        };

        ControllerPresetConfig config = ControllerPresetManager.CreateDefaultFromLegacy(
            values.ContainsKey,
            key => values.GetValueOrDefault(key)?.ToString(),
            key => Convert.ToInt32(values.GetValueOrDefault(key) ?? 0));

        ControllerButtonBinding binding = config.Presets.Single().Bindings["a"];
        Assert.Equal("02-29", binding.Primary.FirmwareCode);
        Assert.Equal("00-00", binding.Secondary.FirmwareCode);
        Assert.Equal(100, binding.PrimaryTurboMs);
        Assert.DoesNotContain("b", config.Presets.Single().Bindings.Keys);
    }

    [Fact]
    public void ConfigurationRoundTripsAndMalformedConfigurationIsRejected()
    {
        ControllerPresetConfig expected = Config(Preset("game", Rule("game.exe", ExecutableMatchMode.Foreground)));
        expected.Combinations.Add(new InputCombination { Name = "Chord", Keys = [17, 65], MouseButton = CombinationMouseButton.Left });

        string json = JsonSerializer.Serialize(expected);
        ControllerPresetConfig? actual = JsonSerializer.Deserialize<ControllerPresetConfig>(json);

        Assert.True(ControllerPresetManager.Validate(actual));
        Assert.Equal("Chord", actual!.Combinations.Single().Name);
        Assert.False(ControllerPresetManager.Validate(new ControllerPresetConfig { Presets = [] }));
    }

    [Fact]
    public void CombinationKeysAreUnlimitedUniqueAndOrdered()
    {
        int[] source = Enumerable.Range(1, 100).Concat([10, 20, 30, 0, -1]).ToArray();
        List<int> normalized = ControllerPresetManager.NormalizeKeys(source);

        Assert.Equal(100, normalized.Count);
        Assert.Equal(Enumerable.Range(1, 100), normalized);
    }

    private static ControllerPresetConfig Config(params ControllerPreset[] presets) => new()
    {
        DefaultPresetId = "default",
        SelectedPresetId = "default",
        Presets = [new ControllerPreset { Id = "default", Name = "Default" }, .. presets]
    };

    private static ControllerPreset Preset(string id, params ExecutableRule[] rules) => new()
    {
        Id = id,
        Name = id,
        Rules = [.. rules]
    };

    private static ExecutableRule Rule(string executable, ExecutableMatchMode mode) => new()
    {
        ExecutablePath = @"C:\Apps\" + executable,
        ExecutableName = executable,
        MatchMode = mode
    };
}

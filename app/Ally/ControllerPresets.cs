using GHelper.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GHelper.Ally;

public enum ExecutableMatchMode
{
    Foreground,
    Running
}

public enum CombinationMouseButton
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2
}

public sealed class InputCombination
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Combination";
    public List<int> Keys { get; set; } = [];
    public CombinationMouseButton MouseButton { get; set; }
}

public sealed class ControllerBindingTarget
{
    public string? FirmwareCode { get; set; }
    public string? CombinationId { get; set; }
}

public sealed class ControllerButtonBinding
{
    public ControllerBindingTarget Primary { get; set; } = new();
    public ControllerBindingTarget Secondary { get; set; } = new();
    public int PrimaryTurboMs { get; set; }
    public int SecondaryTurboMs { get; set; }
}

public sealed class ExecutableRule
{
    public string ExecutablePath { get; set; } = "";
    public string ExecutableName { get; set; } = "";
    public ExecutableMatchMode MatchMode { get; set; }
}

public sealed class ControllerPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Preset";
    public Dictionary<string, ControllerButtonBinding> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ExecutableRule> Rules { get; set; } = [];
}

public sealed class ControllerPresetConfig
{
    public int SchemaVersion { get; set; } = 1;
    public string DefaultPresetId { get; set; } = "default";
    public string SelectedPresetId { get; set; } = "default";
    public bool AutoSwitchEnabled { get; set; }
    public bool ShowSwitchToast { get; set; } = true;
    public List<ControllerPreset> Presets { get; set; } = [];
    public List<InputCombination> Combinations { get; set; } = [];
}

public readonly record struct ControllerPresetSummary(string Id, string Name, bool IsDefault);
public readonly record struct ProcessChoice(string Path, string Name, bool HasVisibleWindow = false, bool IsForeground = false)
{
    public override string ToString()
    {
        string label = string.IsNullOrWhiteSpace(Path) ? Name : $"{Name} — {Path}";
        return IsForeground ? $"● {label}" : label;
    }
}

public static class ControllerPresetManager
{
    private const string ConfigKey = "controller_presets";
    private const uint GwHwndNext = 2;
    private static readonly string[] ButtonIds =
    [
        "m1", "m2", "a", "b", "x", "y", "du", "dd", "dl", "dr",
        "rt", "lt", "rb", "lb", "rs", "ls", "vb", "mb"
    ];

    private static readonly object Sync = new();
    private static ControllerPresetConfig? _config;
    private static string _effectivePresetId = "default";
    private static System.Threading.Timer? _timer;
    private static int _tickBusy;
    private static long _lastProcessScan;
    private static List<ProcessChoice> _running = [];
    private static string? _pendingPresetId;
    private static int _pendingCount;

    public static event Action? Changed;
    public static event Action? ActivePresetChanged;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    public static void Start()
    {
        if (!AppConfig.IsAlly()) return;
        EnsureLoaded();
        lock (Sync)
        {
            if (_timer is not null) return;
            _timer = new System.Threading.Timer(_ => Tick(), null, 100, 400);
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public static IReadOnlyList<ControllerPresetSummary> Presets()
    {
        EnsureLoaded();
        lock (Sync)
            return _config!.Presets.Select(p => new ControllerPresetSummary(p.Id, p.Name, p.Id == _config.DefaultPresetId)).ToArray();
    }

    public static IReadOnlyList<InputCombination> Combinations()
    {
        EnsureLoaded();
        lock (Sync) return _config!.Combinations.Select(CloneCombination).ToArray();
    }

    public static string SelectedPresetId
    {
        get { EnsureLoaded(); lock (Sync) return _config!.SelectedPresetId; }
    }

    public static string EffectivePresetId
    {
        get { EnsureLoaded(); lock (Sync) return _effectivePresetId; }
    }

    public static bool AutoSwitchEnabled
    {
        get { EnsureLoaded(); lock (Sync) return _config!.AutoSwitchEnabled; }
    }

    public static bool ShowSwitchToast
    {
        get { EnsureLoaded(); lock (Sync) return _config!.ShowSwitchToast; }
    }

    public static void SetAutoSwitch(bool enabled)
    {
        EnsureLoaded();
        lock (Sync)
        {
            _config!.AutoSwitchEnabled = enabled;
            SaveLocked();
            _pendingPresetId = null;
            _pendingCount = 0;
        }

        Changed?.Invoke();
        if (enabled) EvaluateNow();
        else ApplyEffective(SelectedPresetId, false);
    }

    public static void SetShowToast(bool enabled)
    {
        EnsureLoaded();
        lock (Sync)
        {
            _config!.ShowSwitchToast = enabled;
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public static void SelectPreset(string id, bool manual = true)
    {
        EnsureLoaded();
        lock (Sync)
        {
            if (_config!.Presets.All(p => p.Id != id)) return;
            _config.SelectedPresetId = id;
            if (manual) _config.AutoSwitchEnabled = false;
            SaveLocked();
        }

        Changed?.Invoke();
        ApplyEffective(id, false);
    }

    public static string AddPreset(string name)
    {
        EnsureLoaded();
        string id;
        lock (Sync)
        {
            name = UniquePresetNameLocked(name);
            id = Guid.NewGuid().ToString("N");
            _config!.Presets.Add(new ControllerPreset { Id = id, Name = name });
            SaveLocked();
        }
        Changed?.Invoke();
        return id;
    }

    public static string DuplicatePreset(string sourceId)
    {
        EnsureLoaded();
        string id;
        lock (Sync)
        {
            ControllerPreset source = PresetLocked(sourceId);
            id = Guid.NewGuid().ToString("N");
            var copy = new ControllerPreset
            {
                Id = id,
                Name = UniquePresetNameLocked(source.Name + " Copy"),
                Bindings = source.Bindings.ToDictionary(p => p.Key, p => CloneBinding(p.Value), StringComparer.OrdinalIgnoreCase)
            };
            _config!.Presets.Add(copy);
            SaveLocked();
        }
        Changed?.Invoke();
        return id;
    }

    public static bool RenamePreset(string id, string name)
    {
        EnsureLoaded();
        lock (Sync)
        {
            ControllerPreset preset = PresetLocked(id);
            string trimmed = name.Trim();
            if (trimmed.Length == 0 || _config!.Presets.Any(p => p.Id != id && p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))) return false;
            preset.Name = trimmed;
            SaveLocked();
        }
        Changed?.Invoke();
        return true;
    }

    public static bool DeletePreset(string id)
    {
        EnsureLoaded();
        bool wasEffective;
        lock (Sync)
        {
            if (id == _config!.DefaultPresetId) return false;
            ControllerPreset? preset = _config.Presets.FirstOrDefault(p => p.Id == id);
            if (preset is null) return false;
            wasEffective = _effectivePresetId == id;
            _config.Presets.Remove(preset);
            if (_config.SelectedPresetId == id) _config.SelectedPresetId = _config.DefaultPresetId;
            SaveLocked();
        }
        Changed?.Invoke();
        if (wasEffective) ApplyEffective(_config!.DefaultPresetId, false);
        return true;
    }

    public static void MovePreset(string id, int delta)
    {
        EnsureLoaded();
        lock (Sync)
        {
            int index = _config!.Presets.FindIndex(p => p.Id == id);
            int target = Math.Clamp(index + delta, 0, _config.Presets.Count - 1);
            if (index < 0 || index == target) return;
            ControllerPreset item = _config.Presets[index];
            _config.Presets.RemoveAt(index);
            _config.Presets.Insert(target, item);
            SaveLocked();
        }
        Changed?.Invoke();
        if (AutoSwitchEnabled) EvaluateNow();
    }

    public static IReadOnlyList<ExecutableRule> Rules(string presetId)
    {
        EnsureLoaded();
        lock (Sync) return PresetLocked(presetId).Rules.Select(CloneRule).ToArray();
    }

    public static void SetRules(string presetId, IEnumerable<ExecutableRule> rules)
    {
        EnsureLoaded();
        lock (Sync)
        {
            ControllerPreset preset = PresetLocked(presetId);
            if (preset.Id == _config!.DefaultPresetId) return;
            preset.Rules = rules.Select(NormalizeRule).Where(r => r.ExecutableName.Length > 0).ToList();
            SaveLocked();
        }
        Changed?.Invoke();
        if (AutoSwitchEnabled) EvaluateNow();
    }

    public static string? GetBindingSelection(string buttonId, bool secondary)
    {
        EnsureLoaded();
        lock (Sync)
        {
            ControllerButtonBinding? binding = BindingLocked(PresetLocked(_effectivePresetId), buttonId, false);
            ControllerBindingTarget? target = secondary ? binding?.Secondary : binding?.Primary;
            if (!string.IsNullOrWhiteSpace(target?.CombinationId)) return "combo:" + target.CombinationId;
            return target?.FirmwareCode;
        }
    }

    public static string ResolveBinding(string buttonId, bool secondary, string fallback)
    {
        EnsureLoaded();
        string? combinationId;
        string? firmwareCode;
        lock (Sync)
        {
            ControllerButtonBinding? binding = BindingLocked(PresetLocked(_effectivePresetId), buttonId, false);
            ControllerBindingTarget? target = secondary ? binding?.Secondary : binding?.Primary;
            combinationId = target?.CombinationId;
            firmwareCode = target?.FirmwareCode;
        }
        if (!string.IsNullOrWhiteSpace(combinationId))
            return CombinationCarrierManager.GetFirmwareCode(combinationId) ?? "00-00";
        return firmwareCode ?? fallback;
    }

    public static void SetBinding(string buttonId, bool secondary, string? selection)
    {
        EnsureLoaded();
        lock (Sync)
        {
            ControllerButtonBinding binding = BindingLocked(PresetLocked(_effectivePresetId), buttonId, true)!;
            ControllerBindingTarget target = secondary ? binding.Secondary : binding.Primary;
            target.FirmwareCode = null;
            target.CombinationId = null;
            if (!string.IsNullOrWhiteSpace(selection))
            {
                if (selection.StartsWith("combo:", StringComparison.OrdinalIgnoreCase))
                    target.CombinationId = selection[6..];
                else
                    target.FirmwareCode = selection;
            }
            SaveLocked();
        }
        Changed?.Invoke();
        RefreshHardware();
    }

    public static int GetTurbo(string buttonId, bool secondary)
    {
        EnsureLoaded();
        lock (Sync)
        {
            ControllerButtonBinding? binding = BindingLocked(PresetLocked(_effectivePresetId), buttonId, false);
            return secondary ? binding?.SecondaryTurboMs ?? 0 : binding?.PrimaryTurboMs ?? 0;
        }
    }

    public static void SetTurbo(string buttonId, bool secondary, int milliseconds)
    {
        EnsureLoaded();
        lock (Sync)
        {
            ControllerButtonBinding binding = BindingLocked(PresetLocked(_effectivePresetId), buttonId, true)!;
            if (secondary) binding.SecondaryTurboMs = milliseconds;
            else binding.PrimaryTurboMs = milliseconds;
            SaveLocked();
        }
        Changed?.Invoke();
        RefreshHardware();
    }

    public static string AddCombination(string name, IEnumerable<int> keys, CombinationMouseButton mouseButton)
    {
        EnsureLoaded();
        string id;
        lock (Sync)
        {
            id = Guid.NewGuid().ToString("N");
            _config!.Combinations.Add(new InputCombination
            {
                Id = id,
                Name = UniqueCombinationNameLocked(name),
                Keys = NormalizeKeys(keys),
                MouseButton = mouseButton
            });
            SaveLocked();
        }
        Changed?.Invoke();
        RefreshHardware();
        return id;
    }

    public static bool UpdateCombination(string id, string name, IEnumerable<int> keys, CombinationMouseButton mouseButton)
    {
        EnsureLoaded();
        lock (Sync)
        {
            InputCombination? combination = _config!.Combinations.FirstOrDefault(c => c.Id == id);
            if (combination is null) return false;
            string trimmed = name.Trim();
            if (trimmed.Length == 0 || _config.Combinations.Any(c => c.Id != id && c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase))) return false;
            combination.Name = trimmed;
            combination.Keys = NormalizeKeys(keys);
            combination.MouseButton = mouseButton;
            SaveLocked();
        }
        Changed?.Invoke();
        RefreshHardware();
        return true;
    }

    public static bool DeleteCombination(string id)
    {
        EnsureLoaded();
        lock (Sync)
        {
            InputCombination? combination = _config!.Combinations.FirstOrDefault(c => c.Id == id);
            if (combination is null) return false;
            _config.Combinations.Remove(combination);
            foreach (ControllerBindingTarget target in _config.Presets.SelectMany(p => p.Bindings.Values).SelectMany(b => new[] { b.Primary, b.Secondary }))
                if (target.CombinationId == id)
                {
                    target.CombinationId = null;
                    target.FirmwareCode = "00-00";
                }
            SaveLocked();
        }
        Changed?.Invoke();
        RefreshHardware();
        return true;
    }

    public static InputCombination? FindCombination(string id)
    {
        EnsureLoaded();
        lock (Sync)
        {
            InputCombination? found = _config!.Combinations.FirstOrDefault(c => c.Id == id);
            return found is null ? null : CloneCombination(found);
        }
    }

    public static IReadOnlyList<ProcessChoice> RunningProcesses()
    {
        uint foregroundPid = GetMostRecentExternalWindowProcessId();
        return SortRunningProcesses(ReadRunningProcesses(null, true, foregroundPid));
    }

    internal static IReadOnlyList<ProcessChoice> SortRunningProcesses(IEnumerable<ProcessChoice> choices) => choices
        .OrderByDescending(p => p.IsForeground)
        .ThenByDescending(p => p.HasVisibleWindow)
        .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static uint GetMostRecentExternalWindowProcessId()
    {
        IntPtr window = GetForegroundWindow();
        for (int inspected = 0; window != IntPtr.Zero && inspected < 256; inspected++)
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (processId != 0 && processId != Environment.ProcessId && IsWindowVisible(window)) return processId;
            window = GetWindow(window, GwHwndNext);
        }
        return 0;
    }

    internal static IReadOnlyList<InputCombination> EffectiveCombinations()
    {
        EnsureLoaded();
        lock (Sync)
        {
            ControllerPreset preset = PresetLocked(_effectivePresetId);
            HashSet<string> ids = preset.Bindings.Values
                .SelectMany(b => new[] { b.Primary.CombinationId, b.Secondary.CombinationId })
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToHashSet();
            return _config!.Combinations.Where(c => ids.Contains(c.Id)).Select(CloneCombination).ToArray();
        }
    }

    private static void EnsureLoaded()
    {
        if (_config is not null) return;
        lock (Sync)
        {
            if (_config is not null) return;
            bool hadStoredConfig = AppConfig.Exists(ConfigKey);
            ControllerPresetConfig? loaded = AppConfig.GetObject<ControllerPresetConfig>(ConfigKey);
            bool valid = Validate(loaded);
            _config = valid ? loaded! : MigrateLegacy();
            _effectivePresetId = _config.SelectedPresetId;
            if (!hadStoredConfig || valid) SaveLocked();
        }
    }

    internal static bool Validate(ControllerPresetConfig? config)
    {
        if (config is null || config.SchemaVersion != 1 || config.Presets is null || config.Combinations is null) return false;
        if (config.Presets.Count == 0 || config.Presets.All(p => p.Id != config.DefaultPresetId)) return false;
        foreach (ControllerPreset preset in config.Presets)
        {
            if (string.IsNullOrWhiteSpace(preset.Id) || preset.Name is null || preset.Bindings is null || preset.Rules is null) return false;
            if (preset.Bindings.Any(p => p.Value is null || p.Value.Primary is null || p.Value.Secondary is null)) return false;
            if (preset.Rules.Any(r => r is null || r.ExecutablePath is null || r.ExecutableName is null)) return false;
            preset.Bindings = new Dictionary<string, ControllerButtonBinding>(preset.Bindings, StringComparer.OrdinalIgnoreCase);
            preset.Rules = preset.Rules.Select(NormalizeRule).ToList();
        }
        if (config.Combinations.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || c.Name is null || c.Keys is null)) return false;
        if (config.Presets.All(p => p.Id != config.SelectedPresetId)) config.SelectedPresetId = config.DefaultPresetId;
        return true;
    }

    private static ControllerPresetConfig MigrateLegacy() => CreateDefaultFromLegacy(
        AppConfig.Exists,
        key => AppConfig.GetString(key),
        key => AppConfig.Get(key, 0));

    internal static ControllerPresetConfig CreateDefaultFromLegacy(
        Func<string, bool> exists,
        Func<string, string?> getString,
        Func<string, int> getInt)
    {
        var preset = new ControllerPreset { Id = "default", Name = "Default" };
        foreach (string button in ButtonIds)
        {
            var binding = new ControllerButtonBinding();
            bool has = false;
            if (exists("bind_" + button)) { binding.Primary.FirmwareCode = getString("bind_" + button); has = true; }
            if (exists("bind2_" + button)) { binding.Secondary.FirmwareCode = getString("bind2_" + button); has = true; }
            if (exists("turbo_" + button)) { binding.PrimaryTurboMs = getInt("turbo_" + button); has = true; }
            if (exists("turbo2_" + button)) { binding.SecondaryTurboMs = getInt("turbo2_" + button); has = true; }
            if (has) preset.Bindings[button] = binding;
        }

        return new ControllerPresetConfig
        {
            DefaultPresetId = preset.Id,
            SelectedPresetId = preset.Id,
            Presets = [preset]
        };
    }

    private static void SaveLocked() => AppConfig.SetObject(ConfigKey, _config!);

    private static ControllerPreset PresetLocked(string id) =>
        _config!.Presets.FirstOrDefault(p => p.Id == id) ?? _config.Presets.First(p => p.Id == _config.DefaultPresetId);

    private static ControllerButtonBinding? BindingLocked(ControllerPreset preset, string buttonId, bool create)
    {
        if (preset.Bindings.TryGetValue(buttonId, out ControllerButtonBinding? binding)) return binding;
        if (!create) return null;
        binding = new ControllerButtonBinding();
        preset.Bindings[buttonId] = binding;
        return binding;
    }

    private static string UniquePresetNameLocked(string name)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Preset" : name.Trim();
        string candidate = baseName;
        int number = 2;
        while (_config!.Presets.Any(p => p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) candidate = $"{baseName} {number++}";
        return candidate;
    }

    private static string UniqueCombinationNameLocked(string name)
    {
        string baseName = string.IsNullOrWhiteSpace(name) ? "Combination" : name.Trim();
        string candidate = baseName;
        int number = 2;
        while (_config!.Combinations.Any(c => c.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))) candidate = $"{baseName} {number++}";
        return candidate;
    }

    internal static List<int> NormalizeKeys(IEnumerable<int> keys) => keys.Where(k => k > 0).Distinct().ToList();
    private static InputCombination CloneCombination(InputCombination c) => new() { Id = c.Id, Name = c.Name, Keys = [.. c.Keys], MouseButton = c.MouseButton };
    private static ControllerButtonBinding CloneBinding(ControllerButtonBinding b) => new()
    {
        Primary = new() { FirmwareCode = b.Primary.FirmwareCode, CombinationId = b.Primary.CombinationId },
        Secondary = new() { FirmwareCode = b.Secondary.FirmwareCode, CombinationId = b.Secondary.CombinationId },
        PrimaryTurboMs = b.PrimaryTurboMs,
        SecondaryTurboMs = b.SecondaryTurboMs
    };
    private static ExecutableRule CloneRule(ExecutableRule r) => new() { ExecutablePath = r.ExecutablePath, ExecutableName = r.ExecutableName, MatchMode = r.MatchMode };
    private static ExecutableRule NormalizeRule(ExecutableRule r)
    {
        string path = "";
        try { if (!string.IsNullOrWhiteSpace(r.ExecutablePath)) path = Path.GetFullPath(r.ExecutablePath); } catch { path = r.ExecutablePath.Trim(); }
        string name = string.IsNullOrWhiteSpace(r.ExecutableName) ? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(r.ExecutableName);
        return new() { ExecutablePath = path, ExecutableName = name, MatchMode = r.MatchMode };
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _tickBusy, 1) != 0) return;
        try
        {
            EnsureLoaded();
            if (!AutoSwitchEnabled) return;
            EvaluateAuto(false);
        }
        catch (Exception ex) { Logger.WriteLine("Controller preset monitor: " + ex.Message); }
        finally { Volatile.Write(ref _tickBusy, 0); }
    }

    private static void EvaluateNow() => Task.Run(() => EvaluateAuto(true));

    private static void EvaluateAuto(bool immediate)
    {
        ControllerPresetConfig config;
        lock (Sync) config = _config!;

        GetWindowThreadProcessId(GetForegroundWindow(), out uint foregroundPid);
        if (foregroundPid == Environment.ProcessId) return;
        ProcessChoice? foreground = foregroundPid == 0 ? null : ReadProcess((int)foregroundPid);

        long now = Environment.TickCount64;
        if (immediate || now - _lastProcessScan >= 2000)
        {
            HashSet<string> configuredNames = config.Presets.SelectMany(p => p.Rules)
                .Where(r => r.MatchMode == ExecutableMatchMode.Running)
                .Select(r => Path.GetFileNameWithoutExtension(r.ExecutableName)).Where(n => n.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _running = ReadRunningProcesses(configuredNames);
            _lastProcessScan = now;
        }

        string target = MatchPreset(config, foreground, _running);
        if (immediate)
        {
            _pendingPresetId = null;
            _pendingCount = 0;
            ApplyEffective(target, true);
            return;
        }

        if (_pendingPresetId != target)
        {
            _pendingPresetId = target;
            _pendingCount = 1;
            return;
        }

        if (++_pendingCount >= 2)
        {
            _pendingCount = 0;
            ApplyEffective(target, true);
        }
    }

    internal static string MatchPreset(ControllerPresetConfig config, ProcessChoice? foreground, IReadOnlyList<ProcessChoice> running)
    {
        foreach (ControllerPreset preset in config.Presets)
            foreach (ExecutableRule rule in preset.Rules.Where(r => r.MatchMode == ExecutableMatchMode.Foreground))
                if (foreground is ProcessChoice process && Matches(rule, process)) return preset.Id;

        foreach (ControllerPreset preset in config.Presets)
            foreach (ExecutableRule rule in preset.Rules.Where(r => r.MatchMode == ExecutableMatchMode.Running))
                if (running.Any(process => Matches(rule, process))) return preset.Id;

        return config.DefaultPresetId;
    }

    private static bool Matches(ExecutableRule rule, ProcessChoice process)
    {
        if (rule.ExecutablePath.Length > 0 && process.Path.Length > 0)
        {
            try { return string.Equals(Path.GetFullPath(rule.ExecutablePath), Path.GetFullPath(process.Path), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(rule.ExecutablePath, process.Path, StringComparison.OrdinalIgnoreCase); }
        }
        return string.Equals(Path.GetFileNameWithoutExtension(rule.ExecutableName), Path.GetFileNameWithoutExtension(process.Name), StringComparison.OrdinalIgnoreCase);
    }

    private static List<ProcessChoice> ReadRunningProcesses(HashSet<string>? names, bool inspectWindows = false, uint foregroundPid = 0)
    {
        var result = new List<ProcessChoice>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string name = process.ProcessName;
                    if (names is not null && !names.Contains(name)) continue;
                    string path = "";
                    try { path = process.MainModule?.FileName ?? ""; } catch { }
                    bool isForeground = inspectWindows && process.Id == foregroundPid;
                    bool hasVisibleWindow = isForeground;
                    if (inspectWindows && !hasVisibleWindow)
                    {
                        try
                        {
                            IntPtr window = process.MainWindowHandle;
                            hasVisibleWindow = window != IntPtr.Zero && IsWindowVisible(window);
                        }
                        catch { }
                    }
                    result.Add(new(path, name, hasVisibleWindow, isForeground));
                }
                catch { }
            }
        }
        return result;
    }

    private static ProcessChoice? ReadProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            string path = "";
            try { path = process.MainModule?.FileName ?? ""; } catch { }
            return new(path, process.ProcessName);
        }
        catch { return null; }
    }

    private static void ApplyEffective(string id, bool automatic)
    {
        string? name = null;
        lock (Sync)
        {
            ControllerPreset preset = PresetLocked(id);
            if (_effectivePresetId == preset.Id) return;
            _effectivePresetId = preset.Id;
            name = preset.Name;
        }

        RefreshHardware();
        ActivePresetChanged?.Invoke();
        Changed?.Invoke();

        if (automatic && ShowSwitchToast && Program.toast is not null && Program.settingsForm?.IsHandleCreated == true)
            Program.settingsForm.BeginInvoke(() => Program.toast.RunToast($"Controller: {name}", ToastIcon.Controller));
    }

    private static void RefreshHardware()
    {
        CombinationCarrierManager.Refresh();
        AllyControl.ApplyMode();
    }
}

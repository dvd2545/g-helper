using GHelper.Helpers;
using GHelper.Input;
using System.Runtime.InteropServices;

namespace GHelper.Ally;

public static class InputCombinationPlayer
{
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyUp = 0x0002;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseXDown = 0x0080;
    private const uint MouseXUp = 0x0100;
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    public static void Play(InputCombination combination)
    {
        var inputs = new List<INPUT>();
        foreach (int key in combination.Keys)
            inputs.Add(Keyboard((ushort)key, 0));

        if (combination.MouseButton != CombinationMouseButton.None)
        {
            var (down, up, data) = MouseFlags(combination.MouseButton);
            inputs.Add(Mouse(down, data));
            inputs.Add(Mouse(up, data));
        }

        for (int i = combination.Keys.Count - 1; i >= 0; i--)
            inputs.Add(Keyboard((ushort)combination.Keys[i], KeyUp));

        if (inputs.Count == 0) return;
        uint sent = SendInput((uint)inputs.Count, [.. inputs], Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count) Logger.WriteLine($"Combination '{combination.Name}' sent {sent}/{inputs.Count} input events");
    }

    public static string Format(InputCombination combination)
    {
        var parts = combination.Keys.Select(k => ((Keys)k).ToString()).ToList();
        if (combination.MouseButton != CombinationMouseButton.None) parts.Add(combination.MouseButton.ToString() + " Click");
        return parts.Count == 0 ? "No input" : string.Join(" + ", parts);
    }

    private static INPUT Keyboard(ushort key, uint flags) => new()
    {
        type = InputKeyboard,
        data = new InputUnion { keyboard = new KEYBDINPUT { virtualKey = key, flags = flags } }
    };

    private static INPUT Mouse(uint flags, uint data) => new()
    {
        type = InputMouse,
        data = new InputUnion { mouse = new MOUSEINPUT { flags = flags, mouseData = data } }
    };

    private static (uint Down, uint Up, uint Data) MouseFlags(CombinationMouseButton button) => button switch
    {
        CombinationMouseButton.Left => (MouseLeftDown, MouseLeftUp, 0),
        CombinationMouseButton.Right => (MouseRightDown, MouseRightUp, 0),
        CombinationMouseButton.Middle => (MouseMiddleDown, MouseMiddleUp, 0),
        CombinationMouseButton.X1 => (MouseXDown, MouseXUp, XButton1),
        CombinationMouseButton.X2 => (MouseXDown, MouseXUp, XButton2),
        _ => (0, 0, 0)
    };
}

public static class CombinationCarrierManager
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Keys, (string FirmwareCode, string CombinationId)> Active = [];
    private static readonly Dictionary<string, string> CodesByCombination = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Running = new(StringComparer.OrdinalIgnoreCase);
    private static KeyboardHook? _hook;

    private static readonly (Keys Key, byte ScanCode)[] CarrierKeys =
    [
        (Keys.D0, 0x45), (Keys.D1, 0x16), (Keys.D2, 0x1E), (Keys.D3, 0x26), (Keys.D4, 0x25),
        (Keys.D5, 0x2E), (Keys.D6, 0x36), (Keys.D7, 0x3D), (Keys.D8, 0x3E), (Keys.D9, 0x46),
        (Keys.A, 0x1C), (Keys.B, 0x32), (Keys.C, 0x21), (Keys.D, 0x23), (Keys.E, 0x24),
        (Keys.F, 0x2B), (Keys.G, 0x34), (Keys.H, 0x33), (Keys.I, 0x43), (Keys.J, 0x3B),
        (Keys.K, 0x42), (Keys.L, 0x4B), (Keys.M, 0x3A), (Keys.N, 0x31), (Keys.O, 0x44),
        (Keys.P, 0x4D), (Keys.Q, 0x15), (Keys.R, 0x2D), (Keys.S, 0x1B), (Keys.T, 0x2C),
        (Keys.U, 0x3C), (Keys.V, 0x2A), (Keys.W, 0x1D), (Keys.X, 0x22), (Keys.Y, 0x35), (Keys.Z, 0x1A)
    ];

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public static void Initialize()
    {
        if (!AppConfig.IsAlly() || _hook is not null) return;
        _hook = new KeyboardHook();
        _hook.KeyPressed += OnCarrierPressed;
        RefreshCore();
    }

    public static void Stop()
    {
        lock (Sync)
        {
            _hook?.Dispose();
            _hook = null;
            Active.Clear();
            CodesByCombination.Clear();
            Running.Clear();
        }
    }

    public static void Refresh()
    {
        if (!AppConfig.IsAlly()) return;
        if (Program.settingsForm?.IsHandleCreated == true && Program.settingsForm.InvokeRequired)
        {
            try { Program.settingsForm.Invoke(RefreshCore); } catch (ObjectDisposedException) { }
            return;
        }
        RefreshCore();
    }

    public static string? GetFirmwareCode(string combinationId)
    {
        lock (Sync) return CodesByCombination.GetValueOrDefault(combinationId);
    }

    private static void RefreshCore()
    {
        int failures = 0;
        lock (Sync)
        {
            if (_hook is null) return;
            _hook.UnregisterAll();
            Active.Clear();
            CodesByCombination.Clear();

            int carrier = 0;
            foreach (InputCombination combination in ControllerPresetManager.EffectiveCombinations())
            {
                bool registered = false;
                while (carrier < CarrierKeys.Length)
                {
                    var candidate = CarrierKeys[carrier++];
                    if (!_hook.RegisterHotKey(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.NoRepeat, candidate.Key)) continue;

                    string code = $"04-04-8C-88-8A-{candidate.ScanCode:X2}";
                    Active[candidate.Key] = (code, combination.Id);
                    CodesByCombination[combination.Id] = code;
                    registered = true;
                    break;
                }

                if (!registered)
                {
                    failures++;
                    Logger.WriteLine($"No carrier hotkey available for combination '{combination.Name}'");
                }
            }
        }

        if (failures > 0 && Program.toast is not null)
            Program.toast.RunToast($"{failures} custom combination hotkey(s) unavailable", ToastIcon.Controller);
    }

    private static void OnCarrierPressed(object? sender, KeyPressedEventArgs e)
    {
        string? id;
        lock (Sync)
        {
            if (!Active.TryGetValue(e.Key, out var carrier)) return;
            id = carrier.CombinationId;
            if (!Running.Add(id)) return;
        }

        Task.Run(() =>
        {
            try
            {
                long timeout = Environment.TickCount64 + 500;
                while (Environment.TickCount64 < timeout && CarrierStillDown(e.Key)) Thread.Sleep(5);
                InputCombination? combination = ControllerPresetManager.FindCombination(id);
                if (combination is not null) InputCombinationPlayer.Play(combination);
            }
            catch (Exception ex) { Logger.WriteLine("Combination playback: " + ex.Message); }
            finally { lock (Sync) Running.Remove(id); }
        });
    }

    private static bool CarrierStillDown(Keys key) =>
        (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0 ||
        (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0 ||
        (GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0 ||
        (GetAsyncKeyState((int)key) & 0x8000) != 0;
}

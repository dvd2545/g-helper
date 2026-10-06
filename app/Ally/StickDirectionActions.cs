using System.Runtime.InteropServices;

namespace GHelper.Ally;

internal sealed class StickDirectionLatch
{
    private readonly bool[] _active = new bool[8];
    private readonly bool[] _armed = new bool[8];

    public void Reset() { Array.Clear(_active); Array.Clear(_armed); }

    // One shot per tilt, with separate activation/release thresholds to avoid jitter.
    internal int[] Update(short leftX, short leftY, short rightX, short rightY)
    {
        float lx = leftX / 32768f, ly = leftY / 32768f;
        float rx = rightX / 32768f, ry = rightY / 32768f;
        float[] magnitudes = [ly, -ly, -lx, lx, ry, -ry, -rx, rx];
        var fired = new List<int>();
        for (int i = 0; i < magnitudes.Length; i++)
        {
            float value = magnitudes[i];
            bool next = _active[i] ? value > 0.40f : value >= 0.60f;
            if (value <= 0.40f) _armed[i] = true;
            if (next && !_active[i] && _armed[i]) fired.Add(i);
            _active[i] = next;
        }
        return fired.ToArray();
    }
}

internal static class StickDirectionActions
{
    internal static readonly string[] BindingIds = ["ls_up", "ls_down", "ls_left", "ls_right", "rs_up", "rs_down", "rs_left", "rs_right"];
    private static readonly StickDirectionLatch Latch = new();
    private static System.Windows.Forms.Timer? _timer;
    private static string? _preset;
    private static int _slot = -1;
    private static int _resetPending;

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct State { public uint Packet; public Gamepad Gamepad; }
    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint index, out State state);

    public static void Start()
    {
        if (_timer is not null || !AppConfig.IsAlly()) return;
        Latch.Reset();
        ControllerPresetManager.Changed += RequestReset;
        _timer = new System.Windows.Forms.Timer { Interval = 20 };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    public static void Stop()
    {
        ControllerPresetManager.Changed -= RequestReset;
        _timer?.Dispose();
        _timer = null;
        Latch.Reset();
    }

    private static void RequestReset() => Interlocked.Exchange(ref _resetPending, 1);

    internal static bool ReadAxes(int slot, out short lx, out short ly, out short rx, out short ry)
    {
        bool connected = XInputGetState((uint)Math.Clamp(slot, 0, 3), out State state) == 0;
        lx = state.Gamepad.LeftX; ly = state.Gamepad.LeftY;
        rx = state.Gamepad.RightX; ry = state.Gamepad.RightY;
        return connected;
    }

    private static void Poll()
    {
        try
        {
            int slot = Math.Clamp(AppConfig.Get("stick_action_xinput_slot", 0), 0, 3);
            var snapshot = ControllerPresetManager.StickActions();
            bool reset = Interlocked.Exchange(ref _resetPending, 0) != 0;
            if (slot != _slot || snapshot.PresetId != _preset || reset)
            {
                Latch.Reset(); _slot = slot; _preset = snapshot.PresetId;
            }
            if (snapshot.Actions.Count == 0 || AppConfig.Is("controller_disabled") || !ReadAxes(slot, out short lx, out short ly, out short rx, out short ry))
            {
                Latch.Reset(); return;
            }
            foreach (int direction in Latch.Update(lx, ly, rx, ry))
                if (snapshot.Actions.TryGetValue(direction, out InputCombination? action))
                    InputCombinationPlayer.Play(action);
        }
        catch (Exception ex) { Stop(); Logger.WriteLine("Stick direction actions: " + ex.Message); }
    }
}

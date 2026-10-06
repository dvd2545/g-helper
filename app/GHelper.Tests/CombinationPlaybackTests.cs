using GHelper.Ally;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GHelper.Tests;

public sealed class CombinationPlaybackTests
{
    private delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint Key, Scan, Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }

    [Fact]
    public void MixedChordActuallyEmitsAllDownAndUpEventsOnWindows()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            IntPtr keyboard = IntPtr.Zero, mouse = IntPtr.Zero;
            var events = new List<string>();
            Hook keyboardCallback = (code, message, data) =>
            {
                if (code >= 0)
                {
                    var input = Marshal.PtrToStructure<KeyboardData>(data);
                    if (input.Extra == InputCombinationPlayer.InputMarker)
                    {
                        events.Add($"key:{input.Key}:{message.ToInt32():X}");
                        return new IntPtr(1); // Consume test input before it reaches any application.
                    }
                }
                return CallNextHookEx(IntPtr.Zero, code, message, data);
            };
            Hook mouseCallback = (code, message, data) =>
            {
                if (code >= 0 && Marshal.PtrToStructure<MouseData>(data).Extra == InputCombinationPlayer.InputMarker)
                {
                    events.Add($"mouse:{message.ToInt32():X}");
                    return new IntPtr(1);
                }
                return CallNextHookEx(IntPtr.Zero, code, message, data);
            };
            try
            {
                using var window = new Form();
                _ = window.Handle;
                keyboard = SetWindowsHookEx(13, keyboardCallback, IntPtr.Zero, 0);
                mouse = SetWindowsHookEx(14, mouseCallback, IntPtr.Zero, 0);
                Assert.NotEqual(IntPtr.Zero, keyboard);
                Assert.NotEqual(IntPtr.Zero, mouse);
                Task playback = Task.Run(() => InputCombinationPlayer.Play(new InputCombination { Keys = [0xA2, 0x41], MouseButton = CombinationMouseButton.Left }));
                var elapsed = Stopwatch.StartNew();
                while ((!playback.IsCompleted || events.Count < 6) && elapsed.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }
                Assert.True(playback.IsCompleted);
                playback.GetAwaiter().GetResult();
                Assert.Equal(new[] { "key:162:100", "key:65:100", "mouse:201", "mouse:202", "key:65:101", "key:162:101" }, events);
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard);
                if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
                GC.KeepAlive(keyboardCallback); GC.KeepAlive(mouseCallback);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

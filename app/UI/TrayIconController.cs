using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace GHelper.UI;

public enum TrayIconMode
{
    Default,
    CpuTemperature,
    GpuTemperature,
    BatteryPower,
    BatteryCharge
}

public readonly record struct TrayIconValue(string Text, Color Color);

public static class TrayIconFormatter
{
    private static readonly Color Normal = Color.FromArgb(54, 210, 112);
    private static readonly Color Amber = Color.FromArgb(255, 190, 40);
    private static readonly Color Hot = Color.FromArgb(255, 72, 72);
    private static readonly Color Neutral = Color.FromArgb(90, 190, 255);
    private static readonly Color Missing = Color.FromArgb(170, 170, 170);

    public static TrayIconValue Temperature(double? value)
    {
        if (value is null || value <= 0 || value >= 125) return new("--", Missing);
        int rounded = (int)Math.Round(value.Value);
        Color color = rounded >= 85 ? Hot : rounded >= 70 ? Amber : Normal;
        return new(rounded.ToString(), color);
    }

    public static TrayIconValue BatteryPower(decimal? value)
    {
        if (value is null) return new("--", Missing);
        int rounded = (int)Math.Round(value.Value, 0, MidpointRounding.AwayFromZero);
        string text = rounded > 0 ? "+" + rounded : rounded.ToString();
        Color color = rounded > 0 ? Normal : rounded == 0 ? Neutral : Math.Abs(rounded) >= 40 ? Hot : Amber;
        return new(text, color);
    }

    public static TrayIconValue BatteryCharge(double? value)
    {
        if (value is null || value < 0) return new("--", Missing);
        int rounded = Math.Clamp((int)Math.Round(value.Value), 0, 100);
        Color color = rounded <= 20 ? Hot : rounded < 50 ? Amber : Normal;
        return new(rounded.ToString(), color);
    }
}

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SettingsForm _settings;
    private readonly System.Threading.Timer _timer;
    private readonly Dictionary<string, Icon> _icons = [];
    private int _reading;
    private bool _disposed;
    private bool _isDark = RForm.CheckSystemDarkModeStatus();

    public TrayIconMode Mode { get; private set; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    public TrayIconController(NotifyIcon notifyIcon, SettingsForm settings)
    {
        _notifyIcon = notifyIcon;
        _settings = settings;
        int value = AppConfig.Get("tray_icon_mode", 0);
        Mode = Enum.IsDefined(typeof(TrayIconMode), value) ? (TrayIconMode)value : TrayIconMode.Default;
        _timer = new System.Threading.Timer(_ => ReadMetric(), null, Timeout.Infinite, Timeout.Infinite);
        ApplyMode();
    }

    public void SetMode(TrayIconMode mode)
    {
        Mode = mode;
        AppConfig.Set("tray_icon_mode", (int)mode);
        ApplyMode();
    }

    public void UpdateDefaultIcon(bool themeChanged = false)
    {
        if (themeChanged) _isDark = RForm.CheckSystemDarkModeStatus();
        if (Mode == TrayIconMode.Default) ApplyDefaultIcon();
    }

    private void ApplyMode()
    {
        if (_disposed) return;
        if (Mode == TrayIconMode.Default)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            ApplyDefaultIcon();
        }
        else
        {
            _timer.Change(0, 2000);
        }
    }

    private void ApplyDefaultIcon()
    {
        int gpuMode = AppConfig.Get("gpu_mode");
        bool bw = AppConfig.IsBWIcon();
        string key = $"default:{gpuMode}:{_isDark}:{bw}";
        if (!_icons.TryGetValue(key, out Icon? icon))
        {
            Icon resource = gpuMode switch
            {
                AsusACPI.GPUModeEco => bw ? (_isDark ? Properties.Resources.light_eco : Properties.Resources.dark_eco) : Properties.Resources.eco,
                AsusACPI.GPUModeUltimate => bw ? (_isDark ? Properties.Resources.light_standard : Properties.Resources.dark_standard) : Properties.Resources.ultimate,
                _ => bw ? (_isDark ? Properties.Resources.light_standard : Properties.Resources.dark_standard) : Properties.Resources.standard,
            };
            icon = (Icon)resource.Clone();
            _icons[key] = icon;
        }
        _notifyIcon.Icon = icon;
    }

    private void ReadMetric()
    {
        if (_disposed || Mode == TrayIconMode.Default || Interlocked.Exchange(ref _reading, 1) != 0) return;
        try
        {
            TrayIconMode readMode = Mode;
            TrayIconValue value = readMode switch
            {
                TrayIconMode.CpuTemperature => TrayIconFormatter.Temperature(HardwareControl.GetCPUTemp()),
                TrayIconMode.GpuTemperature => TrayIconFormatter.Temperature(HardwareControl.GetGPUTemp()),
                TrayIconMode.BatteryPower => ReadBatteryPower(),
                TrayIconMode.BatteryCharge => ReadBatteryCharge(),
                _ => new("--", Color.Gray)
            };

            if (_settings.IsHandleCreated && !_settings.IsDisposed)
                _settings.BeginInvoke(() => ApplyMetric(readMode, value));

            _settings.RefreshSensors();
        }
        catch (Exception ex) { Logger.WriteLine("Tray metric: " + ex.Message); }
        finally { Volatile.Write(ref _reading, 0); }
    }

    private static TrayIconValue ReadBatteryPower()
    {
        if ((SystemInformation.PowerStatus.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0)
            return TrayIconFormatter.BatteryPower(null);
        HardwareControl.ReadBatteryState();
        return TrayIconFormatter.BatteryPower(HardwareControl.batteryRate);
    }

    private static TrayIconValue ReadBatteryCharge()
    {
        return TrayIconFormatter.BatteryCharge(
            HardwareControl.TryGetBatteryChargePercentage(out double charge) ? charge : null);
    }

    private void ApplyMetric(TrayIconMode readMode, TrayIconValue value)
    {
        if (_disposed || Mode != readMode || Mode == TrayIconMode.Default) return;
        string key = $"metric:{value.Text}:{value.Color.ToArgb()}";
        if (!_icons.TryGetValue(key, out Icon? icon))
        {
            icon = Render(value);
            _icons[key] = icon;
        }
        _notifyIcon.Icon = icon;
    }

    private static Icon Render(TrayIconValue value)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var path = new GraphicsPath();
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        path.AddString(value.Text, FontFamily.GenericSansSerif, (int)FontStyle.Bold, 28f,
            PointF.Empty, format);

        // Fit the complete glyph path instead of choosing a font size from the
        // character count. This keeps wide values such as "100" and signed
        // power readings inside the small notification-area icon canvas.
        RectangleF bounds = path.GetBounds();
        const float availableSize = 27f;
        float scale = Math.Min(availableSize / bounds.Width, availableSize / bounds.Height);
        float width = bounds.Width * scale;
        float height = bounds.Height * scale;
        using (var transform = new System.Drawing.Drawing2D.Matrix(
                   scale, 0, 0, scale,
                   (32f - width) / 2f - bounds.X * scale,
                   (32f - height) / 2f - bounds.Y * scale))
        {
            path.Transform(transform);
        }

        using var outline = new Pen(Color.FromArgb(235, 0, 0, 0), 2.4f) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(value.Color);
        graphics.DrawPath(outline, path);
        graphics.FillPath(fill, path);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        foreach (Icon icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}

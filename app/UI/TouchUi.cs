using System.Runtime.InteropServices;

namespace GHelper.UI;

public static class TouchUi
{
    private const int SmMaximumTouches = 95;
    public const int MinimumTargetDip = 28;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static bool IsTouchDevice
    {
        get
        {
            try { return GetSystemMetrics(SmMaximumTouches) > 0; }
            catch { return false; }
        }
    }

    internal static int ScaleDip(int dip, int dpi) =>
        Math.Max(1, (int)Math.Round(dip * Math.Max(96, dpi) / 96f));

    public static int TargetSize(Control control, int dip = MinimumTargetDip) =>
        ScaleDip(dip, control.DeviceDpi);

    public static void PrepareDialog(RForm form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        form.InitTheme(true);
        Apply(form, true);
    }

    public static void Apply(Control root, bool force = false)
    {
        if (!force && !IsTouchDevice) return;
        int target = TargetSize(root);
        int horizontalPadding = ScaleDip(8, root.DeviceDpi);
        int verticalPadding = ScaleDip(5, root.DeviceDpi);
        Apply(root.Controls, target, horizontalPadding, verticalPadding);
    }

    public static void ApplyMenu(ToolStripItemCollection items, int dpi)
    {
        if (!IsTouchDevice) return;
        int horizontal = ScaleDip(10, dpi);
        int vertical = ScaleDip(7, dpi);
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.Padding = new Padding(horizontal, vertical, horizontal, vertical);
                if (menuItem.HasDropDownItems) ApplyMenu(menuItem.DropDownItems, dpi);
            }
        }
    }

    private static void Apply(Control.ControlCollection controls, int target, int horizontalPadding, int verticalPadding)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case Button button:
                    button.MinimumSize = new Size(Math.Max(button.MinimumSize.Width, target), target);
                    if (button.Width < target) button.Width = target;
                    if (button.Height < target) button.Height = target;
                    if (button.Padding.Horizontal < horizontalPadding * 2)
                        button.Padding = new Padding(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);
                    break;

                case CheckBox checkBox:
                    checkBox.MinimumSize = new Size(checkBox.MinimumSize.Width, target);
                    if (checkBox.Padding.Vertical < verticalPadding * 2)
                        checkBox.Padding = new Padding(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);
                    break;

                case RTextBox textBox when !textBox.Multiline:
                    textBox.AutoSize = false;
                    textBox.Height = Math.Max(textBox.Height, target);
                    break;

                case ListBox listBox:
                    listBox.BackColor = RForm.buttonMain;
                    listBox.ForeColor = RForm.foreMain;
                    break;

                case ListView listView:
                    listView.BackColor = RForm.buttonMain;
                    listView.ForeColor = RForm.foreMain;
                    break;

                case FlowLayoutPanel flow when flow.Controls.OfType<Button>().Any():
                    flow.AutoSize = true;
                    flow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    int panelHeight = target + ScaleDip(10, flow.DeviceDpi);
                    flow.MinimumSize = new Size(flow.MinimumSize.Width, panelHeight);
                    if (flow.Height < panelHeight) flow.Height = panelHeight;
                    break;
            }

            if (control.HasChildren) Apply(control.Controls, target, horizontalPadding, verticalPadding);
        }
    }
}

public class TouchListBox : ListBox
{
    public TouchListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        BorderStyle = BorderStyle.None;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ItemHeight = TouchUi.TargetSize(this);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ItemHeight = TouchUi.TargetSize(this);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color background = selected ? SystemColors.Highlight : BackColor;
        Color foreground = selected ? SystemColors.HighlightText : ForeColor;
        using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
        int padding = TouchUi.ScaleDip(12, (int)e.Graphics.DpiX);
        Rectangle textBounds = new(e.Bounds.X + padding, e.Bounds.Y, Math.Max(0, e.Bounds.Width - padding * 2), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textBounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }
}

public class TouchListView : ListView
{
    private ImageList? _rowHeightImages;

    public TouchListView()
    {
        BorderStyle = BorderStyle.None;
        FullRowSelect = true;
        HideSelection = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRowHeight();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateRowHeight();
    }

    private void UpdateRowHeight()
    {
        int target = TouchUi.TargetSize(this);
        ImageList replacement = new() { ImageSize = new Size(1, target), ColorDepth = ColorDepth.Depth32Bit };
        using (var rowImage = new Bitmap(1, target))
            replacement.Images.Add(rowImage);
        ImageList? previous = _rowHeightImages;
        _rowHeightImages = replacement;
        SmallImageList = replacement;
        previous?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SmallImageList = null;
            _rowHeightImages?.Dispose();
            _rowHeightImages = null;
        }
        base.Dispose(disposing);
    }
}

using System.Windows.Forms;
using System.Drawing;

namespace GHelper.Tests;

public sealed class SettingsLayoutTests
{
    [Theory]
    [InlineData(96, false)]
    [InlineData(144, false)]
    [InlineData(192, false)]
    [InlineData(240, false)]
    [InlineData(96, true)]
    [InlineData(144, true)]
    [InlineData(192, true)]
    [InlineData(240, true)]
    public void ActionEditorButtonsRemainSeparatedWhenScaled(int dpi, bool analog)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var ownedFonts = new List<Font>();
            try
            {
                using GHelper.UI.RForm form = analog ? new GHelper.Ally.StickDirectionDialog(layoutPreview: true) : new GHelper.Ally.CombinationEditorDialog();
                form.Opacity = 0;
                form.Show();
                float factor = dpi / (float)form.DeviceDpi;
                form.AutoScaleMode = AutoScaleMode.None;
                var controls = Descendants(form).Select(c => (Control: c, Font: c.Font)).ToArray();
                form.Scale(new SizeF(factor, factor));
                foreach (var item in controls)
                {
                    var font = new Font(item.Font.FontFamily, item.Font.Size * factor, item.Font.Style);
                    ownedFonts.Add(font);
                    item.Control.Font = font;
                }
                form.PerformLayout();
                foreach (var flow in Descendants(form).OfType<FlowLayoutPanel>())
                {
                    flow.PerformLayout();
                    var buttons = flow.Controls.Cast<Control>().OfType<Button>().ToArray();
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        Assert.True(buttons[i].Bottom <= flow.ClientSize.Height, $"{dpi} DPI: {buttons[i].Text} clipped");
                        for (int j = i + 1; j < buttons.Length; j++) Assert.False(buttons[i].Bounds.IntersectsWith(buttons[j].Bounds));
                    }
                }
                if (analog)
                {
                    var close = Assert.Single(form.Controls.OfType<Button>());
                    Assert.True(close.Bottom <= form.ClientSize.Height);
                    Assert.True(Assert.Single(form.Controls.OfType<Panel>()).AutoScroll);
                    var table = Assert.Single(Descendants(form).OfType<TableLayoutPanel>());
                    table.PerformLayout();
                    var combos = table.Controls.OfType<ComboBox>().ToArray();
                    Assert.Equal(9, combos.Length);
                    for (int i = 0; i < combos.Length; i++)
                    {
                        Assert.True(combos[i].ItemHeight >= combos[i].Font.Height, $"{dpi} DPI: dropdown text clipped");
                        Assert.True(combos[i].Right <= table.ClientSize.Width);
                        for (int j = i + 1; j < combos.Length; j++) Assert.False(combos[i].Bounds.IntersectsWith(combos[j].Bounds));
                    }
                }
                string output = Path.Combine(Path.GetTempPath(), "GHelper-layout-review");
                Directory.CreateDirectory(output);
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(output, $"{(analog ? "analog-actions" : "action-editor")}-{dpi}.png"));
            }
            catch (Exception ex) { failure = ex; }
            finally { foreach (var font in ownedFonts) font.Dispose(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    [InlineData(240)]
    public void SettingsSectionsDoNotOverlapAtHighScale(int dpi)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new SettingsForm(layoutPreview: true);
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                form.CreateControl();
                float factor = dpi / (float)form.DeviceDpi;
                form.AutoScaleMode = AutoScaleMode.None;
                var fonts = Descendants(form).Select(c => (Control: c, Font: c.Font)).ToArray();
                form.Scale(new SizeF(factor, factor));
                var scaledFonts = new List<Font>();
                foreach (var item in fonts)
                {
                    var font = new Font(item.Font.FontFamily, item.Font.Size * factor, item.Font.Style);
                    scaledFonts.Add(font);
                    item.Control.Font = font;
                }
                form.PerformLayout();
                Assert.True(form.AutoScroll);
                Assert.False(form.AutoSize);
                var content = Assert.Single(form.Controls.Cast<Control>());
                content.PerformLayout();
                var sections = content.Controls.Cast<Control>().Where(c => c.Visible && c.Dock == DockStyle.Top).OrderBy(c => c.Top).ToArray();
                Assert.NotEmpty(sections);
                for (int i = 1; i < sections.Length; i++)
                    Assert.True(sections[i].Top >= sections[i - 1].Bottom, $"{dpi} DPI: {sections[i - 1].Name} overlaps {sections[i].Name}");
                string output = Path.Combine(Path.GetTempPath(), "GHelper-layout-review");
                Directory.CreateDirectory(output);
                using var bitmap = new Bitmap(content.Width, content.Height);
                content.DrawToBitmap(bitmap, new Rectangle(Point.Empty, content.Size));
                bitmap.Save(Path.Combine(output, $"settings-{dpi}.png"));
                form.Dispose();
                foreach (Font font in scaledFonts) font.Dispose();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (Control descendant in Descendants(child)) yield return descendant;
    }
}

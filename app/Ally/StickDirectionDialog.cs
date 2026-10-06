using GHelper.UI;

namespace GHelper.Ally;

internal sealed class StickDirectionDialog : RForm
{
    private sealed record Choice(string? Id, string Name) { public override string ToString() => Name; }
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 100 };

    // Preview constructor keeps layout tests independent of persisted user settings.
    internal StickDirectionDialog(bool layoutPreview = false)
    {
        Text = "Analog direction actions";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 460);
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var note = new Label
        {
            Text = "Analog movement stays active. Each direction can also trigger a keyboard/mouse action or open a program.\nTilt past 60% once; return below 40% to trigger again. Diagonals can trigger both directions.",
            AutoSize = true, MaximumSize = new Size(500, 0), Dock = DockStyle.Top, Margin = new Padding(3, 3, 3, 12)
        };
        table.Controls.Add(note, 0, 0); table.SetColumnSpan(note, 2);
        var slot = new RComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        slot.Items.AddRange(["Controller 1", "Controller 2", "Controller 3", "Controller 4"]);
        slot.SelectedIndex = layoutPreview ? 0 : Math.Clamp(AppConfig.Get("stick_action_xinput_slot", 0), 0, 3);
        slot.SelectedIndexChanged += (_, _) => { if (!layoutPreview) AppConfig.Set("stick_action_xinput_slot", slot.SelectedIndex); };
        table.Controls.Add(new Label { Text = "Controller", AutoSize = true }, 0, 1); table.Controls.Add(slot, 1, 1);
        var status = new Label { Text = "Move the Ally sticks to verify the selected controller.", AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(3, 8, 3, 10) };
        table.Controls.Add(status, 0, 2); table.SetColumnSpan(status, 2);
        IReadOnlyList<InputCombination> actions = layoutPreview ? [new InputCombination { Id = "sample", Name = "Example action" }] : ControllerPresetManager.Combinations();
        string[] labels = ["Left ↑", "Left ↓", "Left ←", "Left →", "Right ↑", "Right ↓", "Right ←", "Right →"];
        for (int i = 0; i < labels.Length; i++)
        {
            string id = StickDirectionActions.BindingIds[i];
            var combo = new RComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(3, 5, 3, 5) };
            combo.Items.Add(new Choice(null, "No added action"));
            foreach (InputCombination action in actions) combo.Items.Add(new Choice(action.Id, action.Name));
            string? current = layoutPreview ? null : ControllerPresetManager.GetBindingSelection(id, false);
            combo.SelectedIndex = 0;
            for (int j = 1; j < combo.Items.Count; j++) if (current == "combo:" + ((Choice)combo.Items[j]).Id) combo.SelectedIndex = j;
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (!layoutPreview) ControllerPresetManager.SetBinding(id, false, ((Choice)combo.SelectedItem!).Id is string actionId ? "combo:" + actionId : null);
            };
            table.Controls.Add(new Label { Text = labels[i], AutoSize = true, Margin = new Padding(3, 8, 16, 3) }, 0, i + 3);
            table.Controls.Add(combo, 1, i + 3);
        }
        if (actions.Count == 0)
        {
            var empty = new Label { Text = "Create an action first using Actions / Programs… in the controller window.", AutoSize = true, Dock = DockStyle.Top };
            table.Controls.Add(empty, 0, 11); table.SetColumnSpan(empty, 2);
        }
        var close = new RButton { Text = "Close", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.OK };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroll.Controls.Add(table);
        Controls.Add(scroll); Controls.Add(close);
        AcceptButton = close; CancelButton = close;
        TouchUi.PrepareDialog(this);
        Shown += (_, _) =>
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Height = Math.Min(table.PreferredSize.Height + close.Height + Height - ClientSize.Height, area.Height);
            if (!layoutPreview) _previewTimer.Start();
        };
        _previewTimer.Tick += (_, _) =>
        {
            try
            {
                status.Text = StickDirectionActions.ReadAxes(slot.SelectedIndex, out short lx, out short ly, out short rx, out short ry)
                    ? $"Left X {lx / 327.68f:0}%  Y {ly / 327.68f:0}%    Right X {rx / 327.68f:0}%  Y {ry / 327.68f:0}%"
                    : "Selected controller is disconnected. Try another controller number.";
            }
            catch (Exception ex) { _previewTimer.Stop(); status.Text = ex.Message; }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _previewTimer.Dispose();
        base.Dispose(disposing);
    }
}

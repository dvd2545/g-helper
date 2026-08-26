using GHelper.Ally;
using GHelper.UI;

namespace GHelper
{
    public partial class Handheld : RForm
    {

        static string activeBinding = "";
        static RButton? activeButton;
        private readonly Dictionary<string, RButton> _bindingButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RComboBox> _bindingCombos = [];
        private ComboBox? _presetCombo;
        private CheckBox? _autoSwitch;
        private CheckBox? _showToast;
        private Button? _appsButton;
        private bool _updatingPresets;
        private string _combinationSignature = "";

        public Handheld()
        {
            InitializeComponent();
            InitTheme(true);

            _ = ControllerPresetManager.Presets();
            InitPresetToolbar();

            Text = Properties.Strings.Controller;

            labelLSTitle.Text = Properties.Strings.LSDeadzones;
            labelRSTitle.Text = Properties.Strings.RSDeadzones;
            labelLTTitle.Text = Properties.Strings.LTDeadzones;
            labelRTTitle.Text = Properties.Strings.RTDeadzones;
            labelVibraTitle.Text = Properties.Strings.VibrationStrength;
            checkController.Text = Properties.Strings.DisableController;
            buttonReset.Text = Properties.Strings.Reset;

            labelPrimary.Text = Properties.Strings.BindingPrimary;
            labelSecondary.Text = Properties.Strings.BindingSecondary;

            Shown += Handheld_Shown;

            Init();

            trackLSMin.Scroll += Controller_Scroll;
            trackLSMax.Scroll += Controller_Scroll;
            trackRSMin.Scroll += Controller_Scroll;
            trackRSMax.Scroll += Controller_Scroll;

            trackLTMin.Scroll += Controller_Scroll;
            trackLTMax.Scroll += Controller_Scroll;
            trackRTMin.Scroll += Controller_Scroll;
            trackRTMax.Scroll += Controller_Scroll;

            trackVibra.Scroll += Controller_Scroll;

            buttonReset.Click += ButtonReset_Click;

            trackLSMin.ValueChanged += Controller_Complete;
            trackLSMax.ValueChanged += Controller_Complete;
            trackRSMin.ValueChanged += Controller_Complete;
            trackRSMax.ValueChanged += Controller_Complete;

            trackLTMin.ValueChanged += Controller_Complete;
            trackLTMax.ValueChanged += Controller_Complete;
            trackRTMin.ValueChanged += Controller_Complete;
            trackRTMax.ValueChanged += Controller_Complete;

            trackVibra.ValueChanged += Controller_Complete;

            ButtonBinding("m1", "M1", buttonM1);
            ButtonBinding("m2", "M2", buttonM2);

            ButtonBinding("a", "A", buttonA);
            ButtonBinding("b", "B", buttonB);
            ButtonBinding("x", "X", buttonX);
            ButtonBinding("y", "Y", buttonY);

            ButtonBinding("du", "DPad Up", buttonDPU);
            ButtonBinding("dd", "DPad Down", buttonDPD);

            ButtonBinding("dl", "DPad Left", buttonDPL);
            ButtonBinding("dr", "DPad Right", buttonDPR);

            ButtonBinding("rt", "Right Trigger", buttonRT);
            ButtonBinding("lt", "Left Trigger", buttonLT);

            ButtonBinding("rb", "Right Bumper", buttonRB);
            ButtonBinding("lb", "Left Bumper", buttonLB);

            ButtonBinding("rs", "Right Stick", buttonRS);
            ButtonBinding("ll", "Left Stick", buttonLS);

            ButtonBinding("vb", "View", buttonView);
            ButtonBinding("mb", "Menu", buttonMenu);

            ComboBinding(comboPrimary);
            ComboBinding(comboSecondary);
            
            ComboTurbo(comboTurboPrimary);
            ComboTurbo(comboTurboSecondary);

            checkController.Checked = AppConfig.Is("controller_disabled");
            checkController.CheckedChanged += CheckController_CheckedChanged;

            ControllerPresetManager.Changed += PresetsChanged;
            FormClosed += (_, _) => ControllerPresetManager.Changed -= PresetsChanged;

        }

        private void CheckController_CheckedChanged(object? sender, EventArgs e)
        {
            AppConfig.Set("controller_disabled", checkController.Checked ? 1 : 0);
            AllyControl.DisableXBoxController(checkController.Checked);
        }

        private static object[] BuildBindingComboItems()
        {
            var list = new List<object>();
            foreach (var (groupLabel, items) in AllyControl.BindingGroups)
            {
                if (groupLabel != "")
                    list.Add(new BindingSeparator(groupLabel));
                foreach (var (code, name) in items)
                    list.Add(new BindingItem(code, name));
            }

            IReadOnlyList<InputCombination> combinations = ControllerPresetManager.Combinations();
            if (combinations.Count > 0)
            {
                list.Add(new BindingSeparator(DialogText.Get("CustomCombinations", "Custom Combinations")));
                foreach (InputCombination combination in combinations)
                    list.Add(new BindingItem("combo:" + combination.Id, combination.Name));
            }
            return list.ToArray();
        }

        private void ComboBinding(RComboBox combo)
        {
            _bindingCombos.Add(combo);
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.Items.AddRange(BuildBindingComboItems());
            combo.DrawItem += BindingCombo_DrawItem;
            combo.SelectedValueChanged += Binding_SelectedValueChanged;
        }

        private static void BindingCombo_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || sender is not ComboBox cmb) return;

            object obj = cmb.Items[e.Index];
            bool isSep = obj is BindingSeparator;

            Color back = isSep ? RForm.buttonSecond : RForm.buttonMain;

            if (!isSep && (e.State & DrawItemState.Selected) != 0)
                back = RForm.borderMain;

            using var backBrush = new SolidBrush(back);
            e.Graphics.FillRectangle(backBrush, e.Bounds);

            string text = obj.ToString() ?? string.Empty;
            Font font = isSep
                ? new Font(e.Font ?? SystemFonts.DefaultFont, FontStyle.Bold)
                : (e.Font ?? SystemFonts.DefaultFont);

            int indent = isSep ? 2 : 10;
            var textRect = new Rectangle(e.Bounds.X + indent, e.Bounds.Y,
                                         e.Bounds.Width - indent, e.Bounds.Height);

            using var foreBrush = new SolidBrush(RForm.foreMain);
            e.Graphics.DrawString(text, font, foreBrush, textRect,
                new StringFormat { LineAlignment = StringAlignment.Center });

            if (isSep) font.Dispose();
        }

        private void ComboTurbo(RComboBox combo)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DisplayMember = "Value";
            combo.ValueMember = "Key";
            combo.Items.Add(new KeyValuePair<int, string>(0, "Off"));
            combo.Items.Add(new KeyValuePair<int, string>(50, "50"));
            combo.Items.Add(new KeyValuePair<int, string>(100, "100"));
            combo.Items.Add(new KeyValuePair<int, string>(150, "150"));
            combo.Items.Add(new KeyValuePair<int, string>(200, "200"));
            combo.Items.Add(new KeyValuePair<int, string>(250, "250"));
            combo.Items.Add(new KeyValuePair<int, string>(300, "300"));
            combo.Items.Add(new KeyValuePair<int, string>(400, "400"));
            combo.Items.Add(new KeyValuePair<int, string>(500, "500"));
            combo.SelectedValueChanged += TurboSelectedValueChanged;
        }

        private bool _updatingBindings;

        private void Binding_SelectedValueChanged(object? sender, EventArgs e)
        {
            if (_updatingBindings || sender is null) return;
            RComboBox combo = (RComboBox)sender;

            if (combo.SelectedItem is BindingSeparator)
            {
                int next = combo.SelectedIndex + 1;
                if (next < combo.Items.Count && combo.Items[next] is BindingItem)
                    combo.SelectedIndex = next;
                return;
            }

            if (combo.SelectedItem is not BindingItem item) return;

            bool secondary = combo.Name != "comboPrimary";
            ControllerPresetManager.SetBinding(activeBinding, secondary, item.Code == "" ? null : item.Code);

            VisualiseButton(activeButton, activeBinding);
        }

        private void TurboSelectedValueChanged(object? sender, EventArgs e)
        {
            if (_updatingBindings || sender is null) return;
            RComboBox combo = (RComboBox)sender;
            int ms = ((KeyValuePair<int, string>)combo.SelectedItem).Key;
            ControllerPresetManager.SetTurbo(activeBinding, combo.Name != "comboTurboPrimary", ms);
        }

        private void SetComboValue(RComboBox combo, string value)
        {
            _updatingBindings = true;
            foreach (var obj in combo.Items)
                if (obj is BindingItem item && item.Code == value)
                {
                    combo.SelectedItem = item;
                    _updatingBindings = false;
                    return;
                }
            combo.SelectedIndex = 0;
            _updatingBindings = false;
        }

        private void SetTurboValue(RComboBox combo, int ms)
        {
            _updatingBindings = true;
            foreach (var item in combo.Items)
                if (((KeyValuePair<int, string>)item).Key == ms)
                { combo.SelectedItem = item; _updatingBindings = false; return; }
            combo.SelectedIndex = 0;
            _updatingBindings = false;
        }

        private void VisualiseButton(RButton button, string binding)
        {
            if (button == null) return;

            string primary = ControllerPresetManager.GetBindingSelection(binding, false) ?? "";
            string secondary = ControllerPresetManager.GetBindingSelection(binding, true) ?? "";

            if (primary != "" || secondary != "")
            {
                button.BorderColor = colorStandard;
                button.Activated = true;
            }
            else
            {
                button.Activated = false;
            }
        }

        private void ButtonBinding(string binding, string label, RButton button)
        {
            _bindingButtons[binding] = button;
            button.Click += (sender, EventArgs) => { buttonBinding_Click(sender, EventArgs, binding, label); };
            VisualiseButton(button, binding);
        }

        void buttonBinding_Click(object sender, EventArgs e, string binding, string label)
        {

            if (sender is null) return;
            RButton button = (RButton)sender;

            panelBinding.Visible = true;

            activeButton = button;
            activeBinding = binding;

            labelBinding.Text = Properties.Strings.Binding + ": " + label;

            SetComboValue(comboPrimary, ControllerPresetManager.GetBindingSelection(binding, false) ?? "");
            SetComboValue(comboSecondary, ControllerPresetManager.GetBindingSelection(binding, true) ?? "");

            SetTurboValue(comboTurboPrimary, ControllerPresetManager.GetTurbo(binding, false));
            SetTurboValue(comboTurboSecondary, ControllerPresetManager.GetTurbo(binding, true));
        }

        private void InitPresetToolbar()
        {
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 94,
                AutoSize = false,
                Padding = new Padding(8),
                WrapContents = true
            };

            toolbar.Controls.Add(new Label
            {
                Text = DialogText.Get("ControllerPreset", "Preset"),
                AutoSize = true,
                Margin = new Padding(3, 10, 6, 0)
            });

            _presetCombo = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            _presetCombo.SelectedIndexChanged += (_, _) =>
            {
                if (_updatingPresets || _presetCombo.SelectedItem is not PresetItem item) return;
                ControllerPresetManager.SelectPreset(item.Id);
            };
            toolbar.Controls.Add(_presetCombo);

            toolbar.Controls.Add(MakeToolbarButton(DialogText.Get("New", "New"), NewPreset));
            toolbar.Controls.Add(MakeToolbarButton(DialogText.Get("Duplicate", "Duplicate"), DuplicatePreset));
            toolbar.Controls.Add(MakeToolbarButton(DialogText.Get("Rename", "Rename"), RenamePreset));
            toolbar.Controls.Add(MakeToolbarButton(DialogText.Get("Delete", "Delete"), DeletePreset));
            toolbar.Controls.Add(MakeToolbarButton("↑", () => MovePreset(-1), 42));
            toolbar.Controls.Add(MakeToolbarButton("↓", () => MovePreset(1), 42));

            _appsButton = MakeToolbarButton(DialogText.Get("Apps", "Apps"), ManageApps);
            toolbar.Controls.Add(_appsButton);
            toolbar.Controls.Add(MakeToolbarButton(DialogText.Get("Combinations", "Combinations"), ManageCombinations, 125));

            _autoSwitch = new CheckBox { Text = DialogText.Get("AutoSwitch", "Auto switch"), AutoSize = true, Margin = new Padding(12, 9, 3, 0) };
            _autoSwitch.CheckedChanged += (_, _) =>
            {
                if (!_updatingPresets) ControllerPresetManager.SetAutoSwitch(_autoSwitch.Checked);
            };
            toolbar.Controls.Add(_autoSwitch);

            _showToast = new CheckBox { Text = DialogText.Get("PresetToast", "Switch notice"), AutoSize = true, Margin = new Padding(8, 9, 3, 0) };
            _showToast.CheckedChanged += (_, _) =>
            {
                if (!_updatingPresets) ControllerPresetManager.SetShowToast(_showToast.Checked);
            };
            toolbar.Controls.Add(_showToast);

            Controls.Add(toolbar);
            toolbar.BringToFront();
            RefreshPresetToolbar();
            _combinationSignature = GetCombinationSignature();
        }

        private static Button MakeToolbarButton(string text, Action action, int width = 88)
        {
            var button = new Button { Text = text, Width = width, Height = 38, Margin = new Padding(3) };
            button.Click += (_, _) => action();
            return button;
        }

        private PresetItem? SelectedPresetItem => _presetCombo?.SelectedItem as PresetItem;

        private void RefreshPresetToolbar()
        {
            if (_presetCombo is null || _autoSwitch is null || _showToast is null) return;
            _updatingPresets = true;
            string selectedId = ControllerPresetManager.AutoSwitchEnabled
                ? ControllerPresetManager.EffectivePresetId
                : ControllerPresetManager.SelectedPresetId;
            _presetCombo.Items.Clear();
            foreach (ControllerPresetSummary preset in ControllerPresetManager.Presets())
            {
                var item = new PresetItem(preset.Id, preset.Name, preset.IsDefault);
                _presetCombo.Items.Add(item);
                if (preset.Id == selectedId) _presetCombo.SelectedItem = item;
            }
            _autoSwitch.Checked = ControllerPresetManager.AutoSwitchEnabled;
            _showToast.Checked = ControllerPresetManager.ShowSwitchToast;
            _appsButton!.Enabled = SelectedPresetItem is { IsDefault: false };
            _updatingPresets = false;
        }

        private void NewPreset()
        {
            using var prompt = new TextPromptDialog(DialogText.Get("NewPreset", "New Preset"), DialogText.Get("Name", "Name"), "Preset");
            if (prompt.ShowDialog(this) != DialogResult.OK) return;
            string id = ControllerPresetManager.AddPreset(prompt.Value);
            ControllerPresetManager.SelectPreset(id);
        }

        private void DuplicatePreset()
        {
            if (SelectedPresetItem is not PresetItem selected) return;
            string id = ControllerPresetManager.DuplicatePreset(selected.Id);
            ControllerPresetManager.SelectPreset(id);
        }

        private void RenamePreset()
        {
            if (SelectedPresetItem is not PresetItem selected) return;
            using var prompt = new TextPromptDialog(DialogText.Get("Rename", "Rename"), DialogText.Get("Name", "Name"), selected.Name);
            if (prompt.ShowDialog(this) == DialogResult.OK) ControllerPresetManager.RenamePreset(selected.Id, prompt.Value);
        }

        private void DeletePreset()
        {
            if (SelectedPresetItem is not { IsDefault: false } selected) return;
            if (MessageBox.Show(this, DialogText.Get("DeletePresetConfirm", "Delete this preset?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                ControllerPresetManager.DeletePreset(selected.Id);
        }

        private void MovePreset(int delta)
        {
            if (SelectedPresetItem is PresetItem selected) ControllerPresetManager.MovePreset(selected.Id, delta);
        }

        private void ManageApps()
        {
            if (SelectedPresetItem is not { IsDefault: false } selected) return;
            using var dialog = new PresetRulesDialog(selected.Id);
            dialog.ShowDialog(this);
        }

        private void ManageCombinations()
        {
            using var dialog = new CombinationLibraryDialog();
            dialog.ShowDialog(this);
            RefreshBindingItems();
        }

        private void PresetsChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(PresetsChanged); return; }

            RefreshPresetToolbar();
            string signature = GetCombinationSignature();
            if (signature != _combinationSignature)
            {
                _combinationSignature = signature;
                RefreshBindingItems();
            }
            else if (activeBinding.Length > 0)
            {
                SetComboValue(comboPrimary, ControllerPresetManager.GetBindingSelection(activeBinding, false) ?? "");
                SetComboValue(comboSecondary, ControllerPresetManager.GetBindingSelection(activeBinding, true) ?? "");
                SetTurboValue(comboTurboPrimary, ControllerPresetManager.GetTurbo(activeBinding, false));
                SetTurboValue(comboTurboSecondary, ControllerPresetManager.GetTurbo(activeBinding, true));
            }

            foreach (var pair in _bindingButtons) VisualiseButton(pair.Value, pair.Key);
        }

        private string GetCombinationSignature() => string.Join("|", ControllerPresetManager.Combinations().Select(c => c.Id + ":" + c.Name));

        private void RefreshBindingItems()
        {
            object[] items = BuildBindingComboItems();
            _updatingBindings = true;
            foreach (RComboBox combo in _bindingCombos)
            {
                combo.Items.Clear();
                combo.Items.AddRange(items);
            }
            _updatingBindings = false;

            if (activeBinding.Length > 0)
            {
                SetComboValue(comboPrimary, ControllerPresetManager.GetBindingSelection(activeBinding, false) ?? "");
                SetComboValue(comboSecondary, ControllerPresetManager.GetBindingSelection(activeBinding, true) ?? "");
            }
        }



        private void Controller_Complete(object? sender, EventArgs e)
        {
            AllyControl.SetDeadzones();
        }

        private void ButtonReset_Click(object? sender, EventArgs e)
        {
            trackLSMin.Value = 0;
            trackLSMax.Value = 100;
            trackRSMin.Value = 0;
            trackRSMax.Value = 100;

            trackLTMin.Value = 0;
            trackLTMax.Value = 100;
            trackRTMin.Value = 0;
            trackRTMax.Value = 100;

            trackVibra.Value = 100;

            AppConfig.Remove("ls_min");
            AppConfig.Remove("ls_max");
            AppConfig.Remove("rs_min");
            AppConfig.Remove("rs_max");

            AppConfig.Remove("lt_min");
            AppConfig.Remove("lt_max");
            AppConfig.Remove("rt_min");
            AppConfig.Remove("rt_max");
            AppConfig.Remove("vibra");

            VisualiseController();

        }

        private void Init()
        {
            trackLSMin.Value = AppConfig.Get("ls_min", 0);
            trackLSMax.Value = AppConfig.Get("ls_max", 100);
            trackRSMin.Value = AppConfig.Get("rs_min", 0);
            trackRSMax.Value = AppConfig.Get("rs_max", 100);

            trackLTMin.Value = AppConfig.Get("lt_min", 0);
            trackLTMax.Value = AppConfig.Get("lt_max", 100);
            trackRTMin.Value = AppConfig.Get("rt_min", 0);
            trackRTMax.Value = AppConfig.Get("rt_max", 100);

            trackVibra.Value = AppConfig.Get("vibra", 100);

            VisualiseController();
        }

        private void VisualiseController()
        {
            labelLS.Text = $"{trackLSMin.Value} - {trackLSMax.Value}%";
            labelRS.Text = $"{trackRSMin.Value} - {trackRSMax.Value}%";

            labelLT.Text = $"{trackLTMin.Value} - {trackLTMax.Value}%";
            labelRT.Text = $"{trackRTMin.Value} - {trackRTMax.Value}%";

            labelVibra.Text = $"{trackVibra.Value}%";
        }

        private void Controller_Scroll(object? sender, EventArgs e)
        {
            AppConfig.Set("ls_min", trackLSMin.Value);
            AppConfig.Set("ls_max", trackLSMax.Value);
            AppConfig.Set("rs_min", trackRSMin.Value);
            AppConfig.Set("rs_max", trackRSMax.Value);

            AppConfig.Set("lt_min", trackLTMin.Value);
            AppConfig.Set("lt_max", trackLTMax.Value);
            AppConfig.Set("rt_min", trackRTMin.Value);
            AppConfig.Set("rt_max", trackRTMax.Value);

            AppConfig.Set("vibra", trackVibra.Value);

            VisualiseController();

        }

        private void Handheld_Shown(object? sender, EventArgs e)
        {
            Height = Program.settingsForm.Height;
            Top = Program.settingsForm.Top;
            Left = Program.settingsForm.Left - Width - 5;
        }

            private sealed class BindingItem
            {
                public string Code        { get; }
                public string DisplayName { get; }
                public BindingItem(string code, string name) { Code = code; DisplayName = name; }
                public override string ToString() => DisplayName;
            }

            private sealed class BindingSeparator
            {
                public string Label { get; }
                public BindingSeparator(string label) { Label = label; }
                public override string ToString() => Label;
            }

            private sealed record PresetItem(string Id, string Name, bool IsDefault)
            {
                public override string ToString() => Name;
            }
        }
    }

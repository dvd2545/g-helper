using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GHelper.Ally;

internal static class DialogText
{
    public static string Get(string key, string fallback) => Properties.Strings.ResourceManager.GetString(key) ?? fallback;
}

internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _text = new() { Dock = DockStyle.Top };
    public string Value => _text.Text.Trim();

    public TextPromptDialog(string title, string label, string value = "")
    {
        Text = title;
        Width = 480;
        Height = 180;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Padding = new Padding(12);

        var prompt = new Label { Text = label, Dock = DockStyle.Top, Height = 34 };
        _text.Text = value;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = DialogText.Get("OK", "OK"), DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = DialogText.Get("Cancel", "Cancel"), DialogResult = DialogResult.Cancel, Width = 90 };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(_text);
        Controls.Add(prompt);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => { _text.Focus(); _text.SelectAll(); };
    }
}

internal sealed class CombinationEditorDialog : Form
{
    private readonly TextBox _name = new() { Dock = DockStyle.Top };
    private readonly Label _preview = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button _record = new() { Width = 100 };
    private readonly Button _clear = new() { Width = 100 };
    private readonly Button _test = new() { Width = 100 };
    private readonly List<int> _keys = [];
    private readonly HashSet<int> _downKeys = [];
    private CombinationMouseButton _mouse;
    private bool _mouseDown;
    private bool _sawInput;
    private InputCapture? _capture;

    public string CombinationName => _name.Text.Trim();
    public IReadOnlyList<int> Keys => _keys;
    public CombinationMouseButton MouseButton => _mouse;

    public CombinationEditorDialog(InputCombination? combination = null)
    {
        Text = DialogText.Get("CustomCombination", "Custom Combination");
        Width = 620;
        Height = 310;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Padding = new Padding(12);

        _name.Text = combination?.Name ?? "Combination";
        if (combination is not null)
        {
            _keys.AddRange(combination.Keys);
            _mouse = combination.MouseButton;
        }

        var nameLabel = new Label { Text = DialogText.Get("Name", "Name"), Dock = DockStyle.Top, Height = 28 };
        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 12) };
        previewHost.Controls.Add(_preview);

        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
        _record.Text = DialogText.Get("Record", "Record");
        _clear.Text = DialogText.Get("Clear", "Clear");
        _test.Text = DialogText.Get("Test", "Test");
        tools.Controls.AddRange([_record, _clear, _test]);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = DialogText.Get("Save", "Save"), DialogResult = DialogResult.OK, Width = 100 };
        var cancel = new Button { Text = DialogText.Get("Cancel", "Cancel"), DialogResult = DialogResult.Cancel, Width = 100 };
        actions.Controls.Add(ok);
        actions.Controls.Add(cancel);

        Controls.Add(previewHost);
        Controls.Add(_name);
        Controls.Add(nameLabel);
        Controls.Add(tools);
        Controls.Add(actions);
        AcceptButton = ok;
        CancelButton = cancel;

        _record.Click += (_, _) => BeginCapture();
        _clear.Click += (_, _) => { _keys.Clear(); _mouse = CombinationMouseButton.None; UpdatePreview(); };
        _test.Click += (_, _) => InputCombinationPlayer.Play(Current());
        FormClosing += (_, _) => EndCapture();
        UpdatePreview();
    }

    private InputCombination Current() => new() { Name = CombinationName, Keys = [.. _keys], MouseButton = _mouse };

    private void BeginCapture()
    {
        EndCapture();
        _keys.Clear();
        _downKeys.Clear();
        _mouse = CombinationMouseButton.None;
        _mouseDown = false;
        _sawInput = false;
        _record.Text = DialogText.Get("Recording", "Recording…");
        _preview.Text = DialogText.Get("PressCombination", "Press and release the key/mouse combination");
        BeginInvoke(() =>
        {
            try
            {
                _capture = new InputCapture();
                _capture.KeyChanged += CaptureKey;
                _capture.MouseChanged += CaptureMouse;
                _capture.Start();
            }
            catch (Exception ex)
            {
                EndCapture();
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        });
    }

    private void CaptureKey(int key, bool down)
    {
        if (InvokeRequired) { BeginInvoke(() => CaptureKey(key, down)); return; }
        _sawInput = true;
        if (down)
        {
            if (_downKeys.Add(key) && !_keys.Contains(key)) _keys.Add(key);
        }
        else _downKeys.Remove(key);
        UpdatePreview();
        CompleteIfReleased();
    }

    private void CaptureMouse(CombinationMouseButton button, bool down)
    {
        if (InvokeRequired) { BeginInvoke(() => CaptureMouse(button, down)); return; }
        _sawInput = true;
        if (down && _mouse == CombinationMouseButton.None) _mouse = button;
        _mouseDown = down;
        UpdatePreview();
        CompleteIfReleased();
    }

    private void CompleteIfReleased()
    {
        if (_sawInput && _downKeys.Count == 0 && !_mouseDown) BeginInvoke(EndCapture);
    }

    private void EndCapture()
    {
        if (_capture is not null)
        {
            _capture.KeyChanged -= CaptureKey;
            _capture.MouseChanged -= CaptureMouse;
            _capture.Dispose();
            _capture = null;
        }
        _record.Text = DialogText.Get("Record", "Record");
        UpdatePreview();
    }

    private void UpdatePreview() => _preview.Text = InputCombinationPlayer.Format(Current());

    private sealed class InputCapture : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WhMouseLl = 14;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int WmLeftDown = 0x0201;
        private const int WmLeftUp = 0x0202;
        private const int WmRightDown = 0x0204;
        private const int WmRightUp = 0x0205;
        private const int WmMiddleDown = 0x0207;
        private const int WmMiddleUp = 0x0208;
        private const int WmXDown = 0x020B;
        private const int WmXUp = 0x020C;
        private const uint LlkhfInjected = 0x10;

        private IntPtr _keyboardHook;
        private IntPtr _mouseHook;
        private readonly HookProc _keyboardProc;
        private readonly HookProc _mouseProc;

        public event Action<int, bool>? KeyChanged;
        public event Action<CombinationMouseButton, bool>? MouseChanged;

        public InputCapture()
        {
            _keyboardProc = KeyboardCallback;
            _mouseProc = MouseCallback;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardData { public uint vkCode, scanCode, flags, time; public IntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseData { public Point point; public uint mouseData, flags, time; public IntPtr extraInfo; }
        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        public void Start()
        {
            IntPtr module = GetModuleHandle(null);
            _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
            _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
            if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException("Unable to start input capture");
            }
        }

        private IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return CallNextHookEx(_keyboardHook, code, message, data);
            KeyboardData item = Marshal.PtrToStructure<KeyboardData>(data);
            if ((item.flags & LlkhfInjected) != 0) return CallNextHookEx(_keyboardHook, code, message, data);
            int msg = message.ToInt32();
            if (msg is WmKeyDown or WmSysKeyDown) { KeyChanged?.Invoke((int)item.vkCode, true); return (IntPtr)1; }
            if (msg is WmKeyUp or WmSysKeyUp) { KeyChanged?.Invoke((int)item.vkCode, false); return (IntPtr)1; }
            return CallNextHookEx(_keyboardHook, code, message, data);
        }

        private IntPtr MouseCallback(int code, IntPtr message, IntPtr data)
        {
            if (code < 0) return CallNextHookEx(_mouseHook, code, message, data);
            int msg = message.ToInt32();
            MouseData item = Marshal.PtrToStructure<MouseData>(data);
            CombinationMouseButton button = msg switch
            {
                WmLeftDown or WmLeftUp => CombinationMouseButton.Left,
                WmRightDown or WmRightUp => CombinationMouseButton.Right,
                WmMiddleDown or WmMiddleUp => CombinationMouseButton.Middle,
                WmXDown or WmXUp => ((item.mouseData >> 16) & 0xffff) == 1 ? CombinationMouseButton.X1 : CombinationMouseButton.X2,
                _ => CombinationMouseButton.None
            };
            if (button == CombinationMouseButton.None) return CallNextHookEx(_mouseHook, code, message, data);
            bool down = msg is WmLeftDown or WmRightDown or WmMiddleDown or WmXDown;
            MouseChanged?.Invoke(button, down);
            return (IntPtr)1;
        }

        public void Dispose()
        {
            if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            _keyboardHook = _mouseHook = IntPtr.Zero;
        }
    }
}

internal sealed class CombinationLibraryDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, DisplayMember = nameof(InputCombination.Name) };

    public CombinationLibraryDialog()
    {
        Text = DialogText.Get("CustomCombinations", "Custom Combinations");
        Width = 700;
        Height = 460;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
        Button add = MakeButton(DialogText.Get("Add", "Add"), AddCombination);
        Button edit = MakeButton(DialogText.Get("Edit", "Edit"), EditCombination);
        Button rename = MakeButton(DialogText.Get("Rename", "Rename"), RenameCombination);
        Button test = MakeButton(DialogText.Get("Test", "Test"), TestCombination);
        Button delete = MakeButton(DialogText.Get("Delete", "Delete"), DeleteCombination);
        var close = new Button { Text = DialogText.Get("Close", "Close"), Width = 90, DialogResult = DialogResult.OK };
        buttons.Controls.AddRange([add, edit, rename, test, delete, close]);
        Controls.Add(_list);
        Controls.Add(buttons);
        AcceptButton = close;
        RefreshList();
    }

    private static Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, Width = 95 };
        button.Click += (_, _) => action();
        return button;
    }

    private InputCombination? Selected => _list.SelectedItem as InputCombination;
    private void RefreshList(string? selectId = null)
    {
        _list.Items.Clear();
        foreach (InputCombination combination in ControllerPresetManager.Combinations()) _list.Items.Add(combination);
        if (selectId is not null)
            for (int i = 0; i < _list.Items.Count; i++) if (((InputCombination)_list.Items[i]).Id == selectId) _list.SelectedIndex = i;
    }

    private void AddCombination()
    {
        using var editor = new CombinationEditorDialog();
        if (editor.ShowDialog(this) != DialogResult.OK || editor.CombinationName.Length == 0) return;
        if (editor.Keys.Count == 0 && editor.MouseButton == CombinationMouseButton.None)
        {
            MessageBox.Show(this, DialogText.Get("CombinationEmpty", "Record at least one key or mouse button."), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string id = ControllerPresetManager.AddCombination(editor.CombinationName, editor.Keys, editor.MouseButton);
        RefreshList(id);
    }

    private void EditCombination()
    {
        InputCombination? selected = Selected;
        if (selected is null) return;
        using var editor = new CombinationEditorDialog(selected);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        if (editor.Keys.Count == 0 && editor.MouseButton == CombinationMouseButton.None)
        {
            MessageBox.Show(this, DialogText.Get("CombinationEmpty", "Record at least one key or mouse button."), Text,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ControllerPresetManager.UpdateCombination(selected.Id, editor.CombinationName, editor.Keys, editor.MouseButton);
        RefreshList(selected.Id);
    }

    private void RenameCombination()
    {
        InputCombination? selected = Selected;
        if (selected is null) return;
        using var prompt = new TextPromptDialog(DialogText.Get("Rename", "Rename"), DialogText.Get("Name", "Name"), selected.Name);
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        ControllerPresetManager.UpdateCombination(selected.Id, prompt.Value, selected.Keys, selected.MouseButton);
        RefreshList(selected.Id);
    }

    private void TestCombination()
    {
        if (Selected is InputCombination selected) InputCombinationPlayer.Play(selected);
    }

    private void DeleteCombination()
    {
        InputCombination? selected = Selected;
        if (selected is null) return;
        if (MessageBox.Show(this, DialogText.Get("DeleteCombinationConfirm", "Delete this combination? Referenced buttons will be disabled."), Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        ControllerPresetManager.DeleteCombination(selected.Id);
        RefreshList();
    }
}

internal sealed class PresetRulesDialog : Form
{
    private readonly string _presetId;
    private readonly List<ExecutableRule> _rules;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly ComboBox _mode = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private bool _updating;

    public PresetRulesDialog(string presetId)
    {
        _presetId = presetId;
        _rules = ControllerPresetManager.Rules(presetId).ToList();
        Text = DialogText.Get("ExecutableRules", "Executable Rules");
        Width = 850;
        Height = 500;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);

        _list.Columns.Add(DialogText.Get("Executable", "Executable"), 560);
        _list.Columns.Add(DialogText.Get("Detection", "Detection"), 150);
        _list.SelectedIndexChanged += (_, _) => SelectRule();

        _mode.Items.AddRange(Enum.GetNames<ExecutableMatchMode>());
        _mode.SelectedIndexChanged += (_, _) => ChangeMode();

        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50 };
        tools.Controls.Add(MakeButton(DialogText.Get("BrowseExe", "Browse EXE"), AddFile, 110));
        tools.Controls.Add(MakeButton(DialogText.Get("RunningProcess", "Running Process"), AddRunning, 130));
        tools.Controls.Add(MakeButton("↑", () => MoveRule(-1), 45));
        tools.Controls.Add(MakeButton("↓", () => MoveRule(1), 45));
        tools.Controls.Add(MakeButton(DialogText.Get("Delete", "Delete"), Delete, 90));
        tools.Controls.Add(_mode);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = DialogText.Get("Save", "Save"), Width = 100, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = DialogText.Get("Cancel", "Cancel"), Width = 100, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        Controls.Add(_list);
        Controls.Add(tools);
        Controls.Add(actions);
        AcceptButton = save;
        CancelButton = cancel;
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK) ControllerPresetManager.SetRules(_presetId, _rules); };
        RefreshList();
    }

    private static Button MakeButton(string text, Action action, int width)
    {
        var button = new Button { Text = text, Width = width };
        button.Click += (_, _) => action();
        return button;
    }

    private int SelectedIndex => _list.SelectedIndices.Count == 0 ? -1 : _list.SelectedIndices[0];
    private void RefreshList(int select = -1)
    {
        _list.Items.Clear();
        foreach (ExecutableRule rule in _rules)
            _list.Items.Add(new ListViewItem([rule.ExecutablePath.Length > 0 ? rule.ExecutablePath : rule.ExecutableName, rule.MatchMode.ToString()]));
        if (select >= 0 && select < _list.Items.Count) _list.Items[select].Selected = true;
    }

    private void AddFile()
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Applications (*.exe)|*.exe",
                CheckFileExists = true,
                CheckPathExists = true,
                DereferenceLinks = true,
                RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            string path = Path.GetFullPath(dialog.FileName);
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length == 0) return;
            AddRule(new(path, name));
        }
        catch (Exception ex)
        {
            Logger.WriteLine("Executable picker: " + ex);
            MessageBox.Show(this,
                DialogText.Get("ExecutablePickerError", "The selected executable could not be added."),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddRunning()
    {
        ProcessChoice[] choices = ControllerPresetManager.RunningProcesses().Where(p => p.Name != Process.GetCurrentProcess().ProcessName).ToArray();
        using var dialog = new RunningProcessDialog(choices);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Selected is ProcessChoice selected) AddRule(selected);
    }

    private void AddRule(ProcessChoice process)
    {
        if (_rules.Any(r => r.MatchMode == ExecutableMatchMode.Foreground &&
            string.Equals(r.ExecutablePath, process.Path, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.ExecutableName, process.Name, StringComparison.OrdinalIgnoreCase))) return;
        _rules.Add(new() { ExecutablePath = process.Path, ExecutableName = process.Name, MatchMode = ExecutableMatchMode.Foreground });
        RefreshList(_rules.Count - 1);
    }

    private void SelectRule()
    {
        int index = SelectedIndex;
        _updating = true;
        _mode.SelectedIndex = index < 0 ? -1 : (int)_rules[index].MatchMode;
        _updating = false;
    }

    private void ChangeMode()
    {
        int index = SelectedIndex;
        if (_updating || index < 0 || _mode.SelectedIndex < 0) return;
        _rules[index].MatchMode = (ExecutableMatchMode)_mode.SelectedIndex;
        RefreshList(index);
    }

    private void MoveRule(int delta)
    {
        int index = SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _rules.Count) return;
        (_rules[index], _rules[target]) = (_rules[target], _rules[index]);
        RefreshList(target);
    }

    private void Delete()
    {
        int index = SelectedIndex;
        if (index < 0) return;
        _rules.RemoveAt(index);
        RefreshList(Math.Min(index, _rules.Count - 1));
    }
}

internal sealed class RunningProcessDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    public ProcessChoice? Selected => _list.SelectedItem is ProcessChoice item ? item : null;

    public RunningProcessDialog(IEnumerable<ProcessChoice> choices)
    {
        Text = DialogText.Get("RunningProcess", "Running Process");
        Width = 760;
        Height = 500;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(12);
        foreach (ProcessChoice choice in choices) _list.Items.Add(choice);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = DialogText.Get("Add", "Add"), Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = DialogText.Get("Cancel", "Cancel"), Width = 90, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(ok);
        actions.Controls.Add(cancel);
        Controls.Add(_list);
        Controls.Add(actions);
        AcceptButton = ok;
        CancelButton = cancel;
        _list.DoubleClick += (_, _) => { if (Selected is not null) DialogResult = DialogResult.OK; };
    }
}

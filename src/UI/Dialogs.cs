using System.Diagnostics;

namespace G815Studio;

internal static class Ui
{
    public static Button Btn(string text, EventHandler onClick, int width = 0)
    {
        var b = new Button { Text = text, AutoSize = width == 0, Height = 28, Margin = new Padding(3) };
        if (width > 0) b.Width = width;
        b.Click += onClick;
        return b;
    }

    public static Label Lbl(string text, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 7, 3, 3),
        Font = bold ? new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) : SystemFonts.MessageBoxFont
    };

    public static FlowLayoutPanel Flow(FlowDirection dir = FlowDirection.LeftToRight) => new()
    {
        FlowDirection = dir,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = dir == FlowDirection.LeftToRight,
        Margin = new Padding(0)
    };

    public static GroupBox Group(string title, Control content)
    {
        var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8), Margin = new Padding(6) };
        content.Location = new Point(8, 22);
        g.Controls.Add(content);
        return g;
    }

    public static Color? PickColor(IWin32Window owner, Color current)
    {
        using var dlg = new ColorDialog { Color = current, FullOpen = true, AnyColor = true };
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Color : null;
    }

    public static void PaintSwatch(Button b, Color c)
    {
        b.BackColor = c;
        b.ForeColor = ColorUtil.IsLight(c) ? Color.Black : Color.White;
        b.UseVisualStyleBackColor = false;
    }
}

internal sealed class PromptDialog : Form
{
    private readonly TextBox _box;

    private PromptDialog(string title, string prompt, string value, bool multiline)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, multiline ? 260 : 130);
        Font = SystemFonts.MessageBoxFont;

        var lbl = new Label { Text = prompt, Left = 12, Top = 12, Width = 396, AutoSize = false, Height = 20 };
        _box = new TextBox
        {
            Left = 12, Top = 36, Width = 396, Text = value ?? "",
            Multiline = multiline, AcceptsReturn = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            Height = multiline ? 170 : 24
        };
        var ok = new Button { Text = "U redu", DialogResult = DialogResult.OK, Width = 90, Left = 222, Top = ClientSize.Height - 38 };
        var cancel = new Button { Text = "Odustani", DialogResult = DialogResult.Cancel, Width = 90, Left = 318, Top = ClientSize.Height - 38 };
        Controls.AddRange(new Control[] { lbl, _box, ok, cancel });
        AcceptButton = multiline ? null : ok;
        CancelButton = cancel;
    }

    public static string Show(IWin32Window owner, string title, string prompt, string value = "", bool multiline = false)
    {
        using var d = new PromptDialog(title, prompt, value, multiline);
        return d.ShowDialog(owner) == DialogResult.OK ? d._box.Text : null;
    }
}

/// <summary>Hvata sljedeću tipku ili kombinaciju (npr. Ctrl+Shift+S).</summary>
internal sealed class KeyCaptureDialog : Form
{
    private readonly Label _info;
    private Keys _lastModifier = Keys.None;
    public Keys Result { get; private set; } = Keys.None;

    public KeyCaptureDialog()
    {
        Text = "Dodaj tipku";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(380, 120);
        Font = SystemFonts.MessageBoxFont;
        _info = new Label
        {
            Text = "Pritisni tipku ili kombinaciju tipki…\n(samo modifikator: pritisni ga i otpusti)",
            Dock = DockStyle.Top, Height = 70, TextAlign = ContentAlignment.MiddleCenter
        };
        var cancel = new Button { Text = "Odustani", DialogResult = DialogResult.Cancel, Width = 100, Left = 140, Top = 80, TabStop = false };
        Controls.Add(cancel);
        Controls.Add(_info);
    }

    private static bool IsModifier(Keys k) =>
        k is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var code = keyData & Keys.KeyCode;
        if (IsModifier(code))
        {
            _lastModifier = code;
            return true;
        }
        Result = keyData;
        DialogResult = DialogResult.OK;
        Close();
        return true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (Result == Keys.None && _lastModifier != Keys.None && IsModifier(e.KeyCode))
        {
            Result = _lastModifier;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

/// <summary>Odabir pokrenute aplikacije (s prozorom) za automatsko mijenjanje profila.</summary>
internal sealed class ProcessPickerDialog : Form
{
    private readonly ListBox _list;
    public string SelectedExe { get; private set; }

    public ProcessPickerDialog()
    {
        Text = "Odaberi aplikaciju";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 420);
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        Font = SystemFonts.MessageBoxFont;

        _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var ok = new Button { Text = "Dodaj", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Odustani", DialogResult = DialogResult.Cancel, Width = 90 };
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(ok);
        Controls.Add(_list);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;
        _list.DoubleClick += (_, _) => { if (_list.SelectedItem != null) { DialogResult = DialogResult.OK; Close(); } };

        var items = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                string exe = p.ProcessName + ".exe";
                items[exe] = p.MainWindowTitle;
            }
            catch { }
            finally { p.Dispose(); }
        }
        foreach (var kv in items) _list.Items.Add($"{kv.Key}   —   {kv.Value}");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _list.SelectedItem is string s)
            SelectedExe = s.Split("   —   ")[0].Trim().ToLowerInvariant();
        base.OnFormClosing(e);
    }
}

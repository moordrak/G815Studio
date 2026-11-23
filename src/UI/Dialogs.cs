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

    public static Control Group(string title, Control content)
    {
        var card = new Card(title);
        content.Location = new Point(card.Padding.Left, card.Padding.Top);
        content.BackColor = Theme.Surface;
        card.Controls.Add(content);
        return card;
    }

    public static Color? PickColor(IWin32Window owner, Color current)
    {
        using var dlg = new ColorDialog { Color = current, FullOpen = true, AnyColor = true };
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Color : null;
    }

    public static void PaintSwatch(Button b, Color c)
    {
        b.Tag = "swatch";
        Theme.StyleFlat(b, c, Theme.Border);
        b.ForeColor = ColorUtil.IsLight(c) ? Color.Black : Color.White;
    }
}

/// <summary>Dijalog u tamnoj temi.</summary>
internal abstract class DarkDialog : Form
{
    protected override void OnLoad(EventArgs e)
    {
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Theme.Apply(this);
        Theme.DarkTitleBar(this);
        base.OnLoad(e);
    }
}

internal sealed class PromptDialog : DarkDialog
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

    /// <summary>Za korak "Unos vrijednosti": prozor iznad svega, i kad je u fokusu igra ili drugi program.</summary>
    public static string ShowTop(string title, string prompt, string value)
    {
        using var d = new PromptDialog(title, prompt, value, false)
        {
            TopMost = true,
            ShowInTaskbar = true,
            StartPosition = FormStartPosition.CenterScreen
        };
        d.Shown += (_, _) => { d.Activate(); d._box.Focus(); d._box.SelectAll(); };
        return d.ShowDialog() == DialogResult.OK ? d._box.Text : null;
    }

    public static string Show(IWin32Window owner, string title, string prompt, string value = "", bool multiline = false)
    {
        using var d = new PromptDialog(title, prompt, value, multiline);
        return d.ShowDialog(owner) == DialogResult.OK ? d._box.Text : null;
    }
}

/// <summary>Hvata sljedeću tipku ili kombinaciju (npr. Ctrl+Shift+S).</summary>
internal sealed class KeyCaptureDialog : DarkDialog
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
internal sealed class ProcessPickerDialog : DarkDialog
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

/// <summary>Uređivanje koraka "HTTP poziv".</summary>
internal sealed class HttpStepDialog : DarkDialog
{
    private readonly ComboBox _method, _type;
    private readonly TextBox _url, _headers, _body;
    private readonly NumericUpDown _timeout;
    private readonly CheckBox _show;

    public HttpStepDialog(MacroStep step)
    {
        Text = "HTTP poziv";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(620, 560);
        Font = Theme.UiFont();

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label L(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 9, 3, 3) };

        _method = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _method.Items.AddRange(new object[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" });
        _method.SelectedItem = (step.Method ?? "GET").ToUpperInvariant();
        if (_method.SelectedIndex < 0) _method.SelectedIndex = 0;
        _url = new TextBox { Dock = DockStyle.Fill, Text = step.Url ?? "https://" };
        _type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Text = string.IsNullOrWhiteSpace(step.ContentType) ? "application/json" : step.ContentType };
        _type.Items.AddRange(new object[] { "application/json", "application/x-www-form-urlencoded", "text/plain", "application/xml" });
        _headers = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 80, Text = (step.Headers ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
        _body = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical, Height = 150, Font = new Font("Consolas", 10f), Text = (step.Body ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
        _timeout = new NumericUpDown { Minimum = 1, Maximum = 120, Value = Math.Clamp(step.TimeoutMs / 1000, 1, 120), Width = 80 };
        _show = new CheckBox { Text = "Prikaži rezultat u obavijesti", AutoSize = true, Checked = step.ShowResult };

        t.Controls.Add(L("Metoda"), 0, 0); t.Controls.Add(_method, 1, 0);
        t.Controls.Add(L("URL"), 0, 1); t.Controls.Add(_url, 1, 1);
        t.Controls.Add(L("Content-Type"), 0, 2); t.Controls.Add(_type, 1, 2);
        t.Controls.Add(L("Zaglavlja"), 0, 3); t.Controls.Add(_headers, 1, 3);
        t.Controls.Add(L("Tijelo"), 0, 4); t.Controls.Add(_body, 1, 4);
        var tr = new FlowLayoutPanel { AutoSize = true };
        tr.Controls.Add(_timeout);
        tr.Controls.Add(new Label { Text = "sekundi", AutoSize = true, Margin = new Padding(6, 8, 20, 0) });
        tr.Controls.Add(_show);
        t.Controls.Add(L("Čekaj najviše"), 0, 5); t.Controls.Add(tr, 1, 5);
        var help = new Label
        {
            AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 3),
            Text = "Zaglavlja: jedno po retku, npr. \"Authorization: Bearer …\". U URL-u, zaglavljima i tijelu možeš koristiti " +
                   "{naziv} iz koraka \"Unos vrijednosti\", {naziv:url} za kodiranu vrijednost, te {datum}, {vrijeme} i {clipboard}. " +
                   "Nakon poziva su dostupni {status} i {odgovor}."
        };
        t.Controls.Add(help, 1, 6);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 52, Padding = new Padding(10) };
        var ok = new Button { Text = "U redu", DialogResult = DialogResult.OK, Width = 100, Tag = "primary" };
        var cancel = new Button { Text = "Odustani", DialogResult = DialogResult.Cancel, Width = 100 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(t);
        Controls.Add(buttons);
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            if (!Uri.TryCreate(_url.Text.Trim().Replace("{", "x").Replace("}", "x"), UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https"))
            {
                MessageBox.Show(this, "Upiši ispravan URL koji počinje s http:// ili https://", "HTTP poziv", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            step.Method = (string)_method.SelectedItem;
            step.Url = _url.Text.Trim();
            step.ContentType = _type.Text.Trim();
            step.Headers = _headers.Text.Replace("\r\n", "\n");
            step.Body = _body.Text.Replace("\r\n", "\n");
            step.TimeoutMs = (int)_timeout.Value * 1000;
            step.ShowResult = _show.Checked;
        };
    }
}

/// <summary>Uređivanje koraka "Unos vrijednosti".</summary>
internal sealed class InputStepDialog : DarkDialog
{
    public InputStepDialog(MacroStep step)
    {
        Text = "Unos vrijednosti";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 300);
        Font = Theme.UiFont();

        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Label L(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 9, 3, 3) };
        var name = new TextBox { Dock = DockStyle.Fill, Text = string.IsNullOrWhiteSpace(step.Variable) ? "vrijednost" : step.Variable };
        var prompt = new TextBox { Dock = DockStyle.Fill, Text = step.Prompt ?? "" };
        var def = new TextBox { Dock = DockStyle.Fill, Text = step.DefaultValue ?? "" };
        t.Controls.Add(L("Naziv varijable"), 0, 0); t.Controls.Add(name, 1, 0);
        t.Controls.Add(L("Pitanje"), 0, 1); t.Controls.Add(prompt, 1, 1);
        t.Controls.Add(L("Zadana vrijednost"), 0, 2); t.Controls.Add(def, 1, 2);
        t.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 10, 3, 3),
            Text = "Kad makro dođe do ovog koraka, otvori se prozor za unos. Upisana vrijednost se u kasnijim koracima " +
                   "koristi kao {naziv} (u tekstu, URL-u, tijelu, zaglavljima i argumentima). Odustajanje prekida makro."
        }, 1, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 52, Padding = new Padding(10) };
        var ok = new Button { Text = "U redu", DialogResult = DialogResult.OK, Width = 100, Tag = "primary" };
        var cancel = new Button { Text = "Odustani", DialogResult = DialogResult.Cancel, Width = 100 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Controls.Add(t);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, _) =>
        {
            string n = new string(name.Text.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
            if (n.Length == 0)
            {
                MessageBox.Show(this, "Naziv varijable smije imati slova, brojeve i _.", "Unos vrijednosti", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            step.Variable = n;
            step.Prompt = prompt.Text;
            step.DefaultValue = def.Text;
        };
    }
}

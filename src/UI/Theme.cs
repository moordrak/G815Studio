using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace G815Studio;

/// <summary>Tamna tema prema prijedlogu sučelja.</summary>
internal static class Theme
{
    public static readonly Color Ground = Color.FromArgb(13, 15, 19);
    public static readonly Color Sidebar = Color.FromArgb(17, 19, 24);
    public static readonly Color Surface = Color.FromArgb(20, 23, 29);
    public static readonly Color ControlBg = Color.FromArgb(24, 28, 35);
    public static readonly Color Input = Color.FromArgb(15, 18, 22);
    public static readonly Color Border = Color.FromArgb(44, 50, 61);
    public static readonly Color BorderSoft = Color.FromArgb(34, 38, 46);
    public static readonly Color Text = Color.FromArgb(232, 234, 239);
    public static readonly Color TextSoft = Color.FromArgb(195, 201, 212);
    public static readonly Color Muted = Color.FromArgb(154, 163, 178);
    public static readonly Color Accent = Color.FromArgb(76, 194, 255);
    public static readonly Color AccentBg = Color.FromArgb(28, 42, 56);
    public static readonly Color Danger = Color.FromArgb(255, 77, 94);
    public static readonly Color Good = Color.FromArgb(61, 220, 132);

    public static Font UiFont(float size = 9.5f, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);
    public static Font Heading(float size = 11f) => new("Segoe UI Semibold", size, FontStyle.Regular);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Tamni naslovni okvir prozora (Windows 10 20H1+ / 11).</summary>
    public static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }
        catch
        {
        }
    }

    /// <summary>Rekurzivno prebojava standardne kontrole u tamnu temu.</summary>
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            Style(c);
            if (c is not KeyboardView) Apply(c);
        }
    }

    public static void Style(Control c)
    {
        switch (c)
        {
            case KeyboardView:
            case NavButton:
            case EffectTile:
            case Card:
                return;
            case Button b:
                if (b.Tag as string == "swatch") { StyleFlat(b, b.BackColor, Border); return; }
                if (b.Tag as string == "primary") { StyleFlat(b, Accent, Accent); b.ForeColor = Color.FromArgb(6, 18, 28); b.Font = UiFont(9.5f, FontStyle.Bold); return; }
                StyleFlat(b, ControlBg, Border);
                b.ForeColor = Text;
                return;
            case TextBox t:
                t.BackColor = Input;
                t.ForeColor = Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                return;
            case ListBox l:
                l.BackColor = Input;
                l.ForeColor = Text;
                l.BorderStyle = BorderStyle.None;
                return;
            case ComboBox cb:
                cb.BackColor = Input;
                cb.ForeColor = Text;
                cb.FlatStyle = FlatStyle.Flat;
                return;
            case NumericUpDown n:
                n.BackColor = Input;
                n.ForeColor = Text;
                n.BorderStyle = BorderStyle.FixedSingle;
                return;
            case CheckBox ch:
                ch.ForeColor = TextSoft;
                return;
            case RadioButton rb:
                rb.ForeColor = TextSoft;
                return;
            case TrackBar tb:
                tb.BackColor = c.Parent?.BackColor ?? Surface;
                return;
            case Label lb:
                if (lb.ForeColor == SystemColors.GrayText) lb.ForeColor = Muted;
                else if (lb.ForeColor == Color.Firebrick) lb.ForeColor = Color.FromArgb(255, 138, 155);
                else if (lb.ForeColor == SystemColors.ControlText) lb.ForeColor = TextSoft;
                lb.BackColor = Color.Transparent;
                return;
            case StatusStrip:
                return;
            default:
                if (c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel)
                {
                    if (c.BackColor == SystemColors.Control || c.BackColor == Color.Empty)
                        c.BackColor = c.Parent?.BackColor ?? Ground;
                }
                c.ForeColor = Text;
                return;
        }
    }

    public static void StyleFlat(Button b, Color back, Color border)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.BackColor = back;
        b.FlatAppearance.BorderColor = border;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.05f);
        b.Cursor = Cursors.Hand;
        if (b.Height < 34) b.Height = 34;
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>Kartica sa zaobljenim rubom i naslovom (zamjena za GroupBox).</summary>
internal sealed class Card : Panel
{
    private readonly string _title;

    public Card(string title)
    {
        _title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Padding = new Padding(16, string.IsNullOrEmpty(title) ? 14 : 44, 16, 14);
        Margin = new Padding(8);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Ground);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.Rounded(r, 14);
        using var b = new SolidBrush(Theme.Surface);
        g.FillPath(b, path);
        using var p = new Pen(Theme.BorderSoft);
        g.DrawPath(p, path);
        if (!string.IsNullOrEmpty(_title))
        {
            using var f = Theme.Heading(11.5f);
            TextRenderer.DrawText(g, _title, f, new Point(16, 14), Theme.Text);
        }
    }
}

/// <summary>Stavka bočnog izbornika s ikonom (Segoe MDL2 Assets).</summary>
internal sealed class NavButton : Control
{
    private bool _hover;
    public string Glyph { get; set; }
    public bool Active { get; set; }

    public NavButton(string text, string glyph)
    {
        Text = text;
        Glyph = glyph;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Height = 44;
        Margin = new Padding(0, 2, 0, 2);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Sidebar);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Active || _hover)
        {
            using var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), 10);
            using var b = new SolidBrush(Active ? Color.FromArgb(28, 34, 48) : Color.FromArgb(24, 27, 34));
            g.FillPath(b, path);
        }
        if (Focused)
        {
            using var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 10);
            using var p = new Pen(Theme.Accent);
            g.FillPath(Brushes.Transparent, path);
            g.DrawPath(p, path);
        }
        using (var icon = new Font("Segoe MDL2 Assets", 12f))
            TextRenderer.DrawText(g, Glyph, icon, new Rectangle(12, 0, 24, Height), Active ? Theme.Accent : Theme.TextSoft,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        using var f = Active ? Theme.UiFont(10f, FontStyle.Bold) : Theme.UiFont(10f);
        TextRenderer.DrawText(g, Text, f, new Rectangle(46, 0, Width - 50, Height), Active ? Color.White : Theme.TextSoft,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

/// <summary>Pločica efekta s malim pregledom boja.</summary>
internal sealed class EffectTile : Control
{
    private bool _hover;
    private readonly Color[] _preview;
    public bool Selected { get; set; }

    public EffectTile(string text, params Color[] preview)
    {
        Text = text;
        _preview = preview.Length == 0 ? new[] { Theme.Border } : preview;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(150, 62);
        Margin = new Padding(4);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Rounded(r, 10))
        {
            using var b = new SolidBrush(Selected ? Theme.AccentBg : _hover ? Color.FromArgb(30, 34, 42) : Theme.ControlBg);
            g.FillPath(b, path);
            using var p = new Pen(Selected || Focused ? Theme.Accent : Theme.Border, Selected ? 1.6f : 1f);
            g.DrawPath(p, path);
        }
        var bar = new RectangleF(10, 10, Width - 20, 6);
        using (var barPath = Theme.Rounded(bar, 3))
        {
            if (_preview.Length == 1)
            {
                using var sb = new SolidBrush(_preview[0]);
                g.FillPath(sb, barPath);
            }
            else
            {
                using var lb = new LinearGradientBrush(bar, _preview[0], _preview[^1], 0f);
                var blend = new ColorBlend { Colors = _preview, Positions = Enumerable.Range(0, _preview.Length).Select(i => i / (float)(_preview.Length - 1)).ToArray() };
                lb.InterpolationColors = blend;
                g.FillPath(lb, barPath);
            }
        }
        using var f = Theme.UiFont(9.5f, Selected ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, Text, f, new Rectangle(10, 24, Width - 16, Height - 28), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Tamni izgled statusne trake i izbornika.</summary>
internal sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.ForeColor == SystemColors.ControlText) e.TextColor = Theme.TextSoft;
        base.OnRenderItemText(e);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color StatusStripGradientBegin => Theme.Sidebar;
        public override Color StatusStripGradientEnd => Theme.Sidebar;
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuItemSelected => Theme.AccentBg;
        public override Color MenuItemBorder => Theme.Accent;
        public override Color MenuBorder => Theme.Border;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}

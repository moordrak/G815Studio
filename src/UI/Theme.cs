using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Svitac;

/// <summary>
/// HUD tema (verzija 2.0): tamna „zapovjedna ploča“, odrezani kutovi, plavi naglasak i tri fonta
/// (Chakra Petch za naslove, Barlow za tekst, JetBrains Mono za brojke). Fontovi su ugrađeni u exe.
/// </summary>
internal static class Theme
{
    public static readonly Color Ground = Color.FromArgb(7, 9, 14);
    public static readonly Color Sidebar = Color.FromArgb(9, 12, 18);
    public static readonly Color Surface = Color.FromArgb(12, 16, 24);
    public static readonly Color ControlBg = Color.FromArgb(17, 23, 36);
    public static readonly Color Input = Color.FromArgb(10, 14, 21);
    public static readonly Color Border = Color.FromArgb(43, 66, 96);
    public static readonly Color BorderSoft = Color.FromArgb(29, 42, 61);
    public static readonly Color Text = Color.FromArgb(228, 236, 247);
    public static readonly Color TextSoft = Color.FromArgb(169, 183, 204);
    public static readonly Color Muted = Color.FromArgb(107, 124, 149);
    public static readonly Color Accent = Color.FromArgb(76, 194, 255);
    public static readonly Color AccentBg = Color.FromArgb(16, 40, 58);
    public static readonly Color Plasma = Color.FromArgb(255, 63, 180);
    public static readonly Color Danger = Color.FromArgb(255, 77, 94);
    public static readonly Color Good = Color.FromArgb(61, 220, 132);
    public static readonly Color Warn = Color.FromArgb(255, 181, 71);
    public static readonly Color Grid = Color.FromArgb(14, 76, 194, 255);

    // ------------------------------------------------------------------ fontovi

    private static PrivateFontCollection _pfc;
    private static FontFamily _display, _body, _bodySemi, _mono;
    private static readonly List<IntPtr> FontMemory = new();

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

    private static void LoadFonts()
    {
        if (_pfc != null) return;
        _pfc = new PrivateFontCollection();
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("font.", StringComparison.Ordinal)))
        {
            try
            {
                using var s = asm.GetManifestResourceStream(name);
                var data = new byte[s.Length];
                s.ReadExactly(data);
                IntPtr mem = Marshal.AllocCoTaskMem(data.Length);   // ostaje zauzeto dok program radi
                Marshal.Copy(data, 0, mem, data.Length);
                FontMemory.Add(mem);
                _pfc.AddMemoryFont(mem, data.Length);
                uint n = 0;
                AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n);  // da ih vidi i GDI (TextRenderer, kontrole)
            }
            catch (Exception ex)
            {
                Log.Write($"Font {name} nije učitan: {ex.Message}");
            }
        }
        FontFamily Find(string family, string fallback) =>
            _pfc.Families.FirstOrDefault(f => f.Name.Equals(family, StringComparison.OrdinalIgnoreCase)) ?? new FontFamily(fallback);
        _display = Find("Chakra Petch SemiBold", "Segoe UI Semibold");
        _body = Find("Barlow", "Segoe UI");
        _bodySemi = Find("Barlow SemiBold", "Segoe UI Semibold");
        _mono = Find("JetBrains Mono", "Consolas");
    }

    private static Font Make(FontFamily family, float size, FontStyle style, GraphicsUnit unit = GraphicsUnit.Point)
    {
        if (!family.IsStyleAvailable(style)) style = FontStyle.Regular;
        return new Font(family, size, style, unit);
    }

    /// <summary>Tekst sučelja (Barlow). Podebljano koristi Barlow SemiBold.</summary>
    public static Font UiFont(float size = 10f, FontStyle style = FontStyle.Regular)
    {
        LoadFonts();
        return (style & FontStyle.Bold) != 0 ? Make(_bodySemi, size, style & ~FontStyle.Bold) : Make(_body, size, style);
    }

    /// <summary>Naslovi (Chakra Petch SemiBold).</summary>
    public static Font Heading(float size = 11f)
    {
        LoadFonts();
        return Make(_display, size, FontStyle.Regular);
    }

    /// <summary>Brojke, oznake i koraci makroa (JetBrains Mono).</summary>
    public static Font Mono(float size = 9f)
    {
        LoadFonts();
        return Make(_mono, size, FontStyle.Regular);
    }

    public static Font HeadingPx(float px) { LoadFonts(); return Make(_display, px, FontStyle.Regular, GraphicsUnit.Pixel); }
    public static Font MonoPx(float px) { LoadFonts(); return Make(_mono, px, FontStyle.Regular, GraphicsUnit.Pixel); }

    // ------------------------------------------------------------------ naslovna traka

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Tamni naslovni okvir (Windows 10 20H1+); na Windowsima 11 i boja okvira i naslova.</summary>
    public static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
            int caption = ColorTranslator.ToWin32(Sidebar);
            int border = ColorTranslator.ToWin32(BorderSoft);
            int text = ColorTranslator.ToWin32(TextSoft);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------ standardne kontrole

    /// <summary>Rekurzivno prebojava standardne kontrole u temu.</summary>
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
            case HexButton:
            case Dial:
            case SegmentBar:
            case TeleBox:
            case DeviceChip:
                return;
            case Button b:
                if (b.Tag as string == "swatch") { StyleFlat(b, b.BackColor, Border); return; }
                if (b.Tag as string == "primary")
                {
                    StyleFlat(b, Accent, Accent);
                    b.ForeColor = Color.FromArgb(4, 16, 24);
                    b.Font = UiFont(b.Font.Size, FontStyle.Bold);
                    return;
                }
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
                cb.BackColor = ControlBg;
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
                ch.FlatStyle = FlatStyle.Flat;
                ch.FlatAppearance.BorderColor = Border;
                ch.FlatAppearance.CheckedBackColor = AccentBg;
                ch.FlatAppearance.MouseOverBackColor = ControlBg;
                ch.Cursor = Cursors.Hand;
                return;
            case RadioButton rb:
                rb.ForeColor = TextSoft;
                rb.FlatStyle = FlatStyle.Flat;
                rb.FlatAppearance.BorderColor = Border;
                rb.FlatAppearance.CheckedBackColor = AccentBg;
                rb.Cursor = Cursors.Hand;
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

    // ------------------------------------------------------------------ oblici

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

    /// <summary>Pravokutnik s odrezanim gornjim desnim (i po želji donjim lijevim) kutom – osnovni HUD oblik.</summary>
    public static GraphicsPath Cut(RectangleF r, float cut, bool bottomLeft = false)
    {
        var p = new GraphicsPath();
        float b = bottomLeft ? cut : 0;
        p.AddLines(new[]
        {
            new PointF(r.Left, r.Top),
            new PointF(r.Right - cut, r.Top),
            new PointF(r.Right, r.Top + cut),
            new PointF(r.Right, r.Bottom),
            new PointF(r.Left + b, r.Bottom),
            new PointF(r.Left, r.Bottom - b)
        });
        p.CloseFigure();
        return p;
    }

    public static PointF[] Hexagon(RectangleF r)
    {
        float q = r.Width * 0.25f, my = r.Top + r.Height / 2;
        return new[]
        {
            new PointF(r.Left + q, r.Top), new PointF(r.Right - q, r.Top), new PointF(r.Right, my),
            new PointF(r.Right - q, r.Bottom), new PointF(r.Left + q, r.Bottom), new PointF(r.Left, my)
        };
    }

    /// <summary>Kutne oznake (kao na nišanu) oko pravokutnika.</summary>
    public static void Brackets(Graphics g, RectangleF r, float len, Color c)
    {
        using var p = new Pen(c, 1);
        g.DrawLines(p, new[] { new PointF(r.Left, r.Top + len), new PointF(r.Left, r.Top), new PointF(r.Left + len, r.Top) });
        g.DrawLines(p, new[] { new PointF(r.Right - len, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + len) });
        g.DrawLines(p, new[] { new PointF(r.Left, r.Bottom - len), new PointF(r.Left, r.Bottom), new PointF(r.Left + len, r.Bottom) });
        g.DrawLines(p, new[] { new PointF(r.Right - len, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - len) });
    }

    /// <summary>Svijetleća linija: nekoliko slojeva sve slabije boje.</summary>
    public static void GlowLine(Graphics g, PointF a, PointF b, Color c, float width = 2)
    {
        for (int i = 3; i >= 1; i--)
        {
            using var p = new Pen(Color.FromArgb(28, c), width + i * 3);
            g.DrawLine(p, a, b);
        }
        using var core = new Pen(c, width);
        g.DrawLine(core, a, b);
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Tekst velikim slovima s razmakom između slova (HUD oznake).</summary>
    public static void DrawSpaced(Graphics g, string text, Font font, Color color, PointF at, float spacing)
    {
        using var b = new SolidBrush(color);
        using var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        float x = at.X;
        foreach (char ch in text)
        {
            string s = ch.ToString();
            g.DrawString(s, font, b, x, at.Y, sf);
            x += g.MeasureString(s, font, PointF.Empty, sf).Width + spacing;
        }
    }
}

/// <summary>HUD panel s naslovom i odrezanim kutom (zamjena za GroupBox).</summary>
internal sealed class Card : Panel
{
    private readonly string _title;

    public Card(string title)
    {
        _title = title;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Padding = new Padding(16, string.IsNullOrEmpty(title) ? 14 : 46, 16, 14);
        Margin = new Padding(6);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Ground);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.Cut(r, 12);
        using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
        using (var p = new Pen(Theme.BorderSoft)) g.DrawPath(p, path);
        // svjetlosni rub uz odrezani kut
        using (var ap = new Pen(Color.FromArgb(150, Theme.Accent))) g.DrawLine(ap, r.Right - 12, r.Top, r.Right, r.Top + 12);
        if (!string.IsNullOrEmpty(_title))
        {
            using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 16, 17, 3, 14);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var f = Theme.Heading(11f);
            Theme.DrawSpaced(g, _title.ToUpperInvariant(), f, Theme.Text, new PointF(26, 14), 1.2f);
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
        AccessibleName = text;
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
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var r = new Rectangle(0, 0, Width, Height);
        if (Active)
        {
            using var lb = new LinearGradientBrush(r, Color.FromArgb(46, Theme.Accent), Color.FromArgb(0, Theme.Accent), 0f);
            g.FillRectangle(lb, r);
            Theme.GlowLine(g, new PointF(1, 7), new PointF(1, Height - 7), Theme.Accent, 2);
        }
        else if (_hover)
        {
            using var b = new SolidBrush(Theme.Surface);
            g.FillRectangle(b, r);
        }
        if (Focused)
        {
            using var p = new Pen(Theme.Accent);
            g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
        var fore = Active ? Theme.Accent : _hover ? Theme.Text : Theme.TextSoft;
        using (var icon = new Font("Segoe MDL2 Assets", 12f))
            TextRenderer.DrawText(g, Glyph, icon, new Rectangle(12, 0, 24, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        using var f = Theme.Heading(10.5f);
        var size = g.MeasureString("X", f);
        Theme.DrawSpaced(g, Text.ToUpperInvariant(), f, fore, new PointF(46, (Height - size.Height) / 2 + 1), 1.4f);
    }
}

/// <summary>Pločica efekta: red LED-ica koje se polako pomiču (pregled efekta) i naziv.</summary>
internal sealed class EffectTile : Control
{
    private bool _hover;
    private readonly Color[] _preview;
    public bool Selected { get; set; }
    /// <summary>Faza animacije pregleda (MainForm je povećava dok je kartica vidljiva).</summary>
    public float Phase { get; set; }

    public EffectTile(string text, params Color[] preview)
    {
        Text = text;
        AccessibleName = text;
        _preview = preview.Length == 0 ? new[] { Theme.Border } : preview;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(150, 62);
        Margin = new Padding(3);
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

    private Color Sample(float t)
    {
        if (_preview.Length == 1) return _preview[0];
        t = ((t % 1f) + 1f) % 1f * (_preview.Length - 1);
        int i = Math.Min((int)t, _preview.Length - 2);
        return Theme.Lerp(_preview[i], _preview[i + 1], t - i);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var bg = new SolidBrush(_hover && !Selected ? Color.FromArgb(22, 30, 46) : Theme.ControlBg)) g.FillRectangle(bg, r);
        if (Selected)
        {
            using var lb = new LinearGradientBrush(r, Color.FromArgb(60, Theme.Accent), Color.FromArgb(8, Theme.Accent), 90f);
            g.FillRectangle(lb, r);
        }
        using (var p = new Pen(Selected || Focused ? Theme.Accent : _hover ? Theme.Border : Theme.BorderSoft)) g.DrawRectangle(p, r);

        // red LED-ica
        const int cells = 14;
        float cw = (Width - 20) / (float)cells;
        for (int i = 0; i < cells; i++)
        {
            var c = Sample(i / (float)(cells - 1) * 0.9f + Phase);
            using var b = new SolidBrush(c);
            g.FillRectangle(b, 10 + i * cw, 10, Math.Max(2, cw - 2), 6);
        }
        using var f = Theme.UiFont(10f, Selected ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(g, Text, f, new Rectangle(9, 22, Width - 14, Height - 25), Selected ? Theme.Text : Theme.TextSoft,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Šesterokutni gumb (banke M1–M3).</summary>
internal sealed class HexButton : Control
{
    private bool _hover;
    public bool Checked { get; set; }

    public HexButton(string text)
    {
        Text = text;
        AccessibleName = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(56, 48);
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
        var hex = Theme.Hexagon(new RectangleF(4, 3, Width - 9, Height - 7));
        if (Checked)
        {
            for (int i = 3; i >= 1; i--)
            {
                using var gp = new Pen(Color.FromArgb(30, Theme.Accent), i * 2.5f);
                g.DrawPolygon(gp, hex);
            }
        }
        using (var b = new SolidBrush(Checked ? Theme.Accent : _hover ? Color.FromArgb(24, 33, 50) : Theme.ControlBg)) g.FillPolygon(b, hex);
        using (var p = new Pen(Checked || Focused ? Theme.Accent : Theme.BorderSoft)) g.DrawPolygon(p, hex);
        using var f = Theme.Heading(11f);
        TextRenderer.DrawText(g, Text, f, ClientRectangle, Checked ? Color.FromArgb(4, 16, 24) : Theme.TextSoft,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Okrugli kotačić (brzina, svjetlina): povlačenje gore-dolje, kotačić miša ili strelice.</summary>
internal sealed class Dial : Control
{
    private int _value, _min, _max = 100;
    private int? _dragY;
    private int _dragStart;
    public event EventHandler ValueChanged;

    public int Minimum { get => _min; set { _min = value; Value = _value; } }
    public int Maximum { get => _max; set { _max = value; Value = _value; } }

    public int Value
    {
        get => _value;
        set
        {
            int v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Dial(string label)
    {
        Text = label;
        AccessibleName = label;
        AccessibleRole = AccessibleRole.Slider;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(118, 132);
        Cursor = Cursors.SizeNS;
        TabStop = true;
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled) return;
        Focus();
        _dragY = e.Y;
        _dragStart = _value;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragY is int y0) Value = _dragStart + (int)Math.Round((y0 - e.Y) * (_max - _min) / 160.0);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragY = null;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Enabled) Value += Math.Sign(e.Delta) * Math.Max(1, (_max - _min) / 25);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int step = Math.Max(1, (_max - _min) / 50);
        if (e.KeyCode is Keys.Up or Keys.Right) Value += step;
        else if (e.KeyCode is Keys.Down or Keys.Left) Value -= step;
        else if (e.KeyCode == Keys.PageUp) Value += step * 5;
        else if (e.KeyCode == Keys.PageDown) Value -= step * 5;
        else if (e.KeyCode == Keys.Home) Value = _min;
        else if (e.KeyCode == Keys.End) Value = _max;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(Width, Height - 24) - 8;
        var c = new PointF(Width / 2f, 4 + size / 2);
        float r = size / 2 - 10;
        float t = _max > _min ? (_value - _min) / (float)(_max - _min) : 0;
        const float start = 135, sweep = 270;
        var accent = Enabled ? Theme.Accent : Theme.Muted;

        // podjela
        for (int i = 0; i <= 20; i++)
        {
            double a = (start + sweep * i / 20.0) * Math.PI / 180;
            float r1 = r + 6, r2 = r + (i % 5 == 0 ? 10 : 8);
            using var tp = new Pen(i / 20f <= t + 0.001f ? accent : Theme.BorderSoft, 1);
            g.DrawLine(tp, c.X + r1 * (float)Math.Cos(a), c.Y + r1 * (float)Math.Sin(a), c.X + r2 * (float)Math.Cos(a), c.Y + r2 * (float)Math.Sin(a));
        }
        var arc = new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r);
        using (var track = new Pen(Theme.BorderSoft, 5)) g.DrawArc(track, arc, start, sweep);
        if (t > 0)
        {
            if (Enabled)
                for (int i = 3; i >= 1; i--)
                {
                    using var gp = new Pen(Color.FromArgb(26, accent), 5 + i * 4);
                    g.DrawArc(gp, arc, start, sweep * t);
                }
            using var vp = new Pen(accent, 5);
            g.DrawArc(vp, arc, start, sweep * t);
        }
        float inner = r - 12;
        using (var ib = new SolidBrush(Theme.ControlBg)) g.FillEllipse(ib, c.X - inner, c.Y - inner, 2 * inner, 2 * inner);
        using (var ip = new Pen(Focused ? Theme.Accent : Theme.Border)) g.DrawEllipse(ip, c.X - inner, c.Y - inner, 2 * inner, 2 * inner);
        using (var vf = Theme.Mono(13f))
            TextRenderer.DrawText(g, _value.ToString(), vf, new Rectangle((int)(c.X - inner), (int)(c.Y - inner), (int)(2 * inner), (int)(2 * inner)),
                Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var lf = Theme.Mono(7.5f);
        TextRenderer.DrawText(g, Text.ToUpperInvariant(), lf, new Rectangle(0, Height - 20, Width, 18), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Niz gumba od kojih je jedan odabran (smjer efekta).</summary>
internal sealed class SegmentBar : Control
{
    private readonly string[] _items;
    private readonly string[] _tips;
    private readonly ToolTip _tip = new();
    private int _sel = -1, _hover = -1;
    public event EventHandler SelectedIndexChanged;

    public SegmentBar(string[] items, string[] tips = null)
    {
        _items = items;
        _tips = tips;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(260, 34);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PageTabList;
    }

    public int SelectedIndex
    {
        get => _sel;
        set
        {
            int v = Math.Clamp(value, -1, _items.Length - 1);
            if (v == _sel) return;
            _sel = v;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int Hit(int x) => Math.Clamp(x * _items.Length / Math.Max(1, Width), 0, _items.Length - 1);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = Hit(e.X);
        if (h == _hover) return;
        _hover = h;
        _tip.SetToolTip(this, _tips != null && h < _tips.Length ? _tips[h] : null);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (Enabled) { Focus(); SelectedIndex = Hit(e.X); } }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left && _sel > 0) SelectedIndex = _sel - 1;
        else if (e.KeyCode == Keys.Right && _sel < _items.Length - 1) SelectedIndex = _sel + 1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Surface);
        float w = (Width - 1) / (float)_items.Length;
        using var f = Theme.Mono(11f);
        for (int i = 0; i < _items.Length; i++)
        {
            var r = new Rectangle((int)(i * w), 0, (int)w, Height - 1);
            bool on = i == _sel;
            var back = !Enabled ? Theme.Surface : on ? Theme.Accent : i == _hover ? Color.FromArgb(24, 33, 50) : Theme.ControlBg;
            using (var b = new SolidBrush(back)) g.FillRectangle(b, r);
            var fore = !Enabled ? Theme.Muted : on ? Color.FromArgb(4, 16, 24) : Theme.TextSoft;
            TextRenderer.DrawText(g, _items[i], f, r, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        using var p = new Pen(Focused ? Theme.Accent : Theme.Border);
        g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        using var sp = new Pen(Theme.BorderSoft);
        for (int i = 1; i < _items.Length; i++) g.DrawLine(sp, i * w, 1, i * w, Height - 2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Polje telemetrije u zaglavlju: oznaka, vrijednost i (po želji) mala krivulja.</summary>
internal sealed class TeleBox : Control
{
    private readonly Queue<float> _history = new();
    public string Caption { get; set; }
    public string Value { get; private set; } = "—";
    public Color ValueColor { get; set; } = Theme.Text;
    public bool Spark { get; set; }
    public float SparkMax { get; set; } = 60;

    public TeleBox(string caption, int width = 104)
    {
        Caption = caption;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(width, 58);
        Margin = new Padding(0, 0, 4, 0);
    }

    public void Set(string value, float? sample = null)
    {
        Value = value;
        if (sample is float s)
        {
            _history.Enqueue(s);
            while (_history.Count > 30) _history.Dequeue();
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Ground);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, r);
        using (var p = new Pen(Theme.BorderSoft)) g.DrawRectangle(p, r);
        using (var cf = Theme.Mono(7f))
            TextRenderer.DrawText(g, Caption.ToUpperInvariant(), cf, new Point(9, 6), Theme.Muted);
        using (var vf = Theme.Mono(12.5f))
            TextRenderer.DrawText(g, Value, vf, new Rectangle(8, 20, Width - 12, 22), ValueColor, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        if (Spark && _history.Count > 1)
        {
            var pts = _history.Select((v, i) => new PointF(9 + i * (Width - 18) / 29f, Height - 6 - Math.Clamp(v / SparkMax, 0, 1) * 10)).ToArray();
            using var sp = new Pen(Theme.Accent, 1.4f);
            g.DrawLines(sp, pts);
            using var dot = new SolidBrush(Theme.Accent);
            g.FillRectangle(dot, pts[^1].X - 1.5f, pts[^1].Y - 1.5f, 3, 3);
        }
    }
}

/// <summary>Stanje jednog uređaja u lijevoj traci: svjetleća točka, naziv i detalj.</summary>
internal sealed class DeviceChip : Control
{
    public enum State { Off, Missing, Ok }
    private State _state;
    private string _detail = "";

    public DeviceChip(string name)
    {
        Text = name;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(208, 44);
        Margin = new Padding(0, 0, 0, 6);
    }

    public void Set(State s, string detail)
    {
        if (s == _state && detail == _detail) return;
        _state = s;
        _detail = detail ?? "";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Sidebar);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, r);
        using (var p = new Pen(Theme.BorderSoft)) g.DrawRectangle(p, r);
        var dot = _state switch { State.Ok => Theme.Good, State.Missing => Theme.Danger, _ => Theme.Muted };
        if (_state == State.Ok)
            using (var gl = new SolidBrush(Color.FromArgb(50, dot))) g.FillEllipse(gl, 7, Height / 2 - 8, 16, 16);
        using (var db = new SolidBrush(dot)) g.FillEllipse(db, 11, Height / 2 - 4, 8, 8);
        using (var nf = Theme.UiFont(9.5f, FontStyle.Bold))
            TextRenderer.DrawText(g, Text, nf, new Rectangle(28, 4, Width - 32, 18), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        using var df = Theme.Mono(7f);
        TextRenderer.DrawText(g, _detail, df, new Rectangle(28, 23, Width - 32, 16), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
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

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is StatusStrip)
        {
            using var p = new Pen(Theme.BorderSoft);
            e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
            return;
        }
        base.OnRenderToolStripBorder(e);
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
        public override Color SeparatorDark => Theme.BorderSoft;
        public override Color SeparatorLight => Theme.BorderSoft;
    }
}

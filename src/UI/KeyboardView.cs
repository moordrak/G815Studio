using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Svitac;

/// <summary>
/// Crta G815 kako je dogovoreno u prijedlogu: crne uzdignute tipke (bočne stranice dolje i desno),
/// obojeni sjaj ispod tipki, natpisi uz gornji rub, okrugle tipke u gornjoj traci, indikatori,
/// svijetleće slovo u kutu i kotačić s bevelom. Klik / povlačenje za odabir tipki (Ctrl dodaje).
/// Statični dio (kućište, tipke, kotačić) crta se jednom u bitmapu; po okviru se crtaju samo sjaj i natpisi.
/// </summary>
public sealed class KeyboardView : Control
{
    private const float RefU = 44f;           // dimenzije iz prijedloga su za tipku od 44 px
    private const float PadUnits = 24f / RefU;
    private const float BottomUnits = 0.9f;

    private List<KeyDef> _keys = new();
    private bool _dragging;
    private bool _dragAdd;
    private Bitmap _static;
    private Bitmap _bloom;
    private Size _staticSize;
    private Geo _geo;
    private Region _keyRegion;
    private readonly List<(RectangleF Rect, int Bank)> _mHits = new();
    private readonly List<(RectangleF Rect, KeyDef Key)> _roundHits = new();

    public HashSet<string> Selected { get; } = new();
    public Func<KeyDef, Color> ColorOf { get; set; }
    public int Bank { get; set; } = 1;
    public bool Recording { get; set; }
    public event EventHandler SelectionChanged;
    public event Action<int> BankClicked;
    /// <summary>Uređaj je premješten mišem: id uređaja i novi gornji lijevi kut (jedinice tipki).</summary>
    public event Action<string, float, float> DeviceMoved;
    /// <summary>Kliknut je uređaj u načinu uređivanja rasporeda.</summary>
    public event Action<string> DeviceClicked;

    // uređivanje rasporeda: uređaji (miš, memorija, izlazi računala) pomiču se povlačenjem
    private bool _arrange;
    private string _moveDev;
    private Point _moveStart;
    private bool _moved;
    private Geo _frozenGeo;
    private readonly Dictionary<KeyDef, (float X, float Y, float DX, float DY)> _moveOrig = new();
    public string HighlightDevice { get; set; }

    public bool ArrangeMode
    {
        get => _arrange;
        set { _arrange = value; _moveDev = null; _frozenGeo = null; Invalidate(); }
    }

    private static readonly Color Muted = Color.FromArgb(91, 97, 112);

    private enum RoundKind { Text, Game, Light, Prev, Play, Next, Mute }

    private sealed class Geo
    {
        public float S, F, Bx, By, Bw, Bh, PadX;
        public bool HasMouse;
        public float MouseOX = KeyLayouts.MouseX, MouseOY = KeyLayouts.MouseY;
        public RectangleF Rect(KeyDef k) => new(Bx + PadX + k.X * S + 2 * F, By + k.Y * S + 2 * F, k.W * S - 4 * F, k.H * S - 4 * F);
        public PointF Pt(float ux, float uy) => new(Bx + PadX + ux * S, By + uy * S);
        public RectangleF MouseBody => new(Bx + PadX + MouseOX * S, By + MouseOY * S, KeyLayouts.MouseW * S, KeyLayouts.MouseH * S);
        /// <summary>Okvir uređaja na zaslonu.</summary>
        public RectangleF Dev(KeyDef k) { var a = Pt(k.DevX, k.DevY); return new RectangleF(a.X, a.Y, k.DevW * S, k.DevH * S); }
        public RectangleF Led(KeyDef k)
        {
            var c = Pt(k.CenterX, k.CenterY);
            float d = KeyLayouts.MouseLedSize * S;
            return new RectangleF(c.X - d / 2, c.Y - d / 2, d, d);
        }
    }

    public KeyboardView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(5, 7, 11);
        MinimumSize = new Size(480, 180);
    }

    public void SetKeys(List<KeyDef> keys)
    {
        _keys = keys ?? new List<KeyDef>();
        Selected.RemoveWhere(id => _keys.All(k => k.Id != id));
        InvalidateStatic();
    }

    public void SelectWhere(Func<KeyDef, bool> predicate, bool add)
    {
        if (!add) Selected.Clear();
        foreach (var k in _keys.Where(predicate)) Selected.Add(k.Id);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        Selected.Clear();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateStatic()
    {
        _static?.Dispose();
        _static = null;
        _keyRegion?.Dispose();
        _keyRegion = null;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        InvalidateStatic();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _static?.Dispose();
            _bloom?.Dispose();
            _keyRegion?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ geometrija

    private IEnumerable<KeyDef> RealKeys => _keys.Where(k => k.Shape != "logo" && k.Shape != "round" && k.Shape != "mouse" && k.Shape != "pc" && k.Shape != "ram");
    /// <summary>Moduli memorije: po modulu zone odozdo prema gore.</summary>
    private List<List<KeyDef>> RamSticks => _keys.Where(k => k.Shape == "ram").GroupBy(k => k.X).OrderBy(g => g.Key).Select(g => g.OrderByDescending(k => k.Y).ToList()).ToList();
    private IEnumerable<KeyDef> MouseLeds => _keys.Where(k => k.Shape == "mouse");
    /// <summary>Izlazi računala (ASRock): po uređaju, LED-ice redom kojim ih kontroler prima.</summary>
    private List<List<KeyDef>> PcDevices => _keys.Where(k => k.Shape == "pc").GroupBy(k => k.Device).Select(g => g.ToList()).ToList();
    /// <summary>Svi uređaji izvan tipkovnice, po jedna tipka (nosi okvir uređaja).</summary>
    private List<KeyDef> Devices => _keys.Where(k => k.Device != null).GroupBy(k => k.Device).Select(g => g.First()).ToList();

    private Geo ComputeGeo()
    {
        if (_frozenGeo != null) return _frozenGeo;
        var real = RealKeys.ToList();
        if (real.Count == 0) return null;
        float maxX = real.Max(k => k.X + k.W);
        float maxY = real.Max(k => k.Y + k.H);
        float wu = maxX + 2 * PadUnits, hu = maxY + BottomUnits;
        // okvir svega na slici (jedinice tipki, 0 = lijevi rub prve tipke): tipkovnica i svi uređaji gdje god stajali
        float x0 = -PadUnits, y0 = 0, x1 = wu - PadUnits, y1 = hu;
        foreach (var d in Devices)
        {
            x0 = Math.Min(x0, d.DevX - 0.3f);
            y0 = Math.Min(y0, d.DevY - (d.Shape == "pc" ? 0.5f : 0.3f));
            x1 = Math.Max(x1, d.DevX + d.DevW + 0.3f);
            y1 = Math.Max(y1, d.DevY + d.DevH + (d.Shape == "ram" ? 0.6f : 0.3f));
        }
        float tw = x1 - x0, th = y1 - y0;
        float s = Math.Max(8, Math.Min((Width - 24) / tw, (Height - 24) / th));
        var mouseKey = _keys.FirstOrDefault(k => k.Shape == "mouse");
        var g = new Geo { S = s, F = s / RefU, PadX = PadUnits * s, Bw = wu * s, Bh = hu * s, HasMouse = mouseKey != null };
        if (mouseKey != null) { g.MouseOX = mouseKey.DevX; g.MouseOY = mouseKey.DevY; }
        float ox = (Width - tw * s) / 2 - x0 * s;      // zaslonski x jedinice 0
        float oy = (Height - th * s) / 2 - y0 * s;
        g.Bx = ox - g.PadX;
        g.By = oy;
        return g;
    }

    private KeyDef Find(string id) => _keys.FirstOrDefault(k => k.Id == id);

    private float CenterX(Geo g, string id)
    {
        var k = Find(id);
        if (k == null) return float.NaN;
        var r = g.Rect(k);
        return r.X + (r.Width - 7 * g.F) / 2;
    }

    private KeyDef HitTest(Point p)
    {
        var g = _geo ?? ComputeGeo();
        if (g == null) return null;
        var logo = Find("LOGO");
        if (logo != null && LogoRect(g).Contains(p)) return logo;
        foreach (var (rect, key) in _roundHits)
            if (rect.Contains(p)) return key;
        foreach (var m in MouseLeds)
            if (RectangleF.Inflate(g.Led(m), 4 * g.F, 4 * g.F).Contains(p)) return m;
        foreach (var dev in PcDevices)
            foreach (var k in dev)
                if (RectangleF.Inflate(g.Rect(k), 3 * g.F, 3 * g.F).Contains(p)) return k;
        foreach (var stick in RamSticks)
            foreach (var k in stick)
                if (RamSegRect(g, k).Contains(p)) return k;
        return RealKeys.LastOrDefault(k => g.Rect(k).Contains(p));
    }

    private RectangleF LogoRect(Geo g)
    {
        float cx = CenterX(g, "G1");
        if (float.IsNaN(cx)) cx = g.Bx + g.PadX + 0.5f * g.S;
        float size = 58 * g.F;
        return new RectangleF(cx - size / 2, g.By + 38 * g.F - size / 2, size, size);
    }

    // ------------------------------------------------------------------ pomoćne funkcije za crtanje

    private static GraphicsPath Rounded(RectangleF r, float radius)
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

    /// <summary>ISO Enter: gornji dio pune širine, donji dio uvučen za notch s lijeve strane.</summary>
    private static GraphicsPath IsoEnter(RectangleF r, float notch, float split, float radius)
    {
        float x = r.X, y = r.Y, w = r.Width, h = r.Height, d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x + notch, y + h - d, d, d, 90, 90);
        p.AddLine(x + notch, y + h - radius, x + notch, y + split);
        p.AddArc(x, y + split - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private GraphicsPath KeyPath(KeyDef k, RectangleF r, Geo g, bool cap)
    {
        float side = cap ? 7 * g.F : 0;
        var rr = new RectangleF(r.X, r.Y, r.Width - side, r.Height - side);
        if (k.Shape == "isoEnter")
            return IsoEnter(rr, 0.25f * g.S, g.S - 4 * g.F - side, (cap ? 6 : 7) * g.F);
        return Rounded(rr, (cap ? 6 : 7) * g.F);
    }

    private static Color C(string hex) => ColorTranslator.FromHtml(hex);

    private static bool IsDark(Color c) => c.R + c.G + c.B < 30;

    // ------------------------------------------------------------------ statični sloj

    private void BuildStatic(Geo g)
    {
        _static?.Dispose();
        _static = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
        _staticSize = Size;
        using var gr = Graphics.FromImage(_static);
        gr.SmoothingMode = SmoothingMode.AntiAlias;
        gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
        gr.Clear(BackColor);
        float f = g.F;

        // HUD pozadina: mreža (jedna ćelija = jedna tipka) i kutne oznake
        using (var grid = new Pen(Color.FromArgb(16, 76, 194, 255), 1))
        {
            var origin = g.Pt(0, 0);
            float step = g.S;
            for (float x = origin.X % step; x < Width; x += step) gr.DrawLine(grid, x, 0, x, Height);
            for (float y = origin.Y % step; y < Height; y += step) gr.DrawLine(grid, 0, y, Width, y);
        }
        Theme.Brackets(gr, new RectangleF(6, 6, Width - 13, Height - 13), 14, Color.FromArgb(170, Theme.Accent));

        // kućište: oštri kutovi, okomiti prijelaz, brušeni aluminij
        var body = new RectangleF(g.Bx, g.By, g.Bw, g.Bh);
        using (var sh = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            gr.FillRectangle(sh, body.X + 6 * f, body.Y + 16 * f, body.Width, body.Height);
        using (var lg = new LinearGradientBrush(body, C("#2b2e34"), C("#191b1f"), 90f))
        {
            lg.InterpolationColors = new ColorBlend
            {
                Colors = new[] { C("#2b2e34"), C("#1e2025"), C("#191b1f") },
                Positions = new[] { 0f, 0.55f, 1f }
            };
            gr.FillRectangle(lg, body);
        }
        using (var brushed = new Pen(Color.FromArgb(5, 255, 255, 255), 1))
            for (float x = body.X; x < body.Right; x += Math.Max(2, 3 * f))
                gr.DrawLine(brushed, x, body.Y, x, body.Bottom);
        using (var top = new Pen(Color.FromArgb(20, 255, 255, 255), 1))
            gr.DrawLine(top, body.X, body.Y + 0.5f, body.Right, body.Y + 0.5f);

        DrawRoller(gr, g);
        if (g.HasMouse) DrawMouseBody(gr, g);
        DrawPcStrips(gr, g);
        DrawRamBodies(gr, g);

        // tipke
        foreach (var k in RealKeys)
        {
            var r = g.Rect(k);
            using (var shadowPath = KeyPath(k, r, g, false))
            {
                using var m1 = new Matrix();
                m1.Translate(2 * f, 3 * f);
                using (var p1 = (GraphicsPath)shadowPath.Clone())
                {
                    p1.Transform(m1);
                    using var b1 = new SolidBrush(Color.FromArgb(215, 0, 0, 0));
                    gr.FillPath(b1, p1);
                }
                for (int i = 1; i <= 3; i++)
                {
                    using var p2 = (GraphicsPath)shadowPath.Clone();
                    using var m2 = new Matrix();
                    m2.Translate((2 + i * 2.2f) * f, (3 + i * 2.6f) * f);
                    p2.Transform(m2);
                    using var b2 = new SolidBrush(Color.FromArgb(55 - i * 12, 0, 0, 0));
                    gr.FillPath(b2, p2);
                }
            }

            using (var basePath = KeyPath(k, r, g, false))
            using (var baseBrush = new LinearGradientBrush(r, C("#2c2d31"), C("#050506"), 45f))
            {
                baseBrush.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { C("#2c2d31"), C("#151619"), C("#050506") },
                    Positions = new[] { 0f, 0.4f, 1f }
                };
                gr.FillPath(baseBrush, basePath);
            }

            using (var capPath = KeyPath(k, r, g, true))
            {
                var cb = capPath.GetBounds();
                using (var capBrush = new LinearGradientBrush(cb, C("#26272b"), C("#0a0a0c"), 60f))
                {
                    capBrush.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { C("#26272b"), C("#131416"), C("#0a0a0c") },
                        Positions = new[] { 0f, 0.55f, 1f }
                    };
                    gr.FillPath(capBrush, capPath);
                }
                using (var edge = new Pen(C("#34353a"), Math.Max(1, f)))
                {
                    using var shifted = (GraphicsPath)capPath.Clone();
                    using var m = new Matrix();
                    m.Translate(0.8f * f, 0.8f * f);
                    shifted.Transform(m);
                    var clip = gr.Clip;
                    gr.SetClip(capPath, CombineMode.Exclude);
                    gr.DrawPath(edge, shifted);
                    gr.Clip = clip;
                }
                using (var hiBrush = new LinearGradientBrush(cb, Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 45f))
                using (var hi = new Pen(hiBrush, Math.Max(1, f)))
                    gr.DrawPath(hi, capPath);
            }
        }

        // okrugle tipke
        foreach (var rk in RoundKeys(g))
            DrawRoundBody(gr, rk.Center, g);

        // regija svih tipki: sjaj se crta samo izvan tipki
        _keyRegion?.Dispose();
        _keyRegion = new Region(RectangleF.Empty);
        foreach (var k in RealKeys)
            using (var kp = KeyPath(k, g.Rect(k), g, false))
                _keyRegion.Union(kp);
    }

    // ------------------------------------------------------------------ miš (G502 X PLUS, obrisno)

    /// <summary>Obris miša odozgo: dvije glavne tipke, kotačić, proširenje za palac slijeva. Vlastiti crtež, ne fotografija.</summary>
    private static GraphicsPath MouseBodyPath(RectangleF b)
    {
        PointF P(float fx, float fy) => new(b.X + fx * b.Width, b.Y + fy * b.Height);
        var p = new GraphicsPath();
        p.AddBezier(P(0.5f, 0), P(0.80f, 0), P(0.93f, 0.07f), P(0.94f, 0.30f));
        p.AddLine(P(0.94f, 0.30f), P(0.965f, 0.62f));
        p.AddBezier(P(0.965f, 0.62f), P(0.99f, 0.86f), P(0.80f, 1.0f), P(0.53f, 1.0f));
        p.AddBezier(P(0.53f, 1.0f), P(0.25f, 1.0f), P(0.05f, 0.93f), P(0.03f, 0.73f));
        p.AddBezier(P(0.03f, 0.73f), P(0.015f, 0.62f), P(0f, 0.55f), P(0.045f, 0.48f));
        p.AddBezier(P(0.045f, 0.48f), P(0.085f, 0.42f), P(0.06f, 0.30f), P(0.07f, 0.22f));
        p.AddBezier(P(0.07f, 0.22f), P(0.09f, 0.06f), P(0.22f, 0), P(0.5f, 0));
        p.CloseFigure();
        return p;
    }

    private void DrawMouseBody(Graphics gr, Geo g)
    {
        float f = g.F;
        var b = g.MouseBody;
        PointF P(float fx, float fy) => new(b.X + fx * b.Width, b.Y + fy * b.Height);

        using var body = MouseBodyPath(b);
        for (int i = 3; i >= 1; i--)
        {
            using var sp = (GraphicsPath)body.Clone();
            using var m = new Matrix();
            m.Translate((2 + i * 2.5f) * f, (4 + i * 3.5f) * f);
            sp.Transform(m);
            using var sb = new SolidBrush(Color.FromArgb(70 - i * 15, 0, 0, 0));
            gr.FillPath(sb, sp);
        }
        using (var lg = new LinearGradientBrush(b, C("#2a2c31"), C("#08080a"), 60f))
        {
            lg.InterpolationColors = new ColorBlend
            {
                Colors = new[] { C("#2a2c31"), C("#141519"), C("#08080a") },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            gr.FillPath(lg, body);
        }
        using (var hiBrush = new LinearGradientBrush(b, Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 50f))
        using (var hi = new Pen(hiBrush, Math.Max(1, 1.2f * f)))
            gr.DrawPath(hi, body);

        using var seam = new Pen(Color.FromArgb(200, 0, 0, 0), Math.Max(1, 1.6f * f));
        using var seamHi = new Pen(Color.FromArgb(22, 255, 255, 255), Math.Max(1, f));
        // donji rub glavnih tipki i razdjelnica između njih
        gr.DrawBezier(seam, P(0.07f, 0.37f), P(0.30f, 0.43f), P(0.70f, 0.43f), P(0.94f, 0.37f));
        gr.DrawBezier(seamHi, P(0.07f, 0.375f), P(0.30f, 0.435f), P(0.70f, 0.435f), P(0.94f, 0.375f));
        gr.DrawLine(seam, P(0.5f, 0.0f), P(0.5f, 0.07f));
        gr.DrawLine(seam, P(0.5f, 0.29f), P(0.5f, 0.41f));
        // tipke uz lijevu glavnu tipku
        foreach (float y in new[] { 0.13f, 0.20f })
            using (var bp = Rounded(new RectangleF(P(0.14f, y).X, P(0.14f, y).Y, 0.1f * b.Width, 0.05f * b.Height), 2.5f * f))
                gr.DrawPath(seam, bp);
        // tipke za palac uz lijevi rub
        foreach (float y in new[] { 0.42f, 0.50f })
            using (var bp = Rounded(new RectangleF(P(0.075f, y).X, P(0.075f, y).Y, 0.07f * b.Width, 0.065f * b.Height), 2f * f))
                gr.DrawPath(seam, bp);

        // kotačić
        var wheel = new RectangleF(P(0.44f, 0.075f).X, P(0.44f, 0.075f).Y, 0.12f * b.Width, 0.2f * b.Height);
        using (var hole = Rounded(RectangleF.Inflate(wheel, 2 * f, 2 * f), 5 * f))
        using (var hb = new SolidBrush(C("#050506")))
            gr.FillPath(hb, hole);
        using (var wb = new LinearGradientBrush(wheel, C("#1a1b1e"), C("#1a1b1e"), 0f))
        {
            wb.InterpolationColors = new ColorBlend
            {
                Colors = new[] { C("#1a1b1e"), C("#6a6e76"), C("#1a1b1e") },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            using var wp = Rounded(wheel, 4 * f);
            gr.FillPath(wb, wp);
        }
        using (var notch = new Pen(Color.FromArgb(150, 0, 0, 0), Math.Max(1, f)))
            for (float y = wheel.Top + 2 * f; y < wheel.Bottom - f; y += Math.Max(2, 3 * f))
                gr.DrawLine(notch, wheel.Left + f, y, wheel.Right - f, y);
    }

    // ------------------------------------------------------------------ računalo (ASRock ARGB/RGB izlazi)

    private static string ShortName(string n) =>
        (n ?? "").Replace("header ", "").Replace(" / audio", "");

    private static RectangleF RingBand(Geo g, KeyDef k, out PointF center, out float radius)
    {
        var r = g.Dev(k);
        center = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
        radius = (k.DevW / 2 - 0.3f) * g.S;
        return new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
    }

    private void DrawPcStrips(Graphics gr, Geo g)
    {
        float f = g.F;
        using var font = new Font("Segoe UI Semibold", Math.Max(6f, 9f * f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var sfNear = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };
        using var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };
        using var textBrush = new SolidBrush(Muted);
        foreach (var dev in PcDevices)
        {
            var k0 = dev[0];
            var r = g.Dev(k0);
            bool ring = k0.DevShape == "ring";
            using (var sh = new GraphicsPath())
            {
                if (ring) sh.AddEllipse(r.X + 3 * f, r.Y + 5 * f, r.Width, r.Height);
                else sh.AddPath(Rounded(new RectangleF(r.X + 3 * f, r.Y + 5 * f, r.Width, r.Height), Math.Min(r.Width, r.Height) / 2), false);
                using var sb = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
                gr.FillPath(sb, sh);
            }
            using (var body = new GraphicsPath())
            {
                if (ring) body.AddEllipse(r);
                else body.AddPath(Rounded(r, Math.Min(r.Width, r.Height) / 2), false);
                using (var lg = new LinearGradientBrush(r, C("#24262b"), C("#0b0c0e"), 90f)) gr.FillPath(lg, body);
                using var hi = new Pen(Color.FromArgb(28, 255, 255, 255), Math.Max(1, f));
                gr.DrawPath(hi, body);
            }
            if (ring)
            {
                // ventilator: tamna unutrašnjost s glavčinom i lopaticama
                float inner = (k0.DevW / 2 - 0.6f) * g.S;
                var c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
                if (inner > 2)
                {
                    using (var ib = new SolidBrush(C("#08090b"))) gr.FillEllipse(ib, c.X - inner, c.Y - inner, inner * 2, inner * 2);
                    using var blade = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1, 1.5f * f));
                    for (int i = 0; i < 7; i++)
                    {
                        double a = i * 2 * Math.PI / 7;
                        float hub = inner * 0.32f;
                        gr.DrawBezier(blade,
                            new PointF(c.X + (float)Math.Cos(a) * hub, c.Y + (float)Math.Sin(a) * hub),
                            new PointF(c.X + (float)Math.Cos(a + 0.5) * inner * 0.55f, c.Y + (float)Math.Sin(a + 0.5) * inner * 0.55f),
                            new PointF(c.X + (float)Math.Cos(a + 0.8) * inner * 0.8f, c.Y + (float)Math.Sin(a + 0.8) * inner * 0.8f),
                            new PointF(c.X + (float)Math.Cos(a + 1.0) * inner * 0.95f, c.Y + (float)Math.Sin(a + 1.0) * inner * 0.95f));
                    }
                    float hr = inner * 0.32f;
                    using var hb = new SolidBrush(C("#1c1e22"));
                    gr.FillEllipse(hb, c.X - hr, c.Y - hr, hr * 2, hr * 2);
                }
            }
            var label = ring
                ? new RectangleF(r.X - 40 * f, r.Y - 16 * f, r.Width + 80 * f, 14 * f)
                : new RectangleF(r.X + 4 * f, r.Y - 16 * f, Math.Max(r.Width, 160 * f), 14 * f);
            gr.DrawString(ShortName(k0.DeviceName), font, textBrush, label, ring ? sfCenter : sfNear);
        }
    }

    private void DrawPcLeds(Graphics gr, Geo g, Pen selPen)
    {
        float f = g.F;
        foreach (var dev in PcDevices)
        {
            var k0 = dev[0];
            var cols = dev.Select(k => ColorOf?.Invoke(k) ?? Color.Black).ToArray();
            if (k0.DevShape == "ring")
            {
                var band = RingBand(g, k0, out var c, out float radius);
                if (radius < 2) continue;
                float sweep = 360f / dev.Count;
                using (var pen = new Pen(Color.Black, Math.Max(2, 0.4f * g.S)))
                    for (int i = 0; i < dev.Count; i++)
                    {
                        var p = g.Pt(dev[i].CenterX, dev[i].CenterY);
                        float ang = (float)(Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI);
                        pen.Color = IsDark(cols[i]) ? C("#1a1b1f") : cols[i];
                        gr.DrawArc(pen, band, ang - sweep / 2 - 0.5f, sweep + 1f);
                    }
            }
            else
            {
                var strip = g.Dev(k0);
                var inner = RectangleF.Inflate(strip, -5 * f, -5 * f);
                if (inner.Width < 2 || inner.Height < 2) continue;
                bool vert = k0.DevVertical;
                // svjetlo trake: prijelaz između LED-ica, redom kako stoje na traci
                var order = Enumerable.Range(0, dev.Count)
                    .Select(i => (Pos: vert ? Math.Clamp((g.Pt(0, dev[i].CenterY).Y - inner.Y) / inner.Height, 0f, 1f)
                                            : Math.Clamp((g.Pt(dev[i].CenterX, 0).X - inner.X) / inner.Width, 0f, 1f), Col: cols[i]))
                    .OrderBy(x => x.Pos).ToList();
                using var path = Rounded(inner, Math.Min(inner.Width, inner.Height) / 2);
                using var lb = new LinearGradientBrush(inner, order[0].Col, order[^1].Col, vert ? 90f : 0f);
                var blendCols = new List<Color> { order[0].Col };
                var blendPos = new List<float> { 0f };
                foreach (var (pos, col) in order)
                {
                    if (pos <= blendPos[^1]) continue;
                    blendCols.Add(col);
                    blendPos.Add(pos);
                }
                if (blendPos[^1] < 1f) { blendCols.Add(order[^1].Col); blendPos.Add(1f); }
                lb.InterpolationColors = new ColorBlend { Colors = blendCols.ToArray(), Positions = blendPos.ToArray() };
                gr.FillPath(lb, path);
            }
            // sjaj oko LED-ica
            for (int i = 0; i < dev.Count; i++)
            {
                if (IsDark(cols[i])) continue;
                var r = g.Rect(dev[i]);
                for (int j = 3; j >= 1; j--)
                {
                    var gl = RectangleF.Inflate(r, j * 4f * f, j * 4f * f);
                    using var gb = new SolidBrush(Color.FromArgb(20 - j * 5, cols[i]));
                    gr.FillEllipse(gb, gl);
                }
            }
            // oznake LED-ica (mjesta za odabir)
            for (int i = 0; i < dev.Count; i++)
            {
                var p = g.Pt(dev[i].CenterX, dev[i].CenterY);
                float d = Math.Min(5 * f, Math.Max(2, Math.Min(dev[i].W, dev[i].H) * g.S * 0.6f));
                using (var db = new SolidBrush(Color.FromArgb(IsDark(cols[i]) ? 60 : 150, 255, 255, 255)))
                    gr.FillEllipse(db, p.X - d / 2, p.Y - d / 2, d, d);
                if (Selected.Contains(dev[i].Id))
                    using (var sp = Rounded(RectangleF.Inflate(g.Rect(dev[i]), 2 * f, 2 * f), 4 * f)) gr.DrawPath(selPen, sp);
            }
        }
    }

    /// <summary>U načinu uređivanja rasporeda: isprekidani okvir oko svakog uređaja koji se može pomaknuti.</summary>
    private void DrawArrangeFrames(Graphics gr, Geo g)
    {
        float f = g.F;
        foreach (var d in Devices)
        {
            var r = RectangleF.Inflate(g.Dev(d), 5 * f, 5 * f);
            bool hot = d.Device == _moveDev || d.Device == HighlightDevice;
            using var pen = new Pen(hot ? Color.Gold : Color.FromArgb(150, 120, 180, 255), Math.Max(1, (hot ? 2f : 1.4f) * f)) { DashStyle = DashStyle.Dash };
            using var path = Rounded(r, 6 * f);
            gr.DrawPath(pen, path);
        }
    }

    private string DeviceAt(Point p)
    {
        var g = _geo ?? ComputeGeo();
        if (g == null) return null;
        // najmanji okvir pobjeđuje (manji uređaj iznad većeg)
        return Devices
            .Where(d => RectangleF.Inflate(g.Dev(d), 6 * g.F, 6 * g.F).Contains(p))
            .OrderBy(d => d.DevW * d.DevH)
            .Select(d => d.Device)
            .FirstOrDefault();
    }

    // ------------------------------------------------------------------ memorija (Kingston Fury)

    private static RectangleF RamSegRect(Geo g, KeyDef k)
    {
        var a = g.Pt(k.X, k.Y);
        return new RectangleF(a.X, a.Y, k.W * g.S, k.H * g.S);
    }

    private static RectangleF RamStickRect(Geo g, List<KeyDef> stick)
    {
        float top = stick.Min(k => k.Y), bottom = stick.Max(k => k.Y + k.H);
        var a = g.Pt(stick[0].X - 0.12f, top - 0.12f);
        return new RectangleF(a.X, a.Y, (stick[0].W + 0.24f) * g.S, (bottom - top + 0.24f) * g.S);
    }

    private void DrawRamBodies(Graphics gr, Geo g)
    {
        float f = g.F;
        var sticks = RamSticks;
        if (sticks.Count == 0) return;
        using var font = new Font("Segoe UI Semibold", Math.Max(6f, 9f * f), FontStyle.Regular, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
        using var textBrush = new SolidBrush(Muted);
        foreach (var stick in sticks)
        {
            var r = RamStickRect(g, stick);
            using (var sh = Rounded(new RectangleF(r.X + 3 * f, r.Y + 5 * f, r.Width, r.Height), 4 * f))
            using (var sb = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                gr.FillPath(sb, sh);
            using var body = Rounded(r, 4 * f);
            using (var lg = new LinearGradientBrush(r, C("#26282d"), C("#0b0c0e"), 0f)) gr.FillPath(lg, body);
            using var hi = new Pen(Color.FromArgb(28, 255, 255, 255), Math.Max(1, f));
            gr.DrawPath(hi, body);
        }
        var first = RamStickRect(g, sticks[0]);
        var last = RamStickRect(g, sticks[^1]);
        gr.DrawString("RAM", font, textBrush, new RectangleF(first.X, last.Bottom + 4 * f, last.Right - first.X, 14 * f), sf);
    }

    private void DrawRamLeds(Graphics gr, Geo g, Pen selPen)
    {
        float f = g.F;
        foreach (var stick in RamSticks)
        {
            var body = RamStickRect(g, stick);
            var bar = RectangleF.Inflate(body, -4 * f, -4 * f);
            if (bar.Width < 2 || bar.Height < 2) continue;
            var cols = stick.Select(k => ColorOf?.Invoke(k) ?? Color.Black).ToArray();
            // prijelaz odozdo prema gore, kao što se šalje LED-icama
            using (var path = Rounded(bar, 3 * f))
            using (var lb = new LinearGradientBrush(bar, cols[0], cols[^1], 270f))
            {
                if (cols.Length > 1)
                {
                    var pos = Enumerable.Range(0, cols.Length).Select(i => (float)i / (cols.Length - 1)).ToArray();
                    lb.InterpolationColors = new ColorBlend { Colors = cols, Positions = pos };
                }
                gr.FillPath(lb, path);
            }
            for (int i = 0; i < stick.Count; i++)
            {
                if (IsDark(cols[i])) continue;
                var r = RamSegRect(g, stick[i]);
                for (int j = 3; j >= 1; j--)
                {
                    var gl = RectangleF.Inflate(r, j * 3f * f, j * 2f * f);
                    using var gb = new SolidBrush(Color.FromArgb(18 - j * 4, cols[i]));
                    gr.FillEllipse(gb, gl);
                }
            }
            foreach (var k in stick)
                if (Selected.Contains(k.Id))
                    using (var sp = Rounded(RectangleF.Inflate(RamSegRect(g, k), 1.5f * f, 1.5f * f), 3 * f)) gr.DrawPath(selPen, sp);
        }
    }

    private void DrawRoller(Graphics gr, Geo g)
    {
        float f = g.F;
        float cx1 = CenterX(g, "U54"), cx2 = CenterX(g, "U55");
        if (float.IsNaN(cx1) || float.IsNaN(cx2)) return;
        float cx = (cx1 + cx2) / 2 + 3.5f * f;

        // ploha (postolje) uz gornji rub
        var ledge = new RectangleF(cx - 86 * f, g.By, 172 * f, 45 * f);
        using (var lp = new GraphicsPath())
        {
            float d = 28 * f;
            lp.AddLine(ledge.X, ledge.Y, ledge.Right, ledge.Y);
            lp.AddArc(ledge.Right - d, ledge.Bottom - d, d, d, 0, 90);
            lp.AddArc(ledge.X, ledge.Bottom - d, d, d, 90, 90);
            lp.CloseFigure();
            using (var sh = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            {
                using var m = new Matrix();
                m.Translate(0, 4 * f);
                using var sp = (GraphicsPath)lp.Clone();
                sp.Transform(m);
                gr.FillPath(sh, sp);
            }
            using (var b = new SolidBrush(C("#26282d"))) gr.FillPath(b, lp);
        }

        // bevel (skošeni ovratnik) – četiri plohe različite svjetline
        var outer = new RectangleF(ledge.X + 5 * f, ledge.Y, 162 * f, 40 * f);
        float t = 4 * f, rt = 12 * f, bt = 11 * f, lt = 12 * f;
        var inner = new RectangleF(outer.X + lt, outer.Y + t, outer.Width - lt - rt, outer.Height - t - bt);
        void Facet(Color c, params PointF[] pts)
        {
            using var b = new SolidBrush(c);
            gr.FillPolygon(b, pts);
        }
        Facet(C("#2e3035"), new PointF(outer.Left, outer.Top), new PointF(outer.Right, outer.Top), new PointF(inner.Right, inner.Top), new PointF(inner.Left, inner.Top));
        Facet(C("#222428"), new PointF(outer.Right, outer.Top), new PointF(outer.Right, outer.Bottom), new PointF(inner.Right, inner.Bottom), new PointF(inner.Right, inner.Top));
        Facet(C("#17181b"), new PointF(outer.Left, outer.Bottom), new PointF(outer.Right, outer.Bottom), new PointF(inner.Right, inner.Bottom), new PointF(inner.Left, inner.Bottom));
        Facet(C("#27292d"), new PointF(outer.Left, outer.Top), new PointF(outer.Left, outer.Bottom), new PointF(inner.Left, inner.Bottom), new PointF(inner.Left, inner.Top));
        using (var hole = new SolidBrush(C("#050506"))) gr.FillRectangle(hole, inner);

        // valjak: vodoravne crte, svjetlo slijeva nadesno
        var cyl = RectangleF.Inflate(inner, -2 * f, -4 * f);
        if (cyl.Width < 2 || cyl.Height < 2) return;
        using (var cb = new LinearGradientBrush(cyl, C("#17181b"), C("#141518"), 0f))
        {
            cb.InterpolationColors = new ColorBlend
            {
                Colors = new[] { C("#17181b"), C("#5d6169"), C("#8a8e96"), C("#5d6169"), C("#141518") },
                Positions = new[] { 0f, 0.18f, 0.5f, 0.82f, 1f }
            };
            using var path = Rounded(cyl, 4 * f);
            gr.FillPath(cb, path);
        }
        using (var line = new Pen(Color.FromArgb(140, 0, 0, 0), Math.Max(1, f)))
            for (float y = cyl.Top + 1; y < cyl.Bottom; y += Math.Max(2, 3 * f))
                gr.DrawLine(line, cyl.Left + f, y, cyl.Right - f, y);
    }

    private IEnumerable<(PointF Center, RoundKind Kind, string Text, int Bank, KeyDef Key)> RoundKeys(Geo g)
    {
        float f = g.F;
        float topY = g.By + 38 * f;
        var list = new List<(PointF, RoundKind, string, int, KeyDef)>();
        void Add(string id, RoundKind kind, string text, int bank, float y, string rgbId = null)
        {
            float x = CenterX(g, id);
            if (!float.IsNaN(x)) list.Add((new PointF(x, y), kind, text, bank, rgbId == null ? null : Find(rgbId)));
        }
        Add("U3A", RoundKind.Text, "M1", 1, topY);
        Add("U3B", RoundKind.Text, "M2", 2, topY);
        Add("U3C", RoundKind.Text, "M3", 3, topY);
        Add("U3D", RoundKind.Text, "MR", 0, topY);
        Add("U3E", RoundKind.Game, null, -1, topY);
        Add("U3F", RoundKind.Light, null, -1, topY, "BRIGHT");

        var pause = Find("U48");
        float mediaY = topY;
        if (pause != null)
        {
            var pr = g.Rect(pause);
            mediaY = pr.Y + (pr.Height - 7 * f) / 2;
        }
        Add("U53", RoundKind.Prev, null, -1, mediaY, "MEDIA_PREV");
        Add("U54", RoundKind.Play, null, -1, mediaY, "MEDIA_PLAY");
        Add("U55", RoundKind.Next, null, -1, mediaY, "MEDIA_NEXT");
        Add("U56", RoundKind.Mute, null, -1, mediaY, "MEDIA_MUTE");
        return list;
    }

    private static void DrawRoundBody(Graphics gr, PointF c, Geo g)
    {
        float f = g.F, d = 32 * f;
        var r = new RectangleF(c.X - d / 2, c.Y - d / 2, d, d);
        using (var s1 = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            gr.FillEllipse(s1, r.X + 4 * f, r.Y + 6 * f, d, d);
        using (var s2 = new SolidBrush(Color.Black))
            gr.FillEllipse(s2, r.X + 2 * f, r.Y + 3 * f, d, d);
        using (var b = new LinearGradientBrush(r, C("#1e1f22"), C("#050506"), 60f))
        {
            b.InterpolationColors = new ColorBlend
            {
                Colors = new[] { C("#1e1f22"), C("#0c0d0e"), C("#050506") },
                Positions = new[] { 0f, 0.6f, 1f }
            };
            gr.FillEllipse(b, r);
        }
        using var hiBrush = new LinearGradientBrush(r, Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 45f);
        using var hi = new Pen(hiBrush, Math.Max(1, f));
        gr.DrawEllipse(hi, r);
    }

    // ------------------------------------------------------------------ ikone (crtane u okviru 24×24)

    private static void WithBox(Graphics gr, RectangleF box, float vbW, float vbH, Action<Graphics, float> draw)
    {
        var state = gr.Save();
        gr.TranslateTransform(box.X, box.Y);
        float sc = Math.Min(box.Width / vbW, box.Height / vbH);
        gr.ScaleTransform(sc, sc);
        draw(gr, sc);
        gr.Restore(state);
    }

    private static Pen IconPen(Color c, float width)
    {
        var p = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        return p;
    }

    private static void DrawIcon(Graphics gr, string kind, RectangleF box, Color c)
    {
        switch (kind)
        {
            case "enter":
                WithBox(gr, box, 24, 24, (g, _) =>
                {
                    using var p = IconPen(c, 2.2f);
                    using var path = new GraphicsPath();
                    path.AddLine(20, 4, 20, 11);
                    path.AddArc(14, 8, 6, 6, 0, 90);
                    path.AddLine(17, 14, 5, 14);
                    g.DrawPath(p, path);
                    g.DrawLines(p, new[] { new PointF(9, 10), new PointF(5, 14), new PointF(9, 18) });
                });
                break;
            case "win":
                WithBox(gr, box, 24, 24, (g, _) =>
                {
                    using var p = new Pen(c, 3f) { LineJoin = LineJoin.Miter };
                    g.DrawRectangle(p, 3, 3, 18, 18);
                    g.DrawLine(p, 12, 3, 12, 21);
                    g.DrawLine(p, 3, 12, 21, 12);
                });
                break;
            case "menu":
                WithBox(gr, box, 24, 24, (g, _) =>
                {
                    using var p = IconPen(c, 2.6f);
                    foreach (float y in new[] { 6f, 12f, 18f })
                    {
                        g.DrawLine(p, 9, y, 21, y);
                        using var b = new SolidBrush(c);
                        g.FillEllipse(b, 2f, y - 1.7f, 3.4f, 3.4f);
                    }
                });
                break;
            case "backspace":
                WithBox(gr, box, 26, 18, (g, _) =>
                {
                    using var p = IconPen(c, 2.2f);
                    g.DrawPolygon(p, new[] { new PointF(8, 2), new PointF(24.5f, 2), new PointF(24.5f, 16), new PointF(8, 16), new PointF(1.5f, 9) });
                    g.DrawLine(p, 12, 6, 18, 12);
                    g.DrawLine(p, 18, 6, 12, 12);
                });
                break;
        }
    }

    private static void DrawRoundIcon(Graphics gr, RoundKind kind, RectangleF box, Color c)
    {
        WithBox(gr, box, 24, 24, (g, _) =>
        {
            using var p = IconPen(c, 2f);
            using var fill = new SolidBrush(c);
            switch (kind)
            {
                case RoundKind.Light:
                    g.DrawEllipse(p, 8, 8, 8, 8);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p, 12 + (float)Math.Cos(a) * 8, 12 + (float)Math.Sin(a) * 8, 12 + (float)Math.Cos(a) * 10, 12 + (float)Math.Sin(a) * 10);
                    }
                    break;
                case RoundKind.Prev:
                    g.FillRectangle(fill, 5, 5, 3, 14);
                    g.FillPolygon(fill, new[] { new PointF(20, 5), new PointF(20, 19), new PointF(9, 12) });
                    break;
                case RoundKind.Play:
                    g.FillPolygon(fill, new[] { new PointF(4, 5), new PointF(4, 19), new PointF(13, 12) });
                    g.FillRectangle(fill, 15, 5, 2.5f, 14);
                    g.FillRectangle(fill, 19.5f, 5, 2.5f, 14);
                    break;
                case RoundKind.Next:
                    g.FillRectangle(fill, 16, 5, 3, 14);
                    g.FillPolygon(fill, new[] { new PointF(4, 5), new PointF(4, 19), new PointF(15, 12) });
                    break;
                case RoundKind.Mute:
                    g.DrawPolygon(p, new[] { new PointF(11, 5), new PointF(6, 9), new PointF(3, 9), new PointF(3, 15), new PointF(6, 15), new PointF(11, 19) });
                    g.DrawLine(p, 22, 9, 16, 15);
                    g.DrawLine(p, 16, 9, 22, 15);
                    break;
                case RoundKind.Game:
                    // palica za letenje: ručka s gumbom, okidač, manžeta, postolje
                    using (var grip = Rounded(new RectangleF(9.5f, 2, 6, 11.5f), 1.6f)) g.DrawPath(p, grip);
                    g.DrawEllipse(p, 11.6f, 3.4f, 1.8f, 1.8f);
                    g.DrawLine(p, 9.6f, 8.2f, 8.3f, 9.4f);
                    g.DrawPolygon(p, new[] { new PointF(10.5f, 14.5f), new PointF(14.5f, 14.5f), new PointF(15.3f, 17), new PointF(9.7f, 17) });
                    using (var b = Rounded(new RectangleF(4, 18, 16, 3.5f), 1.5f)) g.DrawPath(p, b);
                    break;
            }
        });
    }

    // ------------------------------------------------------------------ crtanje po okviru

    private static void DrawGlowText(Graphics gr, string text, Font font, Color c, RectangleF rect, StringFormat sf, float spread, bool lit)
    {
        if (lit)
        {
            using var gb = new SolidBrush(Color.FromArgb(70, c));
            foreach (var (dx, dy) in new[] { (-spread, 0f), (spread, 0f), (0f, -spread), (0f, spread) })
                gr.DrawString(text, font, gb, new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height), sf);
        }
        using var b = new SolidBrush(c);
        gr.DrawString(text, font, b, rect, sf);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var gr = e.Graphics;
        var g = ComputeGeo();
        _geo = g;
        if (g == null) { gr.Clear(BackColor); return; }
        if (_static == null || _staticSize != Size) BuildStatic(g);
        gr.DrawImageUnscaled(_static, 0, 0);

        gr.SmoothingMode = SmoothingMode.AntiAlias;
        gr.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float f = g.F;

        // 1) sjaj ispod tipki (samo izvan tipki): mekani „bloom“ iz male bitmape + bliži sjaj po tipki
        var saved = gr.Clip;
        if (_keyRegion != null) gr.SetClip(_keyRegion, CombineMode.Exclude);
        DrawBloom(gr, g);
        foreach (var k in RealKeys)
        {
            var c = ColorOf?.Invoke(k) ?? Color.Black;
            if (IsDark(c)) continue;
            var r = g.Rect(k);
            for (int i = 3; i >= 1; i--)
            {
                var gl = RectangleF.Inflate(r, i * 3.2f * f, i * 3.2f * f);
                gl.Offset(i * 1.2f * f, i * 1.6f * f);
                using var path = Rounded(gl, (7 + i * 3) * f);
                using var b = new SolidBrush(Color.FromArgb(34 - i * 7, c));
                gr.FillPath(b, path);
            }
        }
        gr.Clip = saved;

        // 2) natpisi i ikone
        float fontPx = Math.Max(6f, 8f * f);
        using var font = new Font("Segoe UI Semibold", fontPx, FontStyle.Regular, GraphicsUnit.Pixel);
        using var sfTop = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap };
        using var sfMid = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        using var selPen = new Pen(Color.Gold, Math.Max(2, 2.2f * f));

        foreach (var k in RealKeys)
        {
            var c = ColorOf?.Invoke(k) ?? Color.Black;
            bool lit = !IsDark(c);
            var textColor = lit ? c : Muted;
            var r = g.Rect(k);
            float side = 7 * f;
            var cap = new RectangleF(r.X, r.Y, r.Width - side, r.Height - side);
            var top = new RectangleF(cap.X + 2 * f, cap.Y + 4 * f, cap.Width - 4 * f, fontPx * 1.6f);
            float icon = 15 * f;

            if (k.Shape == "isoEnter")
            {
                float notch = 0.25f * g.S;
                var box = new RectangleF(cap.X + notch + (cap.Width - notch - icon) / 2, cap.Y + (cap.Height - icon) / 2, icon, icon);
                DrawIcon(gr, "enter", box, textColor);
            }
            else if (k.Usage == 0xE3 || k.Usage == 0xE7)
                DrawIcon(gr, "win", new RectangleF(cap.X + (cap.Width - icon) / 2, cap.Y + 5 * f, icon, icon), textColor);
            else if (k.Usage == 0x65)
                DrawIcon(gr, "menu", new RectangleF(cap.X + (cap.Width - icon) / 2, cap.Y + 5 * f, icon, icon), textColor);
            else if (k.Usage == 0x2A)
                DrawIcon(gr, "backspace", new RectangleF(cap.X + (cap.Width - 18 * f) / 2, cap.Y + 5 * f, 18 * f, 13 * f), textColor);
            else if (k.Usage == 0x2C)
            {
                using var bar = new SolidBrush(textColor);
                gr.FillRectangle(bar, cap.X + (cap.Width - 30 * f) / 2, cap.Y + 5 * f + 10 * f, 30 * f, Math.Max(1.5f, 3 * f));
            }
            else if (k.Usage == 0x58)
                DrawGlowText(gr, k.Label.ToUpperInvariant(), font, textColor, cap, sfMid, 0.8f * f, lit);
            else if (!string.IsNullOrEmpty(k.Label))
                DrawGlowText(gr, k.Label.ToUpperInvariant(), font, textColor, top, sfTop, 0.8f * f, lit);

            if (Selected.Contains(k.Id))
                using (var sp = KeyPath(k, r, g, false)) gr.DrawPath(selPen, sp);
        }

        // 3) okrugle tipke: natpisi / ikone
        _mHits.Clear();
        _roundHits.Clear();
        using var mFont = new Font("Segoe UI", Math.Max(6f, 10f * f), FontStyle.Bold, GraphicsUnit.Pixel);
        foreach (var rk in RoundKeys(g))
        {
            float d = 32 * f;
            var rect = new RectangleF(rk.Center.X - d / 2, rk.Center.Y - d / 2, d, d);
            if (rk.Kind == RoundKind.Text)
            {
                bool on = rk.Bank > 0 ? rk.Bank == Bank : Recording;
                var col = !on ? Color.FromArgb(107, 114, 128) : rk.Bank > 0 ? Color.White : Color.FromArgb(255, 90, 100);
                DrawGlowText(gr, rk.Text, mFont, col, rect, sfMid, 1.2f * f, on);
                if (rk.Bank > 0) _mHits.Add((rect, rk.Bank));
            }
            else
            {
                // RGB okrugla tipka: ikona svijetli bojom tipke, oko tipke sjaj
                var kc = rk.Key != null ? (ColorOf?.Invoke(rk.Key) ?? Color.Black) : Color.FromArgb(217, 220, 226);
                bool lit = !IsDark(kc);
                if (lit && rk.Key != null)
                {
                    for (int i = 3; i >= 1; i--)
                    {
                        var gl = RectangleF.Inflate(rect, i * 3f * f, i * 3f * f);
                        gl.Offset(i * 1.2f * f, i * 1.6f * f);
                        using var gb = new SolidBrush(Color.FromArgb(30 - i * 7, kc));
                        using var ring = new System.Drawing.Drawing2D.GraphicsPath();
                        ring.AddEllipse(gl);
                        ring.AddEllipse(rect);
                        ring.FillMode = FillMode.Alternate;
                        gr.FillPath(gb, ring);
                    }
                }
                float isz = (rk.Kind == RoundKind.Light ? 17 : rk.Kind == RoundKind.Game ? 16 : rk.Kind == RoundKind.Mute ? 16 : 15) * f;
                DrawRoundIcon(gr, rk.Kind, new RectangleF(rk.Center.X - isz / 2, rk.Center.Y - isz / 2, isz, isz), lit ? kc : Muted);
                if (rk.Key != null)
                {
                    _roundHits.Add((rect, rk.Key));
                    if (Selected.Contains(rk.Key.Id))
                        gr.DrawEllipse(selPen, RectangleF.Inflate(rect, 1.5f * f, 1.5f * f));
                }
            }
        }

        // 4) indikatori Caps Lock / Num Lock iznad F10 i F11
        using var indFont = new Font("Segoe UI Semibold", Math.Max(5f, 6f * f), FontStyle.Regular, GraphicsUnit.Pixel);
        DrawIndicator(gr, g, "U43", "CAPS LOCK", IsLocked(Keys.CapsLock), indFont, sfMid);
        DrawIndicator(gr, g, "U44", "NUM LOCK", IsLocked(Keys.NumLock), indFont, sfMid);

        // 5) LED-ice miša: sjaj po tijelu miša i svijetleća točka
        if (g.HasMouse) DrawMouseLeds(gr, g, selPen);
        DrawPcLeds(gr, g, selPen);
        DrawRamLeds(gr, g, selPen);
        if (_arrange || HighlightDevice != null) DrawArrangeFrames(gr, g);

        // 6) svijetleće slovo u gornjem lijevom kutu (zona loga)
        var logo = Find("LOGO");
        if (logo != null)
        {
            var c = ColorOf?.Invoke(logo) ?? Color.Black;
            bool lit = !IsDark(c);
            var col = lit ? c : Color.FromArgb(42, 44, 49);
            var lr = LogoRect(g);
            using var lf = new Font("Arial Black", 46 * f, FontStyle.Bold, GraphicsUnit.Pixel);
            if (lit)
            {
                for (int i = 3; i >= 1; i--)
                {
                    using var gb = new SolidBrush(Color.FromArgb(28, c));
                    float sp = i * 2.2f * f;
                    foreach (var (dx, dy) in new[] { (-sp, 0f), (sp, 0f), (0f, -sp), (0f, sp) })
                        gr.DrawString("G", lf, gb, new RectangleF(lr.X + dx, lr.Y + dy, lr.Width, lr.Height), sfMid);
                }
            }
            using var lb = new SolidBrush(col);
            gr.DrawString("G", lf, lb, lr, sfMid);
            if (Selected.Contains(logo.Id))
                using (var sp = Rounded(lr, 8 * f)) gr.DrawPath(selPen, sp);
        }
    }

    /// <summary>
    /// Boje svih LED-ica nacrtaju se u bitmapu osam puta manju od kontrole, a zatim razvuku preko slike.
    /// Razvlačenje s bilinearnim filtrom daje mekani sjaj (jeftin blur) i radi i na 60 FPS.
    /// </summary>
    private void DrawBloom(Graphics gr, Geo g)
    {
        const int down = 8;
        int bw = Math.Max(1, Width / down), bh = Math.Max(1, Height / down);
        if (_bloom == null || _bloom.Width != bw || _bloom.Height != bh)
        {
            _bloom?.Dispose();
            _bloom = new Bitmap(bw, bh, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }
        using (var bg = Graphics.FromImage(_bloom))
        {
            bg.Clear(Color.Transparent);
            bg.ScaleTransform(1f / down, 1f / down);
            void Fill(KeyDef k, RectangleF r)
            {
                var c = ColorOf?.Invoke(k) ?? Color.Black;
                if (IsDark(c)) return;
                using var b = new SolidBrush(c);
                bg.FillRectangle(b, RectangleF.Inflate(r, 2 * g.F, 2 * g.F));
            }
            foreach (var k in RealKeys) Fill(k, g.Rect(k));
            foreach (var m in MouseLeds) Fill(m, g.Led(m));
            foreach (var dev in PcDevices) foreach (var k in dev) Fill(k, g.Rect(k));
            foreach (var stick in RamSticks) foreach (var k in stick) Fill(k, RamSegRect(g, k));
            var logo = Find("LOGO");
            if (logo != null) Fill(logo, RectangleF.Inflate(LogoRect(g), -10 * g.F, -10 * g.F));
        }
        using var ia = new System.Drawing.Imaging.ImageAttributes();
        ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.55f });
        var mode = gr.InterpolationMode;
        gr.InterpolationMode = InterpolationMode.HighQualityBilinear;
        gr.DrawImage(_bloom, new Rectangle(-down / 2, -down / 2, bw * down, bh * down), 0, 0, bw, bh, GraphicsUnit.Pixel, ia);
        gr.InterpolationMode = mode;
    }

    private void DrawMouseLeds(Graphics gr, Geo g, Pen selPen)
    {
        float f = g.F;
        var leds = MouseLeds.ToList();
        if (leds.Count == 0) return;
        using var body = MouseBodyPath(g.MouseBody);
        var saved = gr.Clip;
        gr.SetClip(body, CombineMode.Intersect);
        foreach (var k in leds)
        {
            var c = ColorOf?.Invoke(k) ?? Color.Black;
            if (IsDark(c)) continue;
            var r = g.Led(k);
            for (int i = 4; i >= 1; i--)
            {
                var gl = RectangleF.Inflate(r, i * 4.5f * f, i * 4.5f * f);
                using var gb = new SolidBrush(Color.FromArgb(26 - i * 5, c));
                gr.FillEllipse(gb, gl);
            }
        }
        gr.Clip = saved;

        // utor trake (spaja LED-ice) pa same LED-ice
        using (var track = new Pen(Color.FromArgb(160, 0, 0, 0), Math.Max(2, 5 * f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            if (leds.Count > 1)
                gr.DrawCurve(track, leds.Select(k => g.Pt(k.CenterX, k.CenterY)).ToArray());
        foreach (var k in leds)
        {
            var c = ColorOf?.Invoke(k) ?? Color.Black;
            bool lit = !IsDark(c);
            var r = g.Led(k);
            using (var b = new SolidBrush(lit ? c : Color.FromArgb(42, 44, 49))) gr.FillEllipse(b, r);
            if (lit)
                using (var core = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                    gr.FillEllipse(core, RectangleF.Inflate(r, -r.Width * 0.3f, -r.Height * 0.3f));
            if (Selected.Contains(k.Id))
                gr.DrawEllipse(selPen, RectangleF.Inflate(r, 2.5f * f, 2.5f * f));
        }
    }

    private static bool IsLocked(Keys k)
    {
        try { return Control.IsKeyLocked(k); } catch { return false; }
    }

    private void DrawIndicator(Graphics gr, Geo g, string keyId, string text, bool on, Font font, StringFormat sf)
    {
        float cx = CenterX(g, keyId);
        if (float.IsNaN(cx)) return;
        float f = g.F;
        float topY = g.By + 26 * f;
        var textRect = new RectangleF(cx - 30 * f, topY, 60 * f, 9 * f);
        var col = on ? Color.FromArgb(217, 220, 226) : Color.FromArgb(107, 114, 128);
        DrawGlowText(gr, text, font, col, textRect, sf, 0.6f * f, on);
        float led = 5 * f;
        var lr = new RectangleF(cx - led / 2, textRect.Bottom + 4 * f, led, led);
        if (on)
        {
            using var glow = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
            gr.FillEllipse(glow, RectangleF.Inflate(lr, 2.5f * f, 2.5f * f));
            using var b = new SolidBrush(Color.White);
            gr.FillEllipse(b, lr);
        }
        else
        {
            using var b = new SolidBrush(Color.FromArgb(42, 44, 49));
            gr.FillEllipse(b, lr);
        }
    }

    // ------------------------------------------------------------------ miš

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        foreach (var (rect, bank) in _mHits)
        {
            if (!rect.Contains(e.Location)) continue;
            BankClicked?.Invoke(bank);
            return;
        }
        if (_arrange)
        {
            _moveDev = DeviceAt(e.Location);
            if (_moveDev == null) return;
            _moveStart = e.Location;
            _moved = false;
            _frozenGeo = _geo ?? ComputeGeo();
            _moveOrig.Clear();
            foreach (var dk in _keys.Where(x => x.Device == _moveDev)) _moveOrig[dk] = (dk.X, dk.Y, dk.DevX, dk.DevY);
            Capture = true;
            DeviceClicked?.Invoke(_moveDev);
            Invalidate();
            return;
        }
        bool ctrl = (ModifierKeys & System.Windows.Forms.Keys.Control) != 0;
        var k = HitTest(e.Location);
        if (k == null && _geo != null && _geo.HasMouse)
        {
            using var body = MouseBodyPath(_geo.MouseBody);
            if (body.IsVisible(e.Location))
            {
                SelectWhere(m => m.Shape == "mouse", ctrl);
                return;
            }
        }
        if (k == null)
        {
            if (!ctrl) ClearSelection();
            return;
        }
        if (ctrl)
        {
            _dragAdd = !Selected.Contains(k.Id);
            if (_dragAdd) Selected.Add(k.Id); else Selected.Remove(k.Id);
        }
        else
        {
            Selected.Clear();
            Selected.Add(k.Id);
            _dragAdd = true;
        }
        _dragging = true;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_arrange)
        {
            if (_moveDev != null && _frozenGeo != null)
            {
                float s = _frozenGeo.S;
                float dx = (float)Math.Round((e.X - _moveStart.X) / s * 20) / 20;
                float dy = (float)Math.Round((e.Y - _moveStart.Y) / s * 20) / 20;
                if (dx != 0 || dy != 0) _moved = true;
                foreach (var (dk, o) in _moveOrig)
                {
                    dk.X = o.X + dx;
                    dk.Y = o.Y + dy;
                    dk.DevX = o.DX + dx;
                    dk.DevY = o.DY + dy;
                }
                InvalidateStatic();
            }
            else Cursor = DeviceAt(e.Location) != null ? Cursors.SizeAll : Cursors.Default;
            return;
        }
        Cursor = _mHits.Any(h => h.Rect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
        if (!_dragging) return;
        var k = HitTest(e.Location);
        if (k == null) return;
        bool changed = _dragAdd ? Selected.Add(k.Id) : Selected.Remove(k.Id);
        if (changed)
        {
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        if (_moveDev != null)
        {
            string dev = _moveDev;
            var first = _moveOrig.Keys.FirstOrDefault();
            _moveDev = null;
            _frozenGeo = null;
            _moveOrig.Clear();
            Capture = false;
            InvalidateStatic();
            if (_moved && first != null) DeviceMoved?.Invoke(dev, first.DevX, first.DevY);
        }
    }
}

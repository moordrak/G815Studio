using System.Drawing.Drawing2D;

namespace G815Studio;

/// <summary>Crta tipkovnicu s bojama uživo; klik / povlačenje za odabir tipki (Ctrl dodaje u odabir).</summary>
public sealed class KeyboardView : Control
{
    private List<KeyDef> _keys = new();
    private bool _dragging;
    private bool _dragAdd;

    public HashSet<string> Selected { get; } = new();
    public Func<KeyDef, Color> ColorOf { get; set; }
    public event EventHandler SelectionChanged;

    public KeyboardView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(22, 22, 26);
        MinimumSize = new Size(400, 140);
    }

    public void SetKeys(List<KeyDef> keys)
    {
        _keys = keys ?? new List<KeyDef>();
        Selected.RemoveWhere(id => _keys.All(k => k.Id != id));
        Invalidate();
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

    private (float s, float ox, float oy) Geometry()
    {
        if (_keys.Count == 0) return (1, 0, 0);
        float tw = _keys.Max(k => k.X + k.W);
        float th = _keys.Max(k => k.Y + k.H);
        float s = Math.Max(1, Math.Min((Width - 16) / tw, (Height - 16) / th));
        return (s, (Width - tw * s) / 2, (Height - th * s) / 2);
    }

    private static RectangleF RectOf(KeyDef k, (float s, float ox, float oy) g) =>
        new(g.ox + k.X * g.s + 2, g.oy + k.Y * g.s + 2, k.W * g.s - 4, k.H * g.s - 4);

    private KeyDef HitTest(Point p)
    {
        var g = Geometry();
        return _keys.LastOrDefault(k => RectOf(k, g).Contains(p));
    }

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

    protected override void OnPaint(PaintEventArgs e)
    {
        var gr = e.Graphics;
        gr.SmoothingMode = SmoothingMode.AntiAlias;
        gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var g = Geometry();
        float fontPx = Math.Clamp(g.s * 0.24f, 7, 16);
        using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var capBrush = new SolidBrush(Color.FromArgb(44, 44, 50));
        using var selPen = new Pen(Color.Gold, 2.5f);

        foreach (var k in _keys)
        {
            var r = RectOf(k, g);
            using (var cap = Rounded(r, g.s * 0.12f)) gr.FillPath(capBrush, cap);

            var c = ColorOf?.Invoke(k) ?? Color.Black;
            bool dark = c.R + c.G + c.B < 24;
            var inner = RectangleF.Inflate(r, -g.s * 0.10f, -g.s * 0.10f);
            using (var light = Rounded(inner, g.s * 0.08f))
            using (var b = new SolidBrush(dark ? Color.FromArgb(30, 30, 34) : c))
                gr.FillPath(b, light);

            if (!string.IsNullOrEmpty(k.Label))
            {
                var textColor = dark ? Color.FromArgb(150, 150, 160) : ColorUtil.IsLight(c) ? Color.Black : Color.White;
                using var tb = new SolidBrush(textColor);
                gr.DrawString(k.Label, font, tb, r, sf);
            }

            if (Selected.Contains(k.Id))
                using (var sel = Rounded(r, g.s * 0.12f)) gr.DrawPath(selPen, sel);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        bool ctrl = (ModifierKeys & System.Windows.Forms.Keys.Control) != 0;
        var k = HitTest(e.Location);
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
    }
}

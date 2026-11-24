using System.Diagnostics;

namespace G815Studio;

public sealed class MainForm : Form
{
    private readonly AppController _c;
    private bool _loading;

    // zajedničko
    private ComboBox _profileCombo;
    private ToolStripStatusLabel _stDevice, _stProfile, _stBank, _stInfo;
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 33 };
    private readonly Color[] _snapshot = new Color[256];
    private int _tick;

    // okvir (bočni izbornik, zaglavlje)
    private readonly List<(NavButton Nav, Control Page, string Title, string Subtitle)> _pages = new();
    private Panel _host;
    private Label _title, _subtitle, _pill;
    private readonly Button[] _sideBank = new Button[3];
    private readonly List<EffectTile> _tiles = new();

    // osvjetljenje
    private KeyboardView _kbView;
    internal KeyboardView KeyboardViewForScreenshots => _kbView;
    private ComboBox _effectCombo, _dirCombo;
    private TrackBar _speed, _bright;
    private Button _c1Btn, _c2Btn, _bgBtn, _brushBtn;
    private Color _brush = Color.FromArgb(255, 40, 40);
    private Label _effectHelp, _selInfo, _colorsTitle, _colorsNote, _speedLbl, _dirLbl;
    private CheckBox _rainbowChk;

    // makroi
    private readonly RadioButton[] _bankRb = new RadioButton[3];
    private ListBox _gList, _steps;
    private TextBox _macroName, _recSink;
    private ComboBox _modeCombo;
    private Button _recBtn;
    private CheckBox _recDelays;
    private Label _recInfo;
    private int _editBank = 1;

    // profili
    private ListBox _profList, _appList;
    private CheckBox _autoSwitch;
    private Label _effLabel;

    // postavke
    private CheckBox _autostart, _zoneTest;
    private ComboBox _layoutCombo, _zoneKey;
    private NumericUpDown _fps, _zoneNum;
    private Label _devInfo;
    private CheckBox _zoneAuto;
    private readonly System.Windows.Forms.Timer _zoneTimer = new() { Interval = 1000 };

    // koje boje i postavke koristi pojedini efekt (null = ne koristi)
    private static readonly Dictionary<EffectType, (string C1, string C2, string Bg, bool RainbowOpt, bool Speed, bool Dir, string Note)> Slots = new()
    {
        [EffectType.Static] = ("Zadana boja tipki", null, null, false, false, false, "Boje pojedinih tipki mijenjaš kistom."),
        [EffectType.Breathing] = ("Zadana boja tipki", null, null, false, true, false, "Dišu boje po tipkama; kistom obojene tipke zadržavaju svoju boju."),
        [EffectType.Wave] = ("Boja 1", "Boja 2", null, true, true, true, null),
        [EffectType.ColorCycle] = ("Boja 1", "Boja 2", null, true, true, false, null),
        [EffectType.Reactive] = ("Osnovna boja", "Boja pritiska", null, false, true, false, null),
        [EffectType.Ripple] = ("Osnovna boja", "Boja vala", null, false, true, false, null),
        [EffectType.Audio] = ("Sredina (tiho)", "Rubovi (glasno)", "Pozadina", false, true, false, "Brzina = koliko brzo razina pada."),
        [EffectType.Screen] = (null, null, null, false, false, false, "Boje dolaze sa zaslona."),
        [EffectType.Off] = (null, null, null, false, false, false, "Sve tipke su ugašene."),
        [EffectType.Spectrum] = ("Dno stupca", "Vrh stupca", "Pozadina", false, true, false, "Brzina = koliko brzo stupci padaju."),
        [EffectType.BassPulse] = ("Udarac 1", "Udarac 2", "Pozadina", false, true, false, "Brzina = koliko brzo bljesak nestaje."),
        [EffectType.Swirl] = ("Boja 1", "Boja 2", null, true, true, false, null),
        [EffectType.RadialRainbow] = ("Boja 1", "Boja 2", null, true, true, false, null),
        [EffectType.GradientWave] = ("Boja A", "Boja B", null, false, true, true, null),
        [EffectType.Starry] = ("Zvijezda 1", "Zvijezda 2", "Nebo", false, true, false, null),
        [EffectType.Rain] = ("Kapi", "Vrh kapi", "Pozadina", false, true, false, null),
        [EffectType.Fire] = ("Plamen", "Vrhovi plamena", "Pozadina", false, true, false, null),
        [EffectType.Visor] = ("Traka", "Trag", "Pozadina", false, true, false, null),
        [EffectType.Lightning] = ("Munja", "Odsjaj", "Pozadina", false, true, false, null),
        [EffectType.Mosaic] = ("Boja 1", "Boja 2", null, true, true, false, "Uključena duga = nasumične boje; isključena = nijanse između Boje 1 i Boje 2."),
    };

    private static readonly (EffectType Type, string Name, string Help)[] Effects =
    {
        (EffectType.Static, "Statično (boje po tipkama)", "Svaka tipka ima svoju boju (Boja 1 je zadana). Oboji tipke dolje."),
        (EffectType.Breathing, "Disanje", "Boje po tipkama polako se pale i gase. Brzina = tempo."),
        (EffectType.Wave, "Duga – val", "Dugin val preko tipkovnice. Odaberi smjer i brzinu."),
        (EffectType.ColorCycle, "Izmjena boja", "Cijela tipkovnica mijenja boju kroz spektar."),
        (EffectType.Reactive, "Reakcija na pritisak", "Pritisnuta tipka zasvijetli Bojom 2 i polako se vrati na svoju boju."),
        (EffectType.Ripple, "Valovi od pritiska", "Svaki pritisak šalje krug Boje 2 preko tipkovnice."),
        (EffectType.Audio, "Zvuk (VU metar)", "Glasnoća zvuka sa zvučnika: od sredine prema rubovima, prijelaz Boja 1 → Boja 2, ostalo Pozadina."),
        (EffectType.Screen, "Boje sa zaslona", "Tipke preuzimaju boje odgovarajućeg dijela glavnog zaslona."),
        (EffectType.Off, "Isključeno", "Sve tipke ugašene."),
        (EffectType.Spectrum, "Spektar zvuka", "Stupci prate frekvencije glazbe (lijevo bas, desno visoki tonovi). Boja 1 dolje → Boja 2 gore, ostalo Pozadina."),
        (EffectType.BassPulse, "Bas puls", "Na svaki udarac basa tipkovnica bljesne, naizmjence Bojom 1 i Bojom 2. Brzina = koliko brzo se gasi."),
        (EffectType.Swirl, "Spiralna duga", "Duga se okreće oko središta tipkovnice."),
        (EffectType.RadialRainbow, "Radijalna duga", "Dugini krugovi šire se iz središta."),
        (EffectType.GradientWave, "Gradijentni val", "Val koji prelijeva Boju 1 u Boju 2. Odaberi smjer i brzinu."),
        (EffectType.Starry, "Zvjezdano nebo", "Nasumične tipke polako zasvijetle (između Boje 1 i Boje 2) na Pozadini."),
        (EffectType.Rain, "Kiša / Matrix", "Kapi Boje 1 padaju niz tipkovnicu i ostavljaju trag."),
        (EffectType.Fire, "Vatra", "Plamen se diže od donjeg reda prema gore."),
        (EffectType.Visor, "Skener", "Traka Boje 1 putuje lijevo-desno i ostavlja trag, na Pozadini."),
        (EffectType.Lightning, "Munja", "Nasumični bljeskovi Boje 1 preko cijele tipkovnice."),
        (EffectType.Mosaic, "Mozaik", "Svaka tipka povremeno glatko prijeđe u novu nasumičnu boju.")
    };

    public MainForm(AppController controller)
    {
        _c = controller;
        Text = "G815 Studio";
        Icon = IconFactory.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1440, 940);
        MinimumSize = new Size(1080, 700);
        Font = Theme.UiFont();
        BackColor = Theme.Ground;
        ForeColor = Theme.Text;
        KeyPreview = true;

        BuildLayout();
        Theme.Apply(this);

        _c.StateChanged += OnStateChanged;
        _c.MacrosChanged += OnMacrosChanged;
        _c.Recorder.StepsChanged += OnRecorderSteps;
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();

        ReloadAll();
        UpdateRecordingUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _zoneTimer.Stop();
        _zoneTimer.Dispose();
        _c.StateChanged -= OnStateChanged;
        _c.MacrosChanged -= OnMacrosChanged;
        _c.Recorder.StepsChanged -= OnRecorderSteps;
        if (_c.Recorder.IsRecording && !_c.HardwareRecording) _c.StopUiRecording();
        _c.Engine.TestZone = -1;
        base.OnFormClosed(e);
    }

    /// <summary>Dok traje snimanje iz sučelja, tipke ne smiju aktivirati gumbe u ovom prozoru.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_c.Recorder.IsRecording) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Profile P => _c.ManualProfile;

    private static Button Primary(Button b)
    {
        b.Tag = "primary";
        return b;
    }

    // ================================================================= izgled

    private void BuildLayout()
    {
        // ---- bočni izbornik
        var side = new Panel { Dock = DockStyle.Left, Width = 240, BackColor = Theme.Sidebar, Padding = new Padding(16, 22, 16, 18) };
        side.Paint += (_, e) =>
        {
            using var p = new Pen(Theme.BorderSoft);
            e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height);
        };
        var brand = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Sidebar };
        brand.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(4, 6, 38, 38), 10))
            using (var b = new SolidBrush(Color.FromArgb(27, 31, 39)))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }
            Color[] cols = { Color.FromArgb(255, 77, 109), Color.FromArgb(255, 176, 32), Theme.Good, Theme.Accent, Color.FromArgb(160, 123, 255), Color.FromArgb(255, 77, 109) };
            for (int i = 0; i < 6; i++)
                using (var cb = new SolidBrush(cols[i]))
                    g.FillRectangle(cb, 11 + (i % 3) * 8.5f, 16 + (i / 3) * 9f, 6.5f, 7f);
            using var f1 = Theme.Heading(13f);
            using var f2 = Theme.UiFont(8.5f);
            TextRenderer.DrawText(g, "G815 Studio", f1, new Point(52, 8), Theme.Text);
            TextRenderer.DrawText(g, "v" + typeof(MainForm).Assembly.GetName().Version?.ToString(3), f2, new Point(54, 30), Theme.Muted);
        };

        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Sidebar, Padding = new Padding(0, 10, 0, 0) };

        var bankCard = new Panel { Dock = DockStyle.Bottom, Height = 92, BackColor = Theme.Sidebar };
        bankCard.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, bankCard.Width - 1.5f, bankCard.Height - 1.5f), 12);
            using var b = new SolidBrush(Color.FromArgb(22, 26, 33));
            using var pen = new Pen(Theme.BorderSoft);
            g.FillPath(b, path);
            g.DrawPath(pen, path);
            using var f = Theme.UiFont(8f, FontStyle.Bold);
            TextRenderer.DrawText(g, "AKTIVNA BANKA", f, new Point(14, 12), Theme.Muted);
        };
        for (int i = 0; i < 3; i++)
        {
            int bank = i + 1;
            var btn = new Button { Text = "M" + bank, Size = new Size(58, 36), Location = new Point(14 + i * 64, 40), TabStop = true };
            btn.Click += (_, _) => _c.SetBank(bank);
            _sideBank[i] = btn;
            bankCard.Controls.Add(btn);
        }

        side.Controls.Add(nav);
        side.Controls.Add(brand);
        side.Controls.Add(bankCard);

        // ---- zaglavlje
        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Ground, Padding = new Padding(32, 24, 32, 8) };
        _title = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 20f), ForeColor = Theme.Text, Location = new Point(30, 16) };
        _subtitle = new Label { AutoSize = true, ForeColor = Theme.Muted, Location = new Point(34, 56) };
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Ground, Padding = new Padding(0, 6, 0, 0) };
        right.Controls.Add(new Label { Text = "Profil", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 12, 8, 0) });
        _profileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(0, 7, 14, 0) };
        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _profileCombo.SelectedItem is not string name) return;
            _c.SetActiveProfile(name);
            ReloadAll();
        };
        right.Controls.Add(_profileCombo);
        _pill = new Label { AutoSize = false, Size = new Size(250, 38), TextAlign = ContentAlignment.MiddleCenter, Font = Theme.UiFont(9.5f, FontStyle.Bold), Margin = new Padding(0, 2, 0, 0) };
        _pill.Paint += PaintPill;
        right.Controls.Add(_pill);
        header.Controls.Add(right);
        header.Controls.Add(_title);
        header.Controls.Add(_subtitle);

        // ---- stranice
        _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Ground, Padding = new Padding(24, 0, 24, 12) };
        AddPage(nav, "Osvjetljenje", "\uE706", "Klikni ili povuci preko tipki za odabir, zatim ih oboji kistom.", BuildLightingTab());
        AddPage(nav, "Makroi", "\uE765", "G1–G5 u tri banke. Banku na tipkovnici biraš tipkama M1, M2 i M3.", BuildMacroTab());
        AddPage(nav, "Profili", "\uE8FD", "Svaki profil ima svoje boje i makroe; može se paliti sam prema aplikaciji.", BuildProfilesTab());
        AddPage(nav, "Postavke", "\uE713", "Pokretanje, raspored, uređaj i ispravak zona.", BuildSettingsTab());

        // ---- statusna traka
        var status = new StatusStrip { BackColor = Theme.Sidebar, ForeColor = Theme.TextSoft, Renderer = new DarkRenderer(), SizingGrip = false };
        _stDevice = new ToolStripStatusLabel { AutoSize = true };
        _stProfile = new ToolStripStatusLabel { AutoSize = true, Margin = new Padding(16, 3, 0, 2) };
        _stBank = new ToolStripStatusLabel { AutoSize = true, Margin = new Padding(16, 3, 0, 2) };
        _stInfo = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        status.Items.AddRange(new ToolStripItem[] { _stDevice, _stProfile, _stBank, _stInfo });

        Controls.Add(_host);
        Controls.Add(header);
        Controls.Add(side);
        Controls.Add(status);
        ShowPage(0);
    }

    private void AddPage(FlowLayoutPanel nav, string title, string glyph, string subtitle, Control page)
    {
        var btn = new NavButton(title, glyph) { Width = 206 };
        int index = _pages.Count;
        btn.Click += (_, _) => ShowPage(index);
        nav.Controls.Add(btn);
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _host.Controls.Add(page);
        _pages.Add((btn, page, title, subtitle));
    }

    internal void ShowPage(int index)
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            var (nav, page, _, _) = _pages[i];
            nav.Active = i == index;
            nav.Invalidate();
            page.Visible = i == index;
        }
        _title.Text = _pages[index].Title;
        _subtitle.Text = _pages[index].Subtitle;
    }

    private void PaintPill(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Ground);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        bool ok = _c.Keyboard.IsConnected;
        var back = ok ? Color.FromArgb(18, 38, 28) : Color.FromArgb(40, 20, 24);
        var border = ok ? Color.FromArgb(31, 74, 51) : Color.FromArgb(90, 40, 48);
        var fore = ok ? Color.FromArgb(158, 230, 189) : Color.FromArgb(255, 150, 160);
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, _pill.Width - 1.5f, _pill.Height - 1.5f), 10))
        using (var b = new SolidBrush(back))
        using (var p = new Pen(border))
        {
            g.FillPath(b, path);
            g.DrawPath(p, path);
        }
        using (var dot = new SolidBrush(ok ? Theme.Good : Theme.Danger))
            g.FillEllipse(dot, 14, _pill.Height / 2 - 4, 8, 8);
        string text = ok
            ? $"G815 spojena" + (_c.Engine.MeasuredFps > 0 ? $" · {_c.Engine.MeasuredFps:0} FPS" : "")
            : "Tipkovnica nije spojena";
        TextRenderer.DrawText(g, text, _pill.Font, new Rectangle(28, 0, _pill.Width - 34, _pill.Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private Control BuildLightingTab()
    {
        var page = new Panel { Padding = new Padding(0), BackColor = Theme.Ground };
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 44));

        _kbView = new KeyboardView { Dock = DockStyle.Fill, ColorOf = k => _snapshot[k.Zone], Margin = new Padding(0, 0, 0, 8) };
        _kbView.SelectionChanged += (_, _) => UpdateSelInfo();
        _kbView.BankClicked += bank => _c.SetBank(bank);
        split.Controls.Add(_kbView, 0, 0);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };

        // --- efekt
        var fx = Ui.Flow(FlowDirection.TopDown);
        _effectCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Visible = false };
        foreach (var e in Effects) _effectCombo.Items.Add(e.Name);
        _effectCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_effectCombo.SelectedIndex < 0) return;
            _effectHelp.Text = Effects[_effectCombo.SelectedIndex].Help;
            _dirCombo.Enabled = Effects[_effectCombo.SelectedIndex].Type is EffectType.Wave or EffectType.GradientWave;
            for (int t = 0; t < _tiles.Count; t++) { _tiles[t].Selected = t == _effectCombo.SelectedIndex; _tiles[t].Invalidate(); }
            if (_loading) return;
            P.Lighting.Effect = Effects[_effectCombo.SelectedIndex].Type;
            LoadEffectParams();
            _c.LightingChanged(P);
        };
        _effectHelp = new Label { AutoSize = false, Width = 530, Height = 40, ForeColor = SystemColors.GrayText };
        var tileGrid = new FlowLayoutPanel { AutoSize = true, Width = 540, MaximumSize = new Size(540, 0), WrapContents = true, Margin = new Padding(0) };
        Color H(string h) => ColorTranslator.FromHtml(h);
        var previews = new[]
        {
            new[] { H("#ff3b5c"), H("#ff3b5c"), H("#34d0ff"), H("#34d0ff") },
            new[] { H("#0b2a3a"), H("#34d0ff"), H("#0b2a3a") },
            new[] { H("#34d0ff"), H("#8b7bff"), H("#ff3b5c"), H("#ff9f1a"), H("#ffe14d"), H("#3ddc84") },
            new[] { H("#8b7bff"), H("#ff3b5c") },
            new[] { H("#34d0ff"), H("#ff3b6b"), H("#34d0ff"), H("#34d0ff") },
            new[] { H("#34d0ff"), H("#ff3b6b"), H("#34d0ff"), H("#ff3b6b"), H("#34d0ff") },
            new[] { H("#22262e"), H("#ff4d6d"), H("#3ddc84"), H("#3ddc84"), H("#ff4d6d"), H("#22262e") },
            new[] { H("#1e3a5f"), H("#c47b3a"), H("#2f6b4f") },
            new[] { H("#22262e") },
            new[] { H("#3ddc84"), H("#3ddc84"), H("#ffe14d"), H("#ff4d6d"), H("#22262e") },
            new[] { H("#22262e"), H("#ff3b6b"), H("#ff3b6b"), H("#22262e") },
            new[] { H("#ff3b5c"), H("#ffe14d"), H("#3ddc84"), H("#34d0ff"), H("#8b7bff"), H("#ff3b5c") },
            new[] { H("#8b7bff"), H("#34d0ff"), H("#3ddc84"), H("#34d0ff"), H("#8b7bff") },
            new[] { H("#34d0ff"), H("#ff3b6b"), H("#34d0ff") },
            new[] { H("#0b0d12"), H("#ffffff"), H("#0b0d12"), H("#8b7bff"), H("#0b0d12") },
            new[] { H("#0b0d12"), H("#3ddc84"), H("#0b0d12"), H("#3ddc84"), H("#0b0d12") },
            new[] { H("#5a0000"), H("#ff1e00"), H("#ff8200"), H("#ffdc3c") },
            new[] { H("#0b0d12"), H("#0b0d12"), H("#ff3b5c"), H("#0b0d12") },
            new[] { H("#0b0d12"), H("#ffffff"), H("#0b0d12") },
            new[] { H("#ff3b5c"), H("#3ddc84"), H("#ffe14d"), H("#8b7bff"), H("#34d0ff") }
        };
        string[] shortNames = { "Statično", "Disanje", "Val duge", "Izmjena boja", "Reakcija", "Valovi", "Zvuk (VU)", "Zaslon", "Isključeno",
                                "Spektar", "Bas puls", "Spirala", "Radijalna", "Gradijent", "Zvijezde", "Kiša", "Vatra", "Skener", "Munja", "Mozaik" };
        for (int i = 0; i < Effects.Length; i++)
        {
            int idx = i;
            var tile = new EffectTile(shortNames[i], previews[i]) { Size = new Size(124, 56) };
            tile.Click += (_, _) => _effectCombo.SelectedIndex = idx;
            _tiles.Add(tile);
            tileGrid.Controls.Add(tile);
        }
        fx.Controls.Add(tileGrid);
        fx.Controls.Add(_effectCombo);
        fx.Controls.Add(_effectHelp);
        var param = Ui.Flow(FlowDirection.TopDown);

        param.Controls.Add(Ui.Lbl("Brzina"));
        _speed = new TrackBar { Minimum = 1, Maximum = 100, TickFrequency = 10, Width = 280, TickStyle = TickStyle.None };
        _speed.ValueChanged += (_, _) => { if (_loading) return; P.Lighting.Current.Speed = _speed.Value; _c.LightingChanged(P); };
        param.Controls.Add(_speed);

        param.Controls.Add(Ui.Lbl("Svjetlina"));
        _bright = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 280, TickStyle = TickStyle.None };
        _bright.ValueChanged += (_, _) => { if (_loading) return; P.Lighting.Brightness = _bright.Value; _c.LightingChanged(P); };
        param.Controls.Add(_bright);

        param.Controls.Add(Ui.Lbl("Smjer vala"));
        _dirCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        _dirCombo.Items.AddRange(new object[] { "Lijevo → desno", "Desno → lijevo", "Gore → dolje", "Iz središta" });
        _dirCombo.SelectedIndexChanged += (_, _) => { if (_loading) return; P.Lighting.Current.Direction = _dirCombo.SelectedIndex; _c.LightingChanged(P); };
        param.Controls.Add(_dirCombo);
        controls.Controls.Add(Ui.Group("Efekt", fx));
        controls.Controls.Add(Ui.Group("Parametri", param));

        // --- boje efekta
        var cols = Ui.Flow(FlowDirection.TopDown);
        _colorsTitle = new Label { AutoSize = true, Font = Theme.UiFont(10f, FontStyle.Bold), ForeColor = Theme.Text, Margin = new Padding(3, 0, 3, 8) };
        _rainbowChk = new CheckBox { Text = "Duga (isključi za vlastite boje)", AutoSize = true, Margin = new Padding(3, 2, 3, 8) };
        _rainbowChk.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            P.Lighting.Current.Rainbow = _rainbowChk.Checked;
            UpdateColorButtons();
            _c.LightingChanged(P);
        };
        _c1Btn = Ui.Btn("Boja 1", (_, _) => PickConfigColor(1), 250);
        _c2Btn = Ui.Btn("Boja 2", (_, _) => PickConfigColor(2), 250);
        _bgBtn = Ui.Btn("Pozadina", (_, _) => PickConfigColor(3), 250);
        _colorsNote = new Label { AutoSize = true, MaximumSize = new Size(250, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 0) };
        cols.Controls.Add(_colorsTitle);
        cols.Controls.Add(_rainbowChk);
        cols.Controls.Add(_c1Btn);
        cols.Controls.Add(_c2Btn);
        cols.Controls.Add(_bgBtn);
        cols.Controls.Add(_colorsNote);
        controls.Controls.Add(Ui.Group("Boje efekta", cols));

        // --- bojanje tipki
        var paint = Ui.Flow(FlowDirection.TopDown);
        _brushBtn = Ui.Btn("Kist: odaberi boju…", (_, _) =>
        {
            var c = Ui.PickColor(this, _brush);
            if (c.HasValue) { _brush = c.Value; Ui.PaintSwatch(_brushBtn, _brush); }
        }, 250);
        Ui.PaintSwatch(_brushBtn, _brush);
        paint.Controls.Add(_brushBtn);
        paint.Controls.Add(Primary(Ui.Btn("Oboji odabrane tipke", (_, _) => PaintSelected(_brush), 250)));
        paint.Controls.Add(Ui.Btn("Vrati odabrane na Boju 1", (_, _) => PaintSelected(null), 250));
        paint.Controls.Add(Ui.Btn("Oboji SVE tipke kistom", (_, _) =>
        {
            foreach (var k in _c.Keys) P.Lighting.KeyColors[k.Id] = ColorUtil.ToHex(_brush);
            _c.LightingChanged(P);
        }, 250));
        paint.Controls.Add(Ui.Btn("Obriši sve boje po tipkama", (_, _) =>
        {
            P.Lighting.KeyColors.Clear();
            _c.LightingChanged(P);
        }, 250));
        _selInfo = new Label { AutoSize = false, Width = 250, Height = 36, ForeColor = SystemColors.GrayText };
        paint.Controls.Add(_selInfo);
        controls.Controls.Add(Ui.Group("Boje po tipkama", paint));

        // --- brzi odabir
        var quick = Ui.Flow(FlowDirection.TopDown);
        quick.Controls.Add(Ui.Lbl("Klik = jedna tipka, povlačenje = više,\nCtrl+klik = dodaj/makni."));
        void Q(string text, Func<KeyDef, bool> pred) =>
            quick.Controls.Add(Ui.Btn(text, (_, _) => _kbView.SelectWhere(pred, (ModifierKeys & Keys.Control) != 0), 200));
        Q("Sve", _ => true);
        Q("Slova", k => k.Group == "letters");
        Q("Brojevi", k => k.Group == "num");
        Q("F tipke + Esc", k => k.Group == "f");
        Q("WASD", k => k.Id is "U1A" or "U04" or "U16" or "U07");
        Q("Strelice", k => k.Group == "arrows");
        Q("Numerička", k => k.Group == "numpad");
        Q("Navigacija", k => k.Group == "nav");
        Q("Modifikatori", k => k.Group == "mods");
        Q("G tipke + logo", k => k.Group is "g" or "logo");
        Q("Mediji i gornje tipke", k => k.Group is "media" or "top");
        quick.Controls.Add(Ui.Btn("Poništi odabir", (_, _) => _kbView.ClearSelection(), 200));
        controls.Controls.Add(Ui.Group("Brzi odabir (Ctrl = dodaj)", quick));

        split.Controls.Add(controls, 0, 1);
        page.Controls.Add(split);
        return page;
    }

    private Control BuildMacroTab()
    {
        var page = new Panel { Padding = new Padding(8), BackColor = Theme.Ground };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));

        // lijevo: banka + G tipke
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var banks = Ui.Flow();
        banks.Controls.Add(Ui.Lbl("Banka:", true));
        for (int i = 0; i < 3; i++)
        {
            int bank = i + 1;
            _bankRb[i] = new RadioButton { Text = "M" + bank, AutoSize = true, Margin = new Padding(6, 6, 6, 3) };
            _bankRb[i].CheckedChanged += (s, _) =>
            {
                if (!((RadioButton)s).Checked) return;
                _editBank = bank;
                RefreshGList();
            };
            banks.Controls.Add(_bankRb[i]);
        }
        left.Controls.Add(banks, 0, 0);
        left.Controls.Add(Ui.Lbl("Tipke (banka se na tipkovnici bira s M1/M2/M3):"), 0, 1);
        _gList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10.5f) };
        _gList.SelectedIndexChanged += (_, _) => LoadMacro();
        left.Controls.Add(_gList, 0, 2);
        left.Controls.Add(new Label
        {
            AutoSize = false, Dock = DockStyle.Fill, Height = 90, ForeColor = SystemColors.GrayText,
            Text = "Snimanje s tipkovnice: pritisni MR, utipkaj slijed, pa pritisni G tipku na koju ga želiš spremiti. Ponovni MR odustaje."
        }, 0, 3);
        table.Controls.Add(left, 0, 0);

        // sredina: uređivanje makroa
        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(8, 0, 8, 0) };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        mid.Controls.Add(Ui.Lbl("Naziv:"), 0, 0);
        _macroName = new TextBox { Dock = DockStyle.Fill };
        _macroName.TextChanged += (_, _) =>
        {
            if (_loading || CurrentG == 0) return;
            Cur(true).Name = _macroName.Text;
            _c.SaveSoon();
            RefreshGListLabels();
        };
        mid.Controls.Add(_macroName, 1, 0);

        mid.Controls.Add(Ui.Lbl("Način:"), 0, 1);
        _modeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
        _modeCombo.Items.AddRange(new object[] { "Jednom po pritisku", "Ponavljaj dok je tipka držana", "Uključi / isključi ponavljanje" });
        _modeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || CurrentG == 0 || _modeCombo.SelectedIndex < 0) return;
            Cur(true).Mode = (PlayMode)_modeCombo.SelectedIndex;
            _c.SaveSoon();
        };
        mid.Controls.Add(_modeCombo, 1, 1);

        _steps = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Consolas", 10f), SelectionMode = SelectionMode.MultiExtended };
        _steps.DoubleClick += (_, _) => EditStep();
        mid.Controls.Add(_steps, 0, 2);
        mid.SetColumnSpan(_steps, 2);

        _recInfo = new Label { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(3, 6, 3, 3) };
        mid.Controls.Add(_recInfo, 0, 3);
        mid.SetColumnSpan(_recInfo, 2);
        _recSink = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Text = "(ovdje je fokus tijekom snimanja)", TabStop = false };
        mid.Controls.Add(_recSink, 0, 4);
        mid.SetColumnSpan(_recSink, 2);
        table.Controls.Add(mid, 1, 0);

        // desno: akcije
        var right = Ui.Flow(FlowDirection.TopDown);
        _recBtn = Ui.Btn("● Snimi", (_, _) => ToggleUiRecording(), 210);
        right.Controls.Add(_recBtn);
        _recDelays = new CheckBox { Text = "Snimaj kašnjenja", AutoSize = true, Margin = new Padding(6, 2, 3, 10) };
        _recDelays.CheckedChanged += (_, _) => { if (_loading) return; _c.Settings.RecordDelays = _recDelays.Checked; _c.SaveSoon(); };
        right.Controls.Add(_recDelays);
        right.Controls.Add(Ui.Btn("+ Tipka / kombinacija…", (_, _) => AddKeyStep(), 210));
        right.Controls.Add(Ui.Btn("+ Kašnjenje…", (_, _) => AddDelayStep(), 210));
        right.Controls.Add(Ui.Btn("+ Upiši tekst…", (_, _) => AddTextStep(), 210));
        right.Controls.Add(Ui.Btn("+ Pokreni program…", (_, _) => AddRunStep(), 210));
        right.Controls.Add(Ui.Btn("+ HTTP poziv (URL)…", (_, _) => AddHttpStep(), 210));
        right.Controls.Add(Ui.Btn("+ Unos vrijednosti…", (_, _) => AddInputStep(), 210));
        right.Controls.Add(new Label { Height = 10 });
        right.Controls.Add(Ui.Btn("Uredi korak", (_, _) => EditStep(), 210));
        right.Controls.Add(Ui.Btn("Gore", (_, _) => MoveStep(-1), 210));
        right.Controls.Add(Ui.Btn("Dolje", (_, _) => MoveStep(1), 210));
        right.Controls.Add(Ui.Btn("Ukloni odabrane korake", (_, _) => RemoveSteps(), 210));
        right.Controls.Add(Ui.Btn("Očisti makro", (_, _) => ClearMacro(), 210));
        right.Controls.Add(new Label { Height = 10 });
        right.Controls.Add(Primary(Ui.Btn("▶ Testiraj (za 3 s)", async (_, _) => await TestMacro(), 210)));
        table.Controls.Add(right, 2, 0);

        page.Controls.Add(table);
        return page;
    }

    private Control BuildProfilesTab()
    {
        var page = new Panel { Padding = new Padding(8), BackColor = Theme.Ground };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(Ui.Lbl("Profili:", true), 0, 0);
        _profList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _profList.SelectedIndexChanged += (_, _) => RefreshApps();
        left.Controls.Add(_profList, 0, 1);
        var pb = Ui.Flow();
        pb.Controls.Add(Ui.Btn("Novi", (_, _) => NewProfile(false)));
        pb.Controls.Add(Ui.Btn("Dupliciraj", (_, _) => NewProfile(true)));
        pb.Controls.Add(Ui.Btn("Preimenuj", (_, _) => RenameProfile()));
        pb.Controls.Add(Ui.Btn("Obriši", (_, _) => DeleteProfile()));
        pb.Controls.Add(Ui.Btn("Koristi i uređuj ovaj", (_, _) =>
        {
            if (SelectedProfile is { } sp) { _c.SetActiveProfile(sp.Name); ReloadAll(); }
        }));
        left.Controls.Add(pb, 0, 2);
        table.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(10, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _autoSwitch = new CheckBox { Text = "Automatski mijenjaj profil prema aktivnoj aplikaciji", AutoSize = true };
        _autoSwitch.CheckedChanged += (_, _) => { if (_loading) return; _c.Settings.AutoSwitch = _autoSwitch.Checked; _c.SaveSoon(); };
        right.Controls.Add(_autoSwitch, 0, 0);
        _effLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 10) };
        right.Controls.Add(_effLabel, 0, 1);
        right.Controls.Add(Ui.Lbl("Aplikacije koje uključuju odabrani profil:", true), 0, 2);
        _appList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        right.Controls.Add(_appList, 0, 3);
        var ab = Ui.Flow();
        ab.Controls.Add(Ui.Btn("Dodaj iz pokrenutih…", (_, _) => AddAppFromRunning()));
        ab.Controls.Add(Ui.Btn("Dodaj .exe…", (_, _) => AddAppFromFile()));
        ab.Controls.Add(Ui.Btn("Ukloni", (_, _) => RemoveApp()));
        right.Controls.Add(ab, 0, 4);
        table.Controls.Add(right, 1, 0);

        page.Controls.Add(table);
        return page;
    }

    private Control BuildSettingsTab()
    {
        var page = new Panel { Padding = new Padding(8), AutoScroll = true, BackColor = Theme.Ground };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        var general = Ui.Flow(FlowDirection.TopDown);
        _autostart = new CheckBox { Text = "Pokreni s Windowsima (u trayu)", AutoSize = true };
        _autostart.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            try { Autostart.Set(_autostart.Checked); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Autostart", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        general.Controls.Add(_autostart);

        var row = Ui.Flow();
        row.Controls.Add(Ui.Lbl("Raspored tipkovnice:"));
        _layoutCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _layoutCombo.Items.AddRange(new object[] { "ISO (hrvatski / EU)", "ANSI (US)" });
        _layoutCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _c.Settings.Layout = _layoutCombo.SelectedIndex == 1 ? "ANSI" : "ISO";
            _c.RebuildKeys();
            _c.SaveSoon();
            ReloadKeys();
        };
        row.Controls.Add(_layoutCombo);
        row.Controls.Add(Ui.Lbl("   Osvježavanje (FPS):"));
        _fps = new NumericUpDown { Minimum = 5, Maximum = 60, Width = 60 };
        _fps.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _c.Settings.Fps = (int)_fps.Value;
            _c.Engine.Fps = _c.Settings.Fps;
            _c.SaveSoon();
        };
        row.Controls.Add(_fps);
        general.Controls.Add(row);
        flow.Controls.Add(Ui.Group("Općenito", general));

        var dev = Ui.Flow(FlowDirection.TopDown);
        _devInfo = new Label { AutoSize = true, MaximumSize = new Size(900, 0) };
        dev.Controls.Add(_devInfo);
        var devBtns = Ui.Flow();
        devBtns.Controls.Add(Ui.Btn("Ponovno spoji", (_, _) => _c.Reconnect()));
        devBtns.Controls.Add(Ui.Btn("Otvori mapu s postavkama i logom", (_, _) =>
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.Folder}\"") { UseShellExecute = true });
        }));
        dev.Controls.Add(devBtns);
        dev.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = SystemColors.GrayText,
            Text = "Logitech G HUB mora biti potpuno zatvoren (i iz traya) jer inače preuzima tipkovnicu. " +
                   "Kad se G815 Studio zatvori, tipkovnica se vraća na svoje ugrađene postavke."
        });
        flow.Controls.Add(Ui.Group("Uređaj", dev));

        var zones = Ui.Flow(FlowDirection.TopDown);
        zones.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(900, 0),
            Text = "Ako neka tipka ne svijetli ili svijetli kriva: uključi ispitivač, mijenjaj broj zone dok ne zasvijetli " +
                   "željena tipka (samo ona će svijetliti bijelo), odaberi tu tipku na popisu i klikni \"Pridruži\"."
        });
        var zr = Ui.Flow();
        _zoneTest = new CheckBox { Text = "Ispitivač zona uključen", AutoSize = true, Margin = new Padding(3, 7, 10, 3) };
        _zoneTest.CheckedChanged += (_, _) => _c.Engine.TestZone = _zoneTest.Checked ? (int)_zoneNum.Value : -1;
        zr.Controls.Add(_zoneTest);
        zr.Controls.Add(Ui.Lbl("Zona:"));
        _zoneNum = new NumericUpDown { Minimum = 0, Maximum = 255, Hexadecimal = true, Width = 70 };
        _zoneNum.ValueChanged += (_, _) => { if (_zoneTest.Checked) _c.Engine.TestZone = (int)_zoneNum.Value; };
        zr.Controls.Add(_zoneNum);
        zr.Controls.Add(Ui.Lbl("  Tipka:"));
        _zoneKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        zr.Controls.Add(_zoneKey);
        zr.Controls.Add(Ui.Btn("Pridruži", (_, _) =>
        {
            if (_zoneKey.SelectedItem is not KeyDef k) return;
            _c.Settings.ZoneOverrides[k.Id] = (int)_zoneNum.Value;
            _c.RebuildKeys();
            _c.SaveSoon();
            ReloadKeys();
        }));
        zr.Controls.Add(Ui.Btn("Vrati sve zone na zadano", (_, _) =>
        {
            _c.Settings.ZoneOverrides.Clear();
            _c.RebuildKeys();
            _c.SaveSoon();
            ReloadKeys();
        }));
        zones.Controls.Add(zr);

        // pomoćnik: prolaz kroz zone koje još nisu pridružene nijednoj tipki
        var zr2 = Ui.Flow();
        zr2.Controls.Add(Ui.Btn("◀ Prethodna slobodna", (_, _) => StepZone(-1)));
        zr2.Controls.Add(Ui.Btn("Sljedeća slobodna ▶", (_, _) => StepZone(1)));
        _zoneAuto = new CheckBox { Text = "Automatski prolaz (1 s po zoni)", AutoSize = true, Margin = new Padding(12, 9, 10, 3) };
        _zoneAuto.CheckedChanged += (_, _) =>
        {
            if (_zoneAuto.Checked) { _zoneTest.Checked = true; _zoneTimer.Start(); }
            else _zoneTimer.Stop();
        };
        zr2.Controls.Add(_zoneAuto);
        zones.Controls.Add(zr2);
        zones.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(900, 0), ForeColor = SystemColors.GrayText,
            Text = "Za medijske tipke, tipku za svjetlinu i tipku za igre: odaberi tipku na popisu, uključi automatski prolaz " +
                   "i kad ta tipka zasvijetli bijelo, isključi prolaz (po potrebi vrati se korak gumbom ◀) pa klikni \"Pridruži\"."
        });
        _zoneTimer.Tick += (_, _) => StepZone(1);
        flow.Controls.Add(Ui.Group("Ispitivač i ispravak zona", zones));

        page.Controls.Add(flow);
        return page;
    }

    // ================================================================= punjenje

    internal void ReloadAll()
    {
        _loading = true;
        try
        {
            _profileCombo.Items.Clear();
            foreach (var p in _c.Settings.Profiles) _profileCombo.Items.Add(p.Name);
            _profileCombo.SelectedItem = P.Name;

            var L = P.Lighting;
            int fi = Array.FindIndex(Effects, e => e.Type == L.Effect);
            _effectCombo.SelectedIndex = Math.Max(0, fi);
            _effectHelp.Text = Effects[_effectCombo.SelectedIndex].Help;
            _bright.Value = Math.Clamp(L.Brightness, _bright.Minimum, _bright.Maximum);
            LoadEffectParams();

            _recDelays.Checked = _c.Settings.RecordDelays;
            _bankRb[_editBank - 1].Checked = true;

            _autoSwitch.Checked = _c.Settings.AutoSwitch;
            _profList.Items.Clear();
            foreach (var p in _c.Settings.Profiles) _profList.Items.Add(p.Name);
            _profList.SelectedItem = P.Name;

            _autostart.Checked = Autostart.IsEnabled;
            _layoutCombo.SelectedIndex = _c.IsIso ? 0 : 1;
            _fps.Value = Math.Clamp(_c.Settings.Fps, 5, 60);
        }
        finally
        {
            _loading = false;
        }
        ReloadKeys();
        RefreshGList();
        RefreshApps();
        UpdateStatus();
    }

    private void ReloadKeys()
    {
        _kbView.SetKeys(_c.Keys);
        object sel = _zoneKey.SelectedItem;
        string selId = (sel as KeyDef)?.Id;
        _zoneKey.Items.Clear();
        foreach (var k in _c.Keys) _zoneKey.Items.Add(k);
        if (selId != null) _zoneKey.SelectedItem = _c.Keys.FirstOrDefault(k => k.Id == selId);
        UpdateSelInfo();
    }

    private void OnStateChanged()
    {
        if (IsDisposed) return;
        if ((_profileCombo.SelectedItem as string) != P.Name ||_profileCombo.Items.Count != _c.Settings.Profiles.Count)
            ReloadAll();
        else
            UpdateStatus();
        UpdateRecordingUi();
    }

    private void OnMacrosChanged()
    {
        if (IsDisposed) return;
        RefreshGList(keepSelection: true);
    }

    private void OnUiTick(object sender, EventArgs e)
    {
        _c.Engine.CopySnapshot(_snapshot);
        _kbView.Invalidate();
        if (++_tick % 30 == 0) UpdateStatus();
    }

    private void UpdateStatus()
    {
        var kb = _c.Keyboard;
        _stDevice.Text = kb.IsConnected ? "● " + kb.DeviceName : "○ Nije spojeno" + (string.IsNullOrEmpty(_c.LastConnectError) ? "" : " – " + _c.LastConnectError);
        _stProfile.Text = "Aktivni profil: " + _c.EffectiveProfile?.Name;
        _stBank.Text = "Banka: M" + _c.Bank + (_c.HardwareRecording ? "  ● SNIMANJE (MR)" : "");
        var eng = _c.Engine;
        _stInfo.Text = eng.LastError is { } err
            ? "Osvjetljenje: " + err
            : kb.IsConnected && eng.MeasuredFps > 0
                ? $"Osvjetljenje: {eng.MeasuredFps:0} FPS · {eng.PacketsPerFrame:0} paketa · {eng.SendMs:0.0} ms/okvir · zauzeto: {kb.BusyRetries}×"
                : "";

        _devInfo.Text = kb.IsConnected
            ? $"Spojeno: {kb.DeviceName}\nHID++ značajke: {kb.FeatureSummary}\nG tipke preusmjerene: {(kb.HasGKeys ? "da" : "ne")}"
            : "Nije spojeno. " + (_c.LastConnectError ?? "Tražim tipkovnicu…");

        _pill.Invalidate();
        for (int i = 0; i < 3; i++)
        {
            bool on = _c.Bank == i + 1;
            Theme.StyleFlat(_sideBank[i], on ? Theme.AccentBg : Theme.Sidebar, on ? Theme.Accent : Theme.Border);
            _sideBank[i].ForeColor = on ? Color.White : Theme.TextSoft;
            _sideBank[i].Font = Theme.UiFont(9.5f, FontStyle.Bold);
        }
        _kbView.Bank = _c.Bank;
        _kbView.Recording = _c.HardwareRecording;
        _stDevice.ForeColor = kb.IsConnected ? Theme.Good : Color.FromArgb(255, 138, 155);

        string fg = _c.ForegroundApp ?? "?";
        _effLabel.Text = $"Sada aktivan: {_c.EffectiveProfile?.Name}   (zadnja aplikacija u fokusu: {fg})";
    }

    /// <summary>Pomiče ispitivač na sljedeću zonu koju ne koristi nijedna druga tipka.</summary>
    private void StepZone(int dir)
    {
        var selected = _zoneKey.SelectedItem as KeyDef;
        var used = new HashSet<int>(_c.Keys.Where(k => k != selected).Select(k => (int)k.Zone));
        int z = (int)_zoneNum.Value;
        for (int i = 0; i < 256; i++)
        {
            z = (z + dir + 256) % 256;
            if (z != 0 && !used.Contains(z)) break;
        }
        _zoneNum.Value = z;
        if (!_zoneTest.Checked) _zoneTest.Checked = true;
    }

    private void UpdateSelInfo()
    {
        if (_selInfo == null) return;
        int n = _kbView.Selected.Count;
        _selInfo.Text = n == 0 ? "Nijedna tipka nije odabrana." : $"Odabrano tipki: {n}";
    }

    // ================================================================= osvjetljenje

    private void PickConfigColor(int which)
    {
        var ep = P.Lighting.Current;
        string cur = which == 1 ? ep.Color1 : which == 2 ? ep.Color2 : ep.Background;
        var c = Ui.PickColor(this, ColorUtil.Parse(cur));
        if (!c.HasValue) return;
        string hex = ColorUtil.ToHex(c.Value);
        if (which == 1) ep.Color1 = hex;
        else if (which == 2) ep.Color2 = hex;
        else ep.Background = hex;
        UpdateColorButtons();
        _c.LightingChanged(P);
    }

    /// <summary>Učitava boje, brzinu i smjer odabranog efekta u kontrole (svaki efekt ima svoje).</summary>
    private void LoadEffectParams()
    {
        var L = P.Lighting;
        var ep = L.Current;
        var slot = Slots.TryGetValue(L.Effect, out var sl) ? sl : ("Boja 1", "Boja 2", "Pozadina", false, true, false, null);
        bool was = _loading;
        _loading = true;
        try
        {
            string name = Effects.FirstOrDefault(e => e.Type == L.Effect).Name ?? L.Effect.ToString();
            _colorsTitle.Text = name;
            _rainbowChk.Visible = slot.RainbowOpt;
            _rainbowChk.Checked = ep.Rainbow;
            _speed.Value = Math.Clamp(ep.Speed, _speed.Minimum, _speed.Maximum);
            _speed.Enabled = slot.Speed;
            _dirCombo.SelectedIndex = Math.Clamp(ep.Direction, 0, 3);
            _dirCombo.Enabled = slot.Dir;
            _colorsNote.Text = slot.Note ?? "";
            _colorsNote.Visible = !string.IsNullOrEmpty(slot.Note);
        }
        finally
        {
            _loading = was;
        }
        UpdateColorButtons();
    }

    private void UpdateColorButtons()
    {
        var L = P.Lighting;
        var ep = L.Current;
        var slot = Slots.TryGetValue(L.Effect, out var sl) ? sl : ("Boja 1", "Boja 2", "Pozadina", false, true, false, null);
        bool custom = !slot.RainbowOpt || !ep.Rainbow;
        void Set(Button b, string label, string hex, bool enabled)
        {
            b.Visible = label != null;
            if (label == null) return;
            b.Text = label;
            b.Enabled = enabled;
            Ui.PaintSwatch(b, enabled ? ColorUtil.Parse(hex) : Theme.ControlBg);
            if (!enabled) b.ForeColor = Theme.Muted;
        }
        Set(_c1Btn, slot.C1, ep.Color1, custom);
        Set(_c2Btn, slot.C2, ep.Color2, custom);
        Set(_bgBtn, slot.Bg, ep.Background, true);
    }

    private void PaintSelected(Color? color)
    {
        if (_kbView.Selected.Count == 0)
        {
            MessageBox.Show(this, "Najprije odaberi tipke na slici tipkovnice.", "G815 Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (var id in _kbView.Selected)
        {
            if (color.HasValue) P.Lighting.KeyColors[id] = ColorUtil.ToHex(color.Value);
            else P.Lighting.KeyColors.Remove(id);
        }
        if (P.Lighting.Effect is EffectType.Off or EffectType.Wave or EffectType.ColorCycle or EffectType.Audio or EffectType.Screen)
        {
            _stInfo.Text = "Napomena: boje po tipkama vide se u efektima Statično, Disanje, Reakcija i Valovi.";
        }
        _c.LightingChanged(P);
    }

    // ================================================================= makroi

    private int CurrentG => _gList.SelectedIndex >= 0 ? _gList.SelectedIndex + 1 : 0;

    private Macro Cur(bool create) =>
        CurrentG == 0 ? null : create ? _c.GetOrCreateMacro(P, _editBank, CurrentG) : _c.GetMacro(P, _editBank, CurrentG);

    private void RefreshGList(bool keepSelection = false)
    {
        int sel = keepSelection ? _gList.SelectedIndex : Math.Max(0, _gList.SelectedIndex);
        bool wasLoading = _loading;
        _loading = true;
        _gList.BeginUpdate();
        _gList.Items.Clear();
        for (int g = 1; g <= 5; g++)
        {
            var m = _c.GetMacro(P, _editBank, g);
            string desc = m == null || m.Steps.Count == 0
                ? "(prazno)"
                : $"{(string.IsNullOrWhiteSpace(m.Name) ? "bez naziva" : m.Name)}  ·  {m.Steps.Count} kor.";
            _gList.Items.Add($"G{g}   {desc}");
        }
        _gList.EndUpdate();
        _gList.SelectedIndex = Math.Clamp(sel, 0, 4);
        _loading = wasLoading;
        LoadMacro();
    }

    private void LoadMacro()
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            var m = Cur(false);
            if (!_macroName.Focused) _macroName.Text = m?.Name ?? "";
            _modeCombo.SelectedIndex = (int)(m?.Mode ?? PlayMode.Once);
            _steps.BeginUpdate();
            _steps.Items.Clear();
            if (m != null) foreach (var s in m.Steps) _steps.Items.Add(s);
            _steps.EndUpdate();
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private void StepsEdited(int selectIndex = -1)
    {
        _c.SaveSoon();
        var m = Cur(false);
        _steps.BeginUpdate();
        _steps.Items.Clear();
        if (m != null) foreach (var s in m.Steps) _steps.Items.Add(s);
        _steps.EndUpdate();
        if (selectIndex >= 0 && selectIndex < _steps.Items.Count)
        {
            _steps.ClearSelected();
            _steps.SelectedIndex = selectIndex;
        }
        RefreshGListLabels();
    }

    private void RefreshGListLabels()
    {
        for (int g = 1; g <= 5; g++)
        {
            var m = _c.GetMacro(P, _editBank, g);
            string desc = m == null || m.Steps.Count == 0
                ? "(prazno)"
                : $"{(string.IsNullOrWhiteSpace(m.Name) ? "bez naziva" : m.Name)}  ·  {m.Steps.Count} kor.";
            string text = $"G{g}   {desc}";
            if ((string)_gList.Items[g - 1] != text) _gList.Items[g - 1] = text;
        }
    }

    private int InsertIndex(Macro m) =>
        _steps.SelectedIndices.Count > 0 ? _steps.SelectedIndices.Cast<int>().Max() + 1 : m.Steps.Count;

    private void Insert(params MacroStep[] steps)
    {
        if (CurrentG == 0) return;
        var m = Cur(true);
        int at = InsertIndex(m);
        m.Steps.InsertRange(at, steps);
        StepsEdited(at + steps.Length - 1);
    }

    private void AddKeyStep()
    {
        using var d = new KeyCaptureDialog();
        if (d.ShowDialog(this) != DialogResult.OK || d.Result == Keys.None) return;
        var data = d.Result;
        var code = data & Keys.KeyCode;
        var list = new List<MacroStep>();
        var mods = new List<int>();
        bool isMod = code is Keys.ShiftKey or Keys.ControlKey or Keys.Menu;
        if (!isMod)
        {
            if ((data & Keys.Control) != 0) mods.Add(0xA2);
            if ((data & Keys.Shift) != 0) mods.Add(0xA0);
            if ((data & Keys.Alt) != 0) mods.Add(0xA4);
        }
        int vk = code switch { Keys.ShiftKey => 0xA0, Keys.ControlKey => 0xA2, Keys.Menu => 0xA4, _ => (int)code };
        foreach (int mv in mods) list.Add(KeyStep(StepType.KeyDown, mv));
        list.Add(KeyStep(StepType.KeyPress, vk));
        for (int i = mods.Count - 1; i >= 0; i--) list.Add(KeyStep(StepType.KeyUp, mods[i]));
        Insert(list.ToArray());
    }

    private static MacroStep KeyStep(StepType t, int vk) => new()
    {
        Type = t,
        Vk = vk,
        Scan = NativeInput.ScanFromVk(vk),
        Extended = NativeInput.IsExtendedVk(vk)
    };

    private void AddDelayStep()
    {
        string s = PromptDialog.Show(this, "Kašnjenje", "Trajanje u milisekundama:", "100");
        if (s == null) return;
        if (!int.TryParse(s.Trim(), out int ms) || ms < 0 || ms > 600000)
        {
            MessageBox.Show(this, "Upiši broj između 0 i 600000.", "Kašnjenje", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Insert(new MacroStep { Type = StepType.Delay, DelayMs = ms });
    }

    private void AddTextStep()
    {
        string s = PromptDialog.Show(this, "Upiši tekst", "Tekst koji će makro utipkati (podržava i hrvatske znakove):", "", true);
        if (string.IsNullOrEmpty(s)) return;
        Insert(new MacroStep { Type = StepType.Text, Text = s });
    }

    private void AddRunStep()
    {
        using var ofd = new OpenFileDialog { Title = "Program ili datoteka za pokretanje", Filter = "Programi|*.exe;*.bat;*.cmd;*.lnk|Sve datoteke|*.*" };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        string args = PromptDialog.Show(this, "Argumenti", "Argumenti naredbenog retka (može ostati prazno):", "") ?? "";
        Insert(new MacroStep { Type = StepType.Run, Path = ofd.FileName, Args = args });
    }

    private void AddHttpStep()
    {
        if (CurrentG == 0) return;
        var step = new MacroStep { Type = StepType.Http, Method = "GET", Url = "https://" };
        using var d = new HttpStepDialog(step);
        if (d.ShowDialog(this) == DialogResult.OK) Insert(step);
    }

    private void AddInputStep()
    {
        if (CurrentG == 0) return;
        var step = new MacroStep { Type = StepType.Input, Variable = "vrijednost" };
        using var d = new InputStepDialog(step);
        if (d.ShowDialog(this) == DialogResult.OK) Insert(step);
    }

    private void EditStep()
    {
        var m = Cur(false);
        if (m == null || _steps.SelectedIndex < 0) return;
        var s = m.Steps[_steps.SelectedIndex];
        switch (s.Type)
        {
            case StepType.Delay:
                string d = PromptDialog.Show(this, "Kašnjenje", "Trajanje u milisekundama:", s.DelayMs.ToString());
                if (d != null && int.TryParse(d.Trim(), out int ms) && ms >= 0) s.DelayMs = ms;
                break;
            case StepType.Text:
                string t = PromptDialog.Show(this, "Tekst", "Tekst:", s.Text, true);
                if (t != null) s.Text = t;
                break;
            case StepType.Http:
            {
                using var hd = new HttpStepDialog(s);
                hd.ShowDialog(this);
                break;
            }
            case StepType.Input:
            {
                using var idlg = new InputStepDialog(s);
                idlg.ShowDialog(this);
                break;
            }
            case StepType.Run:
                string a = PromptDialog.Show(this, "Argumenti", $"Argumenti za {Path.GetFileName(s.Path)}:", s.Args);
                if (a != null) s.Args = a;
                break;
            default:
            {
                using var dlg = new KeyCaptureDialog();
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var code = dlg.Result & Keys.KeyCode;
                int vk = code switch { Keys.ShiftKey => 0xA0, Keys.ControlKey => 0xA2, Keys.Menu => 0xA4, _ => (int)code };
                s.Vk = vk;
                s.Scan = NativeInput.ScanFromVk(vk);
                s.Extended = NativeInput.IsExtendedVk(vk);
                break;
            }
        }
        StepsEdited(_steps.SelectedIndex);
    }

    private void MoveStep(int dir)
    {
        var m = Cur(false);
        if (m == null || _steps.SelectedIndex < 0) return;
        int i = _steps.SelectedIndex, j = i + dir;
        if (j < 0 || j >= m.Steps.Count) return;
        (m.Steps[i], m.Steps[j]) = (m.Steps[j], m.Steps[i]);
        StepsEdited(j);
    }

    private void RemoveSteps()
    {
        var m = Cur(false);
        if (m == null || _steps.SelectedIndices.Count == 0) return;
        foreach (int i in _steps.SelectedIndices.Cast<int>().OrderByDescending(x => x)) m.Steps.RemoveAt(i);
        StepsEdited(-1);
    }

    private void ClearMacro()
    {
        if (CurrentG == 0) return;
        if (MessageBox.Show(this, $"Obrisati makro na M{_editBank} / G{CurrentG}?", "Očisti", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        P.Macros.Remove(Profile.MacroKey(_editBank, CurrentG));
        _c.SaveSoon();
        RefreshGList(keepSelection: true);
    }

    private void ToggleUiRecording()
    {
        if (CurrentG == 0) return;
        if (!_c.Recorder.IsRecording)
        {
            if (!_c.StartUiRecording()) return;
            _recSink.Focus();
        }
        else
        {
            if (_c.HardwareRecording) return;
            var steps = _c.StopUiRecording();
            if (steps.Count > 0) Insert(steps.ToArray());
        }
        UpdateRecordingUi();
    }

    private void OnRecorderSteps() => UpdateRecordingUi();

    private void UpdateRecordingUi()
    {
        if (IsDisposed) return;
        bool rec = _c.Recorder.IsRecording;
        _recBtn.Text = rec ? "■ Zaustavi snimanje" : "● Snimi";
        Theme.StyleFlat(_recBtn, rec ? Color.FromArgb(70, 22, 30) : Theme.Danger, Theme.Danger);
        _recBtn.ForeColor = rec ? Color.FromArgb(255, 170, 178) : Color.FromArgb(26, 5, 8);
        _recInfo.Text = !rec ? "" :
            _c.HardwareRecording
                ? $"Snimam s tipkovnice (MR)… koraka: {_c.Recorder.Count}. Pritisni G tipku za spremanje."
                : $"Snimam… koraka: {_c.Recorder.Count}. Tipke koje pritišćeš ne utječu na ovaj prozor. Klikni \"Zaustavi\" mišem.";
    }

    private async Task TestMacro()
    {
        var m = Cur(false);
        if (m == null || m.Steps.Count == 0) return;
        _stInfo.Text = "Makro kreće za 3 sekunde – prebaci se u ciljni prozor…";
        await Task.Delay(3000);
        _c.Player.Play(m);
        _stInfo.Text = "";
    }

    // ================================================================= profili

    private Profile SelectedProfile =>
        _profList.SelectedItem is string n ? _c.Settings.Profiles.FirstOrDefault(p => p.Name == n) : null;

    private string UniqueName(string baseName)
    {
        string name = baseName;
        int i = 2;
        while (_c.Settings.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} {i++}";
        return name;
    }

    private void NewProfile(bool duplicate)
    {
        var src = SelectedProfile ?? P;
        string name = PromptDialog.Show(this, duplicate ? "Dupliciraj profil" : "Novi profil", "Naziv profila:",
            UniqueName(duplicate ? src.Name + " (kopija)" : "Novi profil"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = UniqueName(name.Trim());
        Profile p;
        if (duplicate)
        {
            p = SettingsStore.Clone(src);
            p.Name = name;
            p.Apps.Clear();
        }
        else
        {
            p = SettingsStore.CreateDefaultProfile();
            p.Name = name;
        }
        _c.Settings.Profiles.Add(p);
        _c.ProfilesChanged();
        ReloadAll();
        _profList.SelectedItem = name;
    }

    private void RenameProfile()
    {
        var p = SelectedProfile;
        if (p == null) return;
        string name = PromptDialog.Show(this, "Preimenuj profil", "Novi naziv:", p.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == p.Name) return;
        name = UniqueName(name.Trim());
        bool wasActive = _c.Settings.ActiveProfile == p.Name;
        p.Name = name;
        if (wasActive) _c.Settings.ActiveProfile = name;
        _c.ProfilesChanged();
        ReloadAll();
    }

    private void DeleteProfile()
    {
        var p = SelectedProfile;
        if (p == null) return;
        if (_c.Settings.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "Mora postojati barem jedan profil.", "Obriši", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"Obrisati profil \"{p.Name}\" sa svim bojama i makroima?", "Obriši",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _c.Settings.Profiles.Remove(p);
        if (_c.Settings.ActiveProfile == p.Name)
            _c.SetActiveProfile(_c.Settings.Profiles[0].Name);
        _c.ProfilesChanged();
        ReloadAll();
    }

    private void RefreshApps()
    {
        _appList.Items.Clear();
        var p = SelectedProfile;
        if (p == null) return;
        foreach (var a in p.Apps) _appList.Items.Add(a);
    }

    private void AddApp(string exe)
    {
        var p = SelectedProfile;
        if (p == null || string.IsNullOrWhiteSpace(exe)) return;
        exe = exe.Trim().ToLowerInvariant();
        foreach (var other in _c.Settings.Profiles) other.Apps.RemoveAll(a => a.Equals(exe, StringComparison.OrdinalIgnoreCase));
        p.Apps.Add(exe);
        _c.SaveSoon();
        RefreshApps();
    }

    private void AddAppFromRunning()
    {
        if (SelectedProfile == null) return;
        using var d = new ProcessPickerDialog();
        if (d.ShowDialog(this) == DialogResult.OK) AddApp(d.SelectedExe);
    }

    private void AddAppFromFile()
    {
        if (SelectedProfile == null) return;
        using var ofd = new OpenFileDialog { Filter = "Programi (*.exe)|*.exe", Title = "Odaberi program" };
        if (ofd.ShowDialog(this) == DialogResult.OK) AddApp(Path.GetFileName(ofd.FileName));
    }

    private void RemoveApp()
    {
        var p = SelectedProfile;
        if (p == null || _appList.SelectedItem is not string a) return;
        p.Apps.Remove(a);
        _c.SaveSoon();
        RefreshApps();
    }
}

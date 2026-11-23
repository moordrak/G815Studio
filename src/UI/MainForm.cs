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

    // osvjetljenje
    private KeyboardView _kbView;
    private ComboBox _effectCombo, _dirCombo;
    private TrackBar _speed, _bright;
    private Button _c1Btn, _c2Btn, _bgBtn, _brushBtn;
    private Color _brush = Color.FromArgb(255, 40, 40);
    private Label _effectHelp, _selInfo;

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
        (EffectType.Off, "Isključeno", "Sve tipke ugašene.")
    };

    public MainForm(AppController controller)
    {
        _c = controller;
        Text = "G815 Studio";
        Icon = IconFactory.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 780);
        MinimumSize = new Size(980, 640);
        Font = SystemFonts.MessageBoxFont;
        KeyPreview = true;

        BuildLayout();

        _c.StateChanged += OnStateChanged;
        _c.MacrosChanged += OnMacrosChanged;
        _c.Recorder.StepsChanged += OnRecorderSteps;
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();

        ReloadAll();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
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

    // ================================================================= izgled

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8, 6, 8, 0) };
        top.Controls.Add(Ui.Lbl("Profil:", true));
        _profileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
        _profileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading || _profileCombo.SelectedItem is not string name) return;
            _c.SetActiveProfile(name);
            ReloadAll();
        };
        top.Controls.Add(_profileCombo);
        top.Controls.Add(Ui.Lbl("   (uređuješ i koristiš ovaj profil; automatsko mijenjanje u kartici Profili)"));

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(BuildLightingTab());
        tabs.TabPages.Add(BuildMacroTab());
        tabs.TabPages.Add(BuildProfilesTab());
        tabs.TabPages.Add(BuildSettingsTab());

        var status = new StatusStrip();
        _stDevice = new ToolStripStatusLabel { AutoSize = true };
        _stProfile = new ToolStripStatusLabel { AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Left };
        _stBank = new ToolStripStatusLabel { AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Left };
        _stInfo = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        status.Items.AddRange(new ToolStripItem[] { _stDevice, _stProfile, _stBank, _stInfo });

        Controls.Add(tabs);
        Controls.Add(top);
        Controls.Add(status);
    }

    private TabPage BuildLightingTab()
    {
        var page = new TabPage("Osvjetljenje") { Padding = new Padding(6) };
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        _kbView = new KeyboardView { Dock = DockStyle.Fill, ColorOf = k => _snapshot[k.Zone] };
        _kbView.SelectionChanged += (_, _) => UpdateSelInfo();
        split.Controls.Add(_kbView, 0, 0);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };

        // --- efekt
        var fx = Ui.Flow(FlowDirection.TopDown);
        _effectCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        foreach (var e in Effects) _effectCombo.Items.Add(e.Name);
        _effectCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_effectCombo.SelectedIndex < 0) return;
            _effectHelp.Text = Effects[_effectCombo.SelectedIndex].Help;
            _dirCombo.Enabled = Effects[_effectCombo.SelectedIndex].Type == EffectType.Wave;
            if (_loading) return;
            P.Lighting.Effect = Effects[_effectCombo.SelectedIndex].Type;
            _c.LightingChanged(P);
        };
        _effectHelp = new Label { AutoSize = false, Width = 260, Height = 48, ForeColor = SystemColors.GrayText };
        fx.Controls.Add(_effectCombo);
        fx.Controls.Add(_effectHelp);

        fx.Controls.Add(Ui.Lbl("Brzina"));
        _speed = new TrackBar { Minimum = 1, Maximum = 100, TickFrequency = 10, Width = 260 };
        _speed.ValueChanged += (_, _) => { if (_loading) return; P.Lighting.Speed = _speed.Value; _c.LightingChanged(P); };
        fx.Controls.Add(_speed);

        fx.Controls.Add(Ui.Lbl("Svjetlina"));
        _bright = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 260 };
        _bright.ValueChanged += (_, _) => { if (_loading) return; P.Lighting.Brightness = _bright.Value; _c.LightingChanged(P); };
        fx.Controls.Add(_bright);

        fx.Controls.Add(Ui.Lbl("Smjer vala"));
        _dirCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        _dirCombo.Items.AddRange(new object[] { "Lijevo → desno", "Desno → lijevo", "Gore → dolje", "Iz središta" });
        _dirCombo.SelectedIndexChanged += (_, _) => { if (_loading) return; P.Lighting.Direction = _dirCombo.SelectedIndex; _c.LightingChanged(P); };
        fx.Controls.Add(_dirCombo);
        controls.Controls.Add(Ui.Group("Efekt", fx));

        // --- boje efekta
        var cols = Ui.Flow(FlowDirection.TopDown);
        _c1Btn = Ui.Btn("Boja 1 (zadana boja tipki)", (_, _) => PickConfigColor(1), 220);
        _c2Btn = Ui.Btn("Boja 2 (pritisak / vrh)", (_, _) => PickConfigColor(2), 220);
        _bgBtn = Ui.Btn("Pozadina", (_, _) => PickConfigColor(3), 220);
        cols.Controls.Add(_c1Btn);
        cols.Controls.Add(_c2Btn);
        cols.Controls.Add(_bgBtn);
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
        paint.Controls.Add(Ui.Btn("Oboji odabrane tipke", (_, _) => PaintSelected(_brush), 250));
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
        quick.Controls.Add(Ui.Btn("Poništi odabir", (_, _) => _kbView.ClearSelection(), 200));
        controls.Controls.Add(Ui.Group("Brzi odabir (Ctrl = dodaj)", quick));

        split.Controls.Add(controls, 0, 1);
        page.Controls.Add(split);
        return page;
    }

    private TabPage BuildMacroTab()
    {
        var page = new TabPage("Makroi") { Padding = new Padding(8) };
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
        right.Controls.Add(new Label { Height = 10 });
        right.Controls.Add(Ui.Btn("Uredi korak", (_, _) => EditStep(), 210));
        right.Controls.Add(Ui.Btn("Gore", (_, _) => MoveStep(-1), 210));
        right.Controls.Add(Ui.Btn("Dolje", (_, _) => MoveStep(1), 210));
        right.Controls.Add(Ui.Btn("Ukloni odabrane korake", (_, _) => RemoveSteps(), 210));
        right.Controls.Add(Ui.Btn("Očisti makro", (_, _) => ClearMacro(), 210));
        right.Controls.Add(new Label { Height = 10 });
        right.Controls.Add(Ui.Btn("▶ Testiraj (za 3 s)", async (_, _) => await TestMacro(), 210));
        table.Controls.Add(right, 2, 0);

        page.Controls.Add(table);
        return page;
    }

    private TabPage BuildProfilesTab()
    {
        var page = new TabPage("Profili") { Padding = new Padding(8) };
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

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Postavke") { Padding = new Padding(8), AutoScroll = true };
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
        flow.Controls.Add(Ui.Group("Ispitivač i ispravak zona", zones));

        page.Controls.Add(flow);
        return page;
    }

    // ================================================================= punjenje

    private void ReloadAll()
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
            _speed.Value = Math.Clamp(L.Speed, _speed.Minimum, _speed.Maximum);
            _bright.Value = Math.Clamp(L.Brightness, _bright.Minimum, _bright.Maximum);
            _dirCombo.SelectedIndex = Math.Clamp(L.Direction, 0, 3);
            _dirCombo.Enabled = L.Effect == EffectType.Wave;
            Ui.PaintSwatch(_c1Btn, ColorUtil.Parse(L.Color1));
            Ui.PaintSwatch(_c2Btn, ColorUtil.Parse(L.Color2));
            Ui.PaintSwatch(_bgBtn, ColorUtil.Parse(L.Background));

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
        _stDevice.ForeColor = kb.IsConnected ? Color.ForestGreen : Color.Firebrick;
        _stProfile.Text = "Aktivni profil: " + _c.EffectiveProfile?.Name;
        _stBank.Text = "Banka: M" + _c.Bank + (_c.HardwareRecording ? "  ● SNIMANJE (MR)" : "");
        var eng = _c.Engine;
        _stInfo.Text = eng.LastError is { } err
            ? "Osvjetljenje: " + err
            : kb.IsConnected && eng.MeasuredFps > 0
                ? $"Osvjetljenje: {eng.MeasuredFps:0} FPS · slanje {eng.SendMs:0.0} ms/okvir"
                : "";

        _devInfo.Text = kb.IsConnected
            ? $"Spojeno: {kb.DeviceName}\nHID++ značajke: {kb.FeatureSummary}\nG tipke preusmjerene: {(kb.HasGKeys ? "da" : "ne")}"
            : "Nije spojeno. " + (_c.LastConnectError ?? "Tražim tipkovnicu…");

        string fg = _c.ForegroundApp ?? "?";
        _effLabel.Text = $"Sada aktivan: {_c.EffectiveProfile?.Name}   (zadnja aplikacija u fokusu: {fg})";
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
        var L = P.Lighting;
        string cur = which == 1 ? L.Color1 : which == 2 ? L.Color2 : L.Background;
        var c = Ui.PickColor(this, ColorUtil.Parse(cur));
        if (!c.HasValue) return;
        string hex = ColorUtil.ToHex(c.Value);
        if (which == 1) { L.Color1 = hex; Ui.PaintSwatch(_c1Btn, c.Value); }
        else if (which == 2) { L.Color2 = hex; Ui.PaintSwatch(_c2Btn, c.Value); }
        else { L.Background = hex; Ui.PaintSwatch(_bgBtn, c.Value); }
        _c.LightingChanged(P);
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
        _recBtn.BackColor = rec ? Color.MistyRose : SystemColors.Control;
        _recBtn.UseVisualStyleBackColor = !rec;
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

namespace Svitac;

internal static class IconFactory
{
    private static Icon _icon;
    private static readonly Dictionary<int, Icon> Sizes = new();

    /// <summary>Ikona programa (svitac u šesterokutu), ugrađena kao res/svitac.ico sa svim veličinama.</summary>
    public static Icon AppIcon => _icon ??= Load();

    /// <summary>Ikona u određenoj veličini (Windows bira najbližu sliku iz .ico datoteke).</summary>
    public static Icon LogoIcon(int size)
    {
        if (!Sizes.TryGetValue(size, out var ic)) Sizes[size] = ic = new Icon(AppIcon, size, size);
        return ic;
    }

    /// <summary>Mala ikona za tray (16 px na 100 %, veća na većem skaliranju).</summary>
    public static Icon TrayIcon => LogoIcon(SystemInformation.SmallIconSize.Width);

    private static Icon Load()
    {
        using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("svitac.ico");
        return s != null ? new Icon(s) : SystemIcons.Application;
    }
}

/// <summary>Aplikacija živi u trayu; glavni prozor se otvara po potrebi.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppController _c;
    private readonly NotifyIcon _icon;
    private readonly SynchronizationContext _ui;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _profilesMenu, _lightsOff, _status, _open, _reconnect, _exit;
    private MainForm _form;

    public TrayContext(bool startHidden)
    {
        Current = this;
        _ui = SynchronizationContext.Current;
        _c = new AppController();

        _menu = new ContextMenuStrip { Renderer = new DarkRenderer(), BackColor = Theme.Surface, ForeColor = Theme.TextSoft, Font = Theme.UiFont(10f) };
        _status = new ToolStripMenuItem("…") { Enabled = false };
        _profilesMenu = new ToolStripMenuItem();
        _lightsOff = new ToolStripMenuItem { CheckOnClick = true };
        _lightsOff.CheckedChanged += (_, _) => _c.SetLightsOff(_lightsOff.Checked);
        _open = new ToolStripMenuItem("", null, (_, _) => ShowForm()) { Font = Theme.UiFont(10f, FontStyle.Bold) };
        _reconnect = new ToolStripMenuItem("", null, (_, _) => _c.Reconnect());
        _exit = new ToolStripMenuItem("", null, (_, _) => Exit());

        _menu.Items.Add(_open);
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_profilesMenu);
        _menu.Items.Add(_lightsOff);
        _menu.Items.Add(_reconnect);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_exit);
        _menu.Opening += (_, _) => RebuildMenu();

        _icon = new NotifyIcon
        {
            Icon = IconFactory.TrayIcon,
            Text = "SVITAC",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowForm(); };

        _c.Notify += msg => _icon.ShowBalloonTip(2500, "SVITAC", msg, ToolTipIcon.Info);
        _c.StateChanged += UpdateTooltip;
        UpdateTooltip();

        if (!startHidden) ShowForm();
    }

    public void PostShow() => _ui?.Post(_ => ShowForm(), null);

    /// <summary>Zatvara i ponovno otvara glavni prozor (nakon promjene jezika), na istom mjestu.</summary>
    public void ReopenForm()
    {
        if (_form == null || _form.IsDisposed) return;
        var bounds = _form.WindowState == FormWindowState.Normal ? _form.Bounds : _form.RestoreBounds;
        var state = _form.WindowState;
        int page = _form.CurrentPage;
        _form.Close();
        _form = new MainForm(_c) { StartPosition = FormStartPosition.Manual, Bounds = bounds };
        _form.FormClosed += (_, _) => _form = null;
        _form.ShowPage(page);
        _form.Show();
        if (state == FormWindowState.Maximized) _form.WindowState = state;
        _form.Activate();
        UpdateTooltip();
    }

    private void RebuildMenu()
    {
        _open.Text = L.T("Otvori SVITAC");
        _profilesMenu.Text = L.T("Profil");
        _lightsOff.Text = L.T("Ugasi osvjetljenje");
        _reconnect.Text = L.T("Ponovno spoji uređaje");
        _exit.Text = L.T("Izlaz");
        _status.Text = _c.Keyboard.IsConnected ? "● " + _c.Keyboard.DeviceName : "○ " + L.T("Tipkovnica nije spojena");
        _profilesMenu.DropDownItems.Clear();
        foreach (var p in _c.Settings.Profiles)
        {
            string name = p.Name;
            var item = new ToolStripMenuItem(name, null, (_, _) =>
            {
                _c.SetActiveProfile(name);
            })
            {
                Checked = _c.EffectiveProfile == p
            };
            _profilesMenu.DropDownItems.Add(item);
        }
    }

    private void UpdateTooltip()
    {
        string t = $"SVITAC – {_c.EffectiveProfile?.Name} (M{_c.Bank})" + (_c.Keyboard.IsConnected ? "" : " – " + L.T("nije spojeno"));
        _icon.Text = t.Length > 63 ? t[..63] : t;
    }

    private void ShowForm()
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new MainForm(_c);
            _form.FormClosed += (_, _) => _form = null;
        }
        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
        _form.Activate();
        _form.BringToFront();
    }

    /// <summary>Trenutni primjerak (za uredan izlaz iz drugih dijelova programa).</summary>
    public static TrayContext Current { get; private set; }

    public void RequestExit() => Exit();

    private void Exit()
    {
        _form?.Close();
        _icon.Visible = false;
        _icon.Dispose();
        _c.Dispose();
        ExitThread();
    }
}

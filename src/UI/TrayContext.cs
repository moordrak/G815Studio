using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace G815Studio;

internal static class IconFactory
{
    private static Icon _icon;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon AppIcon => _icon ??= Create();

    private static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(30, 30, 36));
            g.FillRectangle(body, 1, 7, 30, 18);
            Color[] cols = { Color.FromArgb(255, 60, 60), Color.FromArgb(255, 170, 0), Color.FromArgb(60, 220, 90), Color.FromArgb(0, 170, 255), Color.FromArgb(180, 80, 255) };
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 6; col++)
                {
                    using var b = new SolidBrush(cols[(row + col) % cols.Length]);
                    g.FillRectangle(b, 3 + col * 4.6f, 9 + row * 5, 3.4f, 3.5f);
                }
        }
        IntPtr h = bmp.GetHicon();
        var tmp = Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone();
        tmp.Dispose();
        DestroyIcon(h);
        return icon;
    }
}

/// <summary>Aplikacija živi u trayu; glavni prozor se otvara po potrebi.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppController _c;
    private readonly NotifyIcon _icon;
    private readonly SynchronizationContext _ui;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _profilesMenu, _lightsOff, _status;
    private MainForm _form;

    public TrayContext(bool startHidden)
    {
        Current = this;
        _ui = SynchronizationContext.Current;
        _c = new AppController();

        _menu = new ContextMenuStrip { Renderer = new DarkRenderer(), BackColor = Theme.Surface, ForeColor = Theme.TextSoft };
        _status = new ToolStripMenuItem("…") { Enabled = false };
        _profilesMenu = new ToolStripMenuItem("Profil");
        _lightsOff = new ToolStripMenuItem("Ugasi osvjetljenje") { CheckOnClick = true };
        _lightsOff.CheckedChanged += (_, _) => _c.Engine.LightsOff = _lightsOff.Checked;

        _menu.Items.Add(new ToolStripMenuItem("Otvori G815 Studio", null, (_, _) => ShowForm()) { Font = new Font(_menu.Font, FontStyle.Bold) });
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_profilesMenu);
        _menu.Items.Add(_lightsOff);
        _menu.Items.Add(new ToolStripMenuItem("Ponovno spoji tipkovnicu", null, (_, _) => _c.Reconnect()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Izlaz", null, (_, _) => Exit()));
        _menu.Opening += (_, _) => RebuildMenu();

        _icon = new NotifyIcon
        {
            Icon = IconFactory.AppIcon,
            Text = "G815 Studio",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowForm(); };

        _c.Notify += msg => _icon.ShowBalloonTip(2500, "G815 Studio", msg, ToolTipIcon.Info);
        _c.StateChanged += UpdateTooltip;
        UpdateTooltip();

        if (!startHidden) ShowForm();
    }

    public void PostShow() => _ui?.Post(_ => ShowForm(), null);

    private void RebuildMenu()
    {
        _status.Text = _c.Keyboard.IsConnected ? "● " + _c.Keyboard.DeviceName : "○ Tipkovnica nije spojena";
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
        string t = $"G815 Studio – {_c.EffectiveProfile?.Name} (M{_c.Bank})" + (_c.Keyboard.IsConnected ? "" : " – nije spojeno");
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

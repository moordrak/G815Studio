using System.Diagnostics;
using System.Runtime.InteropServices;

namespace G815Studio;

/// <summary>Središnja logika: povezuje tipkovnicu, efekte, makroe, profile i postavke. Živi na UI niti.</summary>
public sealed class AppController : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _connectTimer;
    private readonly System.Windows.Forms.Timer _watchTimer;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly string _ownExe = (Process.GetCurrentProcess().ProcessName + ".exe").ToLowerInvariant();
    private bool _connecting;
    private bool _disposed;

    public AppSettings Settings { get; }
    public G815Keyboard Keyboard { get; } = new();
    public LogitechMouse Mouse { get; } = new();
    private bool _mouseConnecting;
    private int _mouseZonesInLayout = -1;
    public LightingEngine Engine { get; }
    public KeyboardHook Hook { get; } = new();
    public MacroPlayer Player { get; } = new();
    public MacroRecorder Recorder { get; }

    public List<KeyDef> Keys { get; private set; }
    public int Bank { get; private set; } = 1;
    public Profile EffectiveProfile { get; private set; }
    public bool HardwareRecording { get; private set; }
    public string LastConnectError { get; private set; }
    public string ForegroundApp { get; private set; }

    public event Action StateChanged;
    public event Action MacrosChanged;
    public event Action<string> Notify;

    public bool IsIso => !string.Equals(Settings.Layout, "ANSI", StringComparison.OrdinalIgnoreCase);

    public Profile ManualProfile =>
        Settings.Profiles.FirstOrDefault(p => p.Name == Settings.ActiveProfile) ?? Settings.Profiles[0];

    public AppController()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Settings = SettingsStore.Load();
        Engine = new LightingEngine(Keyboard) { Fps = Settings.Fps, Mouse = Mouse };
        Mouse.Changed += () => _ui.Post(_ => OnMouseChanged(), null);
        Recorder = new MacroRecorder(Hook);

        Keyboard.GKeyDown += g => _ui.Post(_ => OnGKeyDown(g), null);
        Keyboard.GKeyUp += g => _ui.Post(_ => OnGKeyUp(g), null);
        Keyboard.MKeyPressed += m => _ui.Post(_ => SetBank(m), null);
        Keyboard.MrPressed += () => _ui.Post(_ => OnMr(), null);
        Keyboard.Disconnected += () => _ui.Post(_ => OnDisconnected(), null);
        Hook.KeyEvent += OnHookKey;
        Player.Error += msg => _ui.Post(_ => Notify?.Invoke("Greška makroa: " + msg), null);
        Player.Info += msg => _ui.Post(_ => Notify?.Invoke(msg), null);
        Player.Ui = func =>
        {
            string result = null;
            _ui.Send(_ => result = func(), null);
            return result;
        };

        RebuildKeys();
        Apply(ManualProfile);
        Engine.Start();

        _connectTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _connectTimer.Tick += (_, _) => { TryConnect(); TryConnectMouse(); };
        _connectTimer.Start();

        _watchTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _watchTimer.Tick += (_, _) => WatchForeground();
        _watchTimer.Start();

        _saveTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SettingsStore.Save(Settings); };

        TryConnect();
        TryConnectMouse();
    }

    // ------------------------------------------------------------ spajanje

    public void TryConnect()
    {
        if (_connecting || Keyboard.IsConnected || _disposed) return;
        _connecting = true;
        var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(() =>
        {
            bool ok = Keyboard.TryConnect(out string err);
            return (ok, err);
        }).ContinueWith(t =>
        {
            _connecting = false;
            if (_disposed) return;
            var (ok, err) = t.Result;
            string previous = LastConnectError;
            LastConnectError = ok ? null : err;
            if (ok)
            {
                Keyboard.SetMLeds(1 << (Bank - 1));
                Engine.ForceResend();
                Notify?.Invoke("Tipkovnica spojena: " + Keyboard.DeviceName);
            }
            if (ok || previous != err) StateChanged?.Invoke();
        }, scheduler);
    }

    public void Reconnect()
    {
        Keyboard.Release();
        StateChanged?.Invoke();
        TryConnect();
        TryConnectMouse();
    }

    private void OnDisconnected()
    {
        if (HardwareRecording) CancelHardwareRecording();
        Player.StopAll();
        StateChanged?.Invoke();
    }

    // ------------------------------------------------------------ profili

    public void RebuildKeys()
    {
        int mz = Settings.MouseEnabled && Mouse.IsConnected ? Mouse.ClusterCount : 0;
        _mouseZonesInLayout = mz;
        Keys = KeyLayouts.Build(IsIso, Settings.ZoneOverrides, mz);
        Engine.SetKeys(Keys);
        StateChanged?.Invoke();
    }

    private void Apply(Profile p)
    {
        EffectiveProfile = p;
        Engine.SetConfig(SettingsStore.Clone(p.Lighting));
        UpdateHook();
        StateChanged?.Invoke();
    }

    public void SetActiveProfile(string name)
    {
        if (Settings.Profiles.All(p => p.Name != name)) return;
        Settings.ActiveProfile = name;
        Apply(ManualProfile);
        SaveSoon();
    }

    /// <summary>Pozvati nakon svake izmjene osvjetljenja u nekom profilu.</summary>
    public void LightingChanged(Profile p)
    {
        if (p == EffectiveProfile)
        {
            Engine.SetConfig(SettingsStore.Clone(p.Lighting));
            UpdateHook();
        }
        SaveSoon();
    }

    public void ProfilesChanged()
    {
        if (!Settings.Profiles.Contains(EffectiveProfile)) Apply(ManualProfile);
        SaveSoon();
        StateChanged?.Invoke();
    }

    private void WatchForeground()
    {
        string exe = GetForegroundExe();
        if (exe != null) ForegroundApp = exe;
        if (!Settings.AutoSwitch)
        {
            if (EffectiveProfile != ManualProfile) Apply(ManualProfile);
            return;
        }
        if (exe == null || exe == _ownExe) return;
        var match = Settings.Profiles.FirstOrDefault(p =>
            p.Apps.Any(a => string.Equals(a, exe, StringComparison.OrdinalIgnoreCase)));
        var target = match ?? ManualProfile;
        if (target != EffectiveProfile)
        {
            Apply(target);
            if (match != null) Notify?.Invoke($"Profil: {target.Name}");
        }
    }

    private static string GetForegroundExe()
    {
        try
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return (p.ProcessName + ".exe").ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ------------------------------------------------------------ makroi

    public Macro GetMacro(Profile p, int bank, int g)
    {
        p.Macros.TryGetValue(Profile.MacroKey(bank, g), out var m);
        return m;
    }

    public Macro GetOrCreateMacro(Profile p, int bank, int g)
    {
        string key = Profile.MacroKey(bank, g);
        if (!p.Macros.TryGetValue(key, out var m))
        {
            m = new Macro();
            p.Macros[key] = m;
        }
        return m;
    }

    public void SetBank(int bank)
    {
        if (bank < 1 || bank > 3) return;
        Bank = bank;
        Keyboard.SetMLeds(1 << (bank - 1));
        StateChanged?.Invoke();
    }

    private void OnGKeyDown(int g)
    {
        if (HardwareRecording)
        {
            var steps = Recorder.Stop();
            HardwareRecording = false;
            Keyboard.SetMrLed(false);
            UpdateHook();
            if (steps.Count == 0)
            {
                Notify?.Invoke("Ništa nije snimljeno.");
            }
            else
            {
                var m = GetOrCreateMacro(EffectiveProfile, Bank, g);
                m.Steps = steps;
                if (string.IsNullOrWhiteSpace(m.Name)) m.Name = $"Snimljeno {DateTime.Now:dd.MM. HH:mm}";
                SaveSoon();
                MacrosChanged?.Invoke();
                Notify?.Invoke($"Makro spremljen na M{Bank} / G{g} (profil {EffectiveProfile.Name}).");
            }
            StateChanged?.Invoke();
            return;
        }
        Player.OnKeyDown(g, GetMacro(EffectiveProfile, Bank, g));
    }

    private void OnGKeyUp(int g)
    {
        Player.OnKeyUp(g, GetMacro(EffectiveProfile, Bank, g));
    }

    private void OnMr()
    {
        if (HardwareRecording)
        {
            CancelHardwareRecording();
            Notify?.Invoke("Snimanje otkazano.");
            return;
        }
        if (Recorder.IsRecording) return; // već snima iz sučelja
        Recorder.Start(Settings.RecordDelays);
        HardwareRecording = true;
        UpdateHook();
        Keyboard.SetMrLed(true);
        Notify?.Invoke($"Snimam makro (M{Bank}). Pritisni G tipku za spremanje ili MR za odustajanje.");
        StateChanged?.Invoke();
    }

    private void CancelHardwareRecording()
    {
        Recorder.Stop();
        HardwareRecording = false;
        Keyboard.SetMrLed(false);
        UpdateHook();
        StateChanged?.Invoke();
    }

    public bool StartUiRecording()
    {
        if (Recorder.IsRecording) return false;
        Recorder.Start(Settings.RecordDelays);
        UpdateHook();
        return true;
    }

    public List<MacroStep> StopUiRecording()
    {
        if (HardwareRecording) return new List<MacroStep>();
        var steps = Recorder.Stop();
        UpdateHook();
        return steps;
    }

    // ------------------------------------------------------------ hook

    private bool EffectNeedsPresses =>
        EffectiveProfile?.Lighting.Effect is EffectType.Reactive or EffectType.Ripple;

    private void UpdateHook()
    {
        if (Recorder.IsRecording || EffectNeedsPresses) Hook.Install();
        else Hook.Uninstall();
    }

    private void OnHookKey(KeyHookEvent e)
    {
        if (!e.Down || !EffectNeedsPresses) return;
        int usage = ScanMap.ToUsage(e.Scan, e.Extended, e.Vk, IsIso);
        if (usage > 0) Engine.NotifyKeyPress("U" + usage.ToString("X2"));
    }

    // ------------------------------------------------------------ kraj

    // ------------------------------------------------------------ miš

    private int _mouseTicks;

    public void TryConnectMouse()
    {
        if (!Settings.MouseEnabled || Mouse.IsConnected || _mouseConnecting || _disposed) return;
        if (_mouseTicks++ % 2 != 0) return;     // svakih ~6 s
        _mouseConnecting = true;
        Task.Run(() => Mouse.TryConnect()).ContinueWith(_ => _ui.Post(__ => { _mouseConnecting = false; StateChanged?.Invoke(); }, null));
    }

    /// <summary>Pozvati nakon promjene postavki miša.</summary>
    public void MouseSettingsChanged()
    {
        if (!Settings.MouseEnabled)
        {
            Mouse.Release();
        }
        else
        {
            _mouseTicks = 0;
            TryConnectMouse();
        }
        ApplyMouseMode();
        SaveSoon();
        StateChanged?.Invoke();
    }

    private void ApplyMouseMode()
    {
        Engine.MouseFollow = Settings.MouseFollow;
        if (Settings.MouseEnabled && !Settings.MouseFollow && Mouse.IsConnected)
            Mouse.SetColors(new[] { Engine.LightsOff ? Color.Black : ColorUtil.Parse(Settings.MouseColor) });
    }

    private void OnMouseChanged()
    {
        int mz = Settings.MouseEnabled && Mouse.IsConnected ? Mouse.ClusterCount : 0;
        if (mz != _mouseZonesInLayout) RebuildKeys();
        ApplyMouseMode();
        if (Mouse.IsConnected) Notify?.Invoke("Miš spojen: " + Mouse.Name);
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connectTimer.Stop();
        _watchTimer.Stop();
        _saveTimer.Stop();
        Player.StopAll();
        if (Recorder.IsRecording) Recorder.Stop();
        Hook.Dispose();
        Engine.Dispose();
        Keyboard.Release();
        Mouse.Release();
        SettingsStore.Save(Settings);
    }
}

using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace G815Studio;

/// <summary>Tipka kakvu vidi engine: zona + položaj (u jedinicama tipki i normalizirano 0..1).</summary>
public sealed class LightKey
{
    public string Id;
    public byte Zone;
    public float UX, UY;   // središte u jedinicama tipki
    public float NX, NY;   // normalizirano 0..1
}

public sealed class EffectRenderer : IDisposable
{
    private readonly struct Press
    {
        public readonly string Id;
        public readonly float UX, UY;
        public readonly double T;
        public Press(string id, float ux, float uy, double t) { Id = id; UX = ux; UY = uy; T = t; }
    }

    private readonly List<Press> _presses = new();
    private readonly object _pressLock = new();
    private AudioMeter _audio;
    private ScreenSampler _screen;
    private float _lvlL, _lvlR;
    private double _lastAudioT;
    private int _frameNo;

    public void AddPress(LightKey k, double t)
    {
        lock (_pressLock)
        {
            _presses.Add(new Press(k.Id, k.UX, k.UY, t));
            if (_presses.Count > 64) _presses.RemoveAt(0);
        }
    }

    public void Render(LightingConfig cfg, LightKey[] keys, Color[] baseColors, double t, Color[] frame)
    {
        _frameNo++;
        var c1 = ColorUtil.Parse(cfg.Color1, Color.DeepSkyBlue);
        var c2 = ColorUtil.Parse(cfg.Color2, Color.Red);
        var bg = ColorUtil.Parse(cfg.Background, Color.Black);
        double speed = Math.Clamp(cfg.Speed, 1, 100) / 50.0;   // 0.02 .. 2

        if (cfg.Effect != EffectType.Audio) { _audio?.Dispose(); _audio = null; }
        if (cfg.Effect != EffectType.Screen) { _screen?.Dispose(); _screen = null; }

        Press[] presses;
        lock (_pressLock)
        {
            _presses.RemoveAll(p => t - p.T > 6);
            presses = _presses.ToArray();
        }

        switch (cfg.Effect)
        {
            case EffectType.Static:
                for (int i = 0; i < keys.Length; i++) frame[keys[i].Zone] = baseColors[i];
                break;

            case EffectType.Breathing:
            {
                double f = 0.25 * speed;
                double k = 0.04 + 0.96 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * f * t));
                for (int i = 0; i < keys.Length; i++) frame[keys[i].Zone] = ColorUtil.Scale(baseColors[i], k);
                break;
            }

            case EffectType.ColorCycle:
            {
                var c = ColorUtil.Hsv(t * 0.08 * speed, 1, 1);
                foreach (var key in keys) frame[key.Zone] = c;
                break;
            }

            case EffectType.Wave:
                foreach (var key in keys)
                {
                    double pos = cfg.Direction switch
                    {
                        1 => 1 - key.NX,
                        2 => key.NY * 0.35,
                        3 => Math.Sqrt(Math.Pow(key.NX - 0.5, 2) + Math.Pow((key.NY - 0.5) * 0.35, 2)),
                        _ => key.NX
                    };
                    frame[key.Zone] = ColorUtil.Hsv(pos - t * 0.25 * speed, 1, 1);
                }
                break;

            case EffectType.Reactive:
            {
                double dur = Math.Clamp(1.5 / speed, 0.15, 5);
                for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    double best = 0;
                    foreach (var p in presses)
                    {
                        if (p.Id != key.Id) continue;
                        double a = 1 - (t - p.T) / dur;
                        if (a > best) best = a;
                    }
                    frame[key.Zone] = best > 0 ? ColorUtil.Lerp(baseColors[i], c2, best) : baseColors[i];
                }
                break;
            }

            case EffectType.Ripple:
            {
                double v = 14 * speed;          // jedinica tipke u sekundi
                double maxAge = 26 / v;
                for (int i = 0; i < keys.Length; i++)
                {
                    var key = keys[i];
                    double best = 0;
                    foreach (var p in presses)
                    {
                        double age = t - p.T;
                        if (age > maxAge) continue;
                        double d = Math.Sqrt(Math.Pow(key.UX - p.UX, 2) + Math.Pow(key.UY - p.UY, 2));
                        double ring = 1 - Math.Abs(d - age * v) / 1.6;
                        if (ring <= 0) continue;
                        double a = ring * (1 - age / maxAge);
                        if (a > best) best = a;
                    }
                    frame[key.Zone] = best > 0 ? ColorUtil.Lerp(baseColors[i], c2, best) : baseColors[i];
                }
                break;
            }

            case EffectType.Audio:
            {
                _audio ??= new AudioMeter();
                float l = 0, r = 0;
                if (_audio.TryGetPeaks(out float pl, out float pr)) { l = pl; r = pr; }
                // pad razine ovisi o stvarnom vremenu, ne o broju okvira
                double dt = Math.Clamp(t - _lastAudioT, 0, 0.5);
                _lastAudioT = t;
                double tau = Math.Clamp(0.3 / speed, 0.05, 1.5);
                double decay = Math.Exp(-dt / tau);
                _lvlL = (float)Math.Max(l, _lvlL * decay);
                _lvlR = (float)Math.Max(r, _lvlR * decay);
                foreach (var key in keys)
                {
                    double d = Math.Abs(key.NX - 0.5) * 2;
                    double lvl = Math.Min(1, (key.NX < 0.5 ? _lvlL : _lvlR) * 1.3);
                    frame[key.Zone] = d <= lvl ? ColorUtil.Lerp(c1, c2, d) : bg;
                }
                break;
            }

            case EffectType.Screen:
            {
                _screen ??= new ScreenSampler();
                if (_frameNo % 2 == 0) _screen.Capture();
                foreach (var key in keys)
                    frame[key.Zone] = ColorUtil.Saturate(_screen.Sample(key.NX, key.NY), 1.4);
                break;
            }

            case EffectType.Off:
                foreach (var key in keys) frame[key.Zone] = Color.Black;
                break;
        }

        double bright = Math.Clamp(cfg.Brightness, 0, 100) / 100.0;
        if (bright < 0.999)
            foreach (var key in keys) frame[key.Zone] = ColorUtil.Scale(frame[key.Zone], bright);
    }

    public void Dispose()
    {
        _audio?.Dispose();
        _screen?.Dispose();
    }
}

/// <summary>Pozadinska petlja koja računa efekte i šalje samo promijenjene boje tipkovnici.</summary>
public sealed class LightingEngine : IDisposable
{
    private readonly G815Keyboard _kb;
    private readonly EffectRenderer _renderer = new();
    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Color[] _frame = new Color[256];
    private readonly Color[] _sent = new Color[256];
    private readonly bool[] _sentValid = new bool[256];
    private readonly bool[] _everUsed = new bool[256];
    private readonly Color[] _snapshot = new Color[256];
    private Thread _thread;
    private volatile bool _stop;

    private LightingConfig _cfg = new();
    private LightKey[] _keys = Array.Empty<LightKey>();
    private Color[] _base = Array.Empty<Color>();

    public volatile int Fps = 30;
    public volatile bool LightsOff;
    public volatile int TestZone = -1;
    public string LastError { get; private set; }

    /// <summary>Stvarni broj okvira u sekundi poslanih tipkovnici i prosječno trajanje slanja jednog okvira.</summary>
    public double MeasuredFps { get; private set; }
    public double SendMs { get; private set; }
    private int _framesThisSecond;
    private double _sendMsAccum;
    private long _fpsWindowStart;

    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    public LightingEngine(G815Keyboard kb)
    {
        _kb = kb;
    }

    public EffectType CurrentEffect
    {
        get { lock (_lock) return _cfg.Effect; }
    }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Osvjetljenje", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void SetConfig(LightingConfig cfg)
    {
        lock (_lock)
        {
            _cfg = cfg ?? new LightingConfig();
            RebuildBase();
        }
        ForceResend();
    }

    public void SetKeys(IEnumerable<KeyDef> defs)
    {
        var list = defs.ToList();
        float maxX = Math.Max(1, list.Max(k => k.CenterX));
        float maxY = Math.Max(1, list.Max(k => k.CenterY));
        lock (_lock)
        {
            _keys = list.Select(k => new LightKey
            {
                Id = k.Id,
                Zone = k.Zone,
                UX = k.CenterX,
                UY = k.CenterY,
                NX = k.CenterX / maxX,
                NY = k.CenterY / maxY
            }).ToArray();
            RebuildBase();
        }
        ForceResend();
    }

    private void RebuildBase()
    {
        var c1 = ColorUtil.Parse(_cfg.Color1, Color.DeepSkyBlue);
        var map = _cfg.KeyColors ?? new Dictionary<string, string>();
        _base = _keys.Select(k => map.TryGetValue(k.Id, out var h) ? ColorUtil.Parse(h, c1) : c1).ToArray();
    }

    public void ForceResend()
    {
        lock (_sentValid) Array.Clear(_sentValid);
    }

    public void NotifyKeyPress(string keyId)
    {
        LightKey key;
        lock (_lock) key = _keys.FirstOrDefault(k => k.Id == keyId);
        if (key != null) _renderer.AddPress(key, _clock.Elapsed.TotalSeconds);
    }

    public void CopySnapshot(Color[] dst)
    {
        lock (_snapshot) Array.Copy(_snapshot, dst, Math.Min(dst.Length, _snapshot.Length));
    }

    private void Loop()
    {
        // Bez ovoga Windows "spava" u koracima od ~15,6 ms pa animacije trzaju.
        try { timeBeginPeriod(1); } catch { }
        try { LoopCore(); }
        finally { try { timeEndPeriod(1); } catch { } }
    }

    private void LoopCore()
    {
        while (!_stop)
        {
            long start = _clock.ElapsedMilliseconds;
            LightingConfig cfg;
            LightKey[] keys;
            Color[] bas;
            lock (_lock)
            {
                cfg = _cfg;
                keys = _keys;
                bas = _base;
            }

            Array.Clear(_frame);
            try
            {
                int test = TestZone;
                if (test >= 0 && test < 256)
                {
                    _frame[test] = Color.White;
                    _everUsed[test] = true;
                }
                else if (!LightsOff)
                {
                    _renderer.Render(cfg, keys, bas, _clock.Elapsed.TotalSeconds, _frame);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            lock (_snapshot) Array.Copy(_frame, _snapshot, 256);

            if (_kb.IsConnected) Send(keys);
            UpdateStats();

            int fps = Math.Clamp(Fps, 5, 60);
            int wait = 1000 / fps - (int)(_clock.ElapsedMilliseconds - start);
            Thread.Sleep(Math.Max(1, wait));
        }
    }

    private void Send(LightKey[] keys)
    {
        foreach (var k in keys) _everUsed[k.Zone] = true;

        var changes = new List<(byte Zone, Color Color)>();
        lock (_sentValid)
        {
            for (int z = 0; z < 256; z++)
            {
                if (!_everUsed[z]) continue;
                var c = _frame[z];
                if (_sentValid[z] && !Differs(_sent[z], c)) continue;
                changes.Add(((byte)z, Color.FromArgb(255, c.R, c.G, c.B)));
            }
        }
        if (changes.Count == 0) return;

        long t0 = _clock.ElapsedTicks;
        try
        {
            _kb.SendColors(changes);
            _framesThisSecond++;
            _sendMsAccum += (_clock.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency;
            lock (_sentValid)
            {
                foreach (var (z, c) in changes)
                {
                    _sent[z] = c;
                    _sentValid[z] = true;
                }
            }
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ForceResend();
            Thread.Sleep(200);
        }
    }

    /// <summary>
    /// Sitne promjene (koje oko ne vidi) se ne šalju, što kod glatkih efekata
    /// drastično smanjuje broj USB paketa. Gašenje tipke se uvijek šalje.
    /// </summary>
    private static bool Differs(Color sent, Color now)
    {
        int d = Math.Abs(sent.R - now.R) + Math.Abs(sent.G - now.G) + Math.Abs(sent.B - now.B);
        if (d == 0) return false;
        if (now.R == 0 && now.G == 0 && now.B == 0) return true;
        return d > 6;
    }

    private void UpdateStats()
    {
        long now = _clock.ElapsedMilliseconds;
        long span = now - _fpsWindowStart;
        if (span < 1000) return;
        MeasuredFps = _framesThisSecond * 1000.0 / span;
        SendMs = _framesThisSecond > 0 ? _sendMsAccum / _framesThisSecond : 0;
        _framesThisSecond = 0;
        _sendMsAccum = 0;
        _fpsWindowStart = now;
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(1000);
        _renderer.Dispose();
    }
}

// ---------------------------------------------------------------- Zvuk (WASAPI peak meter)

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCom { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
    [PreserveSig] int GetMeteringChannelCount(out int count);
    [PreserveSig] int GetChannelsPeakValues(int count, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] values);
    [PreserveSig] int QueryHardwareSupport(out int mask);
}

internal sealed class AudioMeter : IDisposable
{
    private IAudioMeterInformation _meter;
    private DateTime _created, _lastTry;
    private float[] _buf = new float[8];

    public bool TryGetPeaks(out float left, out float right)
    {
        left = right = 0;
        try
        {
            // povremeno ponovno spajanje, za slučaj da se promijeni zadani izlaz zvuka
            if (_meter != null && (DateTime.UtcNow - _created).TotalSeconds > 5) Release();
            if (_meter == null)
            {
                if ((DateTime.UtcNow - _lastTry).TotalSeconds < 2) return false;
                _lastTry = DateTime.UtcNow;
                _meter = Create();
                _created = DateTime.UtcNow;
            }
            _meter.GetMeteringChannelCount(out int n);
            if (n >= 2)
            {
                if (_buf.Length < n) _buf = new float[n];
                var arr = new float[n];
                _meter.GetChannelsPeakValues(n, arr);
                left = arr[0];
                right = arr[1];
            }
            else
            {
                _meter.GetPeakValue(out float p);
                left = right = p;
            }
            return true;
        }
        catch
        {
            Release();
            return false;
        }
    }

    private static IAudioMeterInformation Create()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        Marshal.ThrowExceptionForHR(en.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out IMMDevice dev));
        Guid iid = typeof(IAudioMeterInformation).GUID;
        Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object o));
        return (IAudioMeterInformation)o;
    }

    private void Release()
    {
        if (_meter != null)
        {
            try { Marshal.ReleaseComObject(_meter); } catch { }
            _meter = null;
        }
    }

    public void Dispose() => Release();
}

// ---------------------------------------------------------------- Zaslon

internal sealed class ScreenSampler : IDisposable
{
    private const int W = 48, H = 16;
    private readonly Bitmap _bmp = new(W, H, PixelFormat.Format32bppRgb);
    private readonly int[] _px = new int[W * H];

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dst, int xd, int yd, int wd, int hd,
        IntPtr src, int xs, int ys, int ws, int hs, int rop);

    public void Capture()
    {
        try
        {
            var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                using var g = Graphics.FromImage(_bmp);
                IntPtr hdc = g.GetHdc();
                try
                {
                    SetStretchBltMode(hdc, 4 /* HALFTONE */);
                    SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                    StretchBlt(hdc, 0, 0, W, H, screenDc, b.X, b.Y, b.Width, b.Height, 0x00CC0020 /* SRCCOPY */);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }

            var data = _bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < H; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, _px, y * W, W);
            }
            finally
            {
                _bmp.UnlockBits(data);
            }
        }
        catch
        {
            // npr. zaključan zaslon / UAC – samo preskoči
        }
    }

    public Color Sample(float nx, float ny)
    {
        int x = Math.Clamp((int)(nx * (W - 1) + 0.5f), 0, W - 1);
        int y = Math.Clamp((int)(ny * (H - 1) + 0.5f), 0, H - 1);
        int v = _px[y * W + x];
        return Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public void Dispose() => _bmp.Dispose();
}

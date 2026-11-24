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

    // stanje za zvučne i nasumične efekte
    private AudioAnalyzer _analyzer;
    private readonly float[] _bands = new float[16];
    private readonly float[] _bandHold = new float[16];
    private float _bassAvg, _beatEnv, _fastLevel;
    private double _lastBeat = -10, _lastT;
    private int _beatCount;
    private readonly Random _rnd = new();
    private double[] _keyA = Array.Empty<double>(), _keyB = Array.Empty<double>();
    private Color[] _keyFrom = Array.Empty<Color>(), _keyTo = Array.Empty<Color>();
    private readonly List<(float X, double Y, double V)> _drops = new();
    private double _flashT = -10, _flashDur;

    public static bool IsAudioEffect(EffectType e) => e is EffectType.Audio or EffectType.Spectrum or EffectType.BassPulse;

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
        var ep = cfg.Current;                                    // boje i brzina ovog efekta
        var c1 = ColorUtil.Parse(ep.Color1, Color.DeepSkyBlue);
        var c2 = ColorUtil.Parse(ep.Color2, Color.Red);
        var bg = ColorUtil.Parse(ep.Background, Color.Black);
        double speed = Math.Clamp(ep.Speed, 1, 100) / 50.0;    // 0.02 .. 2
        // "duga" ili vlastite boje: h ide 0..1 kroz Boju 1 → Boju 2 → natrag
        Color Pal(double h) => ep.Rainbow ? ColorUtil.Hsv(h, 1, 1)
            : ColorUtil.Lerp(c1, c2, 0.5 - 0.5 * Math.Cos(2 * Math.PI * (h - Math.Floor(h))));

        if (cfg.Effect != EffectType.Audio) { _audio?.Dispose(); _audio = null; }
        if (!IsAudioEffect(cfg.Effect)) { _analyzer?.Dispose(); _analyzer = null; }
        double dt = Math.Clamp(t - _lastT, 0, 0.25);
        _lastT = t;
        if (_keyA.Length != keys.Length)
        {
            _keyA = new double[keys.Length];
            _keyB = new double[keys.Length];
            _keyFrom = new Color[keys.Length];
            _keyTo = new Color[keys.Length];
            for (int i = 0; i < keys.Length; i++) { _keyA[i] = -10; _keyFrom[i] = _keyTo[i] = bg; }
        }
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
                var c = Pal(t * 0.08 * speed);
                foreach (var key in keys) frame[key.Zone] = c;
                break;
            }

            case EffectType.Wave:
                foreach (var key in keys)
                {
                    double pos = ep.Direction switch
                    {
                        1 => 1 - key.NX,
                        2 => key.NY * 0.35,
                        3 => Math.Sqrt(Math.Pow(key.NX - 0.5, 2) + Math.Pow((key.NY - 0.5) * 0.35, 2)),
                        _ => key.NX
                    };
                    frame[key.Zone] = Pal(pos - t * 0.25 * speed);
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
                // vršna razina sa zvučnika (bez kašnjenja), trenutni porast, brzi pad
                _audio ??= new AudioMeter();
                float l = 0, r = 0;
                if (_audio.TryGetPeaks(out float pl, out float pr)) { l = pl; r = pr; }
                double tau = Math.Clamp(0.12 / speed, 0.03, 0.6);
                double decay = Math.Exp(-dt / tau);
                _lvlL = (float)Math.Max(l, _lvlL * decay);
                _lvlR = (float)Math.Max(r, _lvlR * decay);
                const int steps = 12;   // diskretni koraci: tipke istog koraka dijele boju → manje USB paketa
                foreach (var key in keys)
                {
                    double d = Math.Abs(key.NX - 0.5) * 2;
                    double lvl = Math.Min(1, (key.NX < 0.5 ? _lvlL : _lvlR) * 1.25);
                    int lit = (int)Math.Round(lvl * steps);
                    int step = Math.Min(steps - 1, (int)(d * steps));
                    frame[key.Zone] = step < lit ? ColorUtil.Lerp(c1, c2, step / (double)(steps - 1)) : bg;
                }
                break;
            }

            case EffectType.Spectrum:
            {
                // spektar: stupci = frekvencije (lijevo bas, desno visoki), visina = jačina
                _analyzer ??= StartAnalyzer();
                _analyzer.Analyze(_bands, out _, out _);
                double fall = dt * Math.Clamp(2.2 * speed, 0.4, 6);
                for (int b2 = 0; b2 < _bands.Length; b2++)
                    _bandHold[b2] = (float)Math.Max(_bands[b2], _bandHold[b2] - fall);
                const int rows = 7;
                foreach (var key in keys)
                {
                    int band = Math.Clamp((int)(key.NX * _bands.Length), 0, _bands.Length - 1);
                    double h = 1 - key.NY;                       // 0 dolje, 1 gore
                    int row = Math.Clamp((int)(h * rows), 0, rows - 1);
                    int litRows = (int)Math.Round(_bandHold[band] * rows);
                    frame[key.Zone] = row < litRows ? ColorUtil.Lerp(c1, c2, row / (double)(rows - 1)) : bg;
                }
                break;
            }

            case EffectType.BassPulse:
            {
                // bas puls: na svaki udarac basa tipkovnica bljesne i promijeni boju
                _analyzer ??= StartAnalyzer();
                _analyzer.Analyze(_bands, out float level, out float bass);
                _bassAvg = _bassAvg <= 0 ? bass : _bassAvg + (bass - _bassAvg) * (float)Math.Min(1, dt * 2.5);
                if (bass > 0.35f && bass > _bassAvg * 1.18f && t - _lastBeat > 0.11)
                {
                    _lastBeat = t;
                    _beatCount++;
                    _beatEnv = 1;
                }
                _beatEnv *= (float)Math.Exp(-dt / Math.Clamp(0.18 / speed, 0.05, 0.8));
                _fastLevel = Math.Max(level, _fastLevel * (float)Math.Exp(-dt / 0.15));
                var beatColor = _beatCount % 2 == 0 ? c1 : c2;
                double k = Math.Min(1, _beatEnv + _fastLevel * 0.25);
                k = Math.Round(k * 16) / 16;
                var col = ColorUtil.Lerp(bg, beatColor, k);
                foreach (var key in keys) frame[key.Zone] = col;
                break;
            }

            case EffectType.Swirl:
            {
                // spiralna duga: boja ovisi o kutu oko središta, okreće se
                float cx = keys.Average(k2 => k2.UX), cy = keys.Average(k2 => k2.UY);
                foreach (var key in keys)
                {
                    double ang = Math.Atan2(key.UY - cy, key.UX - cx) / (2 * Math.PI);
                    frame[key.Zone] = Pal(ang + t * 0.15 * speed);
                }
                break;
            }

            case EffectType.RadialRainbow:
            {
                float cx = keys.Average(k2 => k2.UX), cy = keys.Average(k2 => k2.UY);
                foreach (var key in keys)
                {
                    double d = Math.Sqrt(Math.Pow(key.UX - cx, 2) + Math.Pow(key.UY - cy, 2));
                    frame[key.Zone] = Pal(d / 14.0 - t * 0.25 * speed);
                }
                break;
            }

            case EffectType.GradientWave:
                foreach (var key in keys)
                {
                    double pos = ep.Direction switch { 1 => 1 - key.NX, 2 => key.NY, 3 => Math.Abs(key.NX - 0.5) * 2, _ => key.NX };
                    double k = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (pos - t * 0.25 * speed));
                    frame[key.Zone] = ColorUtil.Lerp(c1, c2, k);
                }
                break;

            case EffectType.Starry:
            {
                // zvjezdano nebo: nasumične tipke polako zasvijetle i ugase se
                double dur = Math.Clamp(1.6 / speed, 0.3, 6);
                double spawn = dt * keys.Length * 0.09 * speed;
                while (spawn > 0)
                {
                    if (spawn >= 1 || _rnd.NextDouble() < spawn)
                    {
                        int i = _rnd.Next(keys.Length);
                        if (t - _keyA[i] > dur) { _keyA[i] = t; _keyB[i] = _rnd.NextDouble(); }
                    }
                    spawn -= 1;
                }
                for (int i = 0; i < keys.Length; i++)
                {
                    double age = t - _keyA[i];
                    double k = age >= 0 && age < dur ? Math.Sin(Math.PI * age / dur) : 0;
                    var star = ColorUtil.Lerp(c1, c2, _keyB[i]);
                    frame[keys[i].Zone] = ColorUtil.Lerp(bg, star, k);
                }
                break;
            }

            case EffectType.Rain:
            {
                // kiša / matrix: kapi padaju po stupcima i ostavljaju trag
                double v = 9 * speed;
                double spawnRate = dt * 7 * speed;
                while (spawnRate > 0)
                {
                    if (spawnRate >= 1 || _rnd.NextDouble() < spawnRate)
                    {
                        var k0 = keys[_rnd.Next(keys.Length)];
                        _drops.Add((k0.UX, keys.Min(k2 => k2.UY) - 1, v * (0.7 + _rnd.NextDouble() * 0.6)));
                    }
                    spawnRate -= 1;
                }
                float maxY = keys.Max(k2 => k2.UY) + 4;
                for (int d2 = _drops.Count - 1; d2 >= 0; d2--)
                {
                    var dr = _drops[d2];
                    dr.Y += dr.V * dt;
                    if (dr.Y > maxY) _drops.RemoveAt(d2); else _drops[d2] = dr;
                }
                const double trail = 3.0;
                foreach (var key in keys)
                {
                    double best = 0;
                    foreach (var dr in _drops)
                    {
                        if (Math.Abs(key.UX - dr.X) > 0.55) continue;
                        double behind = dr.Y - key.UY;
                        if (behind < -0.5 || behind > trail) continue;
                        double k = behind < 0.5 ? 1 : 1 - (behind - 0.5) / (trail - 0.5);
                        if (k > best) best = k;
                    }
                    var head = best > 0.95 ? c2 : c1;
                    frame[key.Zone] = ColorUtil.Lerp(bg, head, Math.Round(best * 8) / 8);
                }
                break;
            }

            case EffectType.Fire:
            {
                // vatra: najtoplije dolje, treperi prema gore
                foreach (var key in keys)
                {
                    double n = Noise(key.UX * 0.55, key.UY * 0.5 + t * 2.2 * speed);
                    double heat = Math.Clamp(key.NY * 0.95 - 0.2 + n * 0.75, 0, 1);
                    heat = Math.Round(heat * 12) / 12;
                    frame[key.Zone] = FireColor(heat, bg, c1, c2);
                }
                break;
            }

            case EffectType.Visor:
            {
                // skener: svjetlosna traka putuje lijevo-desno i ostavlja trag
                double p = 0.5 - 0.5 * Math.Cos(t * 1.4 * speed);
                double dir = Math.Sin(t * 1.4 * speed);
                foreach (var key in keys)
                {
                    double d = key.NX - p;
                    double k = Math.Abs(d) < 0.04 ? 1 : (Math.Sign(d) == -Math.Sign(dir) ? Math.Max(0, 1 - (Math.Abs(d) - 0.04) / 0.25) : Math.Max(0, 1 - (Math.Abs(d) - 0.04) / 0.05));
                    frame[key.Zone] = k >= 0.999 ? c1 : ColorUtil.Lerp(bg, c2, Math.Round(k * 10) / 10);
                }
                break;
            }

            case EffectType.Lightning:
            {
                // munja: nasumični bljeskovi cijele tipkovnice
                if (t - _flashT > _flashDur && _rnd.NextDouble() < dt * 0.6 * speed)
                {
                    _flashT = t;
                    _flashDur = 0.15 + _rnd.NextDouble() * 0.35;
                }
                double age = t - _flashT;
                double k = 0, flick = 0;
                if (age >= 0 && age < _flashDur)
                {
                    flick = Math.Abs(Math.Sin(age * 60));
                    k = (1 - age / _flashDur) * (0.6 + 0.4 * flick);
                }
                var col = ColorUtil.Lerp(bg, ColorUtil.Lerp(c2, c1, flick), Math.Round(k * 12) / 12);
                foreach (var key in keys) frame[key.Zone] = col;
                break;
            }

            case EffectType.Mosaic:
            {
                // mozaik: svaka tipka povremeno glatko prijeđe u novu nasumičnu boju
                double hold = Math.Clamp(2.5 / speed, 0.3, 10);
                double fade = Math.Clamp(0.8 / speed, 0.1, 3);
                for (int i = 0; i < keys.Length; i++)
                {
                    if (t - _keyA[i] > hold + _keyB[i] * hold)
                    {
                        _keyFrom[i] = ColorUtil.Lerp(_keyFrom[i], _keyTo[i], 1);
                        _keyTo[i] = ep.Rainbow
                            ? ColorUtil.Hsv(_rnd.NextDouble(), 0.85 + _rnd.NextDouble() * 0.15, 1)
                            : ColorUtil.Lerp(c1, c2, _rnd.NextDouble());
                        _keyA[i] = t;
                        _keyB[i] = _rnd.NextDouble();
                    }
                    double k = Math.Clamp((t - _keyA[i]) / fade, 0, 1);
                    frame[keys[i].Zone] = ColorUtil.Lerp(_keyFrom[i], _keyTo[i], k);
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

    private static AudioAnalyzer StartAnalyzer()
    {
        var a = new AudioAnalyzer();
        a.Start();
        return a;
    }

    private static Color FireColor(double h, Color bg, Color baseFlame, Color tips)
    {
        // pozadina → plamen (Boja 1) → vrhovi plamena (Boja 2)
        if (h < 0.5) return ColorUtil.Lerp(bg, baseFlame, h / 0.5);
        return ColorUtil.Lerp(baseFlame, tips, (h - 0.5) / 0.5);
    }

    // glatki 2D šum vrijednosti (0..1)
    private static double Noise(double x, double y)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        double xf = x - xi, yf = y - yi;
        double u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        double a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static double Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / (double)int.MaxValue;
        }
    }

    public void Dispose()
    {
        _audio?.Dispose();
        _analyzer?.Dispose();
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
    public double PacketsPerFrame { get; private set; }
    private double _packetsAccum;
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

    /// <summary>Miš koji prati efekt (null = nema). Zone miša se ne šalju tipkovnici.</summary>
    public LogitechMouse Mouse { get; set; }
    public volatile bool MouseFollow = true;
    private readonly bool[] _mouseZone = new bool[256];
    private int[] _mouseZones = Array.Empty<int>();

    public void SetKeys(IEnumerable<KeyDef> defs)
    {
        var mz = defs.Where(k => k.Shape == "mouse").Select(k => (int)k.Zone).ToArray();
        lock (_lock)
        {
            Array.Clear(_mouseZone);
            foreach (int z in mz) _mouseZone[z] = true;
            _mouseZones = mz;
        }
        var list = defs.ToList();
        float minX = list.Min(k => k.CenterX), minY = list.Min(k => k.CenterY);
        float spanX = Math.Max(1, list.Max(k => k.CenterX) - minX);
        float spanY = Math.Max(1, list.Max(k => k.CenterY) - minY);
        lock (_lock)
        {
            _keys = list.Select(k => new LightKey
            {
                Id = k.Id,
                Zone = k.Zone,
                UX = k.CenterX,
                UY = k.CenterY,
                NX = (k.CenterX - minX) / spanX,
                NY = (k.CenterY - minY) / spanY
            }).ToArray();
            RebuildBase();
        }
        ForceResend();
    }

    private void RebuildBase()
    {
        var e = _cfg.Effect is EffectType.Static or EffectType.Breathing or EffectType.Reactive or EffectType.Ripple
            ? _cfg.Effect : EffectType.Static;
        var c1 = ColorUtil.Parse(_cfg.For(e).Color1, Color.DeepSkyBlue);
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
                if (test >= 0 && test < 0xF0)
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
            PushMouse();
            UpdateStats();

            int fps = Math.Clamp(Fps, 5, 60);
            if (EffectRenderer.IsAudioEffect(cfg.Effect)) fps = Math.Max(fps, 60);
            int wait = 1000 / fps - (int)(_clock.ElapsedMilliseconds - start);
            Thread.Sleep(Math.Max(1, wait));
        }
    }

    private void PushMouse()
    {
        var m = Mouse;
        if (m == null || !MouseFollow || !m.IsConnected) return;
        int[] zones;
        lock (_lock) zones = _mouseZones;
        if (zones.Length == 0) return;
        var cols = new Color[zones.Length];
        for (int i = 0; i < zones.Length; i++) cols[i] = _frame[zones[i]];
        m.SetColors(cols);
    }

    private void Send(LightKey[] keys)
    {
        // zone 0xF0+ su virtualne zone miša i nikad ne idu tipkovnici
        foreach (var k in keys) if (!_mouseZone[k.Zone] && k.Zone < 0xF0) _everUsed[k.Zone] = true;
        for (int z = 0xF0; z < 256; z++) _everUsed[z] = false;

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
            var current = new List<(byte Zone, Color Color)>();
            for (int z = 1; z < 256; z++)
                if (_everUsed[z] && _sentValid[z]) current.Add(((byte)z, _sent[z]));
            _kb.SendColors(changes, current);
            _framesThisSecond++;
            _packetsAccum += _kb.LastPacketCount;
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
        catch (HidppException ex) when (ex.ErrorCode == 0x08)
        {
            // zauzeto i nakon ponavljanja: ne šalji ponovno cijelu tipkovnicu,
            // promjene iz ovog okvira ionako nisu označene kao poslane pa idu u idućem
            Thread.Sleep(10);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            ForceResend();
            Thread.Sleep(50);
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
        PacketsPerFrame = _framesThisSecond > 0 ? _packetsAccum / _framesThisSecond : 0;
        _packetsAccum = 0;
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
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
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

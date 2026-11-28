namespace G815Studio;

/// <summary>
/// Kingston Fury DDR4/DDR5 RGB memorija preko SMBus-a (PawnIO). RGB čip modula u utoru n je na adresi 0x60+n (DDR5)
/// ili 0x58+n (DDR4); prepoznaje se po potpisu „FURY“ u registrima 1–4. Program modul prebacuje u izravni način (0x10)
/// i po okviru upisuje boje LED-ica (DDR5: 12, DDR4: 10). Svaka izmjena je „transakcija“: 0x08 ← 0x53, upisi, 0x08 ← 0x44.
/// Protokol je opisan prema javno dostupnom radu projekta OpenRGB. U SPD (0x50–0x57) se nikad ništa ne upisuje.
/// </summary>
public sealed class KingstonFury : IDisposable
{
    private const byte BaseDdr4 = 0x58, BaseDdr5 = 0x60;
    private const byte RegModel = 0x06, RegApply = 0x08, RegMode = 0x09, RegIndex = 0x0B, RegDirection = 0x0C,
                       RegDelay = 0x0D, RegSpeed = 0x0E, RegBrightness = 0x20, RegNumSlots = 0x27, RegBaseRed = 0x50;
    private const byte Begin = 0x53, End = 0x44, ModeStatic = 0x00, ModeDirect = 0x10;
    private const int Delay = 10;   // ms, čip je spor

    private readonly object _lock = new();
    private PawnIoSmbus _bus;
    private readonly List<byte> _addrs = new();
    private int _ledsPerStick = 12;
    private readonly Dictionary<(byte Addr, byte Reg), byte> _cache = new();
    private byte _origMode, _origBrightness = 80;
    private Color[] _wanted, _sent;
    private Thread _thread;
    private volatile bool _stop;
    private readonly AutoResetEvent _wake = new(false);

    public bool IsConnected { get { lock (_lock) return _bus != null && _addrs.Count > 0; } }
    public int StickCount { get { lock (_lock) return _addrs.Count; } }
    public int LedsPerStick => _ledsPerStick;
    public string Status { get; private set; } = "Memorija nije spojena.";
    /// <summary>Svjetlina u izravnom načinu (0–100).</summary>
    public volatile int Brightness = 100;
    public event Action Changed;

    // ------------------------------------------------------------ spajanje

    public bool TryConnect()
    {
        lock (_lock)
        {
            if (_bus != null) return _addrs.Count > 0;
            string err = PawnIoHost.Ensure();
            if (err != null) { Status = err; Log.Write("RAM: " + err); return false; }
            var bus = new PawnIoSmbus();
            try { err = bus.Open(); }
            catch (Exception ex) { err = "PawnIO greška: " + ex.Message; }
            if (err != null) { bus.Dispose(); Status = err; Log.Write("RAM: " + err); return false; }
            _bus = bus;

            foreach (byte baseAddr in new[] { BaseDdr5, BaseDdr4 })
            {
                for (byte slot = 0; slot < 8; slot++)
                {
                    byte a = (byte)(baseAddr + slot);
                    int model = Probe(a);
                    if (model < 0) continue;
                    _addrs.Add(a);
                    Log.Write($"RAM: Kingston Fury na 0x{a:X2}, model 0x{model:X2}");
                }
                if (_addrs.Count > 0)
                {
                    _ledsPerStick = baseAddr == BaseDdr5 ? 12 : 10;
                    break;
                }
            }
            if (_addrs.Count == 0)
            {
                Status = "Kingston Fury memorija nije pronađena (je li SMBus zauzet drugim programom, npr. SignalRGB?).";
                Log.Write("RAM: " + Status);
                Drop();
                return false;
            }

            _origMode = ReadReg(_addrs[0], RegMode) is int m and >= 0 ? (byte)m : ModeStatic;
            _origBrightness = ReadReg(_addrs[0], RegBrightness) is int b and > 0 and <= 100 ? (byte)b : (byte)80;
            Log.Write($"RAM: prijašnji način 0x{_origMode:X2}, svjetlina {_origBrightness}");
            EnterDirect();
            _wanted = new Color[_addrs.Count * _ledsPerStick];
            _sent = null;
            Status = $"Spojeno: Kingston Fury, {_addrs.Count} modula ({string.Join(", ", _addrs.Select(x => $"0x{x:X2}"))}), {_ledsPerStick} LED-ica po modulu";
            Log.Write("RAM: " + Status);
            StartThread();
        }
        ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
        return true;
    }

    /// <summary>Provjeri potpis „FURY“ na adresi. Vraća kod modela ili -1. Upisuje samo početak/kraj transakcije u RGB čip.</summary>
    private int Probe(byte a)
    {
        if (!_bus.WriteByteData(a, RegApply, Begin)) return -1;
        Thread.Sleep(Delay);
        int result = -1;
        try
        {
            const string sig = "FURY";
            for (int i = 1; i <= 4; i++)
            {
                int w = ReadReg(a, (byte)i);
                if (w < 0 || w != sig[i - 1]) return -1;
            }
            result = ReadReg(a, RegModel);
        }
        finally
        {
            _bus.WriteByteData(a, RegApply, End);
            Thread.Sleep(Delay);
        }
        return result;
    }

    /// <summary>Vrijednost registra je u gornjem bajtu riječi; ponavlja do 5 puta.</summary>
    private int ReadReg(byte a, byte reg)
    {
        for (int t = 1; t <= 5; t++)
        {
            int w = _bus.ReadWordData(a, reg);
            if (w >= 0 && w != 0xFFFF) return (w >> 8) & 0xFF;
            Thread.Sleep(3 * t * Delay / 2);
        }
        return -1;
    }

    private void Write(byte a, byte reg, byte value, bool cached = true)
    {
        if (cached && _cache.TryGetValue((a, reg), out byte old) && old == value) return;
        for (int t = 1; t <= 5; t++)
        {
            if (_bus.WriteByteData(a, reg, value))
            {
                _cache[(a, reg)] = value;
                return;
            }
            Thread.Sleep(3 * t * Delay / 2);
        }
        throw new IOException($"RAM 0x{a:X2}: upis u registar 0x{reg:X2} nije uspio");
    }

    private void BeginAll()
    {
        foreach (var a in _addrs) Write(a, RegApply, Begin, false);
        Thread.Sleep(Delay);
    }

    private void EndAll()
    {
        foreach (var a in _addrs) Write(a, RegApply, End, false);
        Thread.Sleep(Delay);
    }

    private void EnterDirect()
    {
        // uvod pri promjeni načina: svi moduli indeks 0 (izravni način ih ne sinkronizira)
        BeginAll();
        foreach (var a in _addrs) Write(a, RegIndex, 0, false);
        Thread.Sleep(Delay);
        EndAll();

        BeginAll();
        foreach (var a in _addrs)
        {
            Write(a, RegMode, ModeDirect, false);
            Write(a, RegBrightness, (byte)Math.Clamp(Brightness, 0, 100), false);
            Write(a, RegNumSlots, (byte)Math.Min(4, _addrs.Count), false);
        }
        EndAll();
    }

    // ------------------------------------------------------------ boje

    /// <summary>Boje virtualnih zona: po modulu <paramref name="segmentsPerStick"/> zona odozdo prema gore, s prijelazom između njih.</summary>
    public void SetSegmentColors(Color[] segs, int segmentsPerStick)
    {
        lock (_lock)
        {
            if (_wanted == null || segmentsPerStick <= 0) return;
            int n = _ledsPerStick;
            for (int s = 0; s < _addrs.Count; s++)
            {
                for (int i = 0; i < n; i++)
                {
                    int baseIdx = s * segmentsPerStick;
                    Color col;
                    if (baseIdx + segmentsPerStick > segs.Length) col = Color.Black;
                    else if (segmentsPerStick == 1) col = segs[baseIdx];
                    else
                    {
                        double pos = (double)i / (n - 1) * (segmentsPerStick - 1);
                        int k = Math.Min((int)pos, segmentsPerStick - 2);
                        var c1 = segs[baseIdx + k];
                        var c2 = segs[baseIdx + k + 1];
                        double t = pos - k;
                        col = Color.FromArgb((int)(c1.R + (c2.R - c1.R) * t), (int)(c1.G + (c2.G - c1.G) * t), (int)(c1.B + (c2.B - c1.B) * t));
                    }
                    _wanted[s * n + i] = col;
                }
            }
        }
        _wake.Set();
    }

    public void SetAll(Color c)
    {
        lock (_lock)
        {
            if (_wanted == null) return;
            Array.Fill(_wanted, c);
        }
        _wake.Set();
    }

    private void StartThread()
    {
        _stop = false;
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "RAM RGB" };
        _thread.Start();
    }

    private void Loop()
    {
        int failures = 0;
        while (!_stop)
        {
            _wake.WaitOne(500);
            if (_stop) break;
            try
            {
                lock (_lock)
                {
                    if (_bus == null || _wanted == null) continue;
                    if (_sent != null && _sent.SequenceEqual(_wanted)) continue;
                    SendFrame(_wanted);
                    _sent = (Color[])_wanted.Clone();
                }
                failures = 0;
            }
            catch (Exception ex)
            {
                Log.Write("RAM: " + ex.Message);
                lock (_lock) _cache.Clear();
                if (++failures >= 5)
                {
                    Status = "Memorija ne odgovara (SMBus zauzet?).";
                    Thread.Sleep(2000);
                    failures = 0;
                }
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>Jedan okvir: početak, promijenjene boje (R, B, G kao Kingstonov program), svjetlina, kraj.</summary>
    private void SendFrame(Color[] cols)
    {
        BeginAll();
        int n = _ledsPerStick;
        for (int s = 0; s < _addrs.Count; s++)
        {
            byte a = _addrs[s];
            Write(a, RegMode, ModeDirect);
            for (int i = 0; i < n; i++)
            {
                var c = cols[s * n + i];
                byte r = (byte)(RegBaseRed + 3 * i);
                Write(a, r, c.R);
                Write(a, (byte)(r + 2), c.B);
                Write(a, (byte)(r + 1), c.G);
            }
            Write(a, RegBrightness, (byte)Math.Clamp(Brightness, 0, 100));
        }
        EndAll();
    }

    // ------------------------------------------------------------ otpuštanje

    /// <param name="stayDark">true = moduli ostaju ugašeni (crna, svjetlina 0); inače se vraća prijašnji način</param>
    public void Release(bool stayDark = false)
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(1500);
        _thread = null;
        lock (_lock)
        {
            if (_bus != null && _addrs.Count > 0)
            {
                try
                {
                    if (stayDark)
                    {
                        // statični način, crna boja i svjetlina 0: modul to pamti i nakon izlaza
                        BeginAll();
                        foreach (var a in _addrs)
                        {
                            Write(a, RegMode, ModeStatic, false);
                            Write(a, 0x30, 1, false);                       // jedna boja načina
                            Write(a, 0x31, 0, false); Write(a, 0x32, 0, false); Write(a, 0x33, 0, false);
                            Write(a, RegDirection, 1, false);
                            Write(a, RegDelay, 0, false);
                            Write(a, RegSpeed, 0, false);
                            Write(a, RegBrightness, 0, false);
                        }
                        EndAll();
                        Log.Write("RAM: memorija ugašena.");
                    }
                    else
                    {
                        BeginAll();
                        foreach (var a in _addrs)
                        {
                            Write(a, RegMode, _origMode, false);
                            Write(a, RegBrightness, _origBrightness, false);
                        }
                        EndAll();
                        Log.Write("RAM: vraćen prijašnji način memorije.");
                    }
                }
                catch (Exception ex) { Log.Write("RAM: otpuštanje nije uspjelo: " + ex.Message); }
            }
            Drop();
            Status = "Upravljanje memorijom isključeno.";
        }
        ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
    }

    private void Drop()
    {
        _bus?.Dispose();
        _bus = null;
        _addrs.Clear();
        _cache.Clear();
        _wanted = null;
        _sent = null;
    }

    public void Dispose() => Release();
}

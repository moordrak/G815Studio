namespace G815Studio;

/// <summary>
/// Visoka razina upravljanja tipkovnicom G815 preko HID++ 2.0.
/// Indeksi značajki se otkrivaju dinamički, pa se ništa ne pretpostavlja "na slijepo".
/// </summary>
public sealed class G815Keyboard : IDisposable
{
    public const ushort LogitechVid = 0x046D;

    // G815 = 0xC33F. G915 / G915 TKL žičano koriste isti protokol, pa ih dopuštamo kao bonus.
    public static readonly ushort[] SupportedPids = { 0xC33F, 0xC33E, 0xC343 };

    // HID++ 2.0 značajke
    private const ushort FeatDeviceName = 0x0005;
    private const ushort FeatGKeys = 0x8010;
    private const ushort FeatMKeys = 0x8020;
    private const ushort FeatMR = 0x8030;
    private const ushort FeatPerKeyLighting = 0x8081;
    private const ushort FeatOnboardProfiles = 0x8100;

    private readonly object _lock = new();
    private HidppDevice _dev;
    private byte _perKey, _onboard, _gkeys, _mkeys, _mr;
    private int _gMask;

    public bool IsConnected => _dev != null;
    public string DeviceName { get; private set; } = "";
    public string FeatureSummary { get; private set; } = "";
    public bool HasGKeys => _gkeys != 0;

    public event Action<int> GKeyDown;   // 1..5
    public event Action<int> GKeyUp;     // 1..5
    public event Action<int> MKeyPressed; // 1..3
    public event Action MrPressed;
    public event Action Disconnected;

    public bool TryConnect(out string error)
    {
        error = null;
        lock (_lock)
        {
            if (_dev != null) return true;
            List<HidDeviceInfo> all;
            try { all = HidEnumerator.Enumerate(LogitechVid); }
            catch (Exception ex) { error = "Enumeracija HID uređaja nije uspjela: " + ex.Message; return false; }

            var candidates = all
                .Where(d => SupportedPids.Contains(d.ProductId) && d.OutputReportLength == 20)
                .OrderByDescending(d => d.UsagePage == 0xFF43)
                .ThenByDescending(d => d.Usage == 0x0602)
                .ToList();

            if (candidates.Count == 0)
            {
                error = all.Any(d => SupportedPids.Contains(d.ProductId))
                    ? "Tipkovnica je pronađena, ali bez HID++ sučelja."
                    : "Tipkovnica G815 nije pronađena (je li spojena USB kabelom?).";
                return false;
            }

            foreach (var c in candidates)
            {
                Log.Write("Pokušavam: " + c);
                HidppDevice dev = null;
                try
                {
                    var stream = HidStream.Open(c);
                    dev = new HidppDevice(stream);
                    dev.Notification += OnNotification;
                    dev.ErrorReceived += OnDeviceError;
                    dev.Start();

                    byte perKey = dev.GetFeatureIndex(FeatPerKeyLighting);
                    if (perKey == 0) throw new InvalidOperationException("Uređaj ne podržava per-key osvjetljenje (0x8081).");

                    _perKey = perKey;
                    _onboard = SafeIndex(dev, FeatOnboardProfiles);
                    _gkeys = SafeIndex(dev, FeatGKeys);
                    _mkeys = SafeIndex(dev, FeatMKeys);
                    _mr = SafeIndex(dev, FeatMR);
                    FeatureSummary = $"perKey=0x{_perKey:X2} onboard=0x{_onboard:X2} gkeys=0x{_gkeys:X2} mkeys=0x{_mkeys:X2} mr=0x{_mr:X2}";
                    Log.Write("Značajke: " + FeatureSummary);

                    DeviceName = ReadName(dev) ?? $"Logitech 0x{c.ProductId:X4}";

                    // Host (softverski) način: isključuje ugrađene profile i efekte.
                    if (_onboard != 0) TryRun(() => dev.Request(_onboard, 1, 0x02), "host način");
                    // Preusmjeri G/M/MR tipke prema ovom programu.
                    if (_gkeys != 0) TryRun(() => dev.Request(_gkeys, 2, 0x01), "G tipke");

                    _gMask = 0;
                    dev.Disconnected += OnDeviceDisconnected;
                    _dev = dev;
                    Log.Write("Spojeno: " + DeviceName);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write("Neuspjelo: " + ex.Message);
                    error = ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception
                        ? "Uređaj je zauzet. Zatvori Logitech G HUB (i u trayu) pa pokušaj ponovno. (" + ex.Message + ")"
                        : ex.Message;
                    if (dev != null)
                    {
                        dev.Notification -= OnNotification;
                        dev.Dispose();
                    }
                }
            }
            return false;
        }
    }

    private static byte SafeIndex(HidppDevice dev, ushort feature)
    {
        try { return dev.GetFeatureIndex(feature); }
        catch (Exception ex) { Log.Write($"Značajka 0x{feature:X4}: {ex.Message}"); return 0; }
    }

    private static void TryRun(Action a, string what)
    {
        try { a(); }
        catch (Exception ex) { Log.Write($"{what}: {ex.Message}"); }
    }

    internal static string ReadName(HidppDevice dev)
    {
        try
        {
            byte idx = dev.GetFeatureIndex(FeatDeviceName);
            if (idx == 0) return null;
            int len = dev.Request(idx, 0)[0];
            var chars = new List<byte>();
            while (chars.Count < len)
            {
                var part = dev.Request(idx, 1, (byte)chars.Count);
                int take = Math.Min(part.Length, len - chars.Count);
                if (take <= 0) break;
                chars.AddRange(part.Take(take));
            }
            return System.Text.Encoding.ASCII.GetString(chars.ToArray()).TrimEnd('\0', ' ');
        }
        catch
        {
            return null;
        }
    }

    private void OnDeviceDisconnected()
    {
        lock (_lock)
        {
            if (_dev != null)
            {
                _dev.Notification -= OnNotification;
                _dev.Dispose();
                _dev = null;
            }
        }
        Log.Write("Tipkovnica odspojena.");
        Disconnected?.Invoke();
    }

    private void OnNotification(byte feature, byte function, byte[] p)
    {
        if (p.Length == 0) return;
        if (_gkeys != 0 && feature == _gkeys)
        {
            int mask = p[0] | (p.Length > 1 ? p[1] << 8 : 0);
            int changed = mask ^ _gMask;
            _gMask = mask;
            for (int i = 0; i < 16; i++)
            {
                int bit = 1 << i;
                if ((changed & bit) == 0) continue;
                if ((mask & bit) != 0) GKeyDown?.Invoke(i + 1);
                else GKeyUp?.Invoke(i + 1);
            }
        }
        else if (_mkeys != 0 && feature == _mkeys)
        {
            for (int i = 0; i < 3; i++)
                if ((p[0] & (1 << i)) != 0) MKeyPressed?.Invoke(i + 1);
        }
        else if (_mr != 0 && feature == _mr)
        {
            if (p[0] != 0) MrPressed?.Invoke();
        }
    }

    /// <summary>Lampice M1/M2/M3 (bitmaska: 1=M1, 2=M2, 4=M3).</summary>
    public void SetMLeds(int mask)
    {
        var d = _dev;
        if (d == null || _mkeys == 0) return;
        try { d.Request(_mkeys, 1, (byte)mask); } catch (Exception ex) { Log.Write("M LED: " + ex.Message); }
    }

    public void SetMrLed(bool on)
    {
        var d = _dev;
        if (d == null || _mr == 0) return;
        try { d.Request(_mr, 0, (byte)(on ? 1 : 0)); } catch (Exception ex) { Log.Write("MR LED: " + ex.Message); }
    }

    /// <summary>Broj paketa poslanih u zadnjem okviru (za dijagnostiku u statusnoj traci).</summary>
    public int LastPacketCount { get; private set; }
    public int BusyRetries { get; private set; }

    /// <summary>
    /// Šalje promijenjene boje. Tipke slične boje grupiraju se u jedan paket (do 13 zona),
    /// ostale idu po 4 u paketu. Na kraju ide "commit" (funkcija 7).
    /// Svaki paket čeka potvrdu tipkovnice, pa se njezin red nikad ne prepuni (greška 0x08 "zauzeto").
    /// </summary>
    public void SendColors(IReadOnlyList<(byte Zone, Color Color)> changes) => SendColors(changes, null);

    /// <param name="current">trenutne boje svih tipki tipkovnice – njima se popunjavaju prazna mjesta u malim paketima</param>
    public void SendColors(IReadOnlyList<(byte Zone, Color Color)> changes, IReadOnlyList<(byte Zone, Color Color)> current)
    {
        var dev = _dev;
        if (dev == null || _perKey == 0 || changes.Count == 0) return;
        if (changes.Any(c => _zoneBad[c.Zone])) changes = changes.Where(c => !_zoneBad[c.Zone]).ToList();
        if (changes.Count == 0) return;

        // Isto kao OpenRGB: promijenjene tipke grupiraju se po boji. Grupa od 4+ tipke ide
        // "velikim" paketom (funkcija 6: boja + do 13 tipki, ostatak 0), ostale "malim" (funkcija 1: 4× tipka+boja).
        // Mali paket G815 prihvaća samo s 4 različite postojeće tipke (s nulama ili ponavljanjem vraća 0x02),
        // pa se prazna mjesta pune drugim tipkama s njihovom trenutnom bojom.
        var packets = new List<(byte Function, byte[] Args)>();
        var singles = new List<(byte Zone, Color Color)>();
        // kod animacija (puno promjena) gotovo iste boje idu u isti veliki paket: manje paketa, a razlika se ne vidi
        int q = changes.Count > 40 ? 2 : 0;
        foreach (var group in changes.GroupBy(c => ((c.Color.R >> q) << 16) | ((c.Color.G >> q) << 8) | (c.Color.B >> q)))
        {
            var list = group.ToList();
            // veliki paket samo pun (13 tipki): G815 odbija i nule i ponavljanje na praznim mjestima
            int full = list.Count / 13 * 13;
            var col = list[0].Color;
            for (int i = 0; i < full; i += 13)
            {
                var a = new byte[16];
                a[0] = col.R; a[1] = col.G; a[2] = col.B;
                for (int j = 0; j < 13; j++) a[3 + j] = list[i + j].Zone;
                packets.Add((6, a));
            }
            singles.AddRange(list.Skip(full));
        }

        if (singles.Count % 4 != 0 && current != null)
        {
            var changed = new HashSet<byte>(changes.Select(c => c.Zone));
            foreach (var f in current)
            {
                if (singles.Count % 4 == 0) break;
                if (f.Zone == 0 || changed.Contains(f.Zone) || _zoneBad[f.Zone]) continue;
                singles.Add(f);
                changed.Add(f.Zone);
            }
        }
        for (int i = 0; i < singles.Count; i += 4)
        {
            var a = new byte[16];
            int n = Math.Min(4, singles.Count - i);
            for (int j = 0; j < n; j++)
            {
                var (zone, col) = singles[i + j];
                a[j * 4] = zone;
                a[j * 4 + 1] = col.R;
                a[j * 4 + 2] = col.G;
                a[j * 4 + 3] = col.B;
            }
            packets.Add((1, a));
        }

        Interlocked.Exchange(ref _busyInFrame, 0);
        bool pace = DateTime.UtcNow < _paceUntil;
        int sent = 0;
        foreach (var (fn, args) in packets)
        {
            // svaki paket dobiva svoj softverski ID (1–9), pa se iz greške zna koji je paket odbijen i njegove
            // se tipke pošalju ponovno. Nakon 8 paketa kratka stanka: red tipkovnice se isprazni (manje „zauzeto“),
            // a ID-ovi se ne ponove prije nego stigne eventualna greška za stariji paket.
            if (sent > 0 && (pace ? sent % 4 == 0 : sent % 8 == 0)) Thread.Sleep(1);
            _sw = (byte)(_sw % 9 + 1);
            lock (_recent) _recent[_sw] = (fn, args);
            CountSent(fn, args);
            dev.Send(_perKey, fn, args, _sw);  // WriteFile čeka dok paket ne ode na USB
            sent++;
        }

        // commit: kratko pričekaj potvrdu, ali izostanak potvrde nije greška
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                dev.Request(_perKey, 7, Array.Empty<byte>(), 30);
                break;
            }
            catch (TimeoutException)
            {
                break;
            }
            catch (HidppException ex) when (ex.ErrorCode == 0x08)
            {
                Thread.Sleep(2);
            }
        }
        LastPacketCount = packets.Count + 1;

        if (Volatile.Read(ref _busyInFrame) > 0)
        {
            // tipkovnica nije stigla obraditi neke pakete: iduće 2 s šalji s većim razmakom.
            // Odbijeni paketi su već zapisani (TakeRejectedZones), pa se ponovno šalju samo njihove tipke.
            BusyRetries += _busyInFrame;
            _paceUntil = DateTime.UtcNow.AddSeconds(2);
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<byte> _rejected = new();
    private readonly int[] _zoneSent = new int[256];
    private readonly int[] _zoneInvalid = new int[256];
    private readonly bool[] _zoneBad = new bool[256];

    /// <summary>Zona koju tipkovnica odbija kao neispravnu (greška 0x02) – ne postoji na ovoj tipkovnici.</summary>
    public bool IsBadZone(byte z) => Volatile.Read(ref _zoneBad[z]);

    private void CountSent(byte fn, byte[] a)
    {
        if (fn == 6) { for (int j = 3; j < 16; j++) if (a[j] != 0) Interlocked.Increment(ref _zoneSent[a[j]]); }
        else for (int j = 0; j < 4; j++) if (a[j * 4] != 0) Interlocked.Increment(ref _zoneSent[a[j * 4]]);
    }

    /// <summary>Greška 0x02: zona koja je u odbijenim paketima puno češće nego u prihvaćenima je neispravna.</summary>
    private void CountInvalid(byte z)
    {
        int bad = Interlocked.Increment(ref _zoneInvalid[z]);
        if (!_zoneBad[z] && bad >= 3 && bad * 2 > Volatile.Read(ref _zoneSent[z]))
        {
            _zoneBad[z] = true;
            Log.Write($"Tipkovnica: zona 0x{z:X2} ne postoji (odbijena {bad}×), više se ne šalje.");
        }
    }

    /// <summary>Zone iz paketa koje je tipkovnica odbila (zauzeto ili greška), da ih osvjetljenje pošalje ponovno.</summary>
    public List<byte> TakeRejectedZones()
    {
        var list = new List<byte>();
        while (_rejected.TryDequeue(out var z)) list.Add(z);
        return list;
    }

    private void RememberRejected(byte fnSw, bool invalid = false)
    {
        lock (_recent)
        {
            if (!_recent.TryGetValue((byte)(fnSw & 0x0F), out var p) || p.Fn != fnSw >> 4) return;
            var a = p.Args;
            var zones = p.Fn == 6 ? a.Skip(3).Where(z => z != 0) : Enumerable.Range(0, 4).Select(j => a[j * 4]).Where(z => z != 0);
            foreach (byte z in zones)
            {
                if (invalid) CountInvalid(z);
                if (!_zoneBad[z]) _rejected.Enqueue(z);
            }
        }
    }

    private int _busyInFrame;
    private int _resendRequested;

    /// <summary>True (jednom) ako je tipkovnica odbila neki paket pa treba ponovno poslati sve boje.</summary>
    public bool TakeResendRequest() => Interlocked.Exchange(ref _resendRequested, 0) == 1;
    private DateTime _paceUntil;

    private readonly Dictionary<int, DateTime> _errLogged = new();
    private readonly Dictionary<byte, (byte Fn, byte[] Args)> _recent = new();
    private byte _sw;

    private string DescribePacket(byte fnSw)
    {
        lock (_recent)
        {
            if (!_recent.TryGetValue((byte)(fnSw & 0x0F), out var p) || p.Fn != fnSw >> 4) return "";
            var a = p.Args;
            var zones = p.Fn == 6
                ? a.Skip(3).Where(z => z != 0).Select(z => $"0x{z:X2}")
                : Enumerable.Range(0, 4).Select(j => $"0x{a[j * 4]:X2}");
            return " – zone u paketu: " + string.Join(", ", zones);
        }
    }

    private void OnDeviceError(byte feature, byte fnSw, byte code)
    {
        if (feature == _perKey && code == 0x08)
        {
            Interlocked.Increment(ref _busyInFrame);
            RememberRejected(fnSw);
            return;
        }
        // odbijen paket boja: tipke iz njega nisu osvježene → zatraži ponovno slanje svega (rijetko)
        if (feature == _perKey && code == 0x02)
        {
            RememberRejected(fnSw, invalid: true);
        }
        // istu grešku zapiši najviše jednom u minuti (da log ne naraste)
        int key = (feature << 16) | (fnSw << 8) | code;
        lock (_errLogged)
        {
            if (_errLogged.TryGetValue(key, out var last) && (DateTime.UtcNow - last).TotalSeconds < 60) return;
            _errLogged[key] = DateTime.UtcNow;
        }
        Log.Write($"HID++ greška 0x{code:X2} (značajka 0x{feature:X2}, funkcija 0x{fnSw:X2}){(feature == _perKey ? DescribePacket(fnSw) : "")}");
    }

    private static byte[] Filled16()
    {
        var a = new byte[16];
        Array.Fill(a, (byte)0xFF);
        return a;
    }

    /// <summary>Vraća tipkovnicu u normalan (ugrađeni) način rada.</summary>
    /// <param name="stayDark">true = tipkovnica ostaje u host načinu s ugašenim tipkama (ne vraća ugrađene efekte)</param>
    public void Release(bool stayDark = false)
    {
        HidppDevice dev;
        lock (_lock)
        {
            dev = _dev;
            _dev = null;
        }
        if (dev == null) return;
        dev.Disconnected -= OnDeviceDisconnected;
        TryRun(() => { if (_gkeys != 0) dev.Request(_gkeys, 2, new byte[] { 0x00 }, 300); }, "G tipke off");
        TryRun(() => { if (_mr != 0) dev.Request(_mr, 0, new byte[] { 0x00 }, 300); }, "MR off");
        if (!stayDark)
            TryRun(() => { if (_onboard != 0) dev.Request(_onboard, 1, new byte[] { 0x01 }, 300); }, "onboard način");
        dev.Notification -= OnNotification;
        dev.Dispose();
    }

    public void Dispose() => Release();
}

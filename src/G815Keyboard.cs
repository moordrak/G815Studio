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

    private static string ReadName(HidppDevice dev)
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
    public void SendColors(IReadOnlyList<(byte Zone, Color Color)> changes)
    {
        var dev = _dev;
        if (dev == null || _perKey == 0 || changes.Count == 0) return;
        var packets = new List<(byte Function, byte[] Args)>();

        // Kad se mijenja puno tipki (animacije), slične boje se jače grupiraju:
        // manje paketa = više FPS-a, a razlika od nekoliko nijansi na LED-icama se ne vidi.
        int shift = changes.Count > 80 ? 5 : changes.Count > 40 ? 4 : 3;
        var singles = new List<(byte Zone, Color Color)>();
        foreach (var group in changes.GroupBy(c => ((c.Color.R >> shift) << 16) | ((c.Color.G >> shift) << 8) | (c.Color.B >> shift)))
        {
            var list = group.ToList();
            bool black = list.All(c => c.Color.R == 0 && c.Color.G == 0 && c.Color.B == 0);
            // Crna (gašenje) uvijek ide paketima za pojedinačne tipke: skupni paket (funkcija 6)
            // s bojom 0,0,0 tipkovnica ne primjenjuje pouzdano, pa bi tipke ostale upaljene.
            if (list.Count >= 4 && !black)
            {
                int r = 0, g = 0, b = 0;
                foreach (var (_, c) in list) { r += c.R; g += c.G; b += c.B; }
                byte ar = (byte)(r / list.Count), ag = (byte)(g / list.Count), ab = (byte)(b / list.Count);
                if (ar == 0 && ag == 0 && ab == 0) { singles.AddRange(list); continue; }
                for (int i = 0; i < list.Count; i += 13)
                {
                    var a = Filled16();
                    a[0] = ar; a[1] = ag; a[2] = ab;
                    int n = Math.Min(13, list.Count - i);
                    for (int j = 0; j < n; j++) a[3 + j] = list[i + j].Zone;
                    packets.Add((6, a));
                }
            }
            else
            {
                singles.AddRange(list);
            }
        }

        for (int i = 0; i < singles.Count; i += 4)
        {
            var a = Filled16();
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

        if (_noAck)
        {
            foreach (var (fn, args) in packets) { dev.Send(_perKey, fn, args); Thread.Sleep(1); }
        }
        else
        {
            try
            {
                BusyRetries += dev.SendBatch(_perKey, packets, Window, 100);
                _ackTimeouts = 0;
            }
            catch (TimeoutException)
            {
                if (++_ackTimeouts >= 5)
                {
                    _noAck = true;
                    Log.Write("Tipkovnica ne potvrđuje pakete boja – prelazim na slanje bez čekanja.");
                }
            }
        }

        SendPacket(dev, 7, Array.Empty<byte>());
        LastPacketCount = packets.Count + 1;
    }

    /// <summary>Broj paketa boja "u letu" (bez čekanja potvrde). 4 je dobar omjer brzine i pouzdanosti.</summary>
    public int Window { get; set; } = 4;

    // Način rada: čekaj potvrdu svakog paketa (zadano). Ako tipkovnica uopće ne potvrđuje,
    // prelazi se na slanje bez čekanja s malim razmakom između paketa.
    private bool _noAck;
    private int _ackTimeouts;

    private void SendPacket(HidppDevice dev, byte function, byte[] args)
    {
        if (_noAck)
        {
            dev.Send(_perKey, function, args);
            Thread.Sleep(1);
            return;
        }
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                dev.Request(_perKey, function, args, 80);
                _ackTimeouts = 0;
                return;
            }
            catch (HidppException ex) when (ex.ErrorCode == 0x08 && attempt < 10)
            {
                // zauzeto: kratko pričekaj pa ponovi isti paket
                BusyRetries++;
                Thread.Sleep(2 + attempt);
            }
            catch (TimeoutException)
            {
                if (++_ackTimeouts >= 5)
                {
                    _noAck = true;
                    Log.Write("Tipkovnica ne potvrđuje pakete boja – prelazim na slanje bez čekanja.");
                }
                return;
            }
        }
    }

    private static byte[] Filled16()
    {
        var a = new byte[16];
        Array.Fill(a, (byte)0xFF);
        return a;
    }

    /// <summary>Vraća tipkovnicu u normalan (ugrađeni) način rada.</summary>
    public void Release()
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
        TryRun(() => { if (_onboard != 0) dev.Request(_onboard, 1, new byte[] { 0x01 }, 300); }, "onboard način");
        dev.Notification -= OnNotification;
        dev.Dispose();
    }

    public void Dispose() => Release();
}

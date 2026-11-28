namespace Svitac;

/// <summary>
/// RGB osvjetljenje Logitech miša (npr. G502 X PLUS) preko HID++ 2.0, žično ili preko LIGHTSPEED prijamnika.
/// Koristi značajku 0x8071 RGB efekti (ili stariju 0x8070): svakom klasteru LED-ica postavlja se
/// efekt "statično" s bojom. Miš NE prebacuje u host način, pa DPI i tipke miša rade normalno.
/// </summary>
public sealed class LogitechMouse : IDisposable
{
    private const ushort FeatRgbEffects = 0x8071;
    private const ushort FeatColorLedEffects = 0x8070;
    private const ushort StaticEffectId = 0x0001;

    private sealed class Cluster
    {
        public byte Index;
        public byte StaticEffectIndex;
        public ushort Location;
        public int Sent = -1;      // zadnja poslana boja (RGB), -1 = ništa
    }

    private readonly object _lock = new();
    private HidppDevice _dev;
    private byte _feat;
    private bool _v8071;
    private readonly List<Cluster> _clusters = new();
    private int[] _desired = Array.Empty<int>();
    private Thread _thread;
    private volatile bool _stop;
    private byte _persist = 0x01;    // zastavice: bit 0 = VOLATILE (RAM), bit 1 = NON_VOLATILE; puna snaga
    private DateTime _lastReclaim;

    public bool IsConnected => _dev != null;

    /// <summary>Prebaci miš u host (softverski) način dok program radi – potrebno ako ugrađeni profil prepisuje boju.</summary>
    public bool HostMode { get; set; }
    private byte _onboard;
    public string Name { get; private set; } = "";
    public string Status { get; private set; } = L.T("Miš nije pronađen.");
    /// <summary>Broj zona kojima program upravlja: pojedine LED-ice (0x8081) ili klasteri (0x8071).</summary>
    public int ClusterCount { get { lock (_lock) return _ledZones.Count > 0 ? _ledZones.Count : _clusters.Count; } }

    // pojedine LED-ice preko 0x8081 (per-key lighting v2) – isti paketi kao kod tipkovnice
    private byte _perKey;
    private readonly List<byte> _ledZones = new();
    private int[] _ledSent = Array.Empty<int>();
    public event Action Changed;

    /// <summary>Traži Logitech miš s RGB efektima (izostavlja tipkovnicu). Vraća true ako je pronađen.</summary>
    public bool TryConnect()
    {
        lock (_lock)
        {
            if (_dev != null) return true;
            List<HidDeviceInfo> all;
            try { all = HidEnumerator.Enumerate(G815Keyboard.LogitechVid); }
            catch (Exception ex) { Status = L.T("Enumeracija nije uspjela: ") + ex.Message; return false; }

            var candidates = all
                .Where(d => d.OutputReportLength == 20 && !G815Keyboard.SupportedPids.Contains(d.ProductId) && d.UsagePage >= 0xFF00)
                .ToList();
            bool sawMouseWithoutRgb = false;

            foreach (var info in candidates)
            {
                HidppDevice dev = null;
                try
                {
                    dev = new HidppDevice(HidStream.Open(info));
                    dev.Start();
                    foreach (byte idx in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
                    {
                        dev.DeviceIndex = idx;
                        byte f71 = TryFeature(dev, FeatRgbEffects);
                        byte f70 = f71 == 0 ? TryFeature(dev, FeatColorLedEffects) : (byte)0;
                        if (f71 == 0 && f70 == 0)
                        {
                            // odgovara li uopće uređaj na tom indeksu (npr. G502 X bez RGB-a)?
                            if (TryFeature(dev, 0x0005) != 0) sawMouseWithoutRgb |= IsMouseName(G815Keyboard.ReadName(dev));
                            continue;
                        }

                        string name = G815Keyboard.ReadName(dev) ?? $"Logitech 0x{info.ProductId:X4}";
                        if (!Setup(dev, f71 != 0 ? f71 : f70, f71 != 0)) continue;

                        _dev = dev;
                        Name = name;
                        Status = L.F("Spojeno: {0}", name) + $" ({(_v8071 ? "0x8071" : "0x8070")}, {_clusters.Count} × " + L.T("zona") + $", 0x{idx:X2})";
                        Log.Write("Miš: " + Status);
                        dev.Disconnected += OnDisconnected;
                        StartThread();
                        Changed?.Invoke();
                        return true;
                    }
                    dev.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Write($"Miš ({info}): {ex.Message}");
                    dev?.Dispose();
                }
            }
            Status = sawMouseWithoutRgb
                ? L.T("Miš je pronađen, ali nema RGB osvjetljenje (npr. G502 X LIGHTSPEED bez oznake PLUS).")
                : L.T("Miš s RGB osvjetljenjem nije pronađen (je li uključen i spojen prijamnik?).");
            return false;
        }
    }

    private static bool IsMouseName(string n) =>
        n != null && (n.Contains("Mouse", StringComparison.OrdinalIgnoreCase) || n.Contains("G502", StringComparison.OrdinalIgnoreCase)
                      || n.Contains("G30", StringComparison.OrdinalIgnoreCase) || n.Contains("G70", StringComparison.OrdinalIgnoreCase));

    private static byte TryFeature(HidppDevice dev, ushort feature)
    {
        try { return dev.GetFeatureIndex(feature, 250); }
        catch { return 0; }
    }

    private bool Setup(HidppDevice dev, byte feat, bool v8071)
    {
        _clusters.Clear();
        _feat = feat;
        _v8071 = v8071;
        _persist = 0x01;
        int count;
        if (v8071)
        {
            var info = dev.Request(feat, 0, new byte[] { 0xFF, 0xFF, 0x00 }, 500);
            count = info.Length > 2 ? info[2] : 0;
        }
        else
        {
            var info = dev.Request(feat, 0, Array.Empty<byte>(), 500);
            count = info.Length > 0 ? info[0] : 0;
        }

        LogFeatures(dev);
        for (byte c = 0; c < count && c < 8; c++)
        {
            byte[] zi = v8071
                ? dev.Request(feat, 0, new byte[] { c, 0xFF, 0x00 }, 500)
                : dev.Request(feat, 1, new byte[] { c }, 500);
            int off = v8071 ? 2 : 1;
            ushort loc = (ushort)((zi[off] << 8) | zi[off + 1]);
            int effects = zi[off + 2];
            int staticIdx = -1;
            var ids = new List<string>();
            for (byte e = 0; e < effects && e < 32; e++)
            {
                byte[] ei = v8071
                    ? dev.Request(feat, 0, new byte[] { c, e, 0x00 }, 500)
                    : dev.Request(feat, 2, new byte[] { c, e }, 500);
                ushort id = (ushort)((ei[2] << 8) | ei[3]);
                ids.Add($"0x{id:X2}");
                if (id == StaticEffectId && staticIdx < 0) staticIdx = e;
            }
            Log.Write($"Miš: zona {c}, lokacija 0x{loc:X4}, efekata {effects} [{string.Join(", ", ids)}], statični indeks {staticIdx}, " +
                      $"više LED-ica (bajt 7): {(zi.Length > 7 ? zi[7] : 0)}, info {Hex(zi)}");
            if (staticIdx >= 0)
                _clusters.Add(new Cluster { Index = c, Location = loc, StaticEffectIndex = (byte)staticIdx });
        }
        if (_clusters.Count == 0) return false;
        DiscoverLeds(dev);
        _desired = Enumerable.Repeat(-1, _ledZones.Count > 0 ? _ledZones.Count : _clusters.Count).ToArray();
        _ledSent = Enumerable.Repeat(-1, _ledZones.Count).ToArray();
        _onboard = 0;
        if (HostMode)
        {
            _onboard = TryFeature(dev, 0x8100);
            if (_onboard != 0)
            {
                try
                {
                    var r = dev.Request(_onboard, 1, new byte[] { 0x02 }, 400);
                    Log.Write("Miš: host način uključen, odgovor " + Hex(r));
                }
                catch (Exception ex) { Log.Write("Miš: host način nije uspio: " + ex.Message); }
            }
            else Log.Write("Miš nema značajku ugrađenih profila (0x8100).");
        }
        if (v8071) ClaimSoftwareControl(dev);
        return true;
    }

    /// <summary>Preuzimanje softverske kontrole nad RGB efektima (kao OpenRGB: [01 03 05]).</summary>
    private void ClaimSoftwareControl(HidppDevice dev)
    {
        // [0x01 = upiši, kontrola: 0x01 ALL_CLUSTERS (samo boje, ne i napajanje), događaji: 0]
        foreach (var args in new[] { new byte[] { 0x01, 0x01, 0x00 }, new byte[] { 0x01, 0x03, 0x00 } })
        {
            try
            {
                var r = dev.Request(_feat, 5, args, 400);
                if ((DateTime.UtcNow - _lastReclaim).TotalSeconds > 60 || _claimFailLogged)
                    Log.Write($"Miš: softverska kontrola {BitConverter.ToString(args)} → {Hex(r)}");
                _lastReclaim = DateTime.UtcNow;
                ClaimDone();
                return;
            }
            catch (Exception ex)
            {
                // dok miš spava ne odgovara – zapiši samo prvi neuspjeh u nizu
                if (!_claimFailLogged)
                    Log.Write("Miš: softverska kontrola " + BitConverter.ToString(args) + ": " + ex.Message + " (vjerojatno spava)");
            }
        }
        _claimFailLogged = true;
    }

    private bool _claimFailLogged;

    private void ClaimDone()
    {
        _claimFailLogged = false;
    }

    /// <summary>Željene boje po zonama miša (redom kako su otkrivene).</summary>
    public void SetColors(IReadOnlyList<Color> colors)
    {
        lock (_lock)
        {
            for (int i = 0; i < _desired.Length; i++)
            {
                var c = colors.Count == 0 ? Color.Black : colors[Math.Min(i, colors.Count - 1)];
                _desired[i] = ColorUtil.Rgb(c);
            }
        }
    }

    private void StartThread()
    {
        _stop = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Miš RGB" };
        _thread.Start();
    }

    private void Loop()
    {
        int failures = 0;
        while (!_stop)
        {
            Thread.Sleep(33);   // najviše ~30 promjena u sekundi
            HidppDevice dev;
            Cluster[] clusters;
            int[] desired;
            lock (_lock)
            {
                dev = _dev;
                clusters = _clusters.ToArray();
                desired = (int[])_desired.Clone();
            }
            if (dev == null) return;

            try
            {
                // bežični miš nakon spavanja zaboravi softversku kontrolu – povremeno je ponovno preuzmi
                if (_v8071 && (DateTime.UtcNow - _lastReclaim).TotalSeconds > 10)
                {
                    ClaimSoftwareControl(dev);
                    foreach (var cl in clusters) cl.Sent = -1;
                    Array.Fill(_ledSent, -1);
                }
                if (_perKey != 0 && _ledZones.Count > 0)
                {
                    SendLeds(dev, desired, clusters);
                    failures = 0;
                    continue;
                }
                for (int i = 0; i < clusters.Length; i++)
                {
                    var cl = clusters[i];
                    int want = desired[i];
                    if (want < 0 || want == cl.Sent) continue;
                    SendStatic(dev, cl, want);
                    cl.Sent = want;
                }
                failures = 0;
            }
            catch (HidppException ex) when (ex.ErrorCode == 0x05 || ex.ErrorCode == 0x02)
            {
                // "nije dopušteno" – miš je izgubio softversku kontrolu (npr. nakon spavanja)
                Log.Write("Miš: postavljanje boje odbijeno (" + ex.Message + "), ponovno preuzimam kontrolu.");
                _lastReclaim = DateTime.MinValue;
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                if (++failures >= 5)
                {
                    Log.Write("Miš ne odgovara: " + ex.Message);
                    OnDisconnected();
                    return;
                }
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Čita koje LED-ice miš ima (0x8081 funkcija 0: bitmapa zona po stranicama).</summary>
    private void DiscoverLeds(HidppDevice dev)
    {
        _ledZones.Clear();
        _perKey = TryFeature(dev, 0x8081);
        if (_perKey == 0) return;
        try
        {
            var bitmap = new List<byte>();
            for (byte page = 0; page < 3; page++)
            {
                var r = dev.Request(_perKey, 0, new byte[] { 0x00, page }, 400);
                bitmap.AddRange(r.Skip(2));
            }
            for (int z = 1; z < 255 && z / 8 < bitmap.Count; z++)
                if (((bitmap[z / 8] >> (z % 8)) & 1) != 0) _ledZones.Add((byte)z);
            Log.Write($"Miš: pojedine LED-ice (0x8081): {_ledZones.Count} – zone {string.Join(", ", _ledZones.Select(z => $"0x{z:X2}"))}");
            if (_ledZones.Count > 16) { Log.Write("Miš: neuobičajeno mnogo zona, koristim samo klastere."); _ledZones.Clear(); }
            dev.ErrorReceived += (f, fn, code) =>
            {
                if (f == _perKey) Log.Write($"Miš: greška 0x{code:X2} na paketu LED-ica (funkcija 0x{fn:X2})");
            };
        }
        catch (Exception ex)
        {
            Log.Write("Miš: čitanje LED-ica nije uspjelo: " + ex.Message);
            _ledZones.Clear();
        }
    }

    /// <summary>Šalje boje svih LED-ica: mali paketi s 4 različite zone (popunjeni ostalim LED-icama) i commit.</summary>
    private void SendLeds(HidppDevice dev, int[] desired, Cluster[] clusters)
    {
        bool changed = false;
        for (int i = 0; i < _ledZones.Count; i++)
            if (desired[i] >= 0 && desired[i] != _ledSent[i]) { changed = true; break; }
        if (!changed) return;

        // klaster drži na statičnoj boji prve LED-ice (neki miševi bez toga ne prikazuju pojedine LED-ice)
        if (clusters.Length > 0 && clusters[0].Sent < 0 && desired[0] >= 0)
        {
            SendStatic(dev, clusters[0], desired[0]);
            clusters[0].Sent = desired[0];
        }

        int n = _ledZones.Count;
        for (int i = 0; i < n; i += 4)
        {
            var a = new byte[16];
            for (int j = 0; j < 4; j++)
            {
                int idx = n >= 4 ? (i + j) % n : Math.Min(i + j, n - 1);
                if (i + j >= n && n >= 4) idx = (i + j - n) % n;   // popuni ostalim LED-icama
                int rgb = Math.Max(0, desired[idx]);
                a[j * 4] = _ledZones[idx];
                a[j * 4 + 1] = (byte)(rgb >> 16);
                a[j * 4 + 2] = (byte)(rgb >> 8);
                a[j * 4 + 3] = (byte)rgb;
            }
            dev.Send(_perKey, 1, a);
        }
        try { dev.Request(_perKey, 7, Array.Empty<byte>(), 150); }
        catch (TimeoutException) { }
        for (int i = 0; i < n; i++) _ledSent[i] = desired[i];
        if (!_loggedSet)
        {
            _loggedSet = true;
            Log.Write($"Miš: poslane boje za {n} LED-ica preko 0x8081.");
        }
    }

    private void SendStatic(HidppDevice dev, Cluster cl, int rgb)
    {
        // [zona, indeks efekta, 10 bajtova parametara (R G B …), trajnost]
        var a = new byte[13];
        a[0] = cl.Index;
        a[1] = cl.StaticEffectIndex;
        a[2] = (byte)(rgb >> 16);
        a[3] = (byte)(rgb >> 8);
        a[4] = (byte)rgb;
        a[12] = _persist;   // 0x01 = privremeno (RAM), puna snaga
        var r = dev.Request(_feat, (byte)(_v8071 ? 1 : 3), a, 300);
        if (!_loggedSet)
        {
            _loggedSet = true;
            Log.Write($"Miš: postavljena boja #{rgb:X6} (zona {cl.Index}, efekt {cl.StaticEffectIndex}) → {Hex(r)}");
        }
    }

    private bool _loggedSet;
    private static bool _featuresLogged;

    /// <summary>Jednom zapiše sve HID++ značajke miša (FeatureSet 0x0001) – za procjenu što još podržava.</summary>
    private static void LogFeatures(HidppDevice dev)
    {
        if (_featuresLogged) return;
        _featuresLogged = true;
        try
        {
            byte fs = dev.GetFeatureIndex(0x0001, 400);
            if (fs == 0) return;
            int n = dev.Request(fs, 0, Array.Empty<byte>(), 400)[0];
            var list = new List<string>();
            for (int i = 1; i <= n; i++)
            {
                var r = dev.Request(fs, 1, new[] { (byte)i }, 400);
                list.Add($"{i:X2}:{(r[0] << 8) | r[1]:X4}");
            }
            Log.Write("Miš – značajke (indeks:ID): " + string.Join(" ", list));
        }
        catch (Exception ex)
        {
            Log.Write("Miš – popis značajki nije uspio: " + ex.Message);
        }
    }

    private static string Hex(byte[] r) => r == null ? "-" : BitConverter.ToString(r.Take(8).ToArray());

    private void OnDisconnected()
    {
        HidppDevice dev;
        lock (_lock)
        {
            dev = _dev;
            _dev = null;
            Status = L.T("Miš odspojen ili spava – ponovno spajanje…");
        }
        _stop = true;
        if (dev != null)
        {
            dev.Disconnected -= OnDisconnected;
            dev.Dispose();
        }
        Changed?.Invoke();
    }

    /// <summary>Vraća mišu njegove vlastite efekte.</summary>
    /// <param name="stayDark">true = ugasi LED-ice i ne vraćaj mišu njegove efekte (ostaje ugašen)</param>
    public void Release(bool stayDark = false)
    {
        if (stayDark && IsConnected)
        {
            SetColors(new[] { Color.Black });
            Thread.Sleep(250);      // nit za slanje u tom vremenu pošalje crnu svim LED-icama
        }
        _stop = true;
        if (_thread != null && _thread != Thread.CurrentThread) _thread.Join(500);
        HidppDevice dev;
        lock (_lock)
        {
            dev = _dev;
            _dev = null;
            Status = L.T("Upravljanje mišem isključeno.");
        }
        if (dev == null) return;
        dev.Disconnected -= OnDisconnected;
        if (!stayDark)
        {
            try { if (_v8071) dev.Request(_feat, 5, new byte[] { 0x01, 0x00, 0x00 }, 300); } catch { }
            try { if (_onboard != 0) dev.Request(_onboard, 1, new byte[] { 0x01 }, 300); } catch { }
        }
        dev.Dispose();
        Changed?.Invoke();
    }

    public void Dispose() => Release();
}

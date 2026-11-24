namespace G815Studio;

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
    private byte _persist;           // 0 = privremeno; 1 samo ako uređaj odbije 0
    private DateTime _lastReclaim;

    public bool IsConnected => _dev != null;
    public string Name { get; private set; } = "";
    public string Status { get; private set; } = "Miš nije pronađen.";
    public int ClusterCount { get { lock (_lock) return _clusters.Count; } }
    public event Action Changed;

    /// <summary>Traži Logitech miš s RGB efektima (izostavlja tipkovnicu). Vraća true ako je pronađen.</summary>
    public bool TryConnect()
    {
        lock (_lock)
        {
            if (_dev != null) return true;
            List<HidDeviceInfo> all;
            try { all = HidEnumerator.Enumerate(G815Keyboard.LogitechVid); }
            catch (Exception ex) { Status = "Enumeracija nije uspjela: " + ex.Message; return false; }

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
                        Status = $"Spojeno: {name} ({(_v8071 ? "0x8071" : "0x8070")}, {_clusters.Count} zon{(_clusters.Count == 1 ? "a" : "e")}, indeks 0x{idx:X2})";
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
                ? "Miš je pronađen, ali nema RGB osvjetljenje (npr. G502 X LIGHTSPEED bez oznake PLUS)."
                : "Miš s RGB osvjetljenjem nije pronađen (je li uključen i spojen prijamnik?).";
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
        _persist = 0;
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

        for (byte c = 0; c < count && c < 8; c++)
        {
            byte[] zi = v8071
                ? dev.Request(feat, 0, new byte[] { c, 0xFF, 0x00 }, 500)
                : dev.Request(feat, 1, new byte[] { c }, 500);
            int off = v8071 ? 2 : 1;
            ushort loc = (ushort)((zi[off] << 8) | zi[off + 1]);
            int effects = zi[off + 2];
            int staticIdx = -1;
            for (byte e = 0; e < effects && e < 32; e++)
            {
                byte[] ei = v8071
                    ? dev.Request(feat, 0, new byte[] { c, e, 0x00 }, 500)
                    : dev.Request(feat, 2, new byte[] { c, e }, 500);
                ushort id = (ushort)((ei[2] << 8) | ei[3]);
                if (id == StaticEffectId) { staticIdx = e; break; }
            }
            Log.Write($"Miš: zona {c}, lokacija 0x{loc:X4}, efekata {effects}, statični indeks {staticIdx}");
            if (staticIdx >= 0)
                _clusters.Add(new Cluster { Index = c, Location = loc, StaticEffectIndex = (byte)staticIdx });
        }
        if (_clusters.Count == 0) return false;
        _desired = Enumerable.Repeat(-1, _clusters.Count).ToArray();
        if (v8071) ClaimSoftwareControl(dev);
        return true;
    }

    /// <summary>Preuzimanje softverske kontrole nad RGB efektima (kao OpenRGB: [01 03 05]).</summary>
    private void ClaimSoftwareControl(HidppDevice dev)
    {
        foreach (var args in new[] { new byte[] { 0x01, 0x03, 0x05 }, new byte[] { 0x01, 0x01, 0x00 } })
        {
            try
            {
                dev.Request(_feat, 5, args, 400);
                _lastReclaim = DateTime.UtcNow;
                return;
            }
            catch (Exception ex)
            {
                Log.Write("Miš: softverska kontrola " + BitConverter.ToString(args) + ": " + ex.Message);
            }
        }
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
            Thread.Sleep(_persist == 0 ? 33 : 250);   // najviše ~30 promjena u sekundi
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
            catch (HidppException ex) when (ex.ErrorCode == 0x02 && _persist == 0)
            {
                // uređaj ne prihvaća privremenu promjenu – koristi trajnu, ali rjeđe
                _persist = 1;
                Log.Write("Miš: privremena promjena odbijena, koristim trajnu (rjeđe slanje).");
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

    private void SendStatic(HidppDevice dev, Cluster cl, int rgb)
    {
        // [zona, indeks efekta, 10 bajtova parametara (R G B …), trajnost]
        var a = new byte[13];
        a[0] = cl.Index;
        a[1] = cl.StaticEffectIndex;
        a[2] = (byte)(rgb >> 16);
        a[3] = (byte)(rgb >> 8);
        a[4] = (byte)rgb;
        a[12] = _persist;
        dev.Request(_feat, (byte)(_v8071 ? 1 : 3), a, 300);
    }

    private void OnDisconnected()
    {
        HidppDevice dev;
        lock (_lock)
        {
            dev = _dev;
            _dev = null;
            Status = "Miš odspojen ili spava – ponovno spajanje…";
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
    public void Release()
    {
        _stop = true;
        HidppDevice dev;
        lock (_lock)
        {
            dev = _dev;
            _dev = null;
            Status = "Upravljanje mišem isključeno.";
        }
        if (dev == null) return;
        dev.Disconnected -= OnDisconnected;
        try { if (_v8071) dev.Request(_feat, 5, new byte[] { 0x01, 0x00, 0x00 }, 300); } catch { }
        dev.Dispose();
        Changed?.Invoke();
    }

    public void Dispose() => Release();
}

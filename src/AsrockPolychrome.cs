using System.Drawing;

namespace G815Studio;

/// <summary>Jedan izlaz ASRockovog RGB kontrolera: 4-pinski RGB header, ARGB header ili LED-ice na ploči.</summary>
public sealed class AsrockChannel
{
    public byte ZoneType { get; init; }          // 0 RGB1, 1 RGB2, 2 ARGB1, 3 ARGB2, 4 PCH, 5 IO, 6 PCB, 7 ARGB3/audio
    public string Name { get; init; }
    public bool Addressable { get; init; }
    public int LedCount { get; set; }
    public bool HwSwapRG { get; init; }          // kontroler za ovaj izlaz ima uključenu zamjenu R i G
    public int Segments { get; set; }            // broj virtualnih zona u efektima
    public override string ToString() => $"{Name}: {LedCount} LED-ica{(Addressable ? " (ARGB)" : "")}";
}

/// <summary>
/// ASRock Polychrome USB (HID, VID 0x26CE, PID 0x01A2) – RGB kontroler na novijim ASRock pločama, npr. B760M Pro RS.
/// Paketi su 64 bajta (+ report ID 0), na svaki kontroler odgovara. Program kontroler prebacuje u izravni način (0x0F)
/// i šalje boje svih LED-ica po okviru; pri izlazu vraća način rada, boje i zamjenu R/G kakvi su bili.
/// Protokol je opisan prema javno dostupnom radu projekta OpenRGB.
/// </summary>
public sealed class AsrockPolychrome : IDisposable
{
    public const ushort Vid = 0x26CE;
    public static readonly ushort[] Pids = { 0x01A2, 0x01A6 };

    private const byte CmdSetZone = 0x10, CmdReadZone = 0x11, CmdCommit = 0x12, CmdReadHeader = 0x14, CmdWriteHeader = 0x15;
    private const byte CfgPresent = 0x01, CfgLedCount = 0x02, CfgRgSwap = 0x03;
    private const byte ModeRainbow = 0x0E, ModeDirect = 0x0F, ChunkFirst = 0xE3, ChunkNext = 0xE4;
    private const byte NotPresent = 0x1E;
    private const int AddressableMax = 100;
    private const int R = 1;            // pomak odgovora zbog report ID-a

    private static readonly string[] ZoneNames =
    {
        "RGB header 1", "RGB header 2", "ARGB header 1", "ARGB header 2", "PCH", "Pokrov I/O", "Ploča (PCB)", "ARGB header 3 / audio"
    };

    private readonly object _lock = new();
    private HidStream _hid;
    private readonly List<AsrockChannel> _channels = new();
    private byte[] _ledCounts = new byte[8];
    private byte _rgSwapOriginal;
    private readonly List<(byte Zone, byte Mode, byte R, byte G, byte B, byte Speed)> _saved = new();
    private Color[] _wanted;            // boje po LED-icama, redom kanala
    private Color[] _sent;
    private Thread _thread;
    private volatile bool _stop;
    private readonly AutoResetEvent _wake = new(false);
    private int _failures;

    public bool IsConnected { get { lock (_lock) return _hid != null; } }
    public string Status { get; private set; } = "ASRock RGB kontroler nije pronađen.";
    public string Name { get; private set; } = "";
    /// <summary>Dodatna zamjena crvene i zelene (ako ventilatori pokazuju krive boje).</summary>
    public volatile bool SwapRG;
    public event Action Changed;

    public IReadOnlyList<AsrockChannel> Channels { get { lock (_lock) return _channels.ToList(); } }
    public int TotalSegments { get { lock (_lock) return _channels.Sum(c => c.Segments); } }

    // ------------------------------------------------------------ spajanje

    public bool TryConnect()
    {
        lock (_lock)
        {
            if (_hid != null) return true;
            List<HidDeviceInfo> all;
            try { all = HidEnumerator.Enumerate(Vid); }
            catch (Exception ex) { Status = "Enumeracija nije uspjela: " + ex.Message; return false; }
            if (all.Count == 0)
            {
                Status = "ASRock RGB kontroler nije pronađen (USB uređaj 26CE nije prisutan).";
                return false;
            }

            foreach (var d in all)
                Log.Write($"ASRock: HID 0x{d.VendorId:X4}:0x{d.ProductId:X4}, usage page 0x{d.UsagePage:X4}, usage 0x{d.Usage:X2}, ulaz {d.InputReportLength} B, izlaz {d.OutputReportLength} B");

            foreach (var info in all.Where(d => Pids.Contains(d.ProductId) && d.OutputReportLength >= 65 && d.InputReportLength >= 64))
            {
                HidStream hid = null;
                try
                {
                    hid = HidStream.Open(info);
                    _hid = hid;
                    if (!ReadConfig())
                    {
                        _hid = null;
                        hid.Dispose();
                        continue;
                    }
                    Name = info.ProductId == 0x01A6 ? "ASRock Deskmini LED traka" : "ASRock Polychrome USB";
                    EnterDirect();
                    Status = $"Spojeno: {Name}, " + (_channels.Count == 0 ? "nema izlaza" : string.Join(", ", _channels.Select(c => $"{c.Name} ({c.LedCount})")));
                    Log.Write("ASRock: " + Status);
                    _failures = 0;
                    StartThread();
                    ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write($"ASRock ({info}): {ex.Message}");
                    _hid = null;
                    hid?.Dispose();
                }
            }
            Status = "ASRock uređaj je pronađen, ali ne odgovara kao Polychrome USB kontroler (pogledaj log.txt).";
            return false;
        }
    }

    private byte[] Request(byte cmd, byte b3, params (int Index, byte Value)[] rest)
    {
        var buf = new byte[65];
        buf[1] = cmd;
        buf[3] = b3;
        foreach (var (i, v) in rest) buf[i] = v;
        return Transfer(buf);
    }

    /// <summary>Pošalje paket i pričeka odgovor (najviše 250 ms). Vraća null ako odgovora nema.</summary>
    private byte[] Transfer(byte[] buf)
    {
        var hid = _hid ?? throw new IOException("Kontroler nije spojen.");
        hid.Write(buf);
        using var cts = new CancellationTokenSource(250);
        try
        {
            return hid.ReadAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static string Hex(byte[] b, int from, int count) =>
        b == null ? "(nema odgovora)" : string.Join(" ", b.Skip(from).Take(count).Select(x => x.ToString("X2")));

    private bool ReadConfig()
    {
        var counts = Request(CmdReadHeader, CfgLedCount);
        var swap = Request(CmdReadHeader, CfgRgSwap);
        var present = Request(CmdReadHeader, CfgPresent);
        Log.Write($"ASRock: broj LED-ica (cfg 2): {Hex(counts, 0, 16)}");
        Log.Write($"ASRock: zamjena R/G (cfg 3): {Hex(swap, 0, 8)}");
        Log.Write($"ASRock: prisutni izlazi (cfg 1): {Hex(present, 0, 8)}");
        if (counts == null || present == null || counts.Length < 12) return false;

        // ReadFile vraća i report ID (0) na početku, pa je podatak "bajt 4" odgovora ovdje na indeksu 5
        _ledCounts = counts.Skip(R + 4).Take(8).ToArray();
        _rgSwapOriginal = swap != null && swap.Length > R + 4 ? swap[R + 4] : (byte)0;
        byte mask = present.Length > R + 4 ? present[R + 4] : (byte)0;

        _channels.Clear();
        _saved.Clear();
        for (byte z = 0; z < 8; z++)
        {
            bool on = _ledCounts[z] != NotPresent && ((mask >> z) & 1) != 0;
            var zc = Request(CmdReadZone, z);
            Log.Write($"ASRock: izlaz {z} ({ZoneNames[z]}): {(on ? "prisutan" : "nema ga")}, LED-ica {_ledCounts[z]}, stanje {Hex(zc, 0, 10)}");
            if (!on) continue;
            if (zc != null && zc.Length > R + 8) _saved.Add((z, zc[R + 4], zc[R + 5], zc[R + 6], zc[R + 7], zc[R + 8]));
            bool addressable = z is 2 or 3 or 7;
            _channels.Add(new AsrockChannel
            {
                ZoneType = z,
                Name = ZoneNames[z],
                Addressable = addressable,
                LedCount = Math.Min(_ledCounts[z], (byte)AddressableMax),
                HwSwapRG = ((_rgSwapOriginal >> z) & 1) != 0
            });
        }
        AssignSegments();
        AllocateBuffers();
        return true;
    }

    /// <summary>Virtualne zone u efektima: svaki RGB izlaz 1, ARGB izlazi dijele ostatak (najviše 8 po izlazu, ukupno 16).</summary>
    private void AssignSegments()
    {
        const int budget = 16;
        int fixedCount = 0;
        foreach (var c in _channels.Where(c => !c.Addressable))
        {
            c.Segments = c.LedCount > 0 && fixedCount < budget ? 1 : 0;
            fixedCount += c.Segments;
        }
        var argb = _channels.Where(c => c.Addressable && c.LedCount > 0).ToList();
        foreach (var c in _channels.Where(c => c.Addressable && c.LedCount == 0)) c.Segments = 0;
        int left = budget - fixedCount;
        for (int i = 0; i < argb.Count; i++)
        {
            int share = argb.Count - i > 0 ? left / (argb.Count - i) : 0;
            argb[i].Segments = Math.Max(0, Math.Min(Math.Min(8, share), argb[i].LedCount));
            left -= argb[i].Segments;
        }
    }

    private void AllocateBuffers()
    {
        int n = _channels.Sum(c => c.LedCount);
        _wanted = new Color[n];
        _sent = null;
    }

    private void EnterDirect()
    {
        // zamjena R/G u kontroleru kod izravnog načina uzrokuje treperenje (iskustvo OpenRGB-a), pa se isključuje
        Request(CmdWriteHeader, CfgRgSwap, (4, 0));
        foreach (var c in _channels)
            Request(CmdSetZone, c.ZoneType, (4, ModeDirect), (8, 0xE0), (9, 0xFF));
    }

    /// <summary>Upiše novi broj LED-ica za ARGB izlaz u kontroler (pamti se u kontroleru).</summary>
    public void SetLedCount(byte zoneType, int count)
    {
        lock (_lock)
        {
            if (_hid == null) return;
            count = Math.Clamp(count, 0, AddressableMax);
            var cfg = (byte[])_ledCounts.Clone();
            cfg[zoneType] = (byte)count;
            var rest = cfg.Select((v, i) => (4 + i, v)).ToArray();
            Request(CmdWriteHeader, CfgLedCount, rest);
            Log.Write($"ASRock: broj LED-ica za {ZoneNames[zoneType]} postavljen na {count}");
            _ledCounts = cfg;
            var ch = _channels.FirstOrDefault(c => c.ZoneType == zoneType);
            if (ch != null) ch.LedCount = count;
            AssignSegments();
            AllocateBuffers();
            Status = $"Spojeno: {Name}, " + string.Join(", ", _channels.Select(c => $"{c.Name} ({c.LedCount})"));
        }
        ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
    }

    // ------------------------------------------------------------ boje

    /// <summary>
    /// Boje virtualnih zona, redom kanala (koliko je <see cref="TotalSegments"/>). LED-ice između
    /// središta dviju zona dobivaju prijelaz, pa val i duga na ventilatorima nisu stepenasti.
    /// </summary>
    public void SetSegmentColors(Color[] segs)
    {
        lock (_lock)
        {
            if (_hid == null || _wanted == null) return;
            int s = 0, led = 0;
            foreach (var c in _channels)
            {
                for (int i = 0; i < c.LedCount; i++)
                {
                    Color col;
                    if (c.Segments <= 0 || s + c.Segments > segs.Length) col = Color.Black;
                    else if (c.Segments == 1) col = segs[s];
                    else
                    {
                        double pos = c.LedCount <= 1 ? 0 : (double)i / (c.LedCount - 1) * (c.Segments - 1);
                        int a = Math.Min((int)pos, c.Segments - 2);
                        col = Lerp(segs[s + a], segs[s + a + 1], pos - a);
                    }
                    _wanted[led++] = col;
                }
                s += c.Segments;
            }
        }
        _wake.Set();
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    private void StartThread()
    {
        _stop = false;
        if (_thread != null) return;
        _thread = new Thread(SendLoop) { IsBackground = true, Name = "ASRock RGB" };
        _thread.Start();
    }

    private void SendLoop()
    {
        while (!_stop)
        {
            _wake.WaitOne(500);
            if (_stop) break;
            try
            {
                lock (_lock)
                {
                    if (_hid == null || _wanted == null) continue;
                    if (_sent != null && _sent.Length == _wanted.Length && _sent.SequenceEqual(_wanted)) continue;
                    SendAll();
                    _sent = (Color[])_wanted.Clone();
                    _failures = 0;
                }
                Thread.Sleep(15);     // najviše ~60 okvira u sekundi
            }
            catch (Exception ex)
            {
                if (++_failures >= 3)
                {
                    Log.Write("ASRock: kontroler ne odgovara, odspajam: " + ex.Message);
                    Drop("ASRock RGB kontroler je odspojen.");
                }
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>Boje svih LED-ica u nizu paketa: prvi nosi 18 LED-ica od bajta 9, ostali po 19 od bajta 5.</summary>
    private void SendAll()
    {
        int slots = _channels.Sum(c => c.Addressable ? AddressableMax : c.LedCount);
        var data = new byte[Math.Max(_wanted.Length, slots + 8) * 3];
        bool extraSwap = SwapRG;
        int led = 0, o = 0;
        foreach (var c in _channels)
        {
            bool rgFirst = c.HwSwapRG ^ extraSwap;
            for (int i = 0; i < c.LedCount; i++, led++)
            {
                var col = _wanted[led];
                data[o++] = rgFirst ? col.R : col.G;
                data[o++] = rgFirst ? col.G : col.R;
                data[o++] = col.B;
            }
        }

        int pos = 0;
        bool first = true;
        while (pos < data.Length)
        {
            var buf = new byte[65];
            buf[1] = CmdSetZone;
            buf[3] = 0xFF;
            buf[0x40] = 0x65;
            int offset, cap;
            if (first)
            {
                buf[4] = ChunkFirst;
                buf[7] = 0xFF;
                offset = 9;
                cap = 54;
            }
            else
            {
                buf[4] = ChunkNext;
                offset = 5;
                cap = 57;
            }
            int n = Math.Min(cap, data.Length - pos);
            Array.Copy(data, pos, buf, offset, n);
            pos += n;
            first = false;
            if (Transfer(buf) == null) throw new IOException("Nema odgovora na paket boja.");
        }
    }

    // ------------------------------------------------------------ otpuštanje

    private void Drop(string status)
    {
        lock (_lock)
        {
            try { _hid?.Dispose(); } catch { }
            _hid = null;
            _channels.Clear();
            _wanted = null;
            _sent = null;
            Status = status;
        }
        ThreadPool.QueueUserWorkItem(_ => Changed?.Invoke());
    }

    /// <summary>Vrati kontroleru načine rada i boje kakvi su bili prije programa i zatvori ga.</summary>
    public void Release()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(1000);
        _thread = null;
        lock (_lock)
        {
            if (_hid == null) return;
            try
            {
                Request(CmdWriteHeader, CfgRgSwap, (4, _rgSwapOriginal));
                foreach (var s in _saved)
                {
                    // izravni način (0x0F, 0xE2, 0xE3) bez programa znači ugašene LED-ice, pa se tada vraća duga
                    if (s.Mode is ModeDirect or 0xE2 or 0xE3)
                        Request(CmdSetZone, s.Zone, (4, ModeRainbow), (8, 0xE0), (9, 0xFF));
                    else
                        Request(CmdSetZone, s.Zone, (4, s.Mode), (5, s.R), (6, s.G), (7, s.B), (8, s.Speed), (9, 0xFF));
                }
                Log.Write("ASRock: vraćeni prijašnji načini rada.");
            }
            catch (Exception ex)
            {
                Log.Write("ASRock: vraćanje nije uspjelo: " + ex.Message);
            }
        }
        Drop("ASRock RGB je isključen.");
    }

    public void Dispose() => Release();
}

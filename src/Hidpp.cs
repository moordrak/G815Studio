namespace G815Studio;

public sealed class HidppException : Exception
{
    public byte ErrorCode { get; }

    public HidppException(byte code) : base($"HID++ greška 0x{code:X2} ({Describe(code)})")
    {
        ErrorCode = code;
    }

    private static string Describe(byte c) => c switch
    {
        0x01 => "nepoznato",
        0x02 => "neispravan argument",
        0x03 => "izvan raspona",
        0x04 => "hardverska greška",
        0x05 => "Logitech interno",
        0x06 => "neispravan indeks značajke",
        0x07 => "neispravna funkcija",
        0x08 => "zauzeto",
        0x09 => "nije podržano",
        _ => "?"
    };
}

/// <summary>
/// HID++ 2.0 komunikacija preko "long" izvještaja (ID 0x11, 20 bajtova):
/// [0x11][devIdx][featureIdx][(funkcija&lt;&lt;4)|swId][16 bajtova parametara]
/// </summary>
public sealed class HidppDevice : IDisposable
{
    public const byte LongReportId = 0x11;
    public const byte ShortReportId = 0x10;
    private byte _deviceIndex = 0xFF;      // 0xFF = žičani uređaj, 1..6 = uređaj na LIGHTSPEED/Unifying prijamniku

    /// <summary>Indeks uređaja (0xFF žičano, 1–6 preko prijamnika). Promjena briše predmemoriju značajki.</summary>
    public byte DeviceIndex
    {
        get => _deviceIndex;
        set
        {
            _deviceIndex = value;
            lock (_features) _features.Clear();
        }
    }
    private const byte SwIdRequest = 0x0B;  // zahtjevi na koje čekamo odgovor
    private const byte SwIdFire = 0x0A;     // "pošalji i zaboravi" (boje)

    private readonly HidStream _stream;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _requestLock = new();
    private readonly Dictionary<ushort, byte> _features = new();
    // paketi "u letu" kod slanja u prozoru: ključ = (featureIdx << 8) | (fn << 4 | swId)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<byte[]>> _inflight = new();
    private volatile TaskCompletionSource<byte[]> _pending;
    private volatile byte _pendingFeature;
    private volatile byte _pendingFunction;
    private volatile bool _disposed;

    /// <summary>featureIndex, funkcija, parametri</summary>
    public event Action<byte, byte, byte[]> Notification;
    public event Action Disconnected;
    /// <summary>Greška na paket poslan bez čekanja: featureIdx, fn|swId, kod greške.</summary>
    public event Action<byte, byte, byte> ErrorReceived;

    public HidppDevice(HidStream stream)
    {
        _stream = stream;
    }

    public void Start()
    {
        Task.Run(ReadLoop);
    }

    private async Task ReadLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var report = await _stream.ReadAsync(_cts.Token).ConfigureAwait(false);
                try { Handle(report); }
                catch (Exception ex) { Log.Write("HID++ obrada: " + ex.Message); }
            }
        }
        catch (Exception ex)
        {
            if (!_disposed) Log.Write("HID++ čitanje prekinuto: " + ex.Message);
        }
        finally
        {
            _pending?.TrySetException(new IOException("Uređaj je odspojen."));
            if (!_disposed) Disconnected?.Invoke();
        }
    }

    private void Handle(byte[] r)
    {
        if (r.Length < 4) return;
        if (r[1] != _deviceIndex) return;   // poruka za drugi uređaj na istom prijamniku
        if (r[0] != LongReportId && r[0] != ShortReportId) return;

        byte feature = r[2];
        byte fnSw = r[3];
        var pending = _pending;

        // odgovori na pakete poslane u prozoru
        if (feature == 0xFF && r.Length >= 6 && _inflight.TryRemove((r[3] << 8) | r[4], out var failed))
        {
            failed.TrySetException(new HidppException(r[5]));
            return;
        }
        if (feature != 0xFF && _inflight.TryRemove((feature << 8) | fnSw, out var done))
        {
            var p2 = new byte[Math.Max(0, r.Length - 4)];
            Array.Copy(r, 4, p2, 0, p2.Length);
            done.TrySetResult(p2);
            return;
        }

        // HID++ 2.0 greška: [id][dev][0xFF][featIdx][fn|sw][kod]
        if (feature == 0xFF && pending != null && r.Length >= 6 && r[3] == _pendingFeature && r[4] == _pendingFunction)
        {
            _pending = null;
            pending.TrySetException(new HidppException(r[5]));
            return;
        }

        var parameters = new byte[Math.Max(0, r.Length - 4)];
        Array.Copy(r, 4, parameters, 0, parameters.Length);

        if (pending != null && feature == _pendingFeature && fnSw == _pendingFunction)
        {
            _pending = null;
            pending.TrySetResult(parameters);
            return;
        }

        // greška na paket poslan bez čekanja (npr. 0x08 zauzeto)
        if (feature == 0xFF && r.Length >= 6)
        {
            ErrorReceived?.Invoke(r[3], r[4], r[5]);
            return;
        }

        // Obavijesti uređaja imaju softverski ID 0.
        if ((fnSw & 0x0F) == 0 && feature != 0xFF)
            Notification?.Invoke(feature, (byte)(fnSw >> 4), parameters);
    }

    private byte[] Build(byte featureIndex, byte function, byte swId, byte[] args)
    {
        var b = new byte[20];
        b[0] = LongReportId;
        b[1] = _deviceIndex;
        b[2] = featureIndex;
        b[3] = (byte)((function << 4) | swId);
        if (args != null) Array.Copy(args, 0, b, 4, Math.Min(args.Length, 16));
        return b;
    }

    /// <summary>Šalje bez čekanja odgovora (koristi se za boje).</summary>
    public void Send(byte featureIndex, byte function, byte[] args) => Send(featureIndex, function, args, SwIdFire);

    /// <summary>Šalje bez čekanja odgovora s odabranim softverskim ID-om (1–15, ne 0x0B), za praćenje grešaka po paketu.</summary>
    public void Send(byte featureIndex, byte function, byte[] args, byte swId)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HidppDevice));
        _stream.Write(Build(featureIndex, function, (byte)(swId & 0x0F), args));
    }

    /// <summary>
    /// Šalje niz paketa iste značajke s najviše <paramref name="window"/> nepotvrđenih paketa odjednom.
    /// To je višestruko brže od čekanja svakog paketa, a red tipkovnice se ipak ne prepuni.
    /// Paket na koji tipkovnica odgovori "zauzeto" (0x08) šalje se ponovno.
    /// </summary>
    public int SendBatch(byte featureIndex, IReadOnlyList<(byte Function, byte[] Args)> packets, int window, int timeoutMs)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HidppDevice));
        int busy = 0;
        lock (_requestLock)
        {
            var queue = new Queue<(int Index, int Tries)>();
            for (int i = 0; i < packets.Count; i++) queue.Enqueue((i, 0));
            var flight = new Queue<(int Index, int Tries, int Key, Task<byte[]> Task)>();
            byte sw = 1;
            try
            {
                while (queue.Count > 0 || flight.Count > 0)
                {
                    while (flight.Count < window && queue.Count > 0)
                    {
                        var (idx, tries) = queue.Dequeue();
                        var (fn, args) = packets[idx];
                        byte fnSw = (byte)((fn << 4) | sw);
                        int key = (featureIndex << 8) | fnSw;
                        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _inflight[key] = tcs;
                        _stream.Write(Build(featureIndex, fn, sw, args));
                        flight.Enqueue((idx, tries, key, tcs.Task));
                        sw = (byte)(sw % 9 + 1); // softverski ID 1..9 (0x0A i 0x0B su zauzeti)
                    }

                    var (fi, ft, fk, task) = flight.Dequeue();
                    bool finished;
                    try { finished = task.Wait(timeoutMs); }
                    catch (AggregateException ae) when (ae.InnerException is HidppException he && he.ErrorCode == 0x08 && ft < 10)
                    {
                        busy++;
                        Thread.Sleep(1 + ft);
                        queue.Enqueue((fi, ft + 1));
                        continue;
                    }
                    catch (AggregateException ae) when (ae.InnerException != null)
                    {
                        throw ae.InnerException;
                    }
                    if (!finished)
                    {
                        _inflight.TryRemove(fk, out _);
                        throw new TimeoutException("Tipkovnica nije potvrdila paket boja.");
                    }
                }
            }
            finally
            {
                foreach (var f in flight) _inflight.TryRemove(f.Key, out _);
            }
        }
        return busy;
    }

    public byte[] Request(byte featureIndex, byte function, params byte[] args) =>
        Request(featureIndex, function, args, 1000);

    public byte[] Request(byte featureIndex, byte function, byte[] args, int timeoutMs)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HidppDevice));
        lock (_requestLock)
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingFeature = featureIndex;
            _pendingFunction = (byte)((function << 4) | SwIdRequest);
            _pending = tcs;
            try
            {
                _stream.Write(Build(featureIndex, function, SwIdRequest, args));
                if (!tcs.Task.Wait(timeoutMs))
                    throw new TimeoutException($"Nema odgovora (značajka 0x{featureIndex:X2}, funkcija {function}).");
                return tcs.Task.Result;
            }
            catch (AggregateException ae) when (ae.InnerException != null)
            {
                throw ae.InnerException;
            }
            finally
            {
                _pending = null;
            }
        }
    }

    /// <summary>Vraća indeks značajke (0 = nije podržana). Koristi Root značajku (indeks 0, funkcija 0).</summary>
    public byte GetFeatureIndex(ushort featureId) => GetFeatureIndex(featureId, 1000);

    public byte GetFeatureIndex(ushort featureId, int timeoutMs)
    {
        lock (_features)
        {
            if (_features.TryGetValue(featureId, out byte cached)) return cached;
        }
        var r = Request(0x00, 0, new[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF) }, timeoutMs);
        byte idx = r.Length > 0 ? r[0] : (byte)0;
        lock (_features) _features[featureId] = idx;
        return idx;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        _stream.Dispose();
    }
}

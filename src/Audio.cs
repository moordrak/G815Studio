using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Svitac;

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WAVEFORMATEX
{
    public ushort wFormatTag;
    public ushort nChannels;
    public int nSamplesPerSec;
    public int nAvgBytesPerSec;
    public ushort nBlockAlign;
    public ushort wBitsPerSample;
    public ushort cbSize;
}

/// <summary>
/// Hvata zvuk koji ide na zvučnike (WASAPI loopback) i računa razinu, frekvencijske pojaseve (FFT) i bas.
/// Kašnjenje je ~10–20 ms, pa svjetlo prati ritam glazbe.
/// </summary>
internal sealed class AudioAnalyzer : IDisposable
{
    public const int FftSize = 1024;
    private const int Ring = 4096;

    private readonly float[] _ring = new float[Ring];
    private int _write;
    private readonly object _lock = new();
    private Thread _thread;
    private volatile bool _stop;
    private long _lastDataTicks;

    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly float[] _window = new float[FftSize];

    public int SampleRate { get; private set; } = 48000;
    public bool Active { get; private set; }

    public AudioAnalyzer()
    {
        for (int i = 0; i < FftSize; i++)
            _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
    }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Zvuk (loopback)", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop)
        {
            try { Capture(); }
            catch (Exception ex) { Log.Write("Zvuk (loopback): " + ex.Message); }
            Active = false;
            for (int i = 0; i < 20 && !_stop; i++) Thread.Sleep(100);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    private void Capture()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice dev = null;
        IAudioClient client = null;
        IAudioCaptureClient cap = null;
        try
        {
            Check(en.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out dev));
            dev.GetId(out string deviceId);
            Guid iidClient = typeof(IAudioClient).GUID;
            Check(dev.Activate(ref iidClient, 23, IntPtr.Zero, out object o));
            client = (IAudioClient)o;

            Check(client.GetMixFormat(out IntPtr pwfx));
            WAVEFORMATEX fmt;
            bool isFloat;
            try
            {
                fmt = Marshal.PtrToStructure<WAVEFORMATEX>(pwfx);
                isFloat = fmt.wFormatTag == 3;
                if (fmt.wFormatTag == 0xFFFE && fmt.cbSize >= 22)
                {
                    var sub = new byte[16];
                    Marshal.Copy(pwfx + 24, sub, 0, 16);
                    isFloat = new Guid(sub) == FloatSubtype;
                }
                // 20 ms međuspremnik, loopback
                Check(client.Initialize(0 /* shared */, 0x00020000 /* LOOPBACK */, 200_000, 0, pwfx, IntPtr.Zero));
            }
            finally
            {
                Marshal.FreeCoTaskMem(pwfx);
            }

            Guid iidCap = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref iidCap, out object co));
            cap = (IAudioCaptureClient)co;
            Check(client.Start());

            SampleRate = fmt.nSamplesPerSec;
            int channels = Math.Max(1, (int)fmt.nChannels);
            int bits = fmt.wBitsPerSample;
            Active = true;
            Log.Write($"Zvuk (loopback): {SampleRate} Hz, {channels} kan., {bits} bit{(isFloat ? " float" : "")}");

            var fbuf = new float[0];
            var sbuf = new short[0];
            var ibuf = new int[0];
            var check = Stopwatch.StartNew();

            while (!_stop)
            {
                Thread.Sleep(4);
                Check(cap.GetNextPacketSize(out uint packet));
                while (packet > 0)
                {
                    Check(cap.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _));
                    int n = (int)frames * channels;
                    bool silent = (flags & 0x2) != 0;
                    lock (_lock)
                    {
                        if (silent || n == 0)
                        {
                            for (int f = 0; f < frames; f++) Push(0);
                        }
                        else if (isFloat && bits == 32)
                        {
                            if (fbuf.Length < n) fbuf = new float[n];
                            Marshal.Copy(data, fbuf, 0, n);
                            for (int f = 0; f < frames; f++)
                            {
                                float m = 0;
                                for (int c = 0; c < channels; c++) m += fbuf[f * channels + c];
                                Push(m / channels);
                            }
                        }
                        else if (bits == 16)
                        {
                            if (sbuf.Length < n) sbuf = new short[n];
                            Marshal.Copy(data, sbuf, 0, n);
                            for (int f = 0; f < frames; f++)
                            {
                                float m = 0;
                                for (int c = 0; c < channels; c++) m += sbuf[f * channels + c] / 32768f;
                                Push(m / channels);
                            }
                        }
                        else if (bits == 32)
                        {
                            if (ibuf.Length < n) ibuf = new int[n];
                            Marshal.Copy(data, ibuf, 0, n);
                            for (int f = 0; f < frames; f++)
                            {
                                float m = 0;
                                for (int c = 0; c < channels; c++) m += ibuf[f * channels + c] / 2147483648f;
                                Push(m / channels);
                            }
                        }
                        _lastDataTicks = Stopwatch.GetTimestamp();
                    }
                    Check(cap.ReleaseBuffer(frames));
                    Check(cap.GetNextPacketSize(out packet));
                }

                // promjena zadanog izlaza (npr. slušalice) → ponovno spajanje
                if (check.ElapsedMilliseconds > 3000)
                {
                    check.Restart();
                    if (en.GetDefaultAudioEndpoint(0, 1, out IMMDevice now) >= 0 && now != null)
                    {
                        now.GetId(out string nowId);
                        Marshal.ReleaseComObject(now);
                        if (nowId != deviceId) { Log.Write("Zvuk: promijenjen izlaz, ponovno spajanje."); return; }
                    }
                }
            }
        }
        finally
        {
            try { client?.Stop(); } catch { }
            if (cap != null) Marshal.ReleaseComObject(cap);
            if (client != null) Marshal.ReleaseComObject(client);
            if (dev != null) Marshal.ReleaseComObject(dev);
            Marshal.ReleaseComObject(en);
        }
    }

    private void Push(float v)
    {
        _ring[_write] = v;
        _write = (_write + 1) % Ring;
    }

    /// <summary>
    /// Analiza zadnjih ~21 ms zvuka. bands: logaritamski pojasevi 40 Hz – 16 kHz, 0..1.
    /// level: glasnoća 0..1, bass: energija 40–160 Hz 0..1. Vraća false ako nema zvuka.
    /// </summary>
    public bool Analyze(float[] bands, out float level, out float bass)
    {
        level = bass = 0;
        Array.Clear(bands);
        if (!Active) return false;
        double age = (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastDataTicks)) / (double)Stopwatch.Frequency;

        float peak = 0;
        lock (_lock)
        {
            int start = (_write - FftSize + Ring) % Ring;
            for (int i = 0; i < FftSize; i++)
            {
                float v = _ring[(start + i) % Ring];
                if (i >= FftSize - 512) peak = Math.Max(peak, Math.Abs(v));
                _re[i] = v * _window[i];
                _im[i] = 0;
            }
        }
        if (age > 0.5) return false;

        level = Math.Min(1f, peak);
        Fft(_re, _im);

        float binHz = SampleRate / (float)FftSize;
        int half = FftSize / 2;
        double lo = 40, hi = Math.Min(16000, SampleRate / 2.0 - binHz);
        int nb = bands.Length;
        for (int b = 0; b < nb; b++)
        {
            double f0 = lo * Math.Pow(hi / lo, b / (double)nb);
            double f1 = lo * Math.Pow(hi / lo, (b + 1) / (double)nb);
            int i0 = Math.Clamp((int)(f0 / binHz), 1, half - 1);
            int i1 = Math.Clamp((int)Math.Ceiling(f1 / binHz), i0 + 1, half);
            float m = 0;
            for (int i = i0; i < i1; i++) m = Math.Max(m, Mag(i));
            bands[b] = DbScale(m);
        }

        int b0 = Math.Max(1, (int)(40 / binHz)), b1 = Math.Max(b0 + 1, (int)(160 / binHz));
        float bm = 0;
        for (int i = b0; i <= b1 && i < half; i++) bm = Math.Max(bm, Mag(i));
        bass = DbScale(bm);
        return true;
    }

    private float Mag(int i) => (float)Math.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) / (FftSize / 4f);

    // -54 dB .. 0 dB → 0..1
    private static float DbScale(float m)
    {
        if (m <= 1e-6f) return 0;
        float db = 20f * (float)Math.Log10(m);
        return Math.Clamp((db + 54f) / 54f, 0f, 1f);
    }

    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    float xr = re[b] * cr - im[b] * ci;
                    float xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    float t = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = t;
                }
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(500);
    }
}

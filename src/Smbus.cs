using System.Runtime.InteropServices;

namespace G815Studio;

/// <summary>
/// SMBus na matičnoj ploči preko PawnIO (potpisani drajver, https://pawnio.eu) i njegovog modula SmbusI801.bin
/// (Intel PCH, npr. B760). Isti pristup koristi OpenRGB. Za sada se koristi samo za čitanje.
/// Potrebno: instaliran PawnIO, SmbusI801.bin pokraj G815Studio.exe ili u %APPDATA%\G815Studio, pokretanje kao administrator.
/// </summary>
public sealed class PawnIoSmbus : IDisposable
{
    private const string MutexName = @"Global\Access_SMBUS.HTP.Method";   // isti kao OpenRGB, da ne smetaju jedan drugome
    private const ulong Read = 1;
    private const ulong SizeByte = 1, SizeByteData = 2, SizeWordData = 3;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int VersionFn(out uint version);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OpenFn(out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LoadFn(IntPtr handle, byte[] blob, UIntPtr size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int ExecuteFn(IntPtr handle, string name, ulong[] input, UIntPtr inSize, ulong[] output, UIntPtr outSize, out UIntPtr returned);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CloseFn(IntPtr handle);

    private IntPtr _lib, _handle;
    private ExecuteFn _execute;
    private CloseFn _close;
    private Mutex _mutex;

    public string Description { get; private set; } = "";

    public static IEnumerable<string> LibCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "PawnIOLib.dll");
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf, "PawnIO", "PawnIOLib.dll");
    }

    public static IEnumerable<string> ModuleCandidates(string file)
    {
        yield return Path.Combine(AppContext.BaseDirectory, file);
        yield return Path.Combine(SettingsStore.Folder, file);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf, "PawnIO", file);
        yield return Path.Combine(pf, "PawnIO", "modules", file);
    }

    /// <summary>Otvori PawnIO i učita SMBus modul. Vraća null ako je sve u redu, inače opis problema.</summary>
    public string Open(string moduleFile = "SmbusI801.bin")
    {
        string libPath = LibCandidates().FirstOrDefault(File.Exists);
        if (libPath == null || !NativeLibrary.TryLoad(libPath, out _lib))
            return "PawnIO nije instaliran (nema PawnIOLib.dll). Instaliraj ga s https://pawnio.eu.";

        T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));
        var version = Fn<VersionFn>("pawnio_version");
        var open = Fn<OpenFn>("pawnio_open");
        var load = Fn<LoadFn>("pawnio_load");
        _execute = Fn<ExecuteFn>("pawnio_execute");
        _close = Fn<CloseFn>("pawnio_close");

        int hr = version(out uint v);
        Log.Write($"RAM: PawnIOLib {libPath}, verzija 0x{v:X} (hr 0x{hr:X8})");

        string modPath = ModuleCandidates(moduleFile).FirstOrDefault(File.Exists);
        if (modPath == null)
            return $"Nema modula {moduleFile}. Stavi ga pokraj G815Studio.exe ili u {SettingsStore.Folder}.";
        var blob = File.ReadAllBytes(modPath);

        hr = open(out _handle);
        if (hr != 0)
            return unchecked((uint)hr) == 0x80070005
                ? "PawnIO odbija pristup: pokreni G815 Studio kao administrator."
                : $"PawnIO se ne može otvoriti (hr 0x{hr:X8}).";
        hr = load(_handle, blob, (UIntPtr)blob.Length);
        if (hr != 0)
        {
            _close(_handle);
            _handle = IntPtr.Zero;
            return unchecked((uint)hr) == 0x80070032
                ? $"Modul {moduleFile} ne podržava ovu ploču."
                : $"Modul {moduleFile} se ne može učitati (hr 0x{hr:X8}).";
        }

        // identitet: naziv sabirnice (8 znakova) i PCI ID-ovi kontrolera
        var outId = new ulong[3];
        if (Exec("ioctl_identity", new ulong[] { 0 }, outId) == 0)
        {
            var name = new string(BitConverter.GetBytes(outId[0]).TakeWhile(b => b != 0).Select(b => (char)b).ToArray());
            ulong pci = outId[2];
            Description = $"{name}, PCI {pci & 0xFFFF:X4}:{(pci >> 16) & 0xFFFF:X4} (podsustav {(pci >> 32) & 0xFFFF:X4}:{(pci >> 48) & 0xFFFF:X4})";
        }
        Exec("ioctl_set_sleep_mode", new ulong[] { 2 }, Array.Empty<ulong>());
        try { _mutex = new Mutex(false, MutexName); }
        catch (Exception ex) { Log.Write("RAM: zajednički SMBus mutex nije dostupan: " + ex.Message); }
        Log.Write($"RAM: SMBus otvoren: {Description} (modul {modPath})");
        return null;
    }

    private int Exec(string fn, ulong[] input, ulong[] output)
    {
        var o = output.Length == 0 ? new ulong[1] : output;
        return _execute(_handle, fn, input, (UIntPtr)input.Length, o, (UIntPtr)output.Length, out _);
    }

    /// <summary>Jedan SMBus prijenos (samo čitanje). Vraća null ako uređaj ne odgovori.</summary>
    private ulong[] XferRead(byte addr, byte command, ulong size)
    {
        var input = new ulong[9];
        input[0] = addr;
        input[1] = Read;
        input[2] = command;
        input[3] = size;
        var output = new ulong[5];
        bool locked = false;
        try
        {
            try { locked = _mutex != null && _mutex.WaitOne(1000); } catch (AbandonedMutexException) { locked = true; }
            return Exec("ioctl_smbus_xfer", input, output) == 0 ? output : null;
        }
        finally
        {
            if (locked) _mutex?.ReleaseMutex();
        }
    }

    public int ReadByte(byte addr) => XferRead(addr, 0, SizeByte) is { } o ? (int)(o[0] & 0xFF) : -1;
    public int ReadByteData(byte addr, byte reg) => XferRead(addr, reg, SizeByteData) is { } o ? (int)(o[0] & 0xFF) : -1;
    public int ReadWordData(byte addr, byte reg) => XferRead(addr, reg, SizeWordData) is { } o ? (int)(o[0] & 0xFFFF) : -1;

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) { try { _close(_handle); } catch { } _handle = IntPtr.Zero; }
        _mutex?.Dispose();
        _mutex = null;
        if (_lib != IntPtr.Zero) { NativeLibrary.Free(_lib); _lib = IntPtr.Zero; }
    }
}

/// <summary>
/// Kingston Fury (DDR4/DDR5) RGB – 1. korak: samo dijagnostika. Ništa se ne upisuje ni u memoriju ni u njen RGB čip:
/// traži koje adrese na SMBus-u odgovaraju (SPD 0x50–0x57, RGB DDR4 0x58–0x5F, DDR5 0x60–0x67) i čita nekoliko registara.
/// </summary>
public static class RamProbe
{
    public static string Run()
    {
        using var bus = new PawnIoSmbus();
        string err;
        try { err = bus.Open(); }
        catch (Exception ex) { err = "PawnIO greška: " + ex.Message; }
        if (err != null)
        {
            Log.Write("RAM: " + err);
            return err;
        }

        var found = new List<string>();
        int spd = 0, rgb = 0;
        for (int a = 0x50; a <= 0x67; a++)
        {
            int b = bus.ReadByte((byte)a);
            if (b < 0) continue;
            string kind = a < 0x58 ? "SPD" : a < 0x60 ? "RGB DDR4?" : "RGB DDR5?";
            if (a < 0x58) spd++; else rgb++;
            string regs = "";
            if (a >= 0x58)
            {
                // registri 1–4 kod Fury modula nose potpis „FURY“ (u gornjem bajtu riječi), 6 je model
                var words = Enumerable.Range(1, 6).Select(r => bus.ReadWordData((byte)a, (byte)r)).ToArray();
                regs = ", riječi 1–6: " + string.Join(" ", words.Select(w => w < 0 ? "----" : w.ToString("X4")));
                string sig = new string(words.Take(4).Select(w => w < 0 ? '?' : (char)((w >> 8) & 0xFF)).ToArray());
                regs += $" (potpis „{Printable(sig)}“)";
            }
            Log.Write($"RAM: adresa 0x{a:X2} odgovara ({kind}), bajt {b:X2}{regs}");
            found.Add($"0x{a:X2}");
            Thread.Sleep(10);
        }
        string summary = $"SMBus {bus.Description}: SPD {spd}, RGB adresa {rgb} ({(found.Count == 0 ? "ništa ne odgovara" : string.Join(", ", found))}).";
        Log.Write("RAM: " + summary);
        return summary;
    }

    private static string Printable(string s) => new(s.Select(c => c >= 0x20 && c < 0x7F ? c : '·').ToArray());
}

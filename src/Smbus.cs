using System.Runtime.InteropServices;

namespace G815Studio;

/// <summary>
/// SMBus na matičnoj ploči preko PawnIO drajvera (https://pawnio.eu) i njegovog modula SmbusI801.bin (Intel PCH, npr. B760).
/// Isti pristup koristi OpenRGB. S drajverom se razgovara izravno kroz IOCTL (bez PawnIOLib.dll).
/// PawnIO po potrebi privremeno instalira <see cref="PawnIoHost"/>; program mora raditi kao administrator.
/// </summary>
public sealed class PawnIoSmbus : IDisposable
{
    private const string MutexName = @"Global\Access_SMBUS.HTP.Method";   // isti kao OpenRGB, da ne smetaju jedan drugome
    private const ulong Read = 1;
    private const ulong SizeByte = 1, SizeByteData = 2, SizeWordData = 3;

    // CTL_CODE(41394, funkcija, METHOD_BUFFERED, FILE_ANY_ACCESS) iz pawnio_um.h
    private const uint IoctlLoadBinary = (41394u << 16) | (0x821u << 2);
    private const uint IoctlExecuteFn = (41394u << 16) | (0x841u << 2);
    private const int FnNameLength = 32;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

    private Microsoft.Win32.SafeHandles.SafeFileHandle _dev;
    private Mutex _mutex;

    public string Description { get; private set; } = "";
    /// <summary>Win32 greška zadnjeg poziva modula (0 = uspjeh), za dijagnostiku.</summary>
    public int LastError { get; private set; }

    public static IEnumerable<string> ModuleCandidates(string file)
    {
        yield return Path.Combine(PawnIoHost.Folder, file);
        yield return Path.Combine(AppContext.BaseDirectory, file);
        yield return Path.Combine(SettingsStore.Folder, file);
    }

    /// <summary>Otvori PawnIO i učita SMBus modul. Vraća null ako je sve u redu, inače opis problema.</summary>
    public string Open(string moduleFile = "SmbusI801.bin")
    {
        string modPath = ModuleCandidates(moduleFile).FirstOrDefault(File.Exists);
        if (modPath == null) return $"Nema modula {moduleFile}.";
        var blob = File.ReadAllBytes(modPath);

        foreach (var path in new[] { @"\\.\GLOBALROOT\Device\PawnIO", @"\\.\PawnIO" })
        {
            var h = CreateFile(path, 0xC0000000 /* čitanje i pisanje */, 7 /* dijeli sve */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (!h.IsInvalid) { _dev = h; break; }
            int err = Marshal.GetLastWin32Error();
            h.Dispose();
            if (err == 5) return "PawnIO odbija pristup: program mora raditi kao administrator.";
        }
        if (_dev == null) return "PawnIO drajver nije pokrenut.";

        if (!DeviceIoControl(_dev, IoctlLoadBinary, blob, blob.Length, null, 0, out _, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            return err == 50 ? $"Modul {moduleFile} ne podržava ovu ploču." : $"Modul {moduleFile} se ne može učitati (greška {err}).";
        }

        // identitet: naziv sabirnice (8 znakova) i PCI ID-ovi kontrolera
        var outId = new ulong[3];
        if (Exec("ioctl_identity", new ulong[] { 0 }, outId))
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

    /// <summary>Poziv funkcije modula: ulaz je 32 bajta imena + ulazne vrijednosti, izlaz su 64-bitne vrijednosti.</summary>
    private bool Exec(string fn, ulong[] input, ulong[] output)
    {
        var inBuf = new byte[FnNameLength + input.Length * 8];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(fn);
        Array.Copy(nameBytes, inBuf, Math.Min(nameBytes.Length, FnNameLength - 1));
        Buffer.BlockCopy(input, 0, inBuf, FnNameLength, input.Length * 8);
        var outBuf = new byte[Math.Max(8, output.Length * 8)];
        bool ok = DeviceIoControl(_dev, IoctlExecuteFn, inBuf, inBuf.Length, outBuf, output.Length * 8, out int got, IntPtr.Zero);
        LastError = ok ? 0 : Marshal.GetLastWin32Error();
        if (ok && output.Length > 0) Buffer.BlockCopy(outBuf, 0, output, 0, Math.Min(got, output.Length * 8));
        return ok;
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
            return Exec("ioctl_smbus_xfer", input, output) ? output : null;
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
        _dev?.Dispose();
        _dev = null;
        _mutex?.Dispose();
        _mutex = null;
    }
}

/// <summary>
/// Kingston Fury (DDR4/DDR5) RGB – 1. korak: samo dijagnostika. Ništa se ne upisuje ni u memoriju ni u njen RGB čip
/// (PawnIO se po potrebi privremeno instalira):
/// traži koje adrese na SMBus-u odgovaraju (SPD 0x50–0x57, RGB DDR4 0x58–0x5F, DDR5 0x60–0x67) i čita nekoliko registara.
/// </summary>
public static class RamProbe
{
    public static string Run()
    {
        string err = PawnIoHost.Ensure();
        if (err != null)
        {
            Log.Write("RAM: " + err);
            return err;
        }
        using var bus = new PawnIoSmbus();
        try { err = bus.Open(); }
        catch (Exception ex) { err = "PawnIO greška: " + ex.Message; }
        if (err != null)
        {
            Log.Write("RAM: " + err);
            return err;
        }

        var found = new List<string>();
        int spd = 0, rgb = 0;
        var errors = new Dictionary<int, int>();
        for (int a = 0x50; a <= 0x67; a++)
        {
            // tri načina čitanja (sve samo čitanje): primanje bajta, bajt s registra 0, riječ s registra 0
            int b = bus.ReadByte((byte)a);
            int e1 = bus.LastError;
            if (b < 0) { b = bus.ReadByteData((byte)a, 0); }
            int e2 = bus.LastError;
            if (b < 0) { b = bus.ReadWordData((byte)a, 0); }
            int e3 = bus.LastError;
            if (b < 0)
            {
                errors[e1] = errors.GetValueOrDefault(e1) + 1;
                if (a is 0x50 or 0x58 or 0x60)
                    Log.Write($"RAM: adresa 0x{a:X2} ne odgovara (greške: bajt {e1}, bajt s registra {e2}, riječ {e3})");
                continue;
            }
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
        if (errors.Count > 0)
            Log.Write("RAM: greške po broju adresa: " + string.Join(", ", errors.Select(kv => $"{kv.Key} ×{kv.Value}")));
        string summary = $"SMBus {bus.Description}: SPD {spd}, RGB adresa {rgb} ({(found.Count == 0 ? "ništa ne odgovara" : string.Join(", ", found))}).";
        Log.Write("RAM: " + summary);
        return summary;
    }

    private static string Printable(string s) => new(s.Select(c => c >= 0x20 && c < 0x7F ? c : '·').ToArray());
}

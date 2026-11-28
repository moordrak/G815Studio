using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace G815Studio;

/// <summary>
/// PawnIO „bez trajne instalacije“: službeni PawnIO_setup.exe (ugrađen u exe) pokreće se s -install -silent kad je
/// potreban, a pri izlazu s -uninstall -silent. Ako je PawnIO bio instaliran prije programa, program ga ne dira.
/// Oznaka u %APPDATA%\G815Studio\PawnIO\installed-by-g815studio pamti da ga je instalirao program, pa se nakon
/// rušenja ukloni pri sljedećem izlazu.
/// </summary>
public static class PawnIoHost
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private static readonly object Lock = new();

    public static string Folder => Path.Combine(SettingsStore.Folder, "PawnIO");
    private static string Marker => Path.Combine(Folder, "installed-by-g815studio");
    private static string SetupPath => Path.Combine(Folder, "PawnIO_setup.exe");

    public static bool IsElevated
    {
        get
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    public static bool IsInstalled
    {
        get
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(UninstallKey);
                return k != null;
            }
            catch { return false; }
        }
    }

    public static bool InstalledByUs => File.Exists(Marker);

    /// <summary>Raspakira ugrađene datoteke (instalacijski program i SMBus modul) u mapu programa.</summary>
    public static void Extract()
    {
        Directory.CreateDirectory(Folder);
        foreach (var name in new[] { "PawnIO_setup.exe", "SmbusI801.bin" })
        {
            using var src = typeof(PawnIoHost).Assembly.GetManifestResourceStream(name);
            if (src == null) throw new FileNotFoundException("U programu nedostaje " + name);
            string dst = Path.Combine(Folder, name);
            if (File.Exists(dst) && new FileInfo(dst).Length == src.Length) continue;
            using var f = File.Create(dst);
            src.CopyTo(f);
        }
    }

    /// <summary>Osigura da je PawnIO instaliran. Vraća null ako je sve u redu, inače opis problema.</summary>
    public static string Ensure()
    {
        lock (Lock)
        {
            if (!IsElevated) return "Za memoriju program mora raditi kao administrator.";
            try { Extract(); }
            catch (Exception ex) { return "Raspakiravanje PawnIO-a nije uspjelo: " + ex.Message; }
            if (IsInstalled)
            {
                string where = "";
                try
                {
                    using var k = Registry.LocalMachine.OpenSubKey(UninstallKey);
                    where = $" (mapa: {k?.GetValue("InstallLocation")}, verzija: {k?.GetValue("DisplayVersion")})";
                }
                catch { }
                Log.Write(InstalledByUs ? "RAM: PawnIO je od prije instalirao G815 Studio (bit će uklonjen pri izlazu)." + where
                                        : "RAM: PawnIO je već instaliran u sustavu – program ga neće uklanjati." + where);
                return null;
            }
            Log.Write("RAM: instaliram PawnIO (privremeno, do izlaza iz programa)…");
            int code = Run("-install -silent");
            if (!IsInstalled)
                return $"Instalacija PawnIO-a nije uspjela (izlazni kod {code}).";
            File.WriteAllText(Marker, DateTime.Now.ToString("s"));
            Log.Write($"RAM: PawnIO instaliran (izlazni kod {code}).");
            return null;
        }
    }

    /// <summary>Pri izlazu: ukloni PawnIO ako ga je instalirao ovaj program.</summary>
    public static void RemoveIfOurs()
    {
        lock (Lock)
        {
            if (!InstalledByUs) return;
            if (!IsElevated)
            {
                Log.Write("RAM: PawnIO nije uklonjen jer program ne radi kao administrator (bit će uklonjen sljedeći put).");
                return;
            }
            if (IsInstalled)
            {
                try { Extract(); } catch { }
                int code = Run("-uninstall -silent");
                Log.Write($"RAM: PawnIO deinstaliran (izlazni kod {code}){(IsInstalled ? ", ali je još u sustavu" : "")}.");
            }
            if (!IsInstalled) { try { File.Delete(Marker); } catch { } }
        }
    }

    private static int Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(SetupPath, args) { UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return -1;
            if (!p.WaitForExit(60_000)) { Log.Write("RAM: PawnIO_setup ne završava, prekidam čekanje."); return -2; }
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Write("RAM: PawnIO_setup " + args + ": " + ex.Message);
            return -3;
        }
    }

    /// <summary>Pokreni program ponovno kao administrator (UAC) i zatvori ovaj primjerak.</summary>
    public static bool RestartElevated()
    {
        try
        {
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Ponovno pokretanje kao administrator nije uspjelo: " + ex.Message);
            return false;
        }
    }
}

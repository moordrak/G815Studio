namespace Svitac;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\SVITAC_SingleInstance", out bool created);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\SVITAC_Show");
        if (!created && args.Any(a => a.Equals("--restart", StringComparison.OrdinalIgnoreCase)))
        {
            // ponovno pokretanje (npr. kao administrator): pričekaj da se stari primjerak zatvori
            try { created = mutex.WaitOne(20_000); } catch (AbandonedMutexException) { created = true; }
        }
        if (!created)
        {
            // već radi – samo mu javi da otvori prozor
            showEvent.Set();
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportError(e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        int shotIdx = Array.FindIndex(args, a => a.Equals("--screenshots", StringComparison.OrdinalIgnoreCase));
        if (shotIdx >= 0)
        {
            // način za snimke zaslona (koristi ga GitHub Actions za slike u README-u)
            string dir = shotIdx + 1 < args.Length ? args[shotIdx + 1] : "screenshots";
            Application.Run(new ScreenshotRunner(dir));
            return;
        }

        SettingsStore.MigrateFromG815Studio();
        L.Init(SettingsStore.Load().Language);

        bool startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        Log.Write("Pokretanje SVITAC " + typeof(Program).Assembly.GetName().Version);

        var ctx = new TrayContext(startHidden);
        var wait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => ctx.PostShow(), null, -1, false);
        Application.Run(ctx);
        wait.Unregister(null);
        GC.KeepAlive(mutex);
    }

    private static void ReportError(Exception ex)
    {
        if (ex == null) return;
        Log.Write("NEOČEKIVANA GREŠKA: " + ex);
        try
        {
            MessageBox.Show(L.T("Dogodila se greška:") + "\n\n" + ex.Message + "\n\n" + L.T("Detalji su u log.txt (Postavke → Otvori mapu)."),
                "SVITAC", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}

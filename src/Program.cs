namespace G815Studio;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\G815Studio_SingleInstance", out bool created);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\G815Studio_Show");
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

        bool startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        Log.Write("Pokretanje G815 Studio " + typeof(Program).Assembly.GetName().Version);

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
            MessageBox.Show("Dogodila se greška:\n\n" + ex.Message + "\n\nDetalji su u log.txt (Postavke → Otvori mapu).",
                "G815 Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch
        {
        }
    }
}

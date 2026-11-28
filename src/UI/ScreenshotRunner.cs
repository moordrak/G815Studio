using System.Drawing.Imaging;

namespace G815Studio;

/// <summary>
/// Pokreće se s argumentom --screenshots &lt;mapa&gt;: otvori glavni prozor s primjerima profila i makroa,
/// snimi sve ekrane u PNG i zatvori se. Koristi ga GitHub Actions za slike u README-u.
/// Prave postavke korisnika se ne diraju (koristi se privremena mapa).
/// </summary>
internal sealed class ScreenshotRunner : ApplicationContext
{
    public ScreenshotRunner(string dir)
    {
        string temp = Path.Combine(Path.GetTempPath(), "G815Studio-snimke");
        try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        SettingsStore.FolderOverride = temp;
        _ = Run(Path.GetFullPath(dir));
    }

    private async Task Run(string dir)
    {
        Directory.CreateDirectory(dir);
        AppController c = null;
        MainForm form = null;
        try
        {
            c = new AppController();
            Prepare(c);

            form = new MainForm(c)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(0, 0),
                AllowOversize = true
            };
            form.Show();
            // WinForms ne da prozor veći od zaslona; SetWindowPos uz WM_GETMINMAXINFO u MainFormu to dopušta
            SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 1440, 940, 0x0004 /* SWP_NOZORDER */ | 0x0010 /* SWP_NOACTIVATE */);
            form.PerformLayout();
            form.Activate();
            await Task.Delay(2000);

            await Shot(c, form, dir, "osvjetljenje-val.png", EffectType.Wave);
            await Shot(c, form, dir, "osvjetljenje-vatra.png", EffectType.Fire);
            await Shot(c, form, dir, "osvjetljenje-zvijezde.png", EffectType.Starry);

            SetEffect(c, EffectType.Static);
            form.ReloadAll();
            form.ShowPage(0);
            form.KeyboardViewForScreenshots.SelectWhere(k => k.Id is "U1A" or "U04" or "U16" or "U07", false);
            await Task.Delay(1500);
            Save(form, dir, "osvjetljenje-staticno.png");
            form.KeyboardViewForScreenshots.ClearSelection();

            string[] pages = { "makroi.png", "profili.png", "postavke.png" };
            for (int i = 0; i < pages.Length; i++)
            {
                form.ShowPage(i + 1);
                await Task.Delay(1200);
                Save(form, dir, pages[i]);
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
        }
        finally
        {
            try { form?.Close(); } catch { }
            try { c?.Dispose(); } catch { }
            ExitThread();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private static async Task Shot(AppController c, MainForm form, string dir, string name, EffectType effect)
    {
        SetEffect(c, effect);
        form.ReloadAll();
        form.ShowPage(0);
        await Task.Delay(1800);
        Save(form, dir, name);
    }

    private static void SetEffect(AppController c, EffectType e)
    {
        var p = c.ManualProfile;
        p.Lighting.Effect = e;
        c.LightingChanged(p);
    }

    /// <summary>Primjeri profila i makroa da ekrani ne budu prazni.</summary>
    private static void Prepare(AppController c)
    {
        var s = c.Settings;
        // na snimkama se vidi i miš (G502 X PLUS ima 8 LED-ica), iako na runneru nije spojen
        c.PreviewMouseZones = 8;
        c.PreviewPcChannels = new() { new("pc:2", "ARGB header 1", 24), new("pc:3", "ARGB header 2", 9), new("pc:7", "ARGB header 3 / audio", 18), new("pc:6", "Ploča (PCB)", 3) };
        c.PreviewRamSticks = 4;
        c.RebuildKeys();
        var main = c.ManualProfile;
        var L = main.Lighting;
        foreach (var id in new[] { "U1A", "U04", "U16", "U07" }) L.KeyColors[id] = "#FF2A4D";
        for (int i = 0x3A; i <= 0x45; i++) L.KeyColors["U" + i.ToString("X2")] = "#FF9F1A";
        L.KeyColors["U29"] = "#FF9F1A";

        main.Macros[Profile.MacroKey(1, 1)] = new Macro
        {
            Name = "Kopiraj i zalijepi",
            Steps =
            {
                new MacroStep { Type = StepType.KeyDown, Vk = 0xA2 },
                new MacroStep { Type = StepType.KeyPress, Vk = 'C' },
                new MacroStep { Type = StepType.KeyUp, Vk = 0xA2 },
                new MacroStep { Type = StepType.Delay, DelayMs = 50 },
                new MacroStep { Type = StepType.KeyDown, Vk = 0xA2 },
                new MacroStep { Type = StepType.KeyPress, Vk = 'V' },
                new MacroStep { Type = StepType.KeyUp, Vk = 0xA2 }
            }
        };
        main.Macros[Profile.MacroKey(1, 2)] = new Macro
        {
            Name = "Potpis e-pošte",
            Steps = { new MacroStep { Type = StepType.Text, Text = "Lijep pozdrav,\nIvan" } }
        };
        main.Macros[Profile.MacroKey(1, 3)] = new Macro
        {
            Name = "Svjetlo u sobi",
            Steps =
            {
                new MacroStep { Type = StepType.Http, Method = "POST", Url = "http://homeassistant.local:8123/api/webhook/svjetlo", Body = "{\"stanje\":\"toggle\"}" }
            }
        };
        main.Macros[Profile.MacroKey(1, 4)] = new Macro
        {
            Name = "Vremenska prognoza",
            Steps =
            {
                new MacroStep { Type = StepType.Input, Variable = "grad", Prompt = "Za koji grad?", DefaultValue = "Zagreb" },
                new MacroStep { Type = StepType.Http, Method = "GET", Url = "https://wttr.in/{grad:url}?format=3" },
                new MacroStep { Type = StepType.Text, Text = "{odgovor}" }
            }
        };

        var cs2 = SettingsStore.CreateDefaultProfile();
        cs2.Name = "CS2";
        cs2.Apps.Add("cs2.exe");
        cs2.Lighting.Effect = EffectType.Reactive;
        var work = SettingsStore.CreateDefaultProfile();
        work.Name = "Posao";
        work.Apps.Add("outlook.exe");
        work.Apps.Add("teams.exe");
        s.Profiles.Add(cs2);
        s.Profiles.Add(work);
        c.ProfilesChanged();
    }

    private static void Save(Form f, string dir, string name)
    {
        // prozor je veći od zaslona, pa se crta sam (ne snima se zaslon)
        var r = f.RectangleToScreen(f.ClientRectangle);
        using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        {
            using var full = new Bitmap(f.Width, f.Height, PixelFormat.Format32bppArgb);
            f.DrawToBitmap(full, new Rectangle(0, 0, f.Width, f.Height));
            int ox = r.X - f.Left, oy = r.Y - f.Top;
            using var g = Graphics.FromImage(bmp);
            g.DrawImage(full, new Rectangle(0, 0, r.Width, r.Height), new Rectangle(ox, oy, r.Width, r.Height), GraphicsUnit.Pixel);
        }
        bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
    }

    private static bool IsBlank(Bitmap b)
    {
        var first = b.GetPixel(b.Width / 2, b.Height / 2);
        for (int i = 1; i < 60; i++)
        {
            var p = b.GetPixel(b.Width * i / 61, b.Height * ((i * 7) % 60 + 1) / 61);
            if (p.ToArgb() != first.ToArgb()) return false;
        }
        return true;
    }
}

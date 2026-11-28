namespace Svitac;

public static class ColorUtil
{
    public static Color Parse(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var c = ColorTranslator.FromHtml(hex.Trim());
            return Color.FromArgb(255, c.R, c.G, c.B);
        }
        catch
        {
            return fallback;
        }
    }

    public static Color Parse(string hex) => Parse(hex, Color.Black);

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static int Rgb(Color c) => c.ToArgb() & 0xFFFFFF;

    private static int Clamp(double v) => v < 0 ? 0 : v > 255 ? 255 : (int)(v + 0.5);

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(255,
            Clamp(a.R + (b.R - a.R) * t),
            Clamp(a.G + (b.G - a.G) * t),
            Clamp(a.B + (b.B - a.B) * t));
    }

    public static Color Scale(Color c, double k) =>
        Color.FromArgb(255, Clamp(c.R * k), Clamp(c.G * k), Clamp(c.B * k));

    public static Color Hsv(double h, double s, double v)
    {
        h = h - Math.Floor(h);
        double hh = h * 6;
        int i = (int)Math.Floor(hh) % 6;
        double f = hh - Math.Floor(hh);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        double r, g, b;
        switch (i)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return Color.FromArgb(255, Clamp(r * 255), Clamp(g * 255), Clamp(b * 255));
    }

    public static Color Saturate(Color c, double amount)
    {
        double gray = (c.R + c.G + c.B) / 3.0;
        return Color.FromArgb(255,
            Clamp(gray + (c.R - gray) * amount),
            Clamp(gray + (c.G - gray) * amount),
            Clamp(gray + (c.B - gray) * amount));
    }

    public static bool IsLight(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140;
}

public static class Log
{
    private static readonly object Lock = new();
    public static string FilePath => Path.Combine(SettingsStore.Folder, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000) fi.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // logiranje nikad ne smije srušiti program
        }
    }
}

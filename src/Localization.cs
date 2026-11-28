using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Svitac;

/// <summary>
/// Prijevodi sučelja. Ključ je izvorni hrvatski tekst, pa kod ostaje čitljiv: <c>L.T("Spremi")</c>.
/// Prijevodi su ugrađeni kao res/lang/&lt;kod&gt;.json ({ "hrvatski tekst": "prijevod" }).
/// Ako prijevod nedostaje, prikazuje se engleski, a ako ni njega nema, hrvatski izvornik.
/// </summary>
internal static class L
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("hr", "Hrvatski"),
        ("en", "English"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("es", "Español"),
        ("it", "Italiano")
    };

    private static Dictionary<string, string> _map;
    private static Dictionary<string, string> _fallback;

    /// <summary>Trenutni jezik ("hr", "en", …).</summary>
    public static string Lang { get; private set; } = "hr";

    /// <summary>Jezik Windowsa ako je podržan, inače engleski.</summary>
    public static string SystemLanguage
    {
        get
        {
            string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (sys is "bs" or "sr" or "sh") sys = "hr";
            return Languages.Any(l => l.Code == sys) ? sys : "en";
        }
    }

    /// <summary>Učitava jezik; prazno ili nepoznato = jezik Windowsa.</summary>
    public static void Init(string preferred)
    {
        string code = string.IsNullOrWhiteSpace(preferred) || Languages.All(l => l.Code != preferred) ? SystemLanguage : preferred;
        Lang = code;
        _map = code == "hr" ? null : Load(code);
        _fallback = code is "hr" or "en" ? null : Load("en");
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch
        {
        }
    }

    private static Dictionary<string, string> Load(string code)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"lang.{code}.json");
            if (s == null) return null;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s);
        }
        catch (Exception ex)
        {
            Log.Write($"Prijevod {code} nije učitan: {ex.Message}");
            return null;
        }
    }

    /// <summary>Prijevod hrvatskog teksta.</summary>
    public static string T(string hr)
    {
        if (hr == null) return null;
        if (_map != null && _map.TryGetValue(hr, out var t) && !string.IsNullOrEmpty(t)) return t;
        if (_fallback != null && _fallback.TryGetValue(hr, out var e) && !string.IsNullOrEmpty(e)) return e;
        return hr;
    }

    /// <summary>Označava tekst za prijevod bez prevođenja (statična polja); prevodi se tamo gdje se prikazuje, s <see cref="T"/>.</summary>
    public static string N(string hr) => hr;

    /// <summary>Prijevod s parametrima: <c>L.F("Odabrano tipki: {0}", n)</c>.</summary>
    public static string F(string hrFormat, params object[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, T(hrFormat), args); }
        catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, hrFormat, args); }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace G815Studio;

public enum EffectType
{
    Static, Breathing, Wave, ColorCycle, Reactive, Ripple, Audio, Screen, Off,
    // efekti po uzoru na OpenRGB
    Spectrum, BassPulse, Swirl, RadialRainbow, GradientWave, Starry, Rain, Fire, Visor, Lightning, Mosaic
}

public sealed class LightingConfig
{
    public EffectType Effect = EffectType.Static;
    public string Color1 = "#00A0FF";
    public string Color2 = "#FF2060";
    public string Background = "#000000";
    public int Speed = 50;        // 1..100
    public int Brightness = 100;  // 0..100
    public int Direction = 0;     // val: 0 lijevo→desno, 1 desno→lijevo, 2 gore→dolje, 3 iz središta
    public Dictionary<string, string> KeyColors = new();  // KeyDef.Id -> #RRGGBB

    /// <summary>Boje, brzina i smjer posebno za svaki efekt (ključ = naziv efekta).</summary>
    public Dictionary<string, EffectParams> EffectSettings = new();

    /// <summary>Postavke zadanog efekta; ako ih još nema, stvaraju se iz zadanih vrijednosti tog efekta.</summary>
    public EffectParams For(EffectType e)
    {
        EffectSettings ??= new();
        string key = e.ToString();
        if (!EffectSettings.TryGetValue(key, out var p) || p == null)
        {
            p = EffectParams.DefaultFor(e, this);
            EffectSettings[key] = p;
        }
        return p;
    }

    public EffectParams Current => For(Effect);
}

public sealed class EffectParams
{
    public string Color1;
    public string Color2;
    public string Background = "#000000";
    public int Speed = 50;          // 1..100
    public int Direction;           // 0 lijevo→desno, 1 desno→lijevo, 2 gore→dolje, 3 iz središta
    public bool Rainbow = true;     // kod efekata s dugom: duga ili vlastite boje (Boja 1 ↔ Boja 2)

    public static EffectParams DefaultFor(EffectType e, LightingConfig legacy)
    {
        // stari profili imaju zajedničke boje – za efekte koji su ih koristili zadrži ih
        string l1 = legacy?.Color1 ?? "#00A0FF", l2 = legacy?.Color2 ?? "#FF2060", lb = legacy?.Background ?? "#000000";
        int ls = legacy?.Speed ?? 50, ld = legacy?.Direction ?? 0;
        return e switch
        {
            EffectType.Static or EffectType.Breathing => new() { Color1 = l1, Color2 = l2, Background = lb, Speed = ls },
            EffectType.Reactive or EffectType.Ripple => new() { Color1 = l1, Color2 = l2, Background = lb, Speed = ls },
            EffectType.Audio => new() { Color1 = "#3DDC84", Color2 = "#FF3B5C", Background = "#000000", Speed = 50 },
            EffectType.Spectrum => new() { Color1 = "#00C8FF", Color2 = "#FF2A6D", Background = "#000000", Speed = 50 },
            EffectType.BassPulse => new() { Color1 = "#FF2A6D", Color2 = "#00C8FF", Background = "#000000", Speed = 50 },
            EffectType.Wave or EffectType.Swirl or EffectType.RadialRainbow or EffectType.ColorCycle
                => new() { Color1 = "#00C8FF", Color2 = "#B000FF", Speed = ls, Direction = ld, Rainbow = true },
            EffectType.GradientWave => new() { Color1 = "#00C8FF", Color2 = "#FF2A6D", Speed = 50, Direction = ld, Rainbow = false },
            EffectType.Starry => new() { Color1 = "#FFFFFF", Color2 = "#7AB8FF", Background = "#000010", Speed = 50 },
            EffectType.Rain => new() { Color1 = "#00FF66", Color2 = "#C8FFD8", Background = "#000000", Speed = 50 },
            EffectType.Fire => new() { Color1 = "#FF2000", Color2 = "#FFC830", Background = "#000000", Speed = 50 },
            EffectType.Visor => new() { Color1 = "#FF0028", Color2 = "#FF0028", Background = "#000000", Speed = 50 },
            EffectType.Lightning => new() { Color1 = "#DDE8FF", Color2 = "#8A6CFF", Background = "#000000", Speed = 50 },
            EffectType.Mosaic => new() { Color1 = "#00C8FF", Color2 = "#FF2A6D", Speed = 50, Rainbow = true },
            _ => new() { Color1 = l1, Color2 = l2, Background = lb, Speed = ls }
        };
    }
}

public enum StepType { KeyPress, KeyDown, KeyUp, Delay, Text, Run, Http, Input }

public sealed class MacroStep
{
    public StepType Type;
    public int Vk;
    public int Scan;
    public bool Extended;
    public int DelayMs;
    public string Text;
    public string Path;
    public string Args;

    // HTTP poziv
    public string Method = "GET";
    public string Url;
    public string ContentType;
    public string Headers;        // jedan po retku: "Naziv: vrijednost"
    public string Body;
    public int TimeoutMs = 10000;
    public bool ShowResult = true;

    // unos vrijednosti
    public string Variable;       // naziv varijable, koristi se kao {naziv}
    public string Prompt;
    public string DefaultValue;

    public static string KeyName(int vk) => vk switch
    {
        0xA0 => "LShift", 0xA1 => "RShift", 0xA2 => "LCtrl", 0xA3 => "RCtrl", 0xA4 => "LAlt", 0xA5 => "RAlt",
        0x10 => "Shift", 0x11 => "Ctrl", 0x12 => "Alt", 0x5B => "LWin", 0x5C => "RWin",
        _ => ((Keys)vk).ToString()
    };

    public override string ToString() => Type switch
    {
        StepType.KeyPress => $"⇅ Pritisni  {KeyName(Vk)}",
        StepType.KeyDown => $"↓ Spusti  {KeyName(Vk)}",
        StepType.KeyUp => $"↑ Otpusti  {KeyName(Vk)}",
        StepType.Delay => $"⏱ Čekaj  {DelayMs} ms",
        StepType.Text => $"✎ Upiši  \"{(Text ?? "").Replace("\r", "").Replace("\n", "⏎")}\"",
        StepType.Run => $"▶ Pokreni  {System.IO.Path.GetFileName(Path ?? "")} {Args}",
        StepType.Http => $"⇄ HTTP {Method ?? "GET"}  {Url}",
        StepType.Input => $"? Unos  {{{Variable}}}  \"{Prompt}\"",
        _ => Type.ToString()
    };
}

public enum PlayMode { Once, RepeatWhileHeld, Toggle }

public sealed class Macro
{
    public string Name = "";
    public PlayMode Mode = PlayMode.Once;
    public List<MacroStep> Steps = new();
}

public sealed class Profile
{
    public string Name = "Zadano";
    public List<string> Apps = new();   // npr. "cs2.exe"
    public LightingConfig Lighting = new();
    public Dictionary<string, Macro> Macros = new();  // "M1G1" .. "M3G5"

    public static string MacroKey(int bank, int g) => $"M{bank}G{g}";
}

public sealed class AppSettings
{
    public List<Profile> Profiles = new();
    public string ActiveProfile = "Zadano";
    public bool AutoSwitch = true;
    public string Layout = "ISO";
    public int Fps = 30;
    public bool RecordDelays = true;
    public Dictionary<string, int> ZoneOverrides = new();
    public int ConfigVersion;

    // miš (npr. G502 X PLUS)
    public bool MouseEnabled;
    public bool MouseFollow = true;       // true = prati efekt tipkovnice, false = jedna boja
    public string MouseColor = "#00C8FF";
    public bool MouseHostMode;            // miš u host načinu dok program radi
    public bool LightsOffOnExit = true;   // pri izlazu ugasi tipkovnicu, miša i računalo (ništa ne svijetli)
    public bool PcEnabled = true;
    public bool RamEnabled;               // Kingston Fury memorija (treba PawnIO i administratorska prava)         // ASRock RGB kontroler na matičnoj ploči
    public bool PcFollow = true;          // true = prati efekt tipkovnice, false = jedna boja
    public string PcColor = "#00C8FF";
    public bool PcSwapRG;                 // dodatna zamjena crvene i zelene za ventilatore s drugim redoslijedom
}

public static class SettingsStore
{
    /// <summary>Druga mapa za postavke (koristi se u načinu za snimke zaslona da se ne diraju prave postavke).</summary>
    public static string FolderOverride { get; set; }

    public static string Folder => FolderOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G815Studio");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (s != null) return Normalize(s);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Učitavanje postavki nije uspjelo: " + ex.Message);
            try { File.Copy(FilePath, FilePath + ".pokvareno", true); } catch { }
        }
        return Normalize(new AppSettings());
    }

    private static AppSettings Normalize(AppSettings s)
    {
        s.Profiles ??= new();
        s.ZoneOverrides ??= new();
        if (s.Profiles.Count == 0) s.Profiles.Add(CreateDefaultProfile());
        foreach (var p in s.Profiles)
        {
            p.Apps ??= new();
            p.Macros ??= new();
            p.Lighting ??= new();
            p.Lighting.KeyColors ??= new();
            p.Lighting.EffectSettings ??= new();
            foreach (var m in p.Macros.Values) m.Steps ??= new();
        }
        if (s.Profiles.All(p => p.Name != s.ActiveProfile)) s.ActiveProfile = s.Profiles[0].Name;
        s.Fps = Math.Clamp(s.Fps, 5, 60);
        if (s.ConfigVersion < 2)
        {
            // jednokratno: zone medijskih tipki su sada točne zadane vrijednosti
            foreach (var id in new[] { "MEDIA_PREV", "MEDIA_PLAY", "MEDIA_NEXT", "MEDIA_MUTE" })
                s.ZoneOverrides.Remove(id);
            s.ConfigVersion = 2;
        }
        return s;
    }

    public static Profile CreateDefaultProfile()
    {
        var p = new Profile { Name = "Zadano" };
        p.Lighting.KeyColors["LOGO"] = "#FFFFFF";
        for (int i = 1; i <= 5; i++) p.Lighting.KeyColors["G" + i] = "#FF7A00";
        return p;
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex)
        {
            Log.Write("Spremanje postavki nije uspjelo: " + ex.Message);
        }
    }

    public static T Clone<T>(T obj) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj, Options), Options);
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "G815Studio";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) != null;
        }
    }

    public static void Set(bool enable)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
        else k.DeleteValue(ValueName, false);
    }
}

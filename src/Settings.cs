using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace G815Studio;

public enum EffectType { Static, Breathing, Wave, ColorCycle, Reactive, Ripple, Audio, Screen, Off }

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
}

public static class SettingsStore
{
    public static string Folder =>
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
            foreach (var m in p.Macros.Values) m.Steps ??= new();
        }
        if (s.Profiles.All(p => p.Name != s.ActiveProfile)) s.ActiveProfile = s.Profiles[0].Name;
        s.Fps = Math.Clamp(s.Fps, 5, 60);
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

namespace G815Studio;

public sealed class KeyDef
{
    public string Id { get; init; }      // "U04" (HID usage), "G1".."G5", "LOGO"
    public string Label { get; init; }
    public int Usage { get; init; }
    public int DefaultZone { get; init; }
    public int Zone { get; set; }       // stvarna zona (nakon korisničkih izmjena)
    public float X { get; init; }
    public float Y { get; init; }
    public float W { get; init; } = 1;
    public float H { get; init; } = 1;
    public string Group { get; init; }
    public string Shape { get; init; }   // null = pravokutnik, "isoEnter" = Enter sa zubom ulijevo, "round" = okrugla tipka, "logo"

    public float CenterX => X + W / 2;
    public float CenterY => Y + H / 2;
    public override string ToString() => $"{Label}  [{Id}, zona 0x{Zone:X2}]";
}

public static class KeyLayouts
{
    /// <summary>
    /// G815 adresira tipke "zonama": za većinu tipki zona = HID usage − 3,
    /// za modifikatore (0xE0–0xE7) zona = usage − 0x78, G1–G5 = 0xB4–0xB8, logo = 0xD2.
    /// </summary>
    public static byte ZoneFromUsage(int usage) =>
        usage >= 0xE0 && usage <= 0xE7 ? (byte)(usage - 0x78) : (byte)(usage - 0x03);

    // Miš na slici, u jedinicama tipki (isti koordinatni sustav kao tipke): okvir tijela i luk LED-ica.
    public const float MouseX = 25.4f, MouseY = 1.15f, MouseW = 3.8f, MouseH = 7.0f;
    public const float MouseLedSize = 0.42f;

    /// <summary>Središte i-te LED-ice miša: luk preko stražnjeg dijela, slijeva nadesno.</summary>
    public static (float X, float Y) MouseLed(int i, int count)
    {
        double t = count <= 1 ? 0.5 : (double)i / (count - 1);
        double a = (160 - 140 * t) * Math.PI / 180;       // 160° (lijevo) → 20° (desno), kroz dno luka
        float cx = MouseX + MouseW * 0.52f, cy = MouseY + MouseH * 0.6f;
        return ((float)(cx + Math.Cos(a) * 1.3), (float)(cy + Math.Sin(a) * 0.95));
    }

    // Računalo (ASRock RGB): trake ispod tipkovnice, jedna po izlazu. Svaka stvarna LED-ica ima svoju virtualnu
    // zonu od 0x100 naviše (izvan raspona tipkovnice), pa efekti teku glatko duž ARGB traka.
    public const int PcZoneBase = 0x100;
    public const int PcMaxLeds = 0x200;
    /// <summary>Ukupan broj zona u okviru efekata (tipkovnica 0–0xFF + LED-ice računala).</summary>
    public const int ZoneCount = PcZoneBase + PcMaxLeds;
    public const float PcY = 9.3f, PcRowH = 0.85f, PcX = 1.5f, PcW = 22.5f;

    // Memorija (Kingston Fury): okomiti moduli ispod miša; virtualne zone 0xC0–0xCF (po modulu RamSegments zona, odozdo)
    public const byte RamZoneBase = 0xC0;
    public const int RamSegments = 4;
    public const float RamX = 25.9f, RamY = 9.3f, RamH = 3.4f, RamW = 0.55f, RamGap = 0.95f;

    public static List<KeyDef> Build(bool iso, IDictionary<string, int> overrides, int mouseZones = 0,
                                     IReadOnlyList<(string Name, int Segments)> pcChannels = null, int ramSticks = 0)
    {
        var keys = new List<KeyDef>();
        const float ox = 1.5f, oy = 1.5f;   // oy: gornja traka s okruglim tipkama, kotačićem i logom

        void K(string label, int usage, float x, float y, float w = 1, float h = 1, string group = "main", string shape = null)
        {
            byte zone = ZoneFromUsage(usage);
            keys.Add(new KeyDef
            {
                Id = "U" + usage.ToString("X2"),
                Label = label,
                Usage = usage,
                DefaultZone = zone,
                Zone = zone,
                X = ox + x,
                Y = oy + y,
                W = w,
                H = h,
                Group = group,
                Shape = shape
            });
        }

        // Logo (svijetleće slovo u gornjem lijevom kutu) i G tipke poravnate od reda brojeva do reda s Ctrl
        keys.Add(new KeyDef { Id = "LOGO", Label = "G", DefaultZone = 0xD2, Zone = 0xD2, X = 0, Y = 0.25f, W = 1, H = 1, Group = "logo", Shape = "logo" });
        float[] gy = { 1.25f, 2.25f, 3.25f, 4.25f, 5.25f };
        for (int i = 0; i < 5; i++)
        {
            byte z = (byte)(0xB4 + i);
            keys.Add(new KeyDef { Id = "G" + (i + 1), Label = "G" + (i + 1), DefaultZone = z, Zone = z, X = 0, Y = oy + gy[i], Group = "g" });
        }

        // Red 0: Esc + F tipke
        K("Esc", 0x29, 0, 0, group: "f");
        for (int i = 0; i < 12; i++)
        {
            float x = i < 4 ? 2 + i : i < 8 ? 6.5f + (i - 4) : 11 + (i - 8);
            K("F" + (i + 1), 0x3A + i, x, 0, group: "f");
        }
        K("PrtSc", 0x46, 15.25f, 0, group: "nav");
        K("ScrLk", 0x47, 16.25f, 0, group: "nav");
        K("Pause", 0x48, 17.25f, 0, group: "nav");

        // Red 1: brojevi
        float r1 = 1.25f;
        K(iso ? "¸" : "`", 0x35, 0, r1, group: "num");
        string[] nums = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
        for (int i = 0; i < 10; i++) K(nums[i], 0x1E + i, 1 + i, r1, group: "num");
        K(iso ? "'" : "-", 0x2D, 11, r1, group: "num");
        K(iso ? "+" : "=", 0x2E, 12, r1, group: "num");
        K("Backspace", 0x2A, 13, r1, 2, group: "mods");
        K("Ins", 0x49, 15.25f, r1, group: "nav");
        K("Home", 0x4A, 16.25f, r1, group: "nav");
        K("PgUp", 0x4B, 17.25f, r1, group: "nav");
        K("Num", 0x53, 18.5f, r1, group: "numpad");
        K("/", 0x54, 19.5f, r1, group: "numpad");
        K("*", 0x55, 20.5f, r1, group: "numpad");
        K("-", 0x56, 21.5f, r1, group: "numpad");

        // Red 2: QWERTZ / QWERTY
        float r2 = 2.25f;
        K("Tab", 0x2B, 0, r2, 1.5f, group: "mods");
        string[] row2 = iso
            ? new[] { "Q", "W", "E", "R", "T", "Z", "U", "I", "O", "P" }
            : new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };
        int[] row2u = { 0x14, 0x1A, 0x08, 0x15, 0x17, 0x1C, 0x18, 0x0C, 0x12, 0x13 };
        for (int i = 0; i < 10; i++) K(row2[i], row2u[i], 1.5f + i, r2, group: "letters");
        K(iso ? "Š" : "[", 0x2F, 11.5f, r2, group: iso ? "letters" : "main");
        K(iso ? "Đ" : "]", 0x30, 12.5f, r2, group: iso ? "letters" : "main");
        if (iso) K("Enter", 0x28, 13.5f, r2, 1.5f, 2, group: "mods", shape: "isoEnter");
        else K("\\", 0x31, 13.5f, r2, 1.5f);
        K("Del", 0x4C, 15.25f, r2, group: "nav");
        K("End", 0x4D, 16.25f, r2, group: "nav");
        K("PgDn", 0x4E, 17.25f, r2, group: "nav");
        K("7", 0x5F, 18.5f, r2, group: "numpad");
        K("8", 0x60, 19.5f, r2, group: "numpad");
        K("9", 0x61, 20.5f, r2, group: "numpad");
        K("+", 0x57, 21.5f, r2, 1, 2, group: "numpad");

        // Red 3
        float r3 = 3.25f;
        K("Caps", 0x39, 0, r3, 1.75f, group: "mods");
        string[] row3 = { "A", "S", "D", "F", "G", "H", "J", "K", "L" };
        int[] row3u = { 0x04, 0x16, 0x07, 0x09, 0x0A, 0x0B, 0x0D, 0x0E, 0x0F };
        for (int i = 0; i < 9; i++) K(row3[i], row3u[i], 1.75f + i, r3, group: "letters");
        K(iso ? "Č" : ";", 0x33, 10.75f, r3, group: iso ? "letters" : "main");
        K(iso ? "Ć" : "'", 0x34, 11.75f, r3, group: iso ? "letters" : "main");
        if (iso) K("Ž", 0x32, 12.75f, r3, group: "letters");
        else K("Enter", 0x28, 12.75f, r3, 2.25f, group: "mods");
        K("4", 0x5C, 18.5f, r3, group: "numpad");
        K("5", 0x5D, 19.5f, r3, group: "numpad");
        K("6", 0x5E, 20.5f, r3, group: "numpad");

        // Red 4
        float r4 = 4.25f;
        // G815 (hrvatski raspored) nema tipku lijevo od Y, pa je lijevi Shift širok 2,25
        K("Shift", 0xE1, 0, r4, 2.25f, group: "mods");
        string[] row4 = iso
            ? new[] { "Y", "X", "C", "V", "B", "N", "M" }
            : new[] { "Z", "X", "C", "V", "B", "N", "M" };
        int[] row4u = { 0x1D, 0x1B, 0x06, 0x19, 0x05, 0x11, 0x10 };
        for (int i = 0; i < 7; i++) K(row4[i], row4u[i], 2.25f + i, r4, group: "letters");
        K(",", 0x36, 9.25f, r4);
        K(".", 0x37, 10.25f, r4);
        K(iso ? "-" : "/", 0x38, 11.25f, r4);
        K("Shift", 0xE5, 12.25f, r4, 2.75f, group: "mods");
        K("↑", 0x52, 16.25f, r4, group: "arrows");
        K("1", 0x59, 18.5f, r4, group: "numpad");
        K("2", 0x5A, 19.5f, r4, group: "numpad");
        K("3", 0x5B, 20.5f, r4, group: "numpad");
        K("Enter", 0x58, 21.5f, r4, 1, 2, group: "numpad");

        // Red 5
        float r5 = 5.25f;
        K("Ctrl", 0xE0, 0, r5, 1.25f, group: "mods");
        K("Win", 0xE3, 1.25f, r5, 1.25f, group: "mods");
        K("Alt", 0xE2, 2.5f, r5, 1.25f, group: "mods");
        K("", 0x2C, 3.75f, r5, 6.25f, group: "mods");
        K(iso ? "AltGr" : "Alt", 0xE6, 10, r5, 1.25f, group: "mods");
        K("Win", 0xE7, 11.25f, r5, 1.25f, group: "mods");
        K("Menu", 0x65, 12.5f, r5, 1.25f, group: "mods");
        K("Ctrl", 0xE4, 13.75f, r5, 1.25f, group: "mods");
        K("←", 0x50, 15.25f, r5, group: "arrows");
        K("↓", 0x51, 16.25f, r5, group: "arrows");
        K("→", 0x4F, 17.25f, r5, group: "arrows");
        K("0", 0x62, 18.5f, r5, 2, group: "numpad");
        K(iso ? "," : ".", 0x63, 20.5f, r5, group: "numpad");

        // Okrugle tipke s RGB osvjetljenjem: tipka za igre, svjetlina i medijske tipke.
        // Zone medijskih tipki provjerene su na stvarnoj G815. M1–M3 i MR imaju samo bijele lampice (ne RGB).
        void R(string id, string label, byte zone, float x, float y, string group) => keys.Add(new KeyDef
        {
            Id = id, Label = label, DefaultZone = zone, Zone = zone, X = ox + x, Y = y, W = 1, H = 1, Group = group, Shape = "round"
        });
        // tipka za igre ima samo bijelu lampicu (zona 0x9A tipkovnica odbija), pa nije RGB tipka
        R("BRIGHT", "Svjetlina", 0x99, 7.5f, 0.3f, "top");
        R("MEDIA_PREV", "Prethodna", 0x9E, 18.5f, oy, "media");
        R("MEDIA_PLAY", "Play/Pauza", 0x9B, 19.5f, oy, "media");
        R("MEDIA_NEXT", "Sljedeća", 0x9D, 20.5f, oy, "media");
        R("MEDIA_MUTE", "Utišaj", 0x9C, 21.5f, oy, "media");

        // Zone miša: LED-ice nacrtanog miša desno od tipkovnice (traka u luku preko stražnjeg dijela),
        // pa efekti (val, vatra…) "prelaze" i na miš. Ne šalju se tipkovnici (zone 0xF0+).
        int n = Math.Min(mouseZones, 16);
        for (int i = 0; i < n; i++)
        {
            int z = 0xF0 + i;
            var (cx, cy) = MouseLed(i, n);
            keys.Add(new KeyDef
            {
                Id = "MOUSE" + i, Label = "Miš " + (i + 1), DefaultZone = z, Zone = z,
                X = cx - MouseLedSize / 2, Y = cy - MouseLedSize / 2, W = MouseLedSize, H = MouseLedSize,
                Group = "mouse", Shape = "mouse"
            });
        }

        // Izlazi ASRock kontrolera: svaki izlaz je red, zone su ravnomjerno raspoređene po njegovoj širini
        if (pcChannels != null)
        {
            int zi = 0, row = 0;
            foreach (var (name, segs) in pcChannels)
            {
                if (segs <= 0) continue;
                float y = PcY + row * PcRowH;
                float step = segs == 1 ? PcW : (PcW - 1.2f) / (segs - 1);
                float w = Math.Clamp(step * 0.8f, 0.12f, 0.6f);
                for (int i = 0; i < segs && zi < PcMaxLeds; i++, zi++)
                {
                    int z = PcZoneBase + zi;
                    float cx = segs == 1 ? PcX + PcW / 2 : PcX + 0.6f + i * step;
                    keys.Add(new KeyDef
                    {
                        Id = "PC" + zi, Label = segs == 1 ? name : $"{name} · {i + 1}", DefaultZone = z, Zone = z,
                        X = cx - w / 2, Y = y + 0.1f, W = w, H = 0.5f, Group = "pc", Shape = "pc"
                    });
                }
                row++;
            }
        }

        for (int s = 0; s < Math.Min(ramSticks, 16 / RamSegments); s++)
        {
            for (int i = 0; i < RamSegments; i++)
            {
                int z = RamZoneBase + s * RamSegments + i;
                float segH = RamH / RamSegments;
                keys.Add(new KeyDef
                {
                    Id = $"RAM{s}_{i}", Label = $"RAM {s + 1} · {i + 1}", DefaultZone = z, Zone = z,
                    X = RamX + s * RamGap, Y = RamY + RamH - (i + 1) * segH, W = RamW, H = segH,
                    Group = "ram", Shape = "ram"
                });
            }
        }

        if (overrides != null)
        {
            foreach (var k in keys)
                if (k.Shape != "pc" && overrides.TryGetValue(k.Id, out int z) && z >= 0 && z <= 255)
                    k.Zone = z;
        }
        return keys;
    }
}

/// <summary>Pretvorba Windows scan kodova (set 1) u HID usage kodove, za reaktivne efekte.</summary>
public static class ScanMap
{
    private static readonly Dictionary<int, int> Normal = new()
    {
        [0x01] = 0x29, [0x02] = 0x1E, [0x03] = 0x1F, [0x04] = 0x20, [0x05] = 0x21, [0x06] = 0x22,
        [0x07] = 0x23, [0x08] = 0x24, [0x09] = 0x25, [0x0A] = 0x26, [0x0B] = 0x27, [0x0C] = 0x2D,
        [0x0D] = 0x2E, [0x0E] = 0x2A, [0x0F] = 0x2B, [0x10] = 0x14, [0x11] = 0x1A, [0x12] = 0x08,
        [0x13] = 0x15, [0x14] = 0x17, [0x15] = 0x1C, [0x16] = 0x18, [0x17] = 0x0C, [0x18] = 0x12,
        [0x19] = 0x13, [0x1A] = 0x2F, [0x1B] = 0x30, [0x1C] = 0x28, [0x1D] = 0xE0, [0x1E] = 0x04,
        [0x1F] = 0x16, [0x20] = 0x07, [0x21] = 0x09, [0x22] = 0x0A, [0x23] = 0x0B, [0x24] = 0x0D,
        [0x25] = 0x0E, [0x26] = 0x0F, [0x27] = 0x33, [0x28] = 0x34, [0x29] = 0x35, [0x2A] = 0xE1,
        [0x2C] = 0x1D, [0x2D] = 0x1B, [0x2E] = 0x06, [0x2F] = 0x19, [0x30] = 0x05,
        [0x31] = 0x11, [0x32] = 0x10, [0x33] = 0x36, [0x34] = 0x37, [0x35] = 0x38, [0x36] = 0xE5,
        [0x37] = 0x55, [0x38] = 0xE2, [0x39] = 0x2C, [0x3A] = 0x39, [0x3B] = 0x3A, [0x3C] = 0x3B,
        [0x3D] = 0x3C, [0x3E] = 0x3D, [0x3F] = 0x3E, [0x40] = 0x3F, [0x41] = 0x40, [0x42] = 0x41,
        [0x43] = 0x42, [0x44] = 0x43, [0x46] = 0x47, [0x47] = 0x5F, [0x48] = 0x60, [0x49] = 0x61,
        [0x4A] = 0x56, [0x4B] = 0x5C, [0x4C] = 0x5D, [0x4D] = 0x5E, [0x4E] = 0x57, [0x4F] = 0x59,
        [0x50] = 0x5A, [0x51] = 0x5B, [0x52] = 0x62, [0x53] = 0x63, [0x56] = 0x64, [0x57] = 0x44,
        [0x58] = 0x45,
    };

    private static readonly Dictionary<int, int> Extended = new()
    {
        [0x1C] = 0x58, [0x1D] = 0xE4, [0x35] = 0x54, [0x37] = 0x46, [0x38] = 0xE6, [0x47] = 0x4A,
        [0x48] = 0x52, [0x49] = 0x4B, [0x4B] = 0x50, [0x4D] = 0x4F, [0x4F] = 0x4D, [0x50] = 0x51,
        [0x51] = 0x4E, [0x52] = 0x49, [0x53] = 0x4C, [0x5B] = 0xE3, [0x5C] = 0xE7, [0x5D] = 0x65,
    };

    public static int ToUsage(int scan, bool extended, int vk, bool iso)
    {
        if (vk == 0x13) return 0x48;   // Pause
        if (vk == 0x90) return 0x53;   // NumLock
        if (vk == 0x2C) return 0x46;   // PrintScreen
        if (!extended && scan == 0x2B) return iso ? 0x32 : 0x31; // ISO Ž / ANSI \
        if (extended) return Extended.TryGetValue(scan, out int u) ? u : 0;
        return Normal.TryGetValue(scan, out int n) ? n : 0;
    }
}

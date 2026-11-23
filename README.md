# G815 Studio

Lagana zamjena za Logitech G HUB za tipkovnicu **Logitech G815 LIGHTSYNC** (Windows).
Bez servisa u pozadini i bez telemetrije. Nema vanjskih ovisnosti: tipkovnicom upravlja izravno preko USB HID-a (Logitechov HID++ 2.0 protokol).

## Mogućnosti

- **Boja svake tipke posebno**: klikni ili povuci preko tipki pa ih oboji kistom; brzi odabir (slova, WASD, F tipke, numerička…)
- **Efekti**: statično, disanje, dugin val (4 smjera), izmjena boja, reakcija na pritisak, valovi od pritiska, zvuk (VU metar), boje sa zaslona, isključeno; brzina i svjetlina
- **Makroi na G1–G5** u 3 banke (M1/M2/M3), dakle 15 makroa po profilu
  - koraci: tipka ili kombinacija, spusti/otpusti, kašnjenje, upiši tekst (i č/ć/š/đ/ž), pokreni program
  - načini: jednom, ponavljaj dok je tipka držana, uključi/isključi
  - snimanje iz programa ili izravno s tipkovnice: **MR → utipkaj → G tipka**
- **Profili** s automatskim prebacivanjem prema aplikaciji u fokusu (npr. `cs2.exe`)
- **Tray ikona**: otvaranje prozora, brza promjena profila, gašenje osvjetljenja, izlaz
- Pokretanje s Windowsima (u trayu)
- ISO (hrvatski) i ANSI raspored

## Izrada (build)

1. Instaliraj [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Pokreni `build.bat`.
3. Gotov program je `publish\G815Studio.exe` (jedna samostalna datoteka, ne treba ništa instalirati).

Druga mogućnost: stavi projekt na GitHub. Workflow u `.github/workflows/build.yml` sam izradi `.exe` kao artifact.

## Korištenje

- **Logitech G HUB mora biti potpuno zatvoren** (i iz traya). Najbolje ga je deinstalirati ili isključiti njegovo pokretanje s Windowsima, inače preuzima tipkovnicu.
- Tipkovnica mora biti spojena **USB kabelom**.
- Kad zatvoriš G815 Studio (Izlaz u trayu), tipkovnica se vraća na svoje ugrađene postavke.
- Postavke su u `%APPDATA%\G815Studio\settings.json`, a dnevnik u `log.txt` u istoj mapi.

## Ako neka tipka ne svijetli ispravno

Mapa zona napravljena je prema javno dokumentiranom protokolu (g810-led / OpenRGB).
Ako neka tipka ipak ne reagira, idi u **Postavke → Ispitivač zona**:

1. Uključi ispitivač.
2. Mijenjaj broj zone dok ne zasvijetli željena tipka.
3. Na popisu odaberi tu tipku i klikni **Pridruži**.

## Tehničke bilješke

- Indeksi HID++ značajki otkrivaju se dinamički: 0x8081 per-key osvjetljenje, 0x8100 ugrađeni profili (prebacuje se u host način), 0x8010 G tipke, 0x8020 M tipke, 0x8030 MR.
- Šalju se samo promijenjene boje. Tipke iste boje grupiraju se do 13 u jednom paketu.
- Reaktivni efekti i snimanje koriste globalni hook tipkovnice. On je aktivan samo dok je potreban.
- Makroi šalju scan kodove (SendInput) pa rade i u većini igara. Neke igre s anti-cheatom blokiraju umjetni unos.

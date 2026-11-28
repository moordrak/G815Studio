# Popis promjena

## v1.8.0
- **Miš na slici:** G502 X PLUS se crta desno od tipkovnice, s lukom od 8 LED-ica. LED-ice se biraju i boje kistom kao tipke, klik na tijelo miša odabire sve, a u brzom odabiru je dodan gumb „Miš“. Efekti prelaze na miša prema stvarnom položaju LED-ica.
- **RGB osvjetljenje računala (ASRock Polychrome USB):** program pronalazi RGB kontroler na ASRock pločama (npr. B760M Pro RS), prebacuje ga u izravni način i šalje boje svim LED-icama na ARGB i RGB headerima. Izlazi se crtaju kao trake ispod tipkovnice, pa svi efekti prelaze i na ventilatore i LED trake (s glatkim prijelazom između zona). Uključuje se u *Postavke → Računalo (ASRock RGB)*, a moguće je i jedna boja, zamjena crvene i zelene te upis broja LED-ica po ARGB izlazu. Pri izlazu ploča se vraća na prijašnje osvjetljenje.
- **Pri izlazu se sve gasi:** tipkovnica, miš i ASRock izlazi ostaju ugašeni nakon zatvaranja programa (tipkovnica ne vraća ugrađene efekte). Može se isključiti u *Postavke → Općenito*. Nakon ponovnog pokretanja računala tipkovnica i miš same vraćaju svoje ugrađene efekte dok se program ne pokrene.
- Miš i ASRock trake pojavljuju se na slici i kad se spoje tek nakon otvaranja prozora (prije se slika nije osvježila).
- **Memorija (Kingston Fury), 1. korak:** *Postavke → Memorija* ima gumb „Ispitaj RAM (samo čitanje)“. Čita koje adrese na SMBus-u odgovaraju (SPD i RGB čipovi) i zapisuje ih u log, a u memoriju ništa ne upisuje. Za to treba drajver PawnIO: program nosi službeni PawnIO 2.2.0 i SMBus modul u sebi, instalira ga samo kad zatreba i uklanja pri izlazu (ako ga nisi imao od prije). Program tada mora raditi kao administrator, a gumb „Ponovno pokreni kao administrator“ to radi jednim klikom. Licence i izvori su u `third_party/pawnio`.
- U `log.txt` se zapisuje što je pronađeno na ASRock kontroleru (izlazi, broj LED-ica, sirovi odgovori), za provjeru na stvarnoj ploči.

## v1.7.1
- Tipkovnica: ako ipak odbije neki paket boja (greška 0x02), sve se boje odmah ponovno pošalju, pa nijedna tipka ne ostane neosvježena.
- Miš: dok spava, u log se zapisuje samo prvi neuspjeli pokušaj preuzimanja kontrole, a ne svaki.

## v1.7.0
- **Miš – svaka LED-ica posebno:** ako miš ima značajku 0x8081 (npr. G502 X PLUS s 8 LED-ica), program upravlja svakom LED-icom zasebno. U načinu „Prati efekt tipkovnice“ LED-ice su složene u stupac desno od numeričke tipkovnice, pa se val, vatra, spektar i ostali efekti vide i po dužini miša.

## v1.6.4
- **Tipkovnica:** veliki paketi boja šalju se samo puni (13 tipki); ostatak ide malim paketima popunjenima drugim tipkama. Time nestaju i zadnje greške 0x02.
- **Miš:** u log se zapisuju sve HID++ značajke miša i popis njegovih ugrađenih efekata (za procjenu upravljanja pojedinim LED-icama).

## v1.6.3
- **Miš:** boja se sada stvarno postavlja. Naredba za boju šalje zastavicu „privremeno (RAM)“ (prije 0, pa ju je miš ignorirao), a preuzima se samo upravljanje bojama, ne i napajanjem. Kad miš nakon spavanja odbije boju, kontrola se odmah ponovno preuzima.
- **Tipkovnica:** uklonjene dvije zone zbog kojih je odbijala cijele pakete boja (greška 0x02): tipka za igre (ima samo bijelu lampicu, nije RGB) i virtualna zona miša koja je mogla procuriti u pakete tipkovnice.

## v1.6.2
- Miš: nova opcija *Potpuna kontrola (host način)* za miševe kod kojih ugrađeni profil prepisuje boju; detaljniji zapis odgovora miša u logu.
- Tipkovnica: prazna mjesta u malim paketima boja pune se drugim stvarnim tipkama (G815 odbija i nule i ponavljanje iste tipke); log kod greške navodi zone odbijenog paketa.

## v1.6.1
- Pokušaj ispravka greške 0x02 kod nepunih paketa boja (ponavljanjem zadnje tipke).
- Ista HID++ greška zapisuje se u log najviše jednom u minuti.

## v1.6.0
- **Osvjetljenje Logitech miša** (npr. G502 X PLUS), preko LIGHTSPEED prijamnika ili kabela: miš prati efekt tipkovnice (nalazi se desno od numeričke, pa val, vatra i spektar prelaze i na njega) ili svijetli jednom bojom. Uključuje se u *Postavke → Miš*.
- DPI i tipke miša ostaju kako su namješteni u samom mišu; kod izlaza miš dobiva natrag svoje efekte.

## v1.5.0
- **Boje po efektu:** svaki efekt pamti svoje boje, brzinu i smjer. Klik na pločicu efekta prikazuje upravo njegove boje, s nazivima prema namjeni (npr. *Plamen / Vrhovi plamena*, *Dno / Vrh stupca*, *Zvijezda / Nebo*).
- **Duga ili vlastite boje:** Val duge, Izmjena boja, Spiralna duga, Radijalna duga i Mozaik mogu umjesto duge prelijevati dvije odabrane boje.
- Vatra, Kiša, Skener i Munja sada koriste i drugu boju.
- **Efekti na svim tipkama:** logo, G tipke i okrugle tipke sudjeluju u valovima, vatri, spektru i ostalim efektima.
- **Medijske tipke:** ispravne zone (Prethodna 0x9E, Play/Pauza 0x9B, Sljedeća 0x9D, Utišaj 0x9C).

## v1.4.0
- Dodano RGB osvjetljenje tipke za igre, tipke za svjetlinu i medijskih tipki. Mogu se odabrati i obojiti, a na slici svijetle bojom tipke.
- Brzi odabir *Mediji i gornje tipke*.
- Ispitivač zona: prolaz kroz slobodne zone, ručno ili automatski.

## v1.3.1
- **Slanje boja kao u OpenRGB-u:** paketi idu bez čekanja potvrde, a neiskorišteni bajtovi su nule. Promjene boja su višestruko brže.
- Ispravljeno: *Isključeno* i *Ugasi osvjetljenje* sada gase sve tipke.
- Greška 0x08 (zauzeto) više ne usporava osvjetljenje.

## v1.3.0
- **Zvuk u stvarnom vremenu:** hvatanje zvuka sa zvučnika (WASAPI loopback) i FFT, uz kašnjenje oko 20 ms.
- VU metar: trenutni porast, brzi pad, 12 koraka. Zvučni efekti idu najmanje 60 FPS.
- **11 novih efekata po uzoru na OpenRGB:** Spektar zvuka, Bas puls, Spiralna duga, Radijalna duga, Gradijentni val, Zvjezdano nebo, Kiša/Matrix, Vatra, Skener, Munja, Mozaik.

## v1.2.1
- Manje paketa po okviru kod animacija.
- Dijagnostika slanja u statusnoj traci: FPS, paketi po okviru, vrijeme slanja.

## v1.2.0
- **Makroi – HTTP poziv:** metoda, URL, zaglavlja, tijelo, vremensko ograničenje, obavijest s rezultatom.
- **Makroi – Unos vrijednosti:** makro traži vrijednost u prozoru iznad svih ostalih.
- Varijable u koracima: `{naziv}`, `{naziv:url}`, `{datum}`, `{vrijeme}`, `{clipboard}`, `{status}`, `{odgovor}`.

## v1.1.0
- **Novo sučelje:** tamna tema, bočni izbornik, zaglavlje sa statusom, kartice i pločice efekata, tamni dijalozi i izbornik u trayu.
- **Nova slika tipkovnice:** crne uzdignute tipke sa sjajem, okrugle tipke M1–MR i medijske tipke, indikatori Caps/Num Lock, kotačić za glasnoću.
- Banka se mijenja klikom na M1/M2/M3 na slici.
- Raspored: lijevi Shift bez tipke lijevo od Y, G1–G5 poravnate do reda s Ctrl.

## v1.0.1
- Brže animirano osvjetljenje, preciznije tempiranje okvira, VU metar ovisan o stvarnom vremenu.

## v1.0.0
- Prva verzija: boja svake tipke posebno, efekti, makroi na G1–G5 u tri banke (snimanje i s tipkovnice), profili s automatskim prebacivanjem prema aplikaciji, ikona u trayu, pokretanje s Windowsima.

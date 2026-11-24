# Popis promjena

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

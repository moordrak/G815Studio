# PawnIO (treća strana)

G815 Studio koristi PawnIO za pristup SMBus-u (RGB na Kingston Fury memoriji). Datoteke su ugrađene u exe i
raspakiraju se u `%APPDATA%\G815Studio\PawnIO`. Program PawnIO instalira pri pokretanju (ako već nije instaliran)
i deinstalira pri izlazu.

| Datoteka | Izvor | Licenca | SHA-256 |
|---|---|---|---|
| `PawnIO_setup.exe` 2.2.0 | https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0 (službeni, „smije se dijeliti neizmijenjen“) | GPL-2.0-or-later s iznimkom za programe koji koriste IOCTL sučelje (`COPYING.PawnIO.txt`); izvorni kod: https://github.com/namazso/PawnIO | `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032` |
| `SmbusI801.bin` (PawnIO.Modules 0.2.12) | https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.12 | LGPL-2.1-or-later (`COPYING.modules.txt`); izvorni kod: https://github.com/namazso/PawnIO.Modules | `a0f7d066e7efda28c0e754c1f52dbd8dc280d388ba8575b514a80cb67490530c` |

Datoteke su neizmijenjene.

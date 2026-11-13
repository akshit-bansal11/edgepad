# Nederlands (nl)

Android `values-nl`, Windows culture `nl`. Glossary, register and conventions for the Dutch translation. The wording reads the same in the Netherlands and Flanders.

## Register

Informal "je" in both apps, as Dutch Android and Windows address the user. Short and direct: imperatives are the bare stem ("Tik", "Kies", "Probeer het opnieuw"). Buttons and menu items are infinitives or nouns ("Verbinden", "Vergeten", "Macro's…"). No regionalisms.

## Conventions

- Quotes: curly “…” for names of buttons and items quoted in running text; most cross-references are written bare with a capital ("kies Vertrouwen"), as Dutch UI text does.
- Apostrophes: Android strings escape them (`Macro\'s`, `Pagina\'s`, `logo\'s`, `Accu\'s`); Dutch plural -'s after a word ending in a vowel sound. Resx needs no escape.
- Units and numbers: plain space before units (`%1$d dp`, `%1$d Hz`, `%1$.0f ms`), `%1$d%%` without space. Decimal comma is the locale's job.
- Titles and labels: sentence case, no title case ("Instellingen voor bedieningsvlak").
- Wi-Fi: written "Wi-Fi", as Windows does and as the untranslatable `transport_wifi` label shown beside it. Dutch Android itself writes "wifi"; kept hyphenated for one spelling across both apps and the Windows menu item it quotes. Compounds use hyphens: "Wi-Fi- en USB-verbindingen", "USB-tethering", "Bluetooth-instellingen", "Windows-firewall".
- PIN is "pincode" everywhere; pairing code is "koppelcode"; "koppelen" is pairing, "verbinden" is connecting.
- Nearby devices permission: "Apparaten in de buurt", as Dutch Android labels it.
- Key caps: Ctrl, Alt, Shift, Fn (as printed on Dutch keyboards); Alt+Pijl-omhoog / Alt+Pijl-omlaag for the arrow keys in the Windows macro window. Windows logo key keeps Windows' own name.
- Joiners: `list_separator` ", "; `sentences_joined` "%1$s %2$s"; `battery_spoken` "%1$s %2$s"; `corner_slot` "%1$s · %2$s" (parts are standalone labels: "Linksboven · linkerrand").
- Dial short labels: VOL, HELD, ZOOM, SPOEL, APPS, HZ, SCROLL, ZIJ, DESK, TABS, LAPTOP.
- Plurals: `one` and `other`.
- Physical edges: links, rechts, boven, onder (never mirrored).
- Windows menu access keys: tray menu R M G W F B K U E L D A, all unique; Security window P A R O; macro window T B K P V O.

## Terms

| English | Nederlands |
|---|---|
| surface, control surface | Bedieningsvlak |
| dial | Regelaar (corner dial: Hoekregelaar) |
| edge ruler, ruler | Schaal |
| corner | Hoek |
| split corners | Hoeken splitsen |
| volume | Volume (VOL) |
| brightness | Helderheid (HELD) |
| zoom | Zoom |
| media scrub | Spoelen (SPOEL) |
| app switcher | App-wisselaar (APPS) |
| refresh rate | Vernieuwingsfrequentie (HZ) |
| vertical / horizontal scroll | Verticaal scrollen / Horizontaal scrollen (SCROLL / ZIJ) |
| virtual desktops | Virtuele bureaubladen (DESK) |
| task view | Taakweergave |
| browser tabs | Browsertabbladen (TABS) |
| switch laptop | Laptop wisselen (LAPTOP) |
| trackpad | Touchpad |
| pointer | Aanwijzer |
| gesture | Gebaar |
| natural scrolling | Natuurlijk scrollen |
| media, now-playing bar | Media, Nu aan het spelen |
| Controls | Bediening |
| keyboard | Toetsenbord |
| function keys: With Fn, Direct | Functietoetsen: Met Fn, Direct |
| gamepad | Gamepad |
| gamepad presets | Xbox / Platformer / Racen / Shooter | `gamepad_preset_*`; the guide uses the same words | Xbox keys preset (separate from Xbox): Xbox-toetsen.
| layout, preset | Indeling, voorinstelling |
| binding | Toewijzing |
| D-pad, stick, button, shoulder, trigger | D-pad, stick, knop, schouderknop, trigger |
| keyboard mode (gamepad) | Toetsenbordmodus |
| macro, macro deck | Macro, macro's |
| annotate | Annoteren |
| sign | Ondertekenen |
| capture | Vastleggen (recording: Opnemen) |
| shapes | Vormen |
| lock the trackpad / pad | Touchpad vergrendelen |
| share | Delen / sturen |
| share clipboard | Klembord delen |
| laptop, phone | Laptop, telefoon |
| pairing, pairing code | Koppelen, koppelcode |
| PIN | Pincode |
| trust, Don't trust | Vertrouwen, Niet vertrouwen |
| security level | Niveau (Alleen vertrouwen / Pincode eenmalig per telefoon / Pincode bij elke verbinding) |
| forget | Vergeten |
| remembered | Onthouden |
| Devices | Apparaten |
| active (laptop) | Actief |
| connect, disconnect, reconnect | Verbinden, verbinding verbreken, opnieuw verbinden |
| handshake | Handshake (kept; no Dutch UI word) |
| Nearby devices permission | Apparaten in de buurt |
| USB tethering | USB-tethering |
| haptic feedback | Haptische feedback |
| appearance, Look | Weergave, Uiterlijk |
| theme: System, Dark, Light | Thema: Systeem, Donker, Licht |
| upright, sideways | Staand, liggend |
| control colour | Kleur van bediening |
| the tray app | Edgepad in het meldingsgebied (hoek van de taakbalk) |
| firewall | Windows-firewall |
| start with Windows | Starten met Windows |
| update, release notes | Update/bijwerken, releaseopmerkingen |
| round trip | Heen en terug |

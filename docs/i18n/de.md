# Deutsch (de)

Android `values-de`, Windows culture `de`. Glossary, register and conventions for the German translation.

## Register

Informal "du", in both apps, the same way Android does for settings and hints. Imperatives are the du form without "e" where usual ("Tippe", "Wähle", "Versuch es erneut"). Buttons and menu items are bare verbs or nouns ("Trennen", "Vergessen", "Makros…").

## Conventions

- Quotes: „…“ (German low-high). Used for names of buttons, menu items and settings quoted in running text.
- Percent and units: a no-break-style space is written as a plain space in Android strings: `%1$d %%`, `%1$d dp`, `%1$d Hz`, `%1$d ms`. Decimal comma is the locale's job, not ours.
- Titles and labels: nouns capitalised, otherwise sentence case ("Einstellungen der Steuerfläche", "Per Adresse hinzufügen").
- Wi-Fi is "WLAN", as German Android and Windows write it; Bluetooth and USB stay as they are. Compounds use hyphens: "WLAN- und USB-Verbindungen", "USB-Tethering", "Bluetooth-Einstellungen".
- Key caps (keyboard): Strg, Alt, Umschalt, Fn, Entf, Einfg, Pos1, Ende, Bild↑, Bild↓, Esc, Eingabe, Rücktaste, Tab, Feststelltaste. Windows logo key keeps Windows' own name.
- Joiners: `list_separator` ", "; `sentences_joined` "%1$s %2$s"; `battery_spoken` "%1$s %2$s"; `corner_slot` "%1$s · %2$s" (parts are standalone labels: "Oben links · obere Kante").
- Dial short labels: LAUT, HELL, ZOOM, SPUL, APPS, HZ, SCROLL, SEITE, DESK, TABS, LAPTOP.
- Plurals: `one` and `other`.
- Physical edges: links, rechts, oben, unten (never mirrored).
- No apostrophes: German genitive "'s" is avoided ("das Edgepad-Menü", "Menü des Laptops").

## Terms

| English | Deutsch |
|---|---|
| surface, control surface | Steuerfläche |
| dial | Regler (corner dial: Eckregler) |
| edge ruler, ruler | Skala |
| corner | Ecke |
| split corners | Ecken teilen |
| volume | Lautstärke (LAUT) |
| brightness | Helligkeit (HELL) |
| zoom | Zoom |
| media scrub | Spulen (SPUL) |
| app switcher | App-Wechsler (APPS) |
| refresh rate | Bildwiederholrate (HZ) |
| vertical / horizontal scroll | Vertikal scrollen / Horizontal scrollen (SCROLL / SEITE) |
| virtual desktops | Virtuelle Desktops (DESK) |
| task view | Aufgabenansicht |
| browser tabs | Browser-Tabs (TABS) |
| switch laptop | Laptop wechseln (LAPTOP) |
| trackpad | Touchpad (Windows' own word) |
| pointer | Mauszeiger |
| gesture | Geste |
| natural scrolling | Natürliches Scrollen |
| media, now-playing bar | Medien, Aktuelle Wiedergabe |
| Controls | Steuerung |
| keyboard | Tastatur |
| function keys: With Fn, Direct | Funktionstasten: Mit Fn, Direkt |
| gamepad | Gamepad |
| gamepad presets | Xbox / Jump'n'Run / Rennspiel / Shooter | `gamepad_preset_*`; the guide uses the same words |
| layout, preset | Layout, Vorlage |
| binding | Belegung |
| D-pad, stick, button, shoulder, trigger | Steuerkreuz, Stick, Taste, Schultertaste, Trigger |
| keyboard mode (gamepad) | Tastaturmodus |
| macro, macro deck | Makro, Makros |
| annotate | Zeichnen |
| sign | Unterschreiben |
| capture | Aufnahme (Region: Bereich, Full screen: Ganzer Bildschirm, Record: Aufzeichnen) |
| shapes | Formen |
| lock the trackpad / pad | Touchpad sperren |
| focus (hide dials and media) | Regler und Medien ausblenden |
| share clipboard | Zwischenablage teilen |
| laptop | Laptop |
| phone | Handy |
| pairing, pair | Kopplung, koppeln |
| pairing code | Kopplungscode |
| PIN | PIN |
| trust, Trust / Don't trust | Vertrauen / Nicht vertrauen |
| security level | Sicherheitsstufe |
| forget | Vergessen |
| remembered | Gemerkt |
| Devices | Geräte |
| make active, active | Aktivieren, Aktiv |
| connect, disconnect, reconnect | Verbinden, Trennen, Erneut verbinden |
| stay connected in the background | Im Hintergrund verbunden bleiben |
| handshake | Handshake (kept; technical) |
| link, connection | Verbindung |
| Nearby devices permission | Geräte in der Nähe (Android's German label) |
| USB tethering | USB-Tethering |
| haptic feedback, ticks | Haptisches Feedback, Ticken |
| appearance, Look | Darstellung, Look |
| theme: System, Dark, Light | Design: System, Dunkel, Hell |
| upright, sideways | Hochformat, Querformat |
| control colour | Reglerfarbe |
| the tray app | Edgepad im Infobereich der Taskleiste (tray: Infobereich; Mac: Menüleiste) |
| tray menu | Edgepad-Menü |
| Macros… menu item (Windows) | Makros… |
| WLAN and USB connections (Windows menu) | WLAN- und USB-Verbindungen |
| firewall | Firewall |
| start with Windows | Mit Windows starten |
| update, release notes, release page, major release | Update/Aktualisieren, Versionshinweise, Release-Seite, Hauptrelease |
| protocol | Protokoll |
| round trip | Laufzeit |

## Cross-references to keep verbatim in the Windows file

- `Dialog_Trust` = "Vertrauen", `Dialog_DontTrust` = "Nicht vertrauen" (the Android guide and `status_accept_on_laptop` quote "Vertrauen").
- `Menu_Macros` = "Makros…" (quoted in `macros_empty_body`).
- `Menu_Network` = "WLAN- und USB-Verbindungen" (quoted in several Android strings).
- `Common_Forget` = "Vergessen" (matches Android `forget`).

## Windows and shared core

| English | Deutsch |
|---|---|
| tray / taskbar corner | Infobereich der Taskleiste |
| Security (window) | Sicherheit |
| Level N, name | Stufe N, Name (Nur Vertrauen / PIN einmal pro Handy / PIN bei jeder Verbindung) |
| Forget all | Alle vergessen |
| Reset PIN and forget phones | PIN zurücksetzen und Handys vergessen |
| firewall rule / menu | Windows-Firewall: "In der Windows-Firewall zulassen…" |
| log | Protokoll in Fließtext, "Logdatei öffnen" im Menü |
| program / document / icon | Programm / Dokument / Symbol |
| Pair a phone | Handy koppeln |
| Yes, paired (Android button, quoted in CoreStrings) | Ja, gekoppelt |

Menu access keys (tray menu): Mit &Windows starten, &Makros…, Si&cherheit…, WLAN- und USB-&Verbindungen, In der Windows-&Firewall zulassen…, Bluetooth-&Einstellungen öffnen, &Zwischenablage teilen, Täglich nach Updates &suchen, &Logdatei öffnen, &Update verfügbar, &Dokumentation, Edgepad &beenden. Security window: &PIN festlegen…, Alle &vergessen…, PIN zurück&setzen…, Spei&chern. Macros window: &Makro hinzufügen, &Durchsuchen…, &Symbol wählen…, Symbol des &Programms verwenden, &Entfernen.

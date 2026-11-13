# Polski (pl)

Android `values-pl`, Windows culture `pl`. Glossary, register and conventions for the Polish translation.

## Register

Informal second person, as Polish Android does, with as few pronouns as possible. Instructions in running text are singular imperatives ("Stuknij", "Wybierz", "Sprawdź", "Spróbuj ponownie"). Buttons and menu items follow Android's Polish convention: bare imperatives for actions ("Połącz", "Rozłącz", "Zapomnij", "Sparuj ponownie", "Usuń") and nouns for screens and settings ("Ustawienia", "Wygląd", "Makra…"). Possessives are capitalised as in Polish UI text ("Twój laptop", "Twoja sieć").

Where a laptop, phone or app name would need declension, the string is reworded to a "Label: %1$s" form ("Połączono z: %1$s", "Zapomnieć: %1$s?", "Telefon: {0} ({1}).") or the name is placed in „…” after a noun ("laptopa „%2$s”"). Numbers with a changing noun use `<plurals>` with `one`, `few`, `many`, `other`; forms that the count cannot change are written identically in all four.

## Conventions

- Quotes: „…” for names of buttons, menu items and settings quoted in running text, and around device names after a noun.
- Units stay Latin as the README requires: `dp`, `Hz`, `MB`, `ms`, `s`. A plain space separates number and unit; the percent sign is attached ("50%", Polish usage).
- Titles and labels: sentence case, no title case.
- Plurals: `one` (1), `few` (2-4, 22-24 ...), `many` (0, 5-21 ...), `other` (fractions, genitive singular). `other` is never produced by an integer count here.
- Key caps (keyboard, written in Latin as Polish keyboards print them): Ctrl, Alt, Shift, Fn, Esc, Tab, Enter, Backspace, Delete, Insert, Home, End, Page Up, Page Down, Caps Lock; arrows are named "Strzałka w górę" / "Strzałka w dół" in running text (Alt+Strzałka w górę). Windows logo key keeps Windows' own name.
- Joiners: `list_separator` ", "; `sentences_joined` "%1$s %2$s"; `battery_spoken` "%1$s %2$s"; `corner_slot` "%1$s · %2$s" (parts are standalone labels: "Lewy górny róg · lewa krawędź").
- Dial short labels: GŁOŚ, JASN, ZOOM, UTWÓR, APPS, HZ, PION, POZIOM, PULPIT, KARTY, LAPTOP.
- Physical edges: lewa, prawa, górna, dolna krawędź; lewo, prawo, góra, dół (never mirrored).
- Bluetooth, Wi-Fi, USB stay Latin. Android's permission "Nearby devices" is „Urządzenia w pobliżu”; "USB tethering" is "Tethering USB".
- Gamepad preset names are strings (`gamepad_preset_*`): Xbox (klawisze; sends keys without a controller driver), Platformówka, Wyścigi, Strzelanka; the guide uses the same words.
- Windows access keys are Polish letters, unique per menu or window: tray menu U (Uruchamiaj z systemem Windows), M (Makra), Z (Zabezpieczenia), W (Wi-Fi i USB), P (zaporze), B (Bluetooth), S (schowek), A (aktualizacje codziennie), D (dziennik), T (Dostępna aktualizacja), O (Dokumentacja), C (Zakończ). Security window: P (Ustaw PIN), Z (Zapomnij wszystkie), R (Resetuj), S (Zapisz). Macros window: D (Dodaj makro), P (Przeglądaj), I (ikonę), W (własnej ikony), U (Usuń), S (Zapisz).
- Fonts: Lato covers Polish, so there is no `fonts.xml` in `values-pl`.

## Terms

| English | Polski |
|---|---|
| surface, control surface | powierzchnia sterowania |
| dial | suwak (corner dial: suwak w rogu) |
| edge ruler, ruler | suwak (the same word; "skala" only for the drawn tick scale, never in running text) |
| corner | róg |
| split corners | Podziel rogi |
| volume | Głośność (GŁOŚ) |
| brightness | Jasność (JASN) |
| zoom | Powiększenie (ZOOM) |
| media scrub | Przewijanie utworu (UTWÓR) |
| app switcher | Przełącznik aplikacji (APPS) |
| refresh rate | Częstotliwość odświeżania (HZ) |
| vertical / horizontal scroll | Przewijanie w pionie / w poziomie (PION / POZIOM) |
| virtual desktops | Pulpity wirtualne (PULPIT) |
| task view | Widok zadań |
| browser tabs | Karty przeglądarki (KARTY) |
| switch laptop | Zmiana laptopa (LAPTOP) |
| trackpad | Touchpad (Windows' own word) |
| pointer | wskaźnik |
| gesture | gest |
| natural scrolling | Przewijanie naturalne |
| media, now-playing bar | Multimedia, Teraz odtwarzane |
| Controls | Sterowanie |
| keyboard | Klawiatura |
| function keys: With Fn, Direct | Klawisze funkcyjne: Z Fn, Bezpośrednio |
| gamepad | Gamepad |
| layout, preset | układ, gotowy układ |
| binding | Przypisanie |
| D-pad, stick, button, shoulder, trigger | Krzyżak, Gałka, Przycisk, Bumper, Spust |
| keyboard mode (gamepad) | Tryb klawiatury |
| macro, macro deck | makro, Makra |
| annotate | Adnotacje |
| sign | Podpis |
| capture | Przechwytywanie (Region: Obszar, Full screen: Cały ekran, Record: Nagraj) |
| shapes | Kształty |
| lock the trackpad / pad | Zablokuj touchpad |
| focus (hide dials and media) | Ukryj suwaki i multimedia |
| share clipboard | Udostępnij schowek |
| laptop | laptop |
| phone | telefon |
| pairing, pair | parowanie, sparuj |
| pairing code | kod parowania |
| PIN | PIN |
| trust, Trust / Don't trust | Zaufaj / Nie ufaj |
| security level | poziom (Tylko zaufanie, PIN raz na telefon, PIN przy każdym połączeniu) |
| forget | Zapomnij |
| remembered | Zapamiętany |
| Devices | Urządzenia |
| make active, active | Ustaw jako aktywny, Aktywny |
| connect, disconnect, reconnect | Połącz, Rozłącz, Ponowne łączenie |
| stay connected in the background | Utrzymuj połączenie w tle |
| handshake | uzgodnienie połączenia |
| link, connection | połączenie |
| Nearby devices permission | „Urządzenia w pobliżu” (Android's Polish label) |
| USB tethering | Tethering USB |
| haptic feedback, ticks | Reakcja haptyczna, podziałki |
| appearance, Look | Wygląd (both) |
| theme: System, Dark, Light | Motyw: Systemowy, Ciemny, Jasny |
| upright, sideways | Pionowa, Pozioma |
| the tray app | w obszarze powiadomień (Windows' own word) |
| Wi-Fi and USB connections | Połączenia Wi-Fi i USB |
| firewall | Zapora Windows (rule: reguła zapory) |
| start with Windows | Uruchamiaj z systemem Windows |
| round trip | czas odpowiedzi |

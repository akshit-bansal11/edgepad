# Türkçe (tr)

Android `values-tr`, Windows culture `tr`. Glossary, register and conventions for the Turkish translation.

## Register

Polite imperative with the plural/polite ending (-in/-ın/-un/-ün: "Dokunun", "Seçin", "Yazın"), the way Turkish Android and Windows address the user. Buttons and menu items are short imperatives or nouns as the system writes them ("Bağlan", "Unut", "Kaydet", "Makrolar…"). Questions in dialogs use the passive "-sın mı" form ("Unutulsun mu?"), which needs no pronoun.

## Conventions

- Suffixes after placeholders: no vowel-harmony suffix is ever attached to a placeholder. Strings are worded as "Etiket: %1$s" ("Bağlan: %1$s"), with the name as subject ("%1$s unutulsun mu?"), or followed by a separate noun that carries the suffix ("%1$s bilgisayarına", "%1$s üzerindeki", "%1$s sürümüne"). Apostrophe suffixes appear only after fixed words (Edgepad'in, Bluetooth'u, Android'in, GitHub'a).
- Quotes: “…” for names of buttons, menu items and settings quoted in running text.
- Percent is a prefix: `%%%1$d` shows "%50". Units keep a space: `%1$d dp`, `%1$d Hz`, `%1$.0f ms`; seconds in Windows text are "sn".
- Dotted/dotless i: İ/i and I/ı are kept everywhere (İptal, Işık, Izgara; uppercase short labels never use a Latin I where ı is meant).
- Titles and labels: sentence case.
- Key caps: Ctrl, Alt, Shift, Fn (printed that way on Turkish keyboards); arrows are "Yukarı ok", "Aşağı ok" as in Windows.
- Joiners: `list_separator` ", "; `sentences_joined` "%1$s %2$s"; `battery_spoken` "%1$s %2$s"; `corner_slot` "%1$s · %2$s" (parts are standalone labels: "Sol üst · sol kenar").
- Dial short labels: SES, PARL, ZOOM, SAR, UYG, HZ, KAYD, YATAY, MASA, SEKME, PC.
- Plurals: `one` and `other`, identical text (nouns stay singular after numbers).
- Physical edges: sol, sağ, üst, alt (never mirrored).
- Wi-Fi and Bluetooth are written as Turkish Android and Windows write them: "Wi-Fi", "Bluetooth". "Laptop" is "bilgisayar" everywhere (the Turkish everyday word, and it also covers a Mac); "dizüstü" is avoided for length.

## Terms

| English | Türkçe |
|---|---|
| surface, control surface | Kumanda alanı ("kumanda" alone is never used for the gamepad: that is always "Oyun kumandası") |
| dial / edge ruler | Kadran (ruler is the same word) |
| corner | Köşe |
| split corners | Köşeleri böl |
| volume | Ses (SES) |
| brightness | Parlaklık (PARL) |
| zoom | Yakınlaştırma (ZOOM) |
| media scrub | Medya sarma (SAR) |
| app switcher | Uygulama değiştirici (UYG) |
| refresh rate | Yenileme hızı (HZ) |
| vertical / horizontal scroll | Dikey kaydırma / Yatay kaydırma (KAYD / YATAY) |
| virtual desktops | Sanal masaüstleri (MASA) |
| task view | Görev görünümü |
| browser tabs | Tarayıcı sekmeleri (SEKME) |
| switch laptop | Bilgisayar değiştir (PC) |
| trackpad | Dokunmatik yüzey (Windows' own word) |
| pointer | İşaretçi |
| gesture | Hareket |
| natural scrolling | Doğal kaydırma |
| media, now-playing bar | Medya, Şimdi çalıyor |
| Controls | Kontroller |
| keyboard | Klavye |
| function keys: With Fn, Direct | İşlev tuşları: Fn ile, Doğrudan |
| gamepad | Oyun kumandası |
| gamepad presets | Xbox / Xbox (tuşlar) / Platform / Yarış / Nişancı (`gamepad_preset_*`; the guide uses the same words; Xbox keys is its own preset, never plain Xbox) |
| layout, preset | Düzen, hazır ayar |
| binding | Atama |
| D-pad, stick, button, shoulder, trigger | Yön tuşları, çubuk, düğme, omuz tuşu, tetik |
| keyboard mode (gamepad) | Klavye modu |
| macro, macro deck | Makro, Makrolar |
| annotate | Çizim |
| sign | İmza |
| capture | Ekran yakalama (Region: Bölge, Full screen: Tam ekran, Record: Kaydı başlat) |
| shapes | Şekiller |
| lock the trackpad / pad | Dokunmatik yüzeyi kilitle |
| focus (hide dials and media) | Kadranları ve medyayı gizle |
| share / share clipboard | Paylaş / Panoyu paylaş |
| laptop / phone | Bilgisayar / Telefon |
| pairing, pairing code | Eşleştirme, eşleştirme kodu |
| PIN | PIN |
| trust, don't trust | Güven, Güvenme |
| security level | Güvenlik düzeyi (Düzey 1, 2, 3) |
| forget | Unut |
| remembered | Kayıtlı |
| Devices | Cihazlar |
| active | Etkin |
| handshake | El sıkışma |
| Nearby devices permission | Yakındaki cihazlar izni (Android's label) |
| USB tethering | USB ile internet paylaşımı (Android's label) |
| haptic feedback | Dokunsal geri bildirim |
| appearance, Look | Görünüm |
| theme: System, Dark, Light | Sistem, Koyu, Açık |
| upright, sideways | Dikey, yatay |
| control colour | Kontrol rengi |
| the tray app | Görev çubuğunun bildirim alanı |
| tray menu items | Makrolar…, Güvenlik…, Wi-Fi ve USB bağlantıları, Panoyu paylaş, Güncellemeleri günlük denetle, Günlüğü aç, Edgepad'den çık |
| firewall | Windows Güvenlik Duvarı |
| start with Windows | Windows ile başlat |
| update, release notes, release page, major release | Güncelleme, sürüm notları, sürüm sayfası, ana sürüm |
| protocol | Protokol |
| round trip | Gidiş dönüş |

## Notes for the owner

- Lato has no ğ ş İ, so Turkish uses the system font (`values-tr/fonts.xml`, Segoe UI on Windows).
- Windows access keys per menu are unique: tray menu ş M G W i B P d a v e ç; Security window b u s d; Macros window m g e s k d.

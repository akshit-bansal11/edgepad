# العربية (ar)

Android `values-ar`, Windows culture `ar` (UI font Segoe UI on Windows; Android uses the system font, see `fonts.xml`). Glossary, register and conventions for the Arabic translation.

## Register

Modern Standard Arabic as Arabic Android and Windows write it, readable from the Gulf to the Maghreb. Gendered address is avoided: buttons and labels are verbal nouns or nouns ("إعادة المحاولة", "قطع الاتصال", "إضافة"); where an instruction is needed it is the bare imperative Android itself uses ("انقر", "اختر", "اضغط مع الاستمرار"), which reads as neutral. No second-person pronouns or gendered adjectives are addressed to the reader. Tap is "انقر" on both apps (never "المس"); press and hold is "اضغط مع الاستمرار".

## Conventions

- Punctuation: Arabic comma "،", semicolon "؛", question mark "؟". Full stop and colon stay "." and ":". Ellipsis "…".
- Quotes: « » around names of buttons, menu items and settings quoted in running text.
- Digits: Western (0-9), as the placeholders produce them. Number and unit separated by a plain space ("%1$d dp", "%1$d Hz", "%1$d MB"); percent attached ("%1$d%%"). Units stay Latin (dp, Hz, MB, ms, s).
- Latin stays: Edgepad, USB, Windows, Android, Mac, GitHub, ViGEmBus, Xbox, preset names (منصات / سباق / تصويب; gamepad_preset_*), Wi-Fi (as Arabic Android and Windows write it), PIN (written "رقم PIN"), Ctrl, Alt, Shift, Fn, Esc, Tab, F1 to F12 (Arabic keyboards print Latin key caps). Bluetooth is "بلوتوث".
- Bidi: every string starts with an Arabic word (or a digit), never with Latin text, so the paragraph is right to left. Sentences that began with a placeholder or a product name were restructured ("إصدار Edgepad على %1$s…", "لا يستطيع %1$s…", "التطبيق Edgepad…"). `surface_app_time` is reordered to "%2$s من %3$s · %1$s" (times first, app name last). Only `LastConnected_Row` and `LastConnected_Unknown` (CoreStrings) must start with a phone name, so `{0}` is wrapped in U+2068…U+2069 (FSI…PDI) there. No other control characters.
- Path arrows "›" are kept; they are mirrored by the bidi algorithm, so they point left in the Arabic text.
- Physical sides: يسار / يمين / أعلى / أسفل, never mirrored ("سحب لليسار", "أعلى اليسار", "الحافة اليمنى").
- Windows access keys: the `&` sits on a Latin letter appended in parentheses at the end ("حفظ (&S)", "الماكرو… (&M)"), the same letter as the English item. The quoted menu name in running text omits the suffix: «الماكرو…», «اتصالات Wi-Fi وUSB».
- Joiners: `list_separator` "، " (with a trailing space, in quotes); `sentences_joined` "%1$s %2$s"; `battery_spoken` "%1$s %2$s"; `corner_slot` "%1$s · %2$s" with standalone labels ("أعلى اليسار · الحافة اليسرى").
- Dial short labels: صوت, سطوع, تكبير, تنقل, تطبيق, معدل, أفقي, تمرير, مكتب, تبويب, لابتوب.
- Plurals: exactly `zero`, `one`, `two`, `few` (3 to 10), `many` (11 to 99), `other`. The dual and the singular drop the number where Arabic does ("إصبعان", "ثانية واحدة"); few/many/other take the number and the case-correct noun ("3 أصابع", "11 إصبعًا", "100 إصبع"). `show_all_paired` and `apps_full_note` are identical in most forms because the counted noun does not agree.
- The loanword "ماكرو" does not take a plural or dual: counts are written around it ("الحد الأقصى للماكرو … هو %1$d").
- Titles and labels have no capitalisation; labels are nouns or verbal nouns.

## Terms

| English | العربية |
|---|---|
| surface, control surface | سطح التحكم |
| dial | مقياس |
| edge ruler, ruler | مقياس (same word, as the brief allows) |
| corner | زاوية (corner dials: مقاييس الزوايا) |
| split corners | تقسيم الزوايا |
| volume | مستوى الصوت (صوت) |
| brightness | السطوع (سطوع) |
| zoom | التكبير (تكبير) |
| media scrub | التنقل في الوسائط (تنقل) |
| app switcher | مبدّل التطبيقات (تطبيق) |
| refresh rate | معدل التحديث (معدل) |
| vertical / horizontal scroll | تمرير عمودي / تمرير أفقي (تمرير / أفقي) |
| virtual desktops | أسطح المكتب الافتراضية (مكتب) |
| task view | عرض المهام |
| browser tabs | علامات تبويب المتصفح (تبويب) |
| switch laptop | تبديل اللابتوب (لابتوب) |
| trackpad | لوحة اللمس (Windows' own word) |
| pointer | المؤشر |
| gesture | إيماءة |
| natural scrolling | التمرير الطبيعي |
| media, now-playing bar | الوسائط، قيد التشغيل |
| Controls | عناصر التحكم |
| keyboard | لوحة المفاتيح |
| function keys: With Fn, Direct | مفاتيح الوظائف: مع Fn، مباشر |
| gamepad | ذراع الألعاب |
| layout, preset | تخطيط، قالب |
| binding | الربط |
| D-pad, stick, button, shoulder, trigger | أسهم الاتجاه، عصا، زر، زر الكتف، الزناد |
| keyboard mode (gamepad) | وضع لوحة المفاتيح |
| macro, macro deck | ماكرو (الماكرو) |
| annotate | الرسم |
| sign | التوقيع |
| capture | الالتقاط (منطقة، ملء الشاشة، تسجيل) |
| shapes | الأشكال |
| lock the trackpad / pad | قفل لوحة اللمس |
| focus (hide dials and media) | إخفاء المقاييس والوسائط |
| share | مشاركة |
| share clipboard | مشاركة الحافظة |
| laptop | لابتوب (the everyday loanword, understood in every region and short); "computer" in `show_laptops_only` is كمبيوتر |
| phone | الهاتف |
| pairing | الإقران |
| pairing code | رمز الإقران |
| PIN | رقم PIN |
| trust, trusted phone | الوثوق، هاتف موثوق (buttons: الوثوق / عدم الوثوق) |
| security level | المستوى (الوثوق فقط / رقم PIN مرة لكل هاتف / رقم PIN عند كل اتصال) |
| forget | نسيان |
| remembered | محفوظ |
| Devices | الأجهزة |
| active (laptop) | نشط (تعيين كنشط) |
| connect, disconnect, reconnect | اتصال، قطع الاتصال، إعادة الاتصال |
| handshake | بدء الاتصال (rendered by meaning) |
| link, connection | الاتصال |
| Nearby devices permission | الأجهزة المجاورة (Android's label) |
| USB tethering | التوصيل عبر USB (Android's label) |
| haptic feedback | الاستجابة اللمسية |
| appearance, Look | المظهر (both) |
| theme: System, Dark, Light | النظام، داكن، فاتح |
| upright, sideways | عمودي، أفقي |
| control colour | لون عناصر التحكم |
| the tray app, tray icon | منطقة الإعلام (Windows' notification area); "taskbar corner" is زاوية شريط المهام |
| Wi-Fi and USB connections | اتصالات Wi-Fi وUSB (title: عبر Wi-Fi وUSB) |
| firewall | جدار الحماية |
| start with Windows | التشغيل مع Windows |
| release notes, release page | ملاحظات الإصدار، صفحة الإصدار |
| protocol | البروتوكول |
| round trip | زمن الاستجابة |

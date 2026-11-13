# Русский (ru)

Android `values-ru`, Windows culture `ru`. Glossary, register and conventions for the Russian translation.

## Register

"Вы" (lowercase), the same in both apps, as Russian Android and Windows do. Buttons and menu items are infinitives or nouns as Android's own Russian UI does ("Подключить", "Отключить", "Забыть", "Повторить", "Сопрячь снова", "Макросы…"); instructions in running text are polite imperatives ("Нажмите", "Выберите", "Проверьте"). Where a name or number would need declension, the string is reworded: a laptop or phone name is put in «ёлочки» after a noun ("ноутбук «%1$s»", "телефон «{0}»"), or the string takes a "Label: %1$s" form ("Подключено: %1$s", "Сделать активным: %1$s").

## Conventions

- Quotes: «…» for names of buttons, menu items and settings quoted in running text, and around device names after a noun.
- Units stay Latin as the README requires: `dp`, `Hz`, `MB`, `ms`, `s`. A no-break space (`&#160;` in Android, U+00A0 in resx) separates number and unit, and number and percent sign ("50 %", Russian typography).
- Titles and labels: sentence case, no title case.
- Plurals: `one`, `few`, `many`, `other`. `other` is the fractional form (genitive singular); `many` covers 0, 5-20 and so on. Forms that the count cannot change (for example "Нарисовано: %1$d") are written identically in all four.
- Key caps: Ctrl, Alt, Shift, Fn, F1–F12 (Russian keyboards print the Latin names); arrows are ↑ and ↓ (Alt+↑ and Alt+↓). Windows logo key keeps Windows' own name.
- Joiners: `list_separator` ", "; `sentences_joined` "%1$s %2$s"; `battery_spoken` "%1$s %2$s"; `corner_slot` "%1$s · %2$s" (parts are standalone labels: "Вверху слева · верхний край").
- Dial short labels: ГРОМК, ЯРК, ЗУМ, ПЕРЕМ, ПРИЛ, ГЦ, ПРОКР, ГОРИЗ, СТОЛЫ, ВКЛАД, НОУТ.
- Physical edges: слева, справа, вверху, внизу.
- Bluetooth, Wi-Fi, USB are written in Latin as Russian Android and Windows do. Android's permission "Nearby devices" is «Устройства поблизости»; "USB tethering" is «USB-модем».
- Windows access keys are Cyrillic letters, unique per menu or window: tray menu А (Автозапуск), М, З (Безопасность), П (Подключения), Б (брандмауэр), Н (настройки Bluetooth), У (буфер обмена), О (обновления), Ж (журнал), Т (Доступно обновление), Д (Документация), В (Выйти). Security window: З (Задать PIN-код), Б (Забыть все), Р (Сбросить PIN-код…), С (Сохранить).
- Fonts: the Russian build uses the system font (Lato has no Cyrillic), see `values-ru/fonts.xml`.

## Terms

| English | Русский |
|---|---|
| surface, control surface | пульт (Настройки пульта) |
| dial, edge ruler | шкала (угловые шкалы) |
| corner | угол |
| split corners | Разделить углы |
| volume | Громкость (ГРОМК) |
| brightness | Яркость (ЯРК) |
| zoom | Масштаб (ЗУМ) |
| media scrub | Перемотка (ПЕРЕМ) |
| app switcher | Переключение приложений (ПРИЛ) |
| refresh rate | Частота обновления (ГЦ) |
| vertical / horizontal scroll | Вертикальная / Горизонтальная прокрутка (ПРОКР / ГОРИЗ) |
| virtual desktops | Виртуальные рабочие столы (СТОЛЫ) |
| task view | Представление задач |
| browser tabs | Вкладки браузера (ВКЛАД) |
| switch laptop | Смена ноутбука (НОУТ) |
| trackpad | Тачпад |
| pointer | Указатель |
| gesture | жест |
| natural scrolling | Естественная прокрутка |
| media, now playing | Медиа, Сейчас играет |
| Controls | Управление |
| keyboard | Клавиатура |
| function keys: With Fn, Direct | С Fn, Напрямую |
| gamepad | Геймпад |
| layout, preset | раскладка, готовая раскладка |
| D-pad, stick, shoulder, trigger | крестовина, стик, бампер, триггер |
| binding | Назначение |
| macro | макрос |
| annotate | Пометки |
| sign | Подпись |
| capture | Захват |
| shapes | Фигуры |
| share clipboard | Общий буфер обмена |
| laptop, phone | ноутбук, телефон |
| pairing, pair | сопряжение, сопрячь |
| pairing code | код сопряжения |
| PIN | PIN-код |
| trust / don't trust | Доверять / Не доверять |
| forget | Забыть |
| remembered | Сохранён |
| Devices | Устройства |
| make active | Сделать активным |
| connect, disconnect | Подключить, Отключить |
| haptic feedback | Виброотклик |
| appearance, Look | Внешний вид, Оформление |
| upright, sideways | портретная, альбомная |
| tray, tray menu | область уведомлений, меню Edgepad |
| Wi-Fi and USB connections | Подключения по Wi-Fi и USB |
| firewall | брандмауэр |
| start with Windows | Автозапуск с Windows |
| release page, release notes | Страница выпуска, Примечания к выпуску |
| round trip | Отклик |

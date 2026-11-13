# Edgepad translations

English is the source. Edgepad ships in 14 more languages on both apps: the Android phone app and the Windows tray app. Translators read this file together with the translator brief (register, placeholders, escaping, plural categories); this file does not repeat the brief.

## 1. The rule

**Every added or changed English string ships with all 14 translations in the same change.** Two checks enforce it:

- Android lint, `MissingTranslation`, with warnings as errors.
- The Windows satellite-completeness test in `windows/tests/Edgepad.Tests`: each culture's resx has exactly the neutral keys and the same `{n}` placeholders.

Text that stays English: protocol, logs, error codes, registry and file names, the website, guide screenshots. On Android, strings that must not be translated carry `translatable="false"`.

### Where the files live

| Language | Android folder | Windows culture |
|---|---|---|
| Simplified Chinese | `values-zh-rCN` | `zh-Hans` |
| Spanish | `values-es` | `es` |
| Hindi | `values-hi` | `hi` |
| German | `values-de` | `de` |
| French | `values-fr` | `fr` |
| Portuguese (Brazil) | `values-pt-rBR` | `pt-BR` |
| Japanese | `values-ja` | `ja` |
| Arabic | `values-ar` | `ar` |
| Russian | `values-ru` | `ru` |
| Korean | `values-ko` | `ko` |
| Italian | `values-it` | `it` |
| Dutch | `values-nl` | `nl` |
| Turkish | `values-tr` | `tr` |
| Polish | `values-pl` | `pl` |

- Android: `android/app/src/main/res/values-<q>/` holds the same six files as `values/`: `strings.xml`, `strings_gamepad.xml`, `strings_gamepad_play.xml`, `strings_guide.xml`, `strings_keyboard.xml`, `strings_share.xml`.
- Windows: `windows/src/Edgepad/Ui/Strings.<culture>.resx` beside the neutral `Strings.resx`. The shared core library has its own resx (pairing and connection-status text); the same terms apply.
- Per language: `docs/i18n/<code>.md` is that language's glossary and register.

### Adding or changing a string

1. Write the English entry (`values/strings*.xml` or `Strings.resx`) as a whole sentence. Never glue translated pieces together. Use numbered placeholders (`%1$s`, `{0}`); on Android use `<plurals>` for anything with a count. .NET has no plural rules, so word Windows text as "Label: {0}".
2. Add the same key, in the same order, to all 14 language files.
3. If an existing English string changes meaning, re-translate it in all 14, not just the key.
4. If a new feature name appears, add it to the term list below.

## 2. Fonts

Edgepad draws its own UI in **Lato 1.104** (Western + Polish). It has the glyphs for en, es, de, fr, pt, it, nl, pl. It has no glyphs for **tr** (ğ ş İ), **ru**, **hi**, **ar**, **zh**, **ja**, **ko**, so those seven use the system font on purpose.

- Android: `lato_covers` is `true` in `values/` and `false` in a small `fonts.xml` in each of those seven folders.
- Windows: `Theme.cs` picks the UI font for the script (zh-Hans Microsoft YaHei UI, ja Yu Gothic UI, ko Malgun Gothic, hi Nirmala UI, ar, ru and tr Segoe UI).

Do not "fix" this back to Lato: missing glyphs draw as empty boxes.

Arabic mirrors settings screens right to left. The control surface (rulers, trackpad, gesture directions, scrub direction) stays physical. "Left", "right", "top", "bottom" always mean the physical edge of the phone or screen.

## 3. Product terms

Render each term by meaning, in the word the phone's and Windows' own UI use for the same thing. One term per concept, identical on phone and laptop.

### Never translated

Edgepad, Bluetooth, Wi-Fi, USB, Windows, Android, Mac, macOS, GitHub, ViGEmBus, Xbox, Lato, Lucide, AndroidSVG, Spotify and any other app or player name, file names and extensions (`pin-verified.txt`, `paired-links.txt`, `security.txt`, `*.exe`), units (`dp`, `Hz`, `MB`, `ms`, `s`), `F1` to `F12`. Key names follow the brief's keyboard rule.

### Terms

| English | Meaning and where the user meets it | Note |
|---|---|---|
| surface, control surface | The phone's main screen once connected: trackpad in the middle, dials in the corners, media bar. "Surface settings". | Not Microsoft Surface. Means "the touch area that controls the laptop". |
| dial | One control along a screen edge that changes a value (volume, brightness...). Slide along it; clockwise raises. | Coined. Render as a slider/scale you run a finger along, not a literal rotary knob. Same word as ruler where the language allows. |
| edge ruler, ruler | The same dial seen as its drawing: a ruler-like scale that wraps a corner. Guide and README say "ruler". | Coined. Keep the ruler-with-ticks picture. |
| corner, corners | Each of the four phone corners holds a dial, or two with Split corners. Screen "Corners". | Physical corners. |
| split corners | Setting: each corner gets two dials, one on each edge it joins (eight in all). | |
| volume, brightness, zoom, media scrub, app switcher, refresh rate, vertical scroll, horizontal scroll, virtual desktops, browser tabs, switch laptop | The eleven dial kinds. Each also has a very short label drawn on the ruler: VOL, BRI, ZOOM, SCRUB, APPS, RATE, SCROLL, SIDE, DESK, TABS, LAPTOP. | Short labels must stay short (about 3-6 Latin letters, 2-3 CJK characters). Use the usual abbreviation. |
| media scrub | Dial that seeks inside the playing track. | Scrub = drag to seek. |
| app switcher | Dial and four-finger gesture that cycles through open windows (Alt+Tab). | Windows' own wording. |
| virtual desktops, task view | Windows' multiple desktops and its all-windows overview. | Windows' own terms. |
| refresh rate | The laptop screen's refresh rate in Hz. | Not "refresh the list". |
| trackpad | The middle of the surface; it acts as the laptop's touchpad. Also the "Trackpad" settings screen (gesture map). | Use the system word for touchpad. |
| pointer | The mouse cursor on the laptop. | |
| gesture | A finger movement on the trackpad: tap, swipe, pinch, hold. | |
| natural scrolling | Content follows the finger. | System term. |
| media, now-playing bar | The strip with track title, app, position, previous / play / next. "Media layout" moves and resizes its pieces. | "Now playing" is a label, not a sentence. |
| Controls | The popup grid opened by the button on the surface: Keyboard, Gamepad, Macros, Annotate, Capture. | A screen name, not a command. |
| keyboard | On-screen keyboard that types on the laptop. Modifiers (Ctrl, Alt, Shift, Fn) can be held or tapped to apply to the next key. | Key caps follow local keyboard print. |
| function keys: With Fn, Direct | Keyboard setting: top row acts as media keys (With Fn) or as F1 to F12 (Direct). | |
| gamepad | Game-controller screen: sticks, D-pad, buttons, shoulders, triggers. The laptop sees a virtual controller. | Common word for game controller. |
| layout, preset | An arrangement of gamepad controls. Presets ship with Edgepad: Xbox, Platformer, Racing, Shooter; you save your own. | Xbox stays; the other three are genre words. |
| binding | What a gamepad control sends: a stick, a trigger or a keyboard key. | |
| D-pad, stick, button, shoulder, trigger | Gamepad parts. Shoulder = the top bumper buttons. Trigger = the analog trigger. | Use the gaming industry's term in that language. |
| keyboard mode (gamepad) | Fallback when the laptop has no virtual-controller driver: controls only send keys. | |
| macro, macro deck | A button on the phone that opens a program, file, folder or web address on the laptop. The grid of them is the deck. Screens "Macros", "Macro settings". Added from the laptop's tray menu or from the laptop's app list. | Coined. Render by meaning (a user-defined shortcut button) if "macro" is unfamiliar to ordinary users. One word on both apps. |
| annotate | A Controls tile: the phone becomes a pen pad and ink appears on the laptop screen in the same place. Tools: Pen, Highlighter, Line, Arrow, Rectangle, Ellipse. | Draw on the screen. Not "comment". |
| sign | A mode of Annotate: draw a signature; it is copied to the laptop clipboard and saved to Pictures. | Sign a document, not "sign in". |
| capture | A Controls tile: ask the laptop for a region screenshot, a full-screen screenshot or a recording; "Save to" Laptop or Phone. | Not a camera. |
| shapes | Strokes drawn on the trackpad that each run an action (a macro, hide dials, lock the pad). | |
| lock the trackpad / lock the pad | Button on the surface: the trackpad ignores touch while the dials stay live. | Different from "Lock the laptop". |
| focus (hide dials and media) | Surface mode that shows only the trackpad. | |
| share | Android Share > Edgepad sends text or an image to the laptop. | Distinct from "Share clipboard". |
| share clipboard | Setting: copy on one device, paste on the other (text and images). | Not the Share sheet. |
| laptop | Any computer running Edgepad (a Mac too). Used throughout. | Everyday word for a portable computer; "computer" only where the string says computer. |
| phone | The Android device running Edgepad. | |
| pairing | Linking phone and laptop the first time. Over Bluetooth it is the system's own pairing; over Wi-Fi or USB it uses a pairing code. | System word for Bluetooth pairing. |
| pairing code | Short code the phone shows and the laptop's owner types in Edgepad on the laptop. Expires. | Not the PIN. |
| PIN | Six-digit number set in Edgepad's Security window on the laptop and typed on the phone. | Not the pairing code. |
| trust, trusted phone | The laptop asks "Trust (phone)?" on first connection; only trusted phones may control it. Buttons "Trust" and "Don't trust". | A permission decision. |
| security level | Windows Security window: Level 1 Trust only, 2 PIN once per phone, 3 PIN on every connection. | |
| forget | Remove a remembered laptop (phone) or a trusted phone (laptop). | Not "delete the Bluetooth pairing". |
| remembered | A laptop the phone reconnects to when the app opens. | |
| Devices | Phone screen listing laptops with connection state and round-trip time. "Make active" picks which laptop the controls drive. | Its entries are laptops. |
| active (laptop) | The laptop being controlled when several are connected. | |
| connect, disconnect, reconnect, stay connected | Link states. "Stay connected in the background" keeps the link while the app is minimized. | |
| handshake | The opening exchange when a connection starts; appears in error text. | Render by meaning if a literal word reads oddly. |
| link, connection | The Bluetooth, Wi-Fi or USB connection between phone and laptop. | |
| Nearby devices permission | Android's own permission name. | Use exactly the label Android shows in that language. |
| USB tethering | Android's own setting name. | Use Android's label. |
| haptic feedback, ticks, notches | Vibration as a dial passes a step. | System term for vibration feedback. |
| appearance, Look | Theme, orientation, control colour, background and pattern. Row "Appearance", screen title "Look". | May be the same word. |
| theme: System, Dark, Light | | System terms. |
| upright, sideways | The phone held in portrait or in landscape. | Portrait/landscape if that is the system word. |
| control colour | The colour of the dials and controls. | |
| the tray app | The Windows Edgepad app: no window, lives in the notification area ("taskbar corner", "tray icon"); also a menu bar app on a Mac. | Windows' own word for the notification area. |
| tray menu | Right-click menu of the tray icon: Macros..., Security..., Wi-Fi and USB connections, Share clipboard, Check for updates daily, Open log, Quit Edgepad. | Items carry `&` access keys; keep one per item, unique in its menu. |
| Wi-Fi and USB connections | Windows setting that lets phones find the laptop over the network or USB tethering. | |
| firewall | Windows Firewall rule Edgepad adds. | Windows' own term. |
| start with Windows | Run Edgepad at sign-in. | Windows' own wording. |
| update, release notes, release page, major release | Self-update from GitHub. After a major release both apps must be updated together. | |
| protocol | Version number of the phone-laptop language; shown only in "Edgepad X · protocol N". | |
| round trip | Measured time from phone to laptop and back, shown on Devices. | Plain word for response time. |

61 terms (the eleven dial kinds are one row).

## 4. Ambiguous strings

Key: intended meaning, found in the code.

| Key | Intended meaning |
|---|---|
| `back_to_surface` ("Controls") | Back link returning to the Controls grid. A screen name, not a command. |
| `settings_controls` ("Control colour") | Settings row for the colour of the dials. Not the Controls grid. |
| `appearance_title` ("Look") vs `settings_appearance` ("Appearance") | Both about visuals; screen title vs row. |
| `forget`, Windows `Common_Forget` | Verb button: remove this remembered laptop or trusted phone. |
| `chosen` | Screen-reader state after a selected item. Adjective, not a verb. |
| `keyboard_key_on` ("On") | Screen-reader state of a held key (pressed). Not an on/off switch. |
| `keyboard_type_send` ("Type") | Button that sends the typed text to the laptop. Verb. |
| `editor_options` ("Options") | Accessibility label of the gear popup in the layout editors. |
| `pin_delete` | Deletes the last digit typed, not the PIN. |
| `gamepad_bind` ("Binding") | Menu row title: what the control sends. Noun. |
| `gamepad_label` ("Label") | The control's text label. Noun. |
| `gamepad_add`, `gamepad_restore` | Two buttons side by side in a small popup; very short verbs. |
| `gamepad_kind_shoulder`, `gamepad_kind_trigger` | The bumper and the analog trigger. |
| `gamepad_up/down/left/right` | Lowercase direction words slotted into "Key for %1$s" for the D-pad. Physical directions. |
| `dial_refresh` ("Refresh rate") vs `refresh` ("Refresh the list") | Screen refresh rate in Hz, versus reloading a list. |
| `dial_devices` ("Switch laptop") | Name of the dial that changes the active laptop. |
| `dial_scroll_h_short` ("SIDE") | Abbreviation for horizontal scroll. |
| `action_volume` ("Volume (up raises)") | Gesture target. Swiping up raises the volume; the parenthesis states direction. |
| `action_lock` ("Lock the laptop") vs `surface_lock` ("Lock the trackpad") | Two different actions. |
| `action_search`, `action_notifications` | Windows Search and the Windows notification centre. |
| `gesture_three_tap`, `gesture_four_tap` ("Tap") | Row labels under a "three fingers" / "four fingers" heading. |
| `gesture_*_left` and the other directions ("Swipe left") | Physical direction of the fingers. |
| `piece_transport` ("Prev · play · next") | Name of the three transport buttons as one movable piece. |
| `shapes_draw_back` ("Draw") | Back link to the drawing canvas. A link label, not a command. |
| `shape_target_macro` ("Macro · %1$s") | The macro a shape runs. |
| `annotate_sign`, `annotate_to_sign_title` ("Switch to Sign?") | Sign is a mode name. |
| `annotate_continue` | Confirms switching mode. |
| `annotate_width` | Pen thickness, not page width. |
| `annotate_selected` | Screen-reader state of the chosen tool. |
| `annotate_menu` with `_expanded` / `_collapsed` | Label and state of a collapsible toolbar. |
| `annotate_tool_desc` ("%1$s tool") | Screen-reader label; the placeholder is a tool name (Pen, Line...). Word order may change. |
| `annotate_colour_*` and `color_*` | The same colour names in two places. Keep them identical within a language. `color_*` are picker swatches read aloud. |
| `capture_region` ("Region") | Capture mode: a screenshot of an area. |
| `capture_save_laptop`, `capture_save_phone` | Choices under "Save to". |
| `pair_confirm_*` | A yes/no question to the phone user about what the laptop reported. |
| `pair_expires` ("Expires in %1$s") | Placeholder is a ready duration such as "2:30". |
| `pin_title`, `pin_body` | "Enter the PIN"; body says where the PIN was set (on the laptop). |
| `pin_entered` | Screen reader: "Digits entered: 3 of 6". Digits are never spoken. |
| `device_asking` ("Waiting for you. Tap to answer.") | The laptop is waiting for a trust or pairing answer; tapping opens the answer. |
| `status_accept_on_laptop` | "Choose Trust there" is a button on the laptop. |
| `device_more`, `device_more_named` | Overflow "More" on a device row; the named string is its screen-reader label. |
| `more_ways` | Heading above other ways to connect (address, USB...). |
| `connect_via` ("Connect %2$s over %1$s") | %1 is a connection type (Bluetooth, Wi-Fi, USB); %2 is a laptop name. Numbered out of order on purpose. |
| `device_connected_via` | %1 is a connection type. |
| `link_notice*` | Android notification title and text. |
| `lost_title` ("Laptop out of reach") | Title shown when the link drops. |
| `lost_last_seen` | %1 is a duration such as "12 s". |
| `battery_summary` ("Batteries: %1$s") | Screen-reader summary; the placeholder is a list. |
| `list_separator` (", ") | Separator between list items; use the language's own (Arabic "، ", Chinese "、"). Keep it a separator with any trailing space. |
| `sentences_joined` ("%1$s %2$s") | Joins two sentences with a space; CJK may join without one. |
| `*_value`, `battery_percent`, `battery_more`, `feel_summary` | Number formats. Change only spacing and symbols the language requires. |
| `macro_opening` ("Opening") | Screen-reader state while a macro runs. |
| `apps_row_added`, `apps_row_full` | Status badges on list rows. |
| `macros_per_page` ("Macros on screen") | How many macros one page shows. |
| `macros_layout_grid` ("%1$d × %2$d") | Rows by columns. |
| `share_title` ("Send to %1$s?") | %1 is a laptop name; the shared item is shown under it. |
| `share_image` ("an image") | A noun phrase put into a sentence. |
| `share_clipboard_off` | "Turn on Share clipboard on %1$s": setting name, then a laptop name. |
| `guide_*_keys` / `guide_*_values` | Parallel arrays: item N of keys labels item N of values. Same count, same order. |
| `guide_*_kicker` ("1 · Why Edgepad") | Numbered chapter label; keep the number and the middle dot. |
| `guide_step_description` | Screen reader: "Page 2 of 6". |
| `guide_art_*` | Words inside drawings; keep to the space available. |
| Windows `Menu_MacrosCount` ("{0} of {1}") | Used macros out of the limit. |
| Windows `Macro_Full` ("Macros: {0} of {0}") | The same number twice on purpose: the list is full. |
| Windows `Security_LevelName` ("Level {0}, {1}") | {0} is 1, 2 or 3; {1} is the level name (`Security_Level1` to `3`). |
| Windows `Pair_Heading` ("Pair {0}?") | {0} is the phone's name. |
| Windows `Dialog_TrustText`, `Pair_ClosesIn` | "{0} s" is seconds. |
| Windows `Common_Save` vs `Dialog_Save` | `Common_Save` has an `&` access key; `Dialog_Save` does not. |
| Windows `Dialog_Allow`, `Dialog_TurnOn` | Verb buttons on specific dialogs. |
| Windows `Macro_Opens` ("Opens") | Column header: what the macro opens. |
| Windows `Macro_Arguments` | Command-line arguments of a program. |
| Windows `Level_*` | Names of the level readouts shown briefly on the laptop. |
| Windows `Badge_*` | Status badges next to the phone. |
| Windows `Balloon_*` | Tray notification text. Short. |
| Windows `Macro_Filter*` | File-dialog filters; keep the `(*.*)` and extension lists. |

## 5. Hard to translate as written (for the owner)

No English was changed. Each of these makes translation clumsy or lossy.

| Key | Why |
|---|---|
| `battery_low`, `battery_charging`, `battery_more_low` | Lowercase one-word states attached to a number or name. Gender, case and word order differ by language. |
| `gamepad_up`, `gamepad_down`, `gamepad_left`, `gamepad_right` | Lowercase words inserted into `gamepad_bind_key_for` ("Key for %1$s"). Inflected languages need case or gender agreement. |
| `share_image` ("an image") | A noun phrase with a fixed article, inserted into a sentence. |
| `corner_slot` ("%1$s · %2$s") with `corner_edge_*` ("left edge") | Two translated parts joined by a dot; gender and case of the edge noun are fixed. |
| `status_closed`, `status_closed_named` | Another sentence is appended after "Not connected."; it may need to agree. |
| `sentences_joined` | Two whole sentences concatenated; spacing and punctuation rules differ (CJK, Arabic). |
| `list_separator` | One delimiter for several lists; "and" cannot be added before the last item. |
| `lost_last_seen` ("Last seen %1$s ago · %2$s") | A duration fills a slot after "ago"; some languages put the time first. |
| `pair_expires` ("Expires in %1$s") | The duration unit text inside the placeholder is built in code and is not translatable. |
| `show_all_paired` | Plural forms are identical in English; other languages must still write each category. |
| `device_asking` ("Waiting for you. Tap to answer.") | "Waiting for you" is vague: the user must act on the laptop. |
| `piece_transport` ("Prev · play · next") | Three button names squeezed into one label. |
| `dial_*_short` | Latin abbreviations must fit a few pixels; non-Latin scripts need their own tiny forms. |
| `action_volume`, `action_brightness` ("(up raises)") | A direction note inside a label. |
| `keyboard_function_keys_caption` | One very long sentence with doubled `%%` and nested cases (With Fn / Direct, 65% and 60% layouts). |
| `guide_features_values`, `guide_anatomy_values` | Dense items with a dozen nouns that repeat dial names, which must match the dial strings exactly. |
| `guide_surface_values` ("Three sideways for desktops, up for task view...") | Elliptical English: "Three" means three fingers. |
| `guide_features_keys` ("Real-time", "Round trip", "And more") | Label fragments, not sentences. |
| `controls_blackout` ("Black out when idle") | Phrasal verb with a double sense; means the screen goes dark. |
| `macros_empty_body`, `macros_empty_body_mac` | The path "Edgepad's Macros... menu" must match the translated Windows menu item and the Mac menu. |
| `guide_connect_values` (Trust, Wi-Fi or USB items) | Quote names of buttons on the laptop; they must match the Windows `Dialog_Trust` and menu strings. |
| Windows `Error_ForgetOne` | Long multi-sentence message with two placeholders and a file path; "Forget" inside it must match `Common_Forget`. |
| Windows menu items with `&` | Access keys need a letter that exists in the translation and is unique within the menu. |
| Windows `Security_Level1` to `3` | Short names reused inside "Level {0}, {1}". |
| `annotate_tool_desc` ("%1$s tool") | The tool name is a standalone noun; "X tool" word order varies. |
| `gestures_footnote` | Terse chain of clauses describing one- and two-finger gestures. |
| `battery_spoken` ("%1$s %2$s") | A device name then a percentage with no connector. |

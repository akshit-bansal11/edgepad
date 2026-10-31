# Wire protocol

Version 5. The laptop apps (Windows, Mac and Linux) and the phone app refuse any other version at the handshake.

The transport is a byte stream: RFCOMM, or the encrypted network link described below. A frame is one type byte followed by a payload whose length is fixed by the type, except TEXT, FILE_BEGIN and FILE_CHUNK, whose second header byte gives the length of what follows. All multi-byte integers are little-endian. An unknown type byte, a wrong length, a bad magic or a flag byte other than 0 or 1 is a protocol error and closes the connection. A well-formed frame naming an action or control this side does not know is dropped and counted, never treated as an error, so a newer app can talk to an older one until the version check says otherwise.

`protocol/frames.txt` is the authoritative fixture: every line is a frame's fields and its exact bytes, and both test suites encode and decode every line.

## Frames

| Type | Name | Payload | Direction | Meaning |
| --- | --- | --- | --- | --- |
| `0x01` | HELLO | magic `EDGP`, version u8 | phone to laptop | opens a session |
| `0x02` | HELLO_ACK | version u8 | laptop to phone | accepts it; on a version mismatch the laptop sends its own version, then closes |
| `0x10` | MOVE | dx i16, dy i16 | phone to laptop | move the pointer, in laptop pixels before Windows' acceleration |
| `0x11` | BUTTON | button u8, down u8 | phone to laptop | 0 left, 1 right, 2 middle; down is 1 or 0 |
| `0x12` | SCROLL | dx i16, dy i16 | phone to laptop | wheel units; 120 is one notch; positive dy is wheel forward |
| `0x13` | ZOOM | delta i16 | phone to laptop | Ctrl+wheel units |
| `0x20` | ACTION | id u8 | phone to laptop | run an action from the table below |
| `0x21` | SET | control u8, value u8 | phone to laptop | set a control to 0-100; the laptop drops anything above 100 |
| `0x30` | PING | t i64 | phone to laptop | the phone's clock, in nanoseconds |
| `0x31` | PONG | t i64 | laptop to phone | the same value echoed, for the round-trip readout |
| `0x40` | STATE | control u8, value u8, flags u8 | laptop to phone | a control's current value; flags bit 0 is muted for audio controls and playing for media position |
| `0x41` | TEXT | kind u8, length u8, UTF-8 bytes | both ways | up to 255 bytes, never split inside a character |
| `0x22` | KEY | code u16, down u8 | phone to laptop | press or release one key, by Windows virtual-key code |
| `0x23` | PAD_STATE | buttons u16, lt u8, rt u8, lx i16, ly i16, rx i16, ry i16 | phone to laptop | the whole gamepad at once; see below |
| `0x24` | PEN | x u16, y u16, state u8 | phone to laptop | one point of a pen stroke on the annotate or sign surface; state 0 is up, 1 down, 2 move; see [Annotate and sign](#annotate-and-sign) |
| `0x03` | PIN_REQUEST | attemptsLeft u8, lockedSeconds u16 | laptop to phone | asks for the PIN, or says the phone is locked out; only between the trust question and HELLO_ACK; see [The PIN](#the-pin) |
| `0x04` | PIN_ANSWER | six bytes, each 0 to 9 | phone to laptop | the PIN, one digit value per byte (not ASCII); only as the answer to PIN_REQUEST |
| `0x50` | FILE_BEGIN | fileType u8, length u8, size u32, UTF-8 name | laptop to phone | starts a capture file the phone asked for; see [Saving a capture on the phone](#saving-a-capture-on-the-phone) |
| `0x51` | FILE_CHUNK | seq u8, length u8, up to 255 bytes | laptop to phone | one piece of that file; seq is the piece's index mod 256 |
| `0x52` | FILE_END | sha256, 32 bytes | laptop to phone | the SHA-256 of the whole file |
| `0x53` | FILE_RESULT | status u8 | phone to laptop | the phone's answer to the file: 0 saved, 1 bad hash, 2 rejected, 3 write failed |
| `0x54` | BLOB_BEGIN | kind u8, length u8, size u32, UTF-8 label | both ways | starts a blob: clipboard contents, an app list or an app icon; FILE_BEGIN's shape on a lane of its own; see [Blobs](#blobs) |
| `0x55` | BLOB_CHUNK | seq u8, length u8, up to 255 bytes | both ways | one piece of that blob; seq is the piece's index mod 256 |
| `0x56` | BLOB_END | sha256, 32 bytes | both ways | the SHA-256 of the whole blob |

TEXT kinds: 0 what is playing (laptop to phone), 1 the app playing it (laptop to phone), 2 the timeline as `seconds/length` such as `84/227` (laptop to phone), 3 text to type (phone to laptop), where `\b` is backspace and `\n` is enter, 4 the display's available refresh rates as `60/120/144` (laptop to phone), 5 the laptop's macro names as `Chrome/Spotify/Notes` (laptop to phone), 6 one piece of one macro's icon as `slot/chunk/chunks/base64` (laptop to phone), 7 a request for the macro icons, with an empty payload (phone to laptop), 8 whether a virtual controller can be offered, as one PAD_STATUS token (laptop to phone), 9 the optional features the laptop has, as tokens joined by `/` such as `annotate/capture`, where a feature not named is off and a token the phone does not know is ignored (laptop to phone), 10 the primary display in pixels as `1920/1080` (laptop to phone), 11 how a sign-save ended, as one SIGN_STATUS token: `saved`, `empty` or `failed` (laptop to phone), 12 why a capture the phone asked for stayed on the laptop, as one CAPTURE_STATUS token: `too-large`, `not-found` or `failed` (laptop to phone), 13 which laptop this is, as `id/os/name` with the id 32 hex digits and the os one of `windows`, `linux` or `macos` (laptop to phone, once after HELLO_ACK), 14 an app to add as a macro, as `epoch.index` into the list the laptop last sent (phone to laptop), 15 how that add ended, as one APP_ADD_STATUS token: `added`, `full`, `duplicate`, `stale` or `failed` (laptop to phone), 16 which macro apps are running, one character per macro slot in TEXT 5 order: `-` not trackable (a URL, folder, document or an app the laptop cannot see), `0` not running, `1` running, `2` running and in front (laptop to phone, only on change and only while watched with MACROS_WATCH). A slot's state character is data, and a character this build does not know reads as untracked. TEXT 9 names `running` when the laptop can report it. 17 a stroke colour as exactly six hex digits, `RRGGBB`, either case (phone to laptop), for annotate and sign: see [Annotate and sign](#annotate-and-sign).

An icon does not fit the 255 bytes a payload holds, so TEXT 6 carries a PNG in pieces and the phone joins them: the counts are what let it tell a finished icon from a truncated one. Kinds rather than a frame type of their own, which is what keeps the protocol version where it is — an unknown kind is dropped and counted on both sides, while an unknown type closes the connection. The payload of TEXT 7 is a decimal side in pixels, and empty means 128. Icons are answered rather than pushed, so a laptop too old to know TEXT 7 drops it and the phone keeps its labels, and a phone too old to send it costs the link nothing.

The handshake has deadlines on both sides, and none of them is a change to the protocol. From version 5 a laptop whose security level asks for a PIN adds one step after the trust question, described in [The PIN](#the-pin). The laptop closes a connection that sends no HELLO within 10 seconds. When the phone that sent HELLO is not one it trusts, the laptop holds HELLO_ACK and asks its owner whether to trust it; no answer within 60 seconds is a no, and a no is the socket closing without a HELLO_ACK, exactly what a phone the laptop does not trust has always been sent. The phone gives up on HELLO_ACK after 90 seconds, longer than the laptop's question, so it hears the answer rather than abandoning it.

After HELLO_ACK the laptop sends a STATE for volume, microphone and brightness, then TEXT 0, 1 and 2 and a STATE for media position, then TEXT 4 and a STATE for the refresh rate, then TEXT 5 and TEXT 9, and thereafter every change as it happens. Media position is refreshed once a second while playing. Icons are not part of that sequence: the laptop sends TEXT 6 only in answer to TEXT 7.

## The PIN

Version 5 lets the laptop ask for a PIN before it admits a phone. The order is: HELLO, the version check, the lock-out check, the trust question, the PIN gate, then HELLO_ACK. Trust comes first and the PIN second, and the gate sits before the laptop lets a new phone take over from the connected one, so a phone that has not proved itself can never displace the one in use.

The laptop's security level decides whether the gate runs. Level 1 (the default) is trust only: it sends neither PIN frame and behaves as 3.5.0 did. Level 2 asks once per phone: the gate runs for an address not yet verified, and a correct PIN adds it to the verified list. Level 3 asks on every connection. The level and the PIN live on the laptop; the phone never stores a PIN.

At the gate the laptop sends PIN_REQUEST with the attempts left and a lock time of 0, then waits 60 seconds for PIN_ANSWER. No answer, or any other frame, closes the connection. A digit byte above 9 is a protocol error that closes the connection; it is not counted as a failed attempt, since it tests no PIN. A wrong PIN counts as a failure; on every fifth failure the laptop sends PIN_REQUEST with 0 attempts left and the lock time in seconds, then closes, and a phone from a locked address gets the same answer straight after HELLO. The lock lasts 30 seconds for the first five failures and doubles for each further five, up to an hour. A correct PIN zeroes the count.

Changing the PIN or the level clears the verified list, so every phone at level 2 enters the new PIN once. After HELLO_ACK, a PIN_REQUEST or PIN_ANSWER is dropped and counted.

The digits are never logged, written to a status line or shown by a debug print on either side, and they travel in a frame type of their own rather than in TEXT so they can never be routed to typing. RFCOMM already requires the phones to be paired and the Bluetooth stack encrypts the paired link, so the PIN is a second factor and not a second cipher. On the network link the PIN travels inside the encrypted channel.

## Saving a capture on the phone

The phone's Capture screen has a **Save to: Laptop | Phone** setting. The phone sends it as SET CAPTURE_TARGET when the link opens and whenever it changes; the laptop forgets it when the session ends, so a phone that never says anything gets today's behaviour. With Laptop (0), the OS tool saves where it saves and nothing crosses the link.

With Phone (1), after CAPTURE_REGION, CAPTURE_SCREEN or CAPTURE_RECORD the laptop lists the folders its OS tool saves into, presses the shortcut, then watches those folders for a **new** file with an extension in the FILE_TYPE table. It treats a screenshot as done once its size and modification time have held still for a second, and a recording once they have held still for ten; on GNOME and KDE, whose recording shortcut is a toggle, a recording is sent only after the phone has pressed Record again to stop it. In a folder other things save into too (a Mac's Desktop, the Linux Pictures and Videos folders) only a file named like a capture, `Screenshot`, `Screen Shot`, `Screen Recording` or `Screencast` at the start, is taken, so a capture tool naming its files in another language is not followed there. Anything ambiguous fails safe, sending nothing and deleting nothing: no new file within the window, two new files at once, a file that disappears, or a name starting with `.` or `~`. The window is 2 minutes for a screenshot and an hour for a recording. CAPTURE_CANCEL, when the overlay may still be open, cuts the window to 3 seconds, so a file that was already written still goes and a cancelled drag does not leave a watch running.

| Laptop | Screenshots | Recordings |
| --- | --- | --- |
| Windows | `%USERPROFILE%\Pictures\Screenshots`, the Snipping Tool's autosave folder. If no file appears but the clipboard changes to an image, that image is sent as a PNG and nothing is deleted | `%USERPROFILE%\Videos\Screen Recordings` |
| Mac | `defaults read com.apple.screencapture location`, else `~/Desktop` | the same folder |
| Linux | `Screenshots` in the Pictures folder (GNOME) and the Pictures folder itself (KDE Spectacle), the Pictures folder being `$XDG_PICTURES_DIR`, else `~/.config/user-dirs.dirs`, else `~/Pictures` | `Screencasts` in the Videos folder (GNOME) and the Videos folder itself (KDE), found the same way |

The laptop then sends FILE_BEGIN (the type, the size and the file's own name), the file in FILE_CHUNKs of up to 255 bytes, and FILE_END with the SHA-256. FILE_BEGIN and FILE_CHUNK use TEXT's header shape, an id byte then a length byte, so every reader frames them without knowing them, and no frame grows past the 258 bytes a full TEXT already takes. A file over its kind's FILE_LIMIT (50 MB for an image, 200 MB for a video) is not sent: the laptop keeps it and sends TEXT 12 `too-large`. No file found is TEXT 12 `not-found`, and a file found but unreadable is `failed`.

Chunks never starve input. Each chunk is its own write under the send lock, so a PONG or a STATE goes out between any two of them, and they are paced: 16 chunks (about 4 KB) every 20 ms, about 200 KB/s, and 2 per 20 ms while the phone has sent a pointer, key, pad or pen frame in the last quarter second. The input itself travels the other way, phone to laptop, and needs no share of this direction at all; the pacing is for the radio the two directions share.

The phone trusts nothing in a transfer, since it writes a file because the laptop said so. It takes no file unasked: a FILE_BEGIN is accepted only while Save to is Phone and against a permit, and one Region, Full screen or Record tap made on the phone with Save to at Phone grants exactly one, for 5 minutes after a screenshot and 65 minutes after a recording, which is sent only once it stops. A TEXT 12 settles a permit, a connection takes at most 20 files and 1 GB, and switching to Laptop or leaving the Capture screen throws away a file on its way in. A FILE_BEGIN without a permit is answered 2 REJECTED, and the chunks of a file never begun are dropped. It refuses a FILE_BEGIN whose type is not in the table, whose size is 0 or over the limit, or whose name has a `/`, a `\`, a NUL, starts with `.` or is only dots; it replaces any other control or reserved character in the name with `_`, cuts it to 100 characters and makes the extension the type's own. It refuses a chunk out of sequence or past the size. It writes through MediaStore into `Pictures/Edgepad` for an image and `Movies/Edgepad` for a video, which needs no storage permission from Android 10 on, as a pending item no other app sees, checks the SHA-256 at FILE_END, publishes the item and answers FILE_RESULT 0 SAVED. On anything else it deletes the pending item and answers 1 BAD_HASH, 2 REJECTED or 3 WRITE_FAILED. It answers once per file; after a refusal it ignores the rest of that file.

The laptop deletes its copy only after a FILE_RESULT of 0 that arrives after its FILE_END, and only when the file on disk still has the length, the modification time and the SHA-256 that were sent; a file that changed meanwhile stays. An answer before FILE_END stops the sending and keeps the file whatever it says. Any other status, an id it does not know, no answer within 30 seconds, or the link dropping, keeps the file. A FILE_RESULT that arrives early stops the sending. One file goes at a time; a capture found while another is on its way waits for it.

## Actions and controls

`protocol/actions.txt` is the authoritative table; both enums are tested against it.

| Action | Id | Windows laptop does |
| --- | --- | --- |
| MUTE_TOGGLE | 1 | mute key |
| PLAY_PAUSE | 2 | media play/pause key |
| NEXT_TRACK | 3 | media next key |
| PREVIOUS_TRACK | 4 | media previous key |
| MIC_MUTE_TOGGLE | 5 | toggles the default microphone's mute |
| LOCK | 6 | `LockWorkStation` (Win+L cannot be injected) |
| TASK_VIEW | 20 | Win+Tab |
| SHOW_DESKTOP | 21 | Win+D |
| SEARCH | 22 | Win+S |
| NOTIFICATIONS | 23 | Win+N |
| DESKTOP_LEFT | 24 | Win+Ctrl+Left |
| DESKTOP_RIGHT | 25 | Win+Ctrl+Right |
| APP_SWITCH_BEGIN | 26 | holds Alt and presses Tab; Alt stays down until APP_SWITCH_END or the session ends |
| APP_SWITCH_NEXT | 27 | Tab, only while Alt is held |
| APP_SWITCH_PREVIOUS | 28 | Shift+Tab, only while Alt is held |
| APP_SWITCH_END | 29 | releases Alt |
| ZOOM_RESET | 30 | Ctrl+0 |
| VOLUME_UP | 31 | volume-up key |
| VOLUME_DOWN | 32 | volume-down key |
| BRIGHTNESS_UP | 33 | the panel's brightness, plus 10 |
| BRIGHTNESS_DOWN | 34 | the panel's brightness, minus 10 |
| PAD_ATTACH | 35 | plugs in the virtual controller, then answers with TEXT 8 |
| PAD_DETACH | 36 | unplugs it, then answers with TEXT 8 |
| ANNOTATE_BEGIN | 40 | shows the annotation overlay on the primary display |
| ANNOTATE_END | 41 | removes the overlay and discards its strokes |
| ANNOTATE_UNDO | 42 | removes the last stroke |
| ANNOTATE_CLEAR | 43 | removes every stroke |
| TOOL_PEN, TOOL_HIGHLIGHTER, TOOL_LINE, TOOL_ARROW, TOOL_RECTANGLE, TOOL_ELLIPSE | 44 to 49 | picks what the next PEN stroke draws |
| SIGN_BEGIN | 50 | opens a signing surface that shows nothing on the laptop and accepts only the pen tool |
| SIGN_SAVE | 51 | exports the strokes as a signature image, then answers with TEXT 11 |
| CAPTURE_REGION | 52 | Win+Shift+S, the Snipping Tool's region capture |
| CAPTURE_SCREEN | 53 | PrtScn, the whole screen |
| CAPTURE_RECORD | 54 | Win+Shift+R, screen recording; needs a recent Snipping Tool and is **unverified** |
| CAPTURE_CANCEL | 55 | Escape, only while a capture overlay opened by REGION or RECORD may still be open (8 seconds); otherwise nothing. Sent when the phone leaves its capture screen |
| APPS_LIST | 56 | sends the apps it could add as a macro, as BLOB kind 2 and then a kind 3 icon each; only for a laptop that named `apps` in TEXT 9 |
| APPS_CANCEL | 57 | stops that list and its icons |
| MACROS_WATCH | 58 | the macro grid is open: starts a once-a-second survey of which macro apps are running, reported as TEXT 16 on change only; forgotten when the session ends |
| MACROS_UNWATCH | 59 | stops that survey |

| Control | Id | SET does | STATE reports |
| --- | --- | --- | --- |
| VOLUME | 0 | the default speakers' level | level, flag muted |
| BRIGHTNESS | 1 | the built-in panel's brightness through WMI | level |
| MIC_LEVEL | 2 | the default microphone's level | level, flag muted |
| MEDIA_POSITION | 3 | seeks the current track to that percent, where the player allows it | percent, flag playing |
| REFRESH_RATE | 4 | switches the display to the rate at that index of TEXT 4's list | the current rate's index |
| PEN_COLOR | 5 | picks the stroke colour, by index into the palette below; 8 or more is dropped and counted | not reported |
| CAPTURE_TARGET | 6 | where a capture ends up: 0 the laptop keeps it, 1 it is sent to the phone; anything else is dropped and counted | not reported |
| CLIPBOARD | 7 | whether this link shares the clipboard: 1 yes, 0 no; forgotten when the session ends, and honoured only while the laptop's own clipboard toggle is on (it names `clipboard` in TEXT 9 then); anything else is dropped and counted | not reported |
| PEN_WIDTH | 8 | the width of new pen-tool strokes, 1 to 100, as 0.02% of the screen's height per step (5 is 0.1%, 20 is 0.4%, 100 is 2.0%); 0 or more than 100 is dropped and counted; forgotten when the session ends | not reported |
| PEN_OPACITY | 9 | the opacity of new strokes in percent, 10 to 100; anything else is dropped and counted; forgotten when the session ends | not reported |

REFRESH_RATE's value is an index into TEXT 4's list, never a rate in hertz: SET carries value u8 and the laptop drops anything above 100, so 120 or 144 could not cross the wire at all. An index also makes a rate the laptop does not have unrepresentable rather than merely rejected. A laptop that names no rates has none to offer, and the dial has nothing to show.

Actions 64 to 95 are a reserved block of 32 macro slots: the phone runs slot n as `MACRO_BASE + n`. It
sends the index and never what the index opens. The laptop's own list, edited from its tray menu, decides
that, so a slot cannot be repointed from the phone and the protocol stays semantic.

Actions 96 to 127 are a second reserved block of 32, MACRO_CLOSE_BASE: the phone sends `MACRO_CLOSE_BASE + n` to close the app that macro slot n opens, as that app's own close button would and never a kill. As with the run block, the phone names a slot and nothing else.

This is not a containment boundary against a compromised phone, and should not be read as one. KEY carries a
raw virtual-key code and TEXT kind 3 carries arbitrary characters, both of which the laptop injects directly
— that is what the phone's keyboard screen is. A paired phone is a trusted input device, and the trust
boundary is the Bluetooth pairing plus trust-on-first-use, not the macro table.

## Blobs

BLOB_BEGIN, BLOB_CHUNK and BLOB_END (`0x54` to `0x56`) are FILE_BEGIN, FILE_CHUNK and FILE_END byte for byte under their own type bytes, and go **both ways**. They are a lane of their own so a clipboard copy never waits behind a 200 MB capture, and because the file frames' logic (permits, FILE_RESULT) is specific to captures. BEGIN is `kind u8, length u8, size u32, UTF-8 label`, the length counting the size and the label; CHUNK is `seq u8, length u8, up to 255 bytes`; END is the 32-byte SHA-256 of the whole blob. Every decoder bounds them exactly as it bounds the file frames: a length that does not match its header, a BEGIN too short to hold its size or an END that is not 32 bytes closes the link.

The kind is data, not a codec error:

| Kind | Name | Carries |
| --- | --- | --- |
| 0 | CLIP_TEXT | the clipboard's text, UTF-8 |
| 1 | CLIP_IMAGE | the clipboard's image, PNG |
| 2 | APP_LIST | the apps a laptop could add as macros, in response to APPS_LIST |
| 3 | APP_ICON | one app's icon, PNG, the label naming the app |

A receiver reads a kind it does not know to its BLOB_END and drops it, counting it. Which side sends which kind, the size caps and the pacing belong to the feature that uses each kind; this section fixes only the frames. The codecs carry them and nothing in the apps sends or acts on them yet.

## Annotate and sign

The laptop owns the drawing and the phone only steers it. ANNOTATE_BEGIN shows a transparent overlay on the laptop's primary display; SIGN_BEGIN shows nothing and accepts only the pen tool. PEN outside either mode, and a tool action in the wrong mode, is dropped and counted. A laptop says it can annotate by naming `annotate` in TEXT 9, and sends TEXT 10 (`1920/1080`, the primary display in pixels) after the handshake and again when the display changes, so the phone can shape its surface to the same ratio. A phone that never hears either shows nothing to draw on.

PEN carries a position as a fraction of the screen: x and y use the whole u16 span, 0 the left or top edge and 65535 the right or bottom edge, so no pixel count has to cross the link. A state byte other than 0, 1 or 2 decodes like any other value and is dropped and counted, because a newer phone may add states; it is not a protocol error.

**Width.** PEN_WIDTH (CONTROL 8) sets the width of strokes that start after it, as a value of 1 to 100 that the laptop maps linearly to a share of the screen's height: 0.02% per step, so 5 is 0.1% and 100 is 2.0%. 20 is 0.4%, which is what the pen was before the phone could choose, and it is the default: a laptop that is never told uses 20, and the phone sends 20 until its owner moves the slider. 1 to 4 are accepted and all mean 0.1%, so a phone's slider may start at 5. 0 or more than 100 is dropped and counted. The highlighter is always 5 times the width the pen has, so 20 gives its old 2%, and 100 gives 10%. Line, arrow, rectangle and ellipse use the pen's width.

**Opacity.** PEN_OPACITY (CONTROL 9) is a percent of 10 to 100, 100 by default; below 10 a stroke can no longer be seen, so 0 to 9 and more than 100 are dropped and counted. The pen tools draw at that opacity. The highlighter multiplies its own 35% by it, so 100 gives 35% as before and 50 gives 17.5%. A stroke is still painted once per path, so a loop does not darken where it crosses itself at any opacity.

**Colour.** PEN_COLOR (CONTROL 5) picks one of the eight colours below by index. PEN_RGB (TEXT 17) is a colour of the phone's own choosing: exactly six ASCII hex digits, `RRGGBB`, either case, and nothing else. A `#`, a short or long string, a sign, a space or any other character is dropped and counted. The latest PEN_COLOR or PEN_RGB wins, until the session ends. The palette is a shortcut to eight good colours, not a limit. A strictly parsed RGB is safe where a string from the phone usually is not because it is only a colour: the laptop turns it into a number between 0 and 16777215 and nothing else, it names no path, command or file, and a number in that range can only be drawn with.

Width, opacity and colour apply to new strokes only, and each stroke keeps the ones it started with, so a later change never repaints ink already on the screen. They are forgotten when the session ends, so a phone sends all three when its surface opens, as well as on each change. The signature export draws each stroke at the width, opacity and colour it was drawn with. Line, arrow, rectangle and ellipse run from the down point to the latest move. The phone's preview is approximate; what the laptop draws is what counts. END, a session ending or the app quitting removes the overlay and discards the strokes.

SIGN_SAVE renders the strokes at screen resolution, transparent and cropped to the ink with an 8 pixel margin, puts the PNG on the clipboard and saves it under `Pictures\Edgepad\`, then answers with TEXT 11: `saved`, `empty` (nothing was drawn) or `failed`.

PEN_COLOR's value indexes this palette:

| Index | Name | RGB |
| --- | --- | --- |
| 0 | BLACK | `000000` |
| 1 | WHITE | `FFFFFF` |
| 2 | RED | `FF3B30` |
| 3 | ORANGE | `FF9500` |
| 4 | YELLOW | `FFCC00` |
| 5 | GREEN | `34C759` |
| 6 | BLUE | `0068D6` |
| 7 | PURPLE | `AF52DE` |

## The gamepad

PAD_STATE carries the entire controller — every button, both triggers, both sticks — in one 12-byte payload.
The phone sends it whenever any of them changes and the laptop holds the last one until the next arrives.
A snapshot rather than an edge per control, because a gamepad is read as a state: a dropped button release
would otherwise leave a button held down until the user pressed it again, and a link that coalesces frames
can throw away any PAD_STATE but the newest without losing anything.

The payload is deliberately byte-for-byte the shape of Windows' `XINPUT_GAMEPAD` struct — `wButtons`,
`bLeftTrigger`, `bRightTrigger`, `sThumbLX`, `sThumbLY`, `sThumbRX`, `sThumbRY`, in that order and at those
widths — so the laptop copies the payload into the struct rather than translating it. A range or a button
order of our own would have been a conversion on the hot path and a second definition to hold against
Microsoft's. Triggers are 0..255. The four stick axes are -32768..32767, positive up and right, exactly as
XInput reports them.

`buttons` is XInput's `wButtons` mask, unchanged:

| Mask | Button | Mask | Button | Mask | Button | Mask | Button |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `0x0001` | DPAD_UP | `0x0010` | START | `0x0100` | LEFT_SHOULDER | `0x1000` | A |
| `0x0002` | DPAD_DOWN | `0x0020` | BACK | `0x0200` | RIGHT_SHOULDER | `0x2000` | B |
| `0x0004` | DPAD_LEFT | `0x0040` | LEFT_THUMB | `0x0400` | GUIDE | `0x4000` | X |
| `0x0008` | DPAD_RIGHT | `0x0080` | RIGHT_THUMB | | | `0x8000` | Y |

`0x0000` is nothing held and `0x0800` is unused by XInput and stays unused here. `protocol/actions.txt` holds
the masks as `PAD_BUTTON` rows so neither side keeps its own copy, the same way it holds the action ids.

Nothing in a PAD_STATE can be out of range, so a well-formed one is never a protocol error. Every field
spans its whole width, and an undefined button bit is an unknown id, which this protocol drops and counts.
That is the line BUTTON already draws: its `button` id may be anything and is dropped by the laptop, while
its `down` byte closes the connection, because a flag with two legal values out of 256 means the stream is
corrupt rather than newer. A wrong payload length still closes the connection, as for any type.

At 13 bytes on the wire PAD_STATE is the longest fixed-length frame, and still far inside the 258 that a
255-byte TEXT sets as the maximum. No buffer on either side changed.

The phone asks for the controller with PAD_ATTACH and gives it back with PAD_DETACH, and the laptop answers
both with TEXT 8. The reply is one token from a fixed vocabulary, never prose, because the phone branches on
it:

| Token | Means |
| --- | --- |
| `ready` | the virtual controller is plugged in and PAD_STATE will be acted on |
| `no-driver` | the virtual-controller driver is not installed on the laptop |
| `attach-failed` | the driver is there but plugging the pad in did not work |

Only `ready` leaves the pad usable; the other two are the two different things the phone has to tell its user
when it falls back to the keyboard. A token the phone does not know is read as `no-driver`: not usable, and
it cannot say why. Matching on the laptop's wording instead of on a token would make that wording part of the
protocol and so unchangeable. The laptop also sends TEXT 8 once after the handshake, so a phone can hide the
pad rather than offer a button that quietly does nothing.

## On a Mac

The protocol is unchanged: the Mac app speaks version 5 and the tables above are the whole of the wire format. What differs is what the Mac does with a frame.

KEY still carries a Windows virtual-key code, and the Mac maps it by position: Win is Command, Ctrl is Control, Alt is Option. The Menu key has no Mac equivalent and is dropped and counted. Mute, volume and the media keys go out as the Mac's own media keys.

| Action | A Mac does |
| --- | --- |
| MUTE_TOGGLE, VOLUME_UP, VOLUME_DOWN, PLAY_PAUSE, NEXT_TRACK, PREVIOUS_TRACK | the Mac's own media key |
| MIC_MUTE_TOGGLE | toggles the default microphone's mute through Core Audio |
| LOCK | Control+Command+Q |
| TASK_VIEW | Mission Control (Control+Up) |
| SHOW_DESKTOP | F11 |
| SEARCH | Spotlight (Command+Space) |
| NOTIFICATIONS | nothing: macOS has no default shortcut, so it is dropped and counted |
| DESKTOP_LEFT, DESKTOP_RIGHT | Control+Left, Control+Right |
| APP_SWITCH_BEGIN | holds Command and presses Tab |
| APP_SWITCH_NEXT, APP_SWITCH_PREVIOUS | Tab, Shift+Tab, only while Command is held |
| APP_SWITCH_END | releases Command |
| ZOOM_RESET | Command+0 |
| BRIGHTNESS_UP, BRIGHTNESS_DOWN | the panel's brightness, plus or minus 10; the brightness key where the panel cannot be read |
| PAD_ATTACH, PAD_DETACH | nothing is plugged in; answered with TEXT 8 `no-driver` |
| CAPTURE_REGION, CAPTURE_SCREEN | Command+Shift+4, Command+Shift+3 |
| CAPTURE_RECORD | Command+Shift+5, which opens the capture toolbar |
| CAPTURE_CANCEL | Escape, only while the crosshair or the toolbar may still be open (8 seconds). Any capture action sent while one may be open presses Escape first, then waits 50 ms. A recording already running is not stopped: Escape does not stop it, and its stop button is in the menu bar |
| macro slots (64 to 95) | the macro in that slot of the Mac's own list, opened through NSWorkspace: a file, folder or web address is opened, and an `.app` is launched with its arguments split on spaces, with no quoting. An empty or missing slot is dropped and counted. Never a shell, and never anything the phone sent |
| ANNOTATE_*, TOOL_*, SIGN_* (40 to 51) | the annotation overlay, as on Windows: a borderless, transparent, click-through window over the primary display only, above full-screen apps and never key or main, so it takes no focus. SIGN_SAVE writes a transparent PNG cropped to the ink to the pasteboard (PNG and TIFF) and to `~/Pictures/Edgepad/signature-<timestamp>.png`, never over an existing file (`-2`, `-3` are added), and answers TEXT 11 |

ZOOM sums wheel units until a whole notch (120) is owed, then presses Command+plus or Command+minus once per notch. These are macOS's default shortcuts: if one was changed or switched off in System Settings > Keyboard > Keyboard Shortcuts, the gesture does whatever that shortcut now does, or nothing.

Controls are as on Windows, with these exceptions. MEDIA_POSITION is dropped, because now playing is not built on the Mac. VOLUME and MIC_LEVEL go through Core Audio. BRIGHTNESS and REFRESH_RATE act on the display, and BRIGHTNESS needs a private framework that may be missing; see [ARCHITECTURE.md](ARCHITECTURE.md). The Mac sends TEXT 5 (the macro names) from the list edited under **Macros…** in its menu bar, again whenever that list is saved, and answers TEXT 7 with the icons as TEXT 6 chunks, read from the target or the icon file with NSWorkspace; TEXT 8 is always `no-driver`. PAD_STATE is dropped. PEN, PEN_COLOR, PEN_WIDTH, PEN_OPACITY, PEN_RGB and TEXT 9 `annotate/capture` and TEXT 10 (the primary display in pixels, sent again when the displays change) are live as on Windows. Drawing over other apps should not need Screen Recording permission, since the overlay only draws; that is UNKNOWN until tried on a Mac. STATE for volume, microphone and brightness is sent after the handshake and then whenever a poll, twice a second, finds a change.

## On Linux

The protocol is unchanged: the Linux app speaks version 5 and the tables above are the whole of the wire format. It is the shared C# core with a Linux action table, so everything the Windows app drops and counts, it drops and counts the same way. What differs is what it does with a frame, and what it cannot do. Nothing here has been seen working on a Linux laptop; [LINUX.md](LINUX.md) says how to check each item.

KEY still carries a Windows virtual-key code, and Linux maps it by position to an evdev key: Win is the left Meta key and the Menu key is `KEY_COMPOSE`. Input goes through a uinput keyboard and mouse, so it works the same on Wayland and X11. A KEY for SysRq (the phone's PrtSc) is dropped and counted while Alt is held, since Alt+SysRq+key is the kernel's magic SysRq. ZOOM is Ctrl plus the wheel.

| Action | Linux does |
| --- | --- |
| MUTE_TOGGLE, VOLUME_UP, VOLUME_DOWN | the keyboard's own volume keys, so the desktop's on-screen display shows |
| PLAY_PAUSE, NEXT_TRACK, PREVIOUS_TRACK | the MPRIS player that is playing (else a paused one, else the first); the media key when none exports MPRIS |
| MIC_MUTE_TOGGLE | toggles the default microphone's mute through `wpctl`, else `pactl` |
| LOCK | `loginctl lock-session` |
| TASK_VIEW, SHOW_DESKTOP, SEARCH, NOTIFICATIONS, DESKTOP_LEFT, DESKTOP_RIGHT | the default shortcut of the running desktop, chosen from `XDG_CURRENT_DESKTOP`; GNOME and KDE only, dropped and counted on any other. **Written from memory, unverified** |
| APP_SWITCH_BEGIN, APP_SWITCH_NEXT, APP_SWITCH_PREVIOUS, APP_SWITCH_END | holds Alt and presses Tab; Tab and Shift+Tab only while Alt is held; End releases Alt |
| ZOOM_RESET | Ctrl+0 |
| BRIGHTNESS_UP, BRIGHTNESS_DOWN | the panel's brightness, plus or minus 10, through logind and a `/sys/class/backlight` device; dropped and counted on a laptop with none |
| PAD_ATTACH, PAD_DETACH | plugs in and removes a virtual Xbox 360 controller through uinput; TEXT 8 is `no-driver` when `/dev/uinput` cannot be opened |
| CAPTURE_REGION, CAPTURE_SCREEN | Print, which opens the desktop's own screenshot tool where either is picked; tried on an unknown desktop too |
| CAPTURE_RECORD | the GNOME or KDE recording shortcut; dropped and counted on any other desktop. **Unverified** |
| CAPTURE_CANCEL | Escape, only while the screenshot tool opened by Print may still be open (8 seconds). A capture action sent while it may be open presses Escape first. The recording shortcut opens no overlay and a running recording is never touched |
| macro slots (64 to 95) | the macro in that slot of `~/.config/edgepad/macros.txt`, a text file edited by hand (there is no editor): a web address, folder or non-executable file is opened with `xdg-open`, anything else is run as a program with its arguments split on spaces, never through a shell. An empty or missing slot is dropped and counted. The phone sends only the slot number |
| ANNOTATE_*, TOOL_*, SIGN_* (40 to 51) | best effort, below |

Controls are as on Windows, with these exceptions. VOLUME and MIC_LEVEL go through `wpctl`, else `pactl`, and follow the laptop's own volume within half a second. BRIGHTNESS needs logind and a backlight device; where there is none the dial has nothing to show and the phone is not told one. MEDIA_POSITION reads and seeks through MPRIS, as far as the player reports a position and allows seeking. **REFRESH_RATE is not offered:** Linux has no shared way to switch display modes, so TEXT 4 is an empty list and the dial has nothing to show. TEXT 5 lists the names in `macros.txt` and TEXT 6 is never sent: **there are no macro icons on Linux**, and the phone keeps its labels.

**Annotate is best effort.** Avalonia has only an X11 backend on Linux, so on a Wayland session the overlay is an XWayland window: borderless, topmost, never activated, with an ARGB background and an empty X input shape so clicks pass through. A laptop names `annotate` in TEXT 9 only if the window is an X11 one, a compositor is running and the X server took the input shape; otherwise TEXT 9 is `capture` alone and a phone shows no Annotate tile. If showing the layer fails later, annotate is turned off for the rest of the run and the phone is told. Whether the layer stays above native Wayland windows on GNOME, whether clicks reach the window beneath, and where strokes land under fractional scaling are UNKNOWN. TEXT 10 is the primary display in physical pixels, sent again when Avalonia reports the screens changed. SIGN_SAVE writes a transparent PNG cropped to the ink to `Edgepad/signature-<timestamp>.png` in the Pictures folder (`$XDG_PICTURES_DIR`, else `~/.config/user-dirs.dirs`, else `~/Pictures`), never over an existing file, and puts it on the clipboard as `image/png`; TEXT 11 answers `saved`, `empty` or `failed`.

**Capture is per desktop**, as the table says: Linux has no one screenshot or recording shortcut, so the laptop presses the running desktop's own and the phone cannot tell which. TEXT 9 names `capture` always; whether a given press does anything depends on the desktop.

**The pad is uinput.** The controller is a second virtual device that copies what the kernel's `xpad` driver reports for a wired Xbox 360 pad (USB `045e:028e`), with the sticks, triggers, hat and A, B, X, Y laid out as `xpad` lays them out, so PAD_STATE's `XINPUT_GAMEPAD` fields map onto it directly. There is no rumble. It exists only while the phone has the gamepad open and is destroyed with the session.

**Sessions are the logged-in user's.** Edgepad runs as the user and hangs up on the phone while logind says that user's session is not the one in front, so a fast user switch cannot let the phone type into another user's desktop. STATE for volume, microphone and brightness is sent after the handshake and then whenever a poll, twice a second, finds a change.

## The network link (Wi-Fi and USB)

The same frames run over TCP, inside a link layer of their own called EDGL1. It has its own version byte, `linkVersion` (1), independent of the protocol version above: the protocol version stays 5, and a change to this layer moves `linkVersion` only. Everything below the frames is EDGL1; `FrameStream` and every frame above it are unchanged. `protocol/link-vectors.txt` holds worked values for every derivation here, written by the core test `LinkVectorsOutput` from the same code the laptop runs (CI uploads it as the `edgepad-link-vectors` artifact). The C#, Kotlin and Swift suites must each reproduce every line of it. It is a cross-check between implementations, not a review of the scheme.

Network listening is off until the laptop's owner turns it on. All crypto is platform crypto: P-256 ECDH, HKDF and HMAC over SHA-256, AES-256-GCM.

### Discovery

The laptop listens on TCP port 47321 by default (configurable) and announces the service `_edgepad._tcp` over mDNS, instance name = the laptop's name, with TXT keys `id` (the laptop id, 32 hex digits), `n` (its name), `v` (the protocol version) and `lv` (the link version). A phone may also be given `host:port` by hand. Over USB tethering the phone probes the tether subnet for the port and asks INFO. The port, service name and TXT keys were chosen here; none was checked against a registry.

### Notation

All multi-byte integers are little-endian unless marked BE. `H` is SHA-256. `pkP` and `pkL` are 65-byte X9.63 uncompressed P-256 points (`04 | X | Y`); a receiver rejects any other length, any other first byte and any point off the curve. `nP` and `nL` are 16 random bytes. `phoneId` and `laptopId` are 16 random bytes, kept per install. A name field is `u8 length | UTF-8`, cut at a character boundary to at most 255 bytes, and always used exactly as sent.

Every cleartext message is `u16 length | body`, the length at most 512; a longer one closes the connection. The raw ECDH secret `Z` is the X coordinate of the shared point. HKDF is RFC 5869 with SHA-256; `Expand(prk, label, n)` takes the label as `info`.

### Modes

A phone opens with `"EDGL" | linkVersion u8 | mode u8`. A laptop that does not speak that link version answers status 2 and closes, without reading further.

| Mode | Name | Purpose |
| --- | --- | --- |
| 0 | INFO | Who is this laptop. No secret. |
| 1 | PAIR | First connection: the owner confirms, both ends keep a key. |
| 2 | RESUME | Every later connection: prove the key, fresh session keys. |

**INFO.**

```
P->L  "EDGL" lv=1 mode=0
L->P  status=0 | laptopId[16] | protocolVersion u8 | name field        then the laptop closes
```

**PAIR.** The phone commits to its key before it sees the laptop's.

```
P->L  "EDGL" lv=1 mode=1 | phoneId[16] | phone name field | commit[32]
        commit = H("EDGL1 commit" | pkP | nP)
L->P  status=0 | laptopId[16] | laptop name field | pkL[65] | nL[16]
        status 1 = busy or pairing locked, 2 = link version unsupported; either is the whole reply, then the laptop closes
P->L  pkP[65] | nP[16]                      the reveal; the laptop checks it against commit, else closes and counts a failure
        both: Z = ECDH; PRK = HKDF-Extract(salt = th, ikm = Z)
        the phone shows SAS; the laptop shows a prompt and waits for the owner to type it
L->P  tagL[32]                              sent ONLY if the typed digits equal the laptop's own SAS
P->L  tagP[32]                              sent only if tagL verifies
        both: PK = Expand(PRK, "EDGL1 pairing key", 32), stored under the peer's id
```

**RESUME.**

```
P->L  "EDGL" lv=1 mode=2 | phoneId[16] | pkP[65] | nP[16]
L->P  status=0 | pkL[65] | nL[16] | tagL[32]
        status 3 = phone unknown (the whole reply; the phone offers to pair again, never silently)
        PRK = HKDF-Extract(salt = th, ikm = Z | PK)
P->L  tagP[32]
```

After the last message of PAIR or RESUME the same TCP connection carries encrypted records.

### Transcript, code and confirmation

```
th (PAIR)   = H("EDGL1 th" | 0x01 | phoneId | laptopId | phoneNameField | laptopNameField | pkP | nP | pkL | nL)
th (RESUME) = H("EDGL1 th" | 0x02 | phoneId | laptopId | pkP | nP | pkL | nL)          no names travel in a resume
SAS         = ( u32_BE( H("EDGL1 sas" | th)[0..4] ) mod 1_000_000 ) as six decimal digits, zero padded
tagL (PAIR)   = HMAC( Expand(PRK, "EDGL1 confirm laptop", 32), "pair" | th )
tagP (PAIR)   = HMAC( Expand(PRK, "EDGL1 confirm phone", 32),  "pair" | th )
tagL (RESUME) = HMAC( Expand(PRK, "EDGL1 confirm laptop", 32), th )
tagP (RESUME) = HMAC( Expand(PRK, "EDGL1 confirm phone", 32),  th )
```

Both names are in the pairing transcript as sent, so a relay that changes either name changes the code. Every tag, commitment and code is compared in constant time.

### Who types the code

**The phone shows the six digits; the owner types them on the laptop.** The laptop shows "Pair <phone name>?" with a field for the code, a Pair button and a Cancel button. Any device on the network that runs the protocol can work out its own code, but only a person at this laptop can type one, and only the code of the phone in their hand matches this laptop's transcript. The phone sends no value that depends on the digits. The laptop's `tagL` goes out only after the typed digits match, so a phone that receives a valid `tagL` knows the owner typed this phone's code for this transcript. The phone's `tagP` follows, and only when the laptop has verified it does the laptop trust the phone (`TrustStore.Grant`) and store PK. A relay between a phone and a laptop gets the same code on both legs with probability about 2^-20, because the phone's commitment fixes `pkP` and `nP` before the relay sees the laptop's; the owner would then type a code that does not match the laptop's leg.

A rogue device on the network can start a pairing, which makes the laptop show a prompt; it cannot complete it, because the owner cannot read its code. Typing their own phone's code into that prompt fails and counts as a failure.

### Session keys and records

```
kP2L = Expand(PRK, "EDGL1 key p2l", 32)      kL2P = Expand(PRK, "EDGL1 key l2p", 32)      iv = Expand(PRK, "EDGL1 iv", 8)
nonce (12 bytes) = prefix[4] | counter u64 BE        prefix = iv[0..4] phone to laptop, iv[4..8] laptop to phone; counters start at 0
record = u16 length | ciphertext | tag[16]          length = ciphertext + 16; at most 16384 bytes of plaintext per record
AES-256-GCM, 16-byte tag, AAD = the 2 length bytes
```

A record carries any bytes; the frame stream above does not know where one ends. A write over 16384 bytes is split into several records. A length of 16 or less, or over 16384 + 16, is an error. A record that fails its tag, arrives out of order, repeats, or is cut short closes the link for good in both directions; nothing is read or sent after the first failure.

### Limits, timeouts and lockout

The phone has 10 seconds for each message the laptop waits on (the request, the reveal, each confirmation); the owner has 60 seconds on the laptop to type the code. At most 4 connections that have not authenticated are open at once, each with a 10 second deadline until the link layer finishes. Only one pairing runs at a time; another is answered with status 1. Five pairings in a row that fail (a wrong code typed, a commitment that does not open, a phone that does not confirm, no code typed in time) lock pairing for 5 minutes, doubling with every further five and never more than an hour; a successful pairing clears the count. The owner cancelling, or the link ending under the prompt, does not count. Failures are counted in memory only. Every attempt uses fresh keys, so an old code is useless.

### Trust

After a pairing the laptop calls `TrustStore.Grant("link:<phoneIdHex>")`, where `<phoneIdHex>` is the phone id as 32 lowercase hex digits. The owner has already consented by typing the code, so the handshake finds the phone trusted. The PIN levels, the lock-out and the hand-off rules use the same `link:<hex>` address and run unchanged inside the encrypted channel. The laptop keeps `paired-links.txt` beside the trust file (one line per phone: `idHex<TAB>base64(PK)<TAB>name`), written through the private-folder helper. A file that exists and cannot be read refuses every network phone; Bluetooth is unaffected. "Forget trusted phones" also forgets paired links. The phone keeps PK wrapped by a non-exportable Android Keystore key.

### What this does not protect

- Someone who copies PK from a phone or a laptop can impersonate that phone to that laptop. This is the same posture as the existing trust file.
- A device on the same network can open sockets and send pairing requests (rate-limited as above), and can see the phone's and the laptop's names, the laptop's id and its port; mDNS shows the same.
- A passive capture of a whole pairing or resume exchange yields nothing that can be tested offline, and the session keys do not depend on the digits.
- The scheme is implemented three times against shared vectors. No cryptographer has reviewed it, and nothing here claims one has.
- No latency over this link has been measured, and none is stated.

## Versions

| Version | Release | Change |
| --- | --- | --- |
| 1 | 0.1.0 | HELLO through STATE; TEXT kinds 0 and 1 arrived in 0.2.0 without a bump |
| 2 | 0.3.0 | TEXT kind 2; a mismatch is refused with the laptop's version |
| 3 | 0.6.0 | TEXT kind 3; actions 31 to 34 arrived in 0.5.0, CONTROL 4, TEXT 4 and 5 and the macro block in 1.1.0, and TEXT 6 and 7 in 2.3.0 |
| 4 | 3.0.0 | PAD_STATE, a new frame type; actions 35 and 36 and TEXT 8 came with it and would not have needed one |
| 5 | 4.0.0 | PIN_REQUEST and PIN_ANSWER, PEN, and FILE_BEGIN, FILE_CHUNK, FILE_END and FILE_RESULT, all new frame types; actions 40 to 55, CONTROL 5 and 6 and TEXT 9 to 12 came with them and would not have needed a bump |

Version 4 was a new frame type. PAD_STATE's type byte `0x23` is
the whole reason: an unknown type closes the connection, so a v3 laptop meeting a v4 phone's PAD_STATE would
hang up mid-session instead of ignoring a feature it does not have. Refusing at the handshake turns that into
a message about which side needs updating. The rest of the gamepad rode along free — PAD_ATTACH, PAD_DETACH
and TEXT 8 are new ids in tables that already existed, and an older build drops and counts them.

Version 5 is the same kind of move, made for new frame types: PIN_REQUEST (`0x03`), PIN_ANSWER (`0x04`), PEN (`0x24`) and the four file frames (`0x50` to `0x53`). A v4 phone meeting a PIN_REQUEST would hang up mid-handshake instead of saying which side is old, and a v4 laptop meeting a PEN would close the link mid-session. From version 5 a v4 peer is refused at HELLO exactly as before: the laptop answers with its own version, then closes. Everything else in 4.0.0 (actions 40 to 55, CONTROL 5 and 6, TEXT 9 to 12, the Linux app) is a new id in a table that already existed, so an older build drops and counts it. Because 4.0.0 is the first release at version 5, a frame added while it was being built was still "version 5": every half in a build comes from one commit. PEN_WIDTH (CONTROL 8), PEN_OPACITY (CONTROL 9) and PEN_RGB (TEXT 17) are new ids in tables that already existed too, so they cost no version: a laptop without them drops and counts them and draws the strokes as it always did.

The blob frames (`0x54` to `0x56`) are new frame types too, and the version stays 5 because version 5 has not shipped: they belong to the same unreleased step as the file frames. CONTROL 7, ACTION 56 and 57 and TEXT 13 to 15 are new ids in existing tables and would not have needed a bump.

The refresh-rate dial and the macro buttons both arrived in 1.1.0 **without** a bump, which is the rule
working rather than being broken. An unknown action, control or text kind is dropped and counted, so a
build that predates them meets them by ignoring them. Only a new frame **type** forces a version, because
an unknown type closes the connection. From 1.0 this number moves only in a major release.

The rule from version 2 on: any change to what a frame means, or a new frame type, bumps the version on both sides in the same commit and adds a fixture line.

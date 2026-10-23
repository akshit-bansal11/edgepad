# Wire protocol

Version 5. Both apps refuse any other version at the handshake.

The transport is an RFCOMM byte stream. A frame is one type byte followed by a payload whose length is fixed by the type, except TEXT, whose second header byte gives the length of the text that follows. All multi-byte integers are little-endian. An unknown type byte, a wrong length, a bad magic or a flag byte other than 0 or 1 is a protocol error and closes the connection. A well-formed frame naming an action or control this side does not know is dropped and counted, never treated as an error, so a newer app can talk to an older one until the version check says otherwise.

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

TEXT kinds: 0 what is playing (laptop to phone), 1 the app playing it (laptop to phone), 2 the timeline as `seconds/length` such as `84/227` (laptop to phone), 3 text to type (phone to laptop), where `\b` is backspace and `\n` is enter, 4 the display's available refresh rates as `60/120/144` (laptop to phone), 5 the laptop's macro names as `Chrome/Spotify/Notes` (laptop to phone), 6 one piece of one macro's icon as `slot/chunk/chunks/base64` (laptop to phone), 7 a request for the macro icons, with an empty payload (phone to laptop), 8 whether a virtual controller can be offered, as one PAD_STATUS token (laptop to phone), 9 the optional features the laptop has, as tokens joined by `/` such as `annotate/capture`, where a feature not named is off and a token the phone does not know is ignored (laptop to phone), 10 the primary display in pixels as `1920/1080` (laptop to phone), 11 how a sign-save ended, as one SIGN_STATUS token: `saved`, `empty` or `failed` (laptop to phone).

An icon does not fit the 255 bytes a payload holds, so TEXT 6 carries a PNG in pieces and the phone joins them: the counts are what let it tell a finished icon from a truncated one. Kinds rather than a frame type of their own, which is what keeps the protocol version where it is — an unknown kind is dropped and counted on both sides, while an unknown type closes the connection. Icons are answered rather than pushed, so a laptop too old to know TEXT 7 drops it and the phone keeps its labels, and a phone too old to send it costs the link nothing.

The handshake has deadlines on both sides, and none of them is a change to the protocol. From version 5 a laptop whose security level asks for a PIN adds one step after the trust question, described in [The PIN](#the-pin). The laptop closes a connection that sends no HELLO within 10 seconds. When the phone that sent HELLO is not one it trusts, the laptop holds HELLO_ACK and asks its owner whether to trust it; no answer within 60 seconds is a no, and a no is the socket closing without a HELLO_ACK, exactly what a phone the laptop does not trust has always been sent. The phone gives up on HELLO_ACK after 90 seconds, longer than the laptop's question, so it hears the answer rather than abandoning it.

After HELLO_ACK the laptop sends a STATE for volume, microphone and brightness, then TEXT 0, 1 and 2 and a STATE for media position, then TEXT 4 and a STATE for the refresh rate, then TEXT 5 and TEXT 9, and thereafter every change as it happens. Media position is refreshed once a second while playing. Icons are not part of that sequence: the laptop sends TEXT 6 only in answer to TEXT 7.

## The PIN

Version 5 lets the laptop ask for a PIN before it admits a phone. The order is: HELLO, the version check, the lock-out check, the trust question, the PIN gate, then HELLO_ACK. Trust comes first and the PIN second, and the gate sits before the laptop lets a new phone take over from the connected one, so a phone that has not proved itself can never displace the one in use.

The laptop's security level decides whether the gate runs. Level 1 (the default) is trust only: it sends neither PIN frame and behaves as 3.5.0 did. Level 2 asks once per phone: the gate runs for an address not yet verified, and a correct PIN adds it to the verified list. Level 3 asks on every connection. The level and the PIN live on the laptop; the phone never stores a PIN.

At the gate the laptop sends PIN_REQUEST with the attempts left and a lock time of 0, then waits 60 seconds for PIN_ANSWER. No answer, or any other frame, closes the connection. A digit byte above 9 is a protocol error that closes the connection; it is not counted as a failed attempt, since it tests no PIN. A wrong PIN counts as a failure; on every fifth failure the laptop sends PIN_REQUEST with 0 attempts left and the lock time in seconds, then closes, and a phone from a locked address gets the same answer straight after HELLO. The lock lasts 30 seconds for the first five failures and doubles for each further five, up to an hour. A correct PIN zeroes the count.

Changing the PIN or the level clears the verified list, so every phone at level 2 enters the new PIN once. After HELLO_ACK, a PIN_REQUEST or PIN_ANSWER is dropped and counted.

The digits are never logged, written to a status line or shown by a debug print on either side, and they travel in a frame type of their own rather than in TEXT so they can never be routed to typing. RFCOMM already requires the phones to be paired and the Bluetooth stack encrypts the paired link, so the PIN is a second factor and not a second cipher.

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

| Control | Id | SET does | STATE reports |
| --- | --- | --- | --- |
| VOLUME | 0 | the default speakers' level | level, flag muted |
| BRIGHTNESS | 1 | the built-in panel's brightness through WMI | level |
| MIC_LEVEL | 2 | the default microphone's level | level, flag muted |
| MEDIA_POSITION | 3 | seeks the current track to that percent, where the player allows it | percent, flag playing |
| REFRESH_RATE | 4 | switches the display to the rate at that index of TEXT 4's list | the current rate's index |
| PEN_COLOR | 5 | picks the stroke colour, by index into the palette below; 8 or more is dropped and counted | not reported |

REFRESH_RATE's value is an index into TEXT 4's list, never a rate in hertz: SET carries value u8 and the laptop drops anything above 100, so 120 or 144 could not cross the wire at all. An index also makes a rate the laptop does not have unrepresentable rather than merely rejected. A laptop that names no rates has none to offer, and the dial has nothing to show.

Actions 64 to 95 are a reserved block of 32 macro slots: the phone runs slot n as `MACRO_BASE + n`. It
sends the index and never what the index opens. The laptop's own list, edited from its tray menu, decides
that, so a slot cannot be repointed from the phone and the protocol stays semantic.

This is not a containment boundary against a compromised phone, and should not be read as one. KEY carries a
raw virtual-key code and TEXT kind 3 carries arbitrary characters, both of which the laptop injects directly
— that is what the phone's keyboard screen is. A paired phone is a trusted input device, and the trust
boundary is the Bluetooth pairing plus trust-on-first-use, not the macro table.

## Annotate and sign

The laptop owns the drawing and the phone only steers it. ANNOTATE_BEGIN shows a transparent overlay on the laptop's primary display; SIGN_BEGIN shows nothing and accepts only the pen tool. PEN outside either mode, and a tool action in the wrong mode, is dropped and counted. A laptop says it can annotate by naming `annotate` in TEXT 9, and sends TEXT 10 (`1920/1080`, the primary display in pixels) after the handshake and again when the display changes, so the phone can shape its surface to the same ratio. A phone that never hears either shows nothing to draw on.

PEN carries a position as a fraction of the screen: x and y use the whole u16 span, 0 the left or top edge and 65535 the right or bottom edge, so no pixel count has to cross the link. A state byte other than 0, 1 or 2 decodes like any other value and is dropped and counted, because a newer phone may add states; it is not a protocol error.

Pen width is 0.4% of the screen height and the highlighter's 2%, with the highlighter at 35% opacity and stroked once per path so a loop does not darken where it crosses itself. Line, arrow, rectangle and ellipse run from the down point to the latest move. The phone's preview is approximate; what the laptop draws is what counts. END, a session ending or the app quitting removes the overlay and discards the strokes.

SIGN_SAVE renders the strokes at screen resolution, transparent and cropped to the ink with an 8 pixel margin, puts the PNG on the clipboard and saves it under `Pictures\Edgepad\`, then answers with TEXT 11: `saved`, `empty` (nothing was drawn) or `failed`.

PEN_COLOR's value indexes this palette, never a colour of the phone's choosing:

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
| macro slots (64 to 95) | nothing: macros are not built on the Mac, so they are dropped and counted |
| ANNOTATE_*, TOOL_*, SIGN_* (40 to 51) | the annotation overlay, as on Windows: a borderless, transparent, click-through window over the primary display only, above full-screen apps and never key or main, so it takes no focus. SIGN_SAVE writes a transparent PNG cropped to the ink to the pasteboard (PNG and TIFF) and to `~/Pictures/Edgepad/signature-<timestamp>.png`, never over an existing file (`-2`, `-3` are added), and answers TEXT 11 |

ZOOM sums wheel units until a whole notch (120) is owed, then presses Command+plus or Command+minus once per notch. These are macOS's default shortcuts: if one was changed or switched off in System Settings > Keyboard > Keyboard Shortcuts, the gesture does whatever that shortcut now does, or nothing.

Controls are as on Windows, with these exceptions. MEDIA_POSITION is dropped, because now playing is not built on the Mac. VOLUME and MIC_LEVEL go through Core Audio. BRIGHTNESS and REFRESH_RATE act on the display, and BRIGHTNESS needs a private framework that may be missing; see [ARCHITECTURE.md](ARCHITECTURE.md). The Mac sends TEXT 5 (the macro names) empty, so the phone's grid says there are none, and TEXT 8 is always `no-driver`. PAD_STATE is dropped, and TEXT 7 is ignored. PEN, PEN_COLOR and TEXT 9 `annotate/capture` and TEXT 10 (the primary display in pixels, sent again when the displays change) are live as on Windows. Drawing over other apps should not need Screen Recording permission, since the overlay only draws; that is UNKNOWN until tried on a Mac. STATE for volume, microphone and brightness is sent after the handshake and then whenever a poll, twice a second, finds a change.

## Versions

| Version | Release | Change |
| --- | --- | --- |
| 1 | 0.1.0 | HELLO through STATE; TEXT kinds 0 and 1 arrived in 0.2.0 without a bump |
| 2 | 0.3.0 | TEXT kind 2; a mismatch is refused with the laptop's version |
| 3 | 0.6.0 | TEXT kind 3; actions 31 to 34 arrived in 0.5.0, CONTROL 4, TEXT 4 and 5 and the macro block in 1.1.0, and TEXT 6 and 7 in 2.3.0 |
| 4 | 3.0.0 | PAD_STATE, a new frame type; actions 35 and 36 and TEXT 8 came with it and would not have needed one |
| 5 | 4.0.0 | PIN_REQUEST and PIN_ANSWER, and PEN; actions 40 to 51, CONTROL 5 and TEXT 10 and 11 came with PEN and would not have needed a bump |

Version 5 is the same kind of move: PIN_REQUEST (`0x03`) and PIN_ANSWER (`0x04`) are new frame types, and a v4 phone meeting a PIN_REQUEST would hang up mid-handshake instead of saying which side is old. The first move since v3 in 0.6.0, and it took a new frame type to earn it. PAD_STATE's type byte `0x23` is
the whole reason: an unknown type closes the connection, so a v3 laptop meeting a v4 phone's PAD_STATE would
hang up mid-session instead of ignoring a feature it does not have. Refusing at the handshake turns that into
a message about which side needs updating. The rest of the gamepad rode along free — PAD_ATTACH, PAD_DETACH
and TEXT 8 are new ids in tables that already existed, and an older build drops and counts them.

The refresh-rate dial and the macro buttons both arrived in 1.1.0 **without** a bump, which is the rule
working rather than being broken. An unknown action, control or text kind is dropped and counted, so a
build that predates them meets them by ignoring them. Only a new frame **type** forces a version, because
an unknown type closes the connection. From 1.0 this number moves only in a major release.

The rule from version 2 on: any change to what a frame means, or a new frame type, bumps the version on both sides in the same commit and adds a fixture line.

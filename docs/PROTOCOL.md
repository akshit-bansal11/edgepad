# Wire protocol

Version 3. Both apps refuse any other version at the handshake.

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
| `0x21` | SET | control u8, value u8 | phone to laptop | set a control to 0-100 |
| `0x30` | PING | t i64 | phone to laptop | the phone's clock, in nanoseconds |
| `0x31` | PONG | t i64 | laptop to phone | the same value echoed, for the round-trip readout |
| `0x40` | STATE | control u8, value u8, flags u8 | laptop to phone | a control's current value; flags bit 0 is muted for audio controls and playing for media position |
| `0x41` | TEXT | kind u8, length u8, UTF-8 bytes | both ways | up to 255 bytes, never split inside a character |
| `0x22` | KEY | code u16, down u8 | phone to laptop | press or release one key, by Windows virtual-key code |

TEXT kinds: 0 what is playing (laptop to phone), 1 the app playing it (laptop to phone), 2 the timeline as `seconds/length` such as `84/227` (laptop to phone), 3 text to type (phone to laptop), where `\b` is backspace and `\n` is enter.

After HELLO_ACK the laptop sends a STATE for volume, microphone and brightness, then TEXT 0, 1 and 2 and a STATE for media position, and thereafter every change as it happens. Media position is refreshed once a second while playing.

## Actions and controls

`protocol/actions.txt` is the authoritative table; both enums are tested against it.

| Action | Id | Laptop does |
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

| Control | Id | SET does | STATE reports |
| --- | --- | --- | --- |
| VOLUME | 0 | the default speakers' level | level, flag muted |
| BRIGHTNESS | 1 | the built-in panel's brightness through WMI | level |
| MIC_LEVEL | 2 | the default microphone's level | level, flag muted |
| MEDIA_POSITION | 3 | seeks the current track to that percent, where the player allows it | percent, flag playing |

## Versions

| Version | Release | Change |
| --- | --- | --- |
| 1 | 0.1.0 | HELLO through STATE; TEXT kinds 0 and 1 arrived in 0.2.0 without a bump |
| 2 | 0.3.0 | TEXT kind 2; a mismatch is refused with the laptop's version |
| 3 | 0.6.0 | TEXT kind 3; actions 31 to 34 arrived in 0.5.0 |

The rule from version 2 on: any change to what a frame means, or a new frame type, bumps the version on both sides in the same commit and adds a fixture line.

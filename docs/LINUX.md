# Linux app: what is not verified yet

The Linux laptop app (`linux/`) is built and unit-tested on GitHub Actions only. Nobody on the project has run it on a Linux laptop with a phone. Everything below was written from documentation, not seen working. Each item says how to check it. Report results on the issue tracker.

The app logs to `~/.local/share/Edgepad/edgepad.log`. Its settings live in `$XDG_CONFIG_HOME/edgepad/` (normally `~/.config/edgepad/`).

## Until the tray app lands (step 8.5)

There is no trust prompt yet. Every phone that is not already trusted is refused, and the log says so. To test, trust a phone by hand. Pair it in the desktop's Bluetooth settings first. Then put its address, upper case, on a line of its own in `~/.config/edgepad/trusted-phone.txt` (for example `AA:BB:CC:DD:EE:FF`; `bluetoothctl devices` lists it).

Keys, the pointer, scrolling, pinch zoom, typing and the gamepad work through virtual input devices. Actions, dials and media are still received and dropped until step 8.4.

## Input: what it needs

Input goes through the kernel's `uinput` module as a virtual keyboard and mouse named `Edgepad`. It works the same on Wayland and X11, and needs no root, but your user must be able to open `/dev/uinput`. The release will ship a udev rule for this (step 8.6). Until then, as root:

```sh
echo 'KERNEL=="uinput", SUBSYSTEM=="misc", TAG+="uaccess", OPTIONS+="static_node=uinput"' > /etc/udev/rules.d/60-edgepad-uinput.rules
modprobe uinput
udevadm control --reload && udevadm trigger
```

Then log out and back in. If `/dev/uinput` cannot be opened, the log says why once, and every key and pointer frame does nothing. The session's closing log line counts them.

Typing (the phone's keyboard text, TEXT 3) presses keys only for plain ASCII, and only when `localectl status` reports the layout as exactly `us` with no variant. Backspace, Enter and Tab are always keys. Everything else (accented letters, emoji, or any text on another layout or one that cannot be read) is put on the clipboard and pasted with Ctrl+V. That replaces what was on your clipboard. It needs `wl-copy` from `wl-clipboard` on Wayland, or `xclip` on X11; without the right one, that text does nothing and the log says so once. In a terminal, Ctrl+V does not paste, so non-ASCII text cannot be typed there.

## The gamepad

The phone's gamepad plugs in a second virtual device, `Edgepad virtual Xbox 360 controller`, through the same `/dev/uinput` and the same udev rule as the keyboard. It is plugged in when you open the gamepad on the phone, and unplugged when the phone gives it back or disconnects. If `/dev/uinput` cannot be opened, the phone is told there is no driver and offers the keyboard instead.

It copies what the kernel's `xpad` driver reports for a wired Microsoft Xbox 360 pad (USB `045e:028e`), so games, SDL and Steam treat it as one: the same ids on the USB bus, sticks on `ABS_X`/`ABS_Y` and `ABS_RX`/`ABS_RY` (-32768..32767, Y positive down), triggers on `ABS_Z`/`ABS_RZ` (0..255), the D-pad as the hat `ABS_HAT0X`/`ABS_HAT0Y`, and A, B, X, Y as `BTN_A`, `BTN_B`, `BTN_X`, `BTN_Y`. Like `xpad`, it reports X as `BTN_X` and Y as `BTN_Y`, although the kernel's headers name those codes `BTN_NORTH` and `BTN_WEST`. Pressing both directions of the D-pad at once centres it. There is no rumble.

To check it, with the phone's gamepad open:

1. `evtest` lists `Edgepad virtual Xbox 360 controller`. Select it: each button prints its own `BTN_*` code, pushing the left stick up prints `ABS_Y` going negative, and the triggers go from 0 to 255.
2. `jstest /dev/input/jsN` (from `joystick`; `ls /dev/input/js*` shows N) shows 8 axes and 11 buttons, all moving.
3. In Steam, **Settings › Controller** shows an Xbox 360 controller, and its test screen shows A where A is pressed and X where X is pressed. Steam Input may also take the pad over; in a game that reads the pad itself, turn Steam Input off for that game.
4. SDL: `sdl2-jstest --list` (or any SDL game) names it as an Xbox 360 controller with a game controller mapping, not as a plain joystick.

## Unverified

| What | Why it is unknown | How to check |
| --- | --- | --- |
| BlueZ writes an SDP record for the bare service id | `RegisterProfile` is called with `Role=server` and no `Channel` or `ServiceRecord`, trusting bluetoothd to pick a channel and describe the id itself. BlueZ's docs do not say it does this for an id it does not know. | `sdptool browse local` lists a record with `758bb618-7b72-4cd3-9aa2-c9b88e54d555`, and the phone finds the laptop. If it does not, the fix is a `ServiceRecord` XML option. |
| A `FileStream` reads and writes the RFCOMM socket | The connected socket arrives as a file descriptor over D-Bus and is wrapped in a `FileStream` (`linux/src/Edgepad.Linux/Bluetooth/LinuxSession.cs` says why). .NET is documented to use plain `read`/`write` on a handle that cannot seek, but not on this kind of socket. | Connect a phone: the log shows `Connected AA:...`. A failure on the first read or write points here; the fallback is written in that file's comment. |
| Shutting the socket down ends a blocked read | Ending a session (a phone replacing another, quitting, the 10 s HELLO timeout) relies on `shutdown(2)` waking the reader thread. | Connect a phone, then a second trusted one: the log shows the first replaced and its session ended within a second. |
| Who may call RegisterProfile | Ubuntu's and Fedora's D-Bus policy for `org.bluez` are believed to let any local user register a profile; not checked. | The log shows `RFCOMM profile registered with bluetoothd`. An `AccessDenied` error instead means the policy refuses it. |
| Re-registering after bluetoothd restarts | The app watches the `org.bluez` name and registers again when it gets a new owner. | `sudo systemctl restart bluetooth`: the log shows `bluetoothd restarted; registering the profile again`, and the phone can connect again. |
| Two users logged in at once | Each runs its own copy, and both would register the same service id with the one bluetoothd. | Expected: the second copy logs a failed start and retries every 30 s. |
| The single-instance lock | Uses .NET's `FileShare.None`, which is `flock` on Linux, on `~/.cache/edgepad/lock`. | Start the app twice: the second prints `Edgepad is already running.` and exits. |
| The virtual device is picked up in time | Each session creates its own device when the phone connects, and the desktop takes a moment to start reading a new one. The handshake should give it that moment. | Connect, then move the pointer straight away: the first movement arrives. |
| Scrolling on X11 apps | The wheel sends the high-resolution value (120 to a notch, as the phone does) and the whole-notch legacy value together. Wayland apps should read the first, X11 apps the second. | Scroll slowly in a Wayland app (smooth) and an X11 one such as `xterm` (one line per notch, no skipped or doubled notches). |
| Typing as keys on a US layout | `localectl` reports the system's default layout, not one you chose for your own session in GNOME's or KDE's settings. If those differ, ASCII typed as keys comes out in the system layout's places. | With the system on `us` and the session on another layout, type `yz` on the phone: `zy` means this is being missed. |
| `wl-copy` on GNOME Wayland | GNOME does not offer the data-control protocol that `wl-copy` prefers. It has a fallback that briefly shows a surface to take the clipboard. Whether that works from a background app, and whether the clipboard is set before the Ctrl+V arrives, is not known. | On GNOME Wayland, type `é` on the phone into gedit: `é` appears. If nothing does, test again with `xclip` under XWayland. |
| The paste arrives after the clipboard is set | Ctrl+V is sent as soon as `wl-copy` or `xclip` exits. Both are believed to hold the clipboard before they exit. | Type `é` several times quickly: each one appears, and never the previous clipboard. |
| SDL's mapping for the virtual pad | SDL keys its controller mappings on a GUID made from the bus, vendor, product and version. The pad sends version `0x0114`, believed to be a wired 360 pad's own; whether every SDL version maps it, and whether SDL or Steam treat a device without a USB parent differently, is not known. | Step 4 above. A plain joystick with numbered buttons means the mapping missed. |
| Y-axis sign and button positions in games | Copied from `xpad` (Y is flipped with a bitwise NOT, so a centred stick reads -1; X is `BTN_X`), not seen in a game. | In a game, push the stick up: the character moves forward. Press X: the game shows X, not Y. |

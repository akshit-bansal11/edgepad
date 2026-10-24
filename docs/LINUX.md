# Linux app: what is not verified yet

The Linux laptop app (`linux/`) is built and unit-tested on GitHub Actions only. Nobody on the project has run it on a Linux laptop with a phone. Everything below was written from documentation, not seen working. Each item says how to check it. Report results on the issue tracker.

The app logs to `~/.local/share/Edgepad/edgepad.log`. Its settings live in `$XDG_CONFIG_HOME/edgepad/` (normally `~/.config/edgepad/`).

## Until the tray app lands (step 8.5)

There is no trust prompt yet. Every phone that is not already trusted is refused, and the log says so. To test, trust a phone by hand. Pair it in the desktop's Bluetooth settings first. Then put its address, upper case, on a line of its own in `~/.config/edgepad/trusted-phone.txt` (for example `AA:BB:CC:DD:EE:FF`; `bluetoothctl devices` lists it).

Keys, the pointer, scrolling, pinch zoom and typing work through a virtual input device. Actions, dials, media and the gamepad are still received and dropped until steps 8.3 and 8.4.

## Input: what it needs

Input goes through the kernel's `uinput` module as a virtual keyboard and mouse named `Edgepad`. It works the same on Wayland and X11, and needs no root, but your user must be able to open `/dev/uinput`. The release will ship a udev rule for this (step 8.6). Until then, as root:

```sh
echo 'KERNEL=="uinput", SUBSYSTEM=="misc", TAG+="uaccess", OPTIONS+="static_node=uinput"' > /etc/udev/rules.d/60-edgepad-uinput.rules
modprobe uinput
udevadm control --reload && udevadm trigger
```

Then log out and back in. If `/dev/uinput` cannot be opened, the log says why once, and every key and pointer frame does nothing. The session's closing log line counts them.

Typing (the phone's keyboard text, TEXT 3) presses keys only for plain ASCII, and only when `localectl status` reports the layout as exactly `us` with no variant. Backspace, Enter and Tab are always keys. Everything else (accented letters, emoji, or any text on another layout or one that cannot be read) is put on the clipboard and pasted with Ctrl+V. That replaces what was on your clipboard. It needs `wl-copy` from `wl-clipboard` on Wayland, or `xclip` on X11; without the right one, that text does nothing and the log says so once. In a terminal, Ctrl+V does not paste, so non-ASCII text cannot be typed there.

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

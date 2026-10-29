# Linux app: what is not verified yet

The Linux laptop app (`linux/`) is built and unit-tested on GitHub Actions only. Nobody on the project has run it on a Linux laptop with a phone. Everything below was written from documentation, not seen working. Each item says how to check it. Report results on the issue tracker.

The app logs to `~/.local/share/Edgepad/edgepad.log`. Its settings live in `$XDG_CONFIG_HOME/edgepad/` (normally `~/.config/edgepad/`).

## Install

Download `Edgepad-<version>-linux-x64.tar.gz` from the release (check it against `SHA256SUMS.txt`), then:

```sh
tar xzf Edgepad-<version>-linux-x64.tar.gz
cd edgepad-<version>
sudo ./install.sh            # or: sudo ./install.sh /opt/edgepad  to install under another prefix
```

`install.sh` only copies files and loads a module; it downloads nothing. It puts `edgepad` in `<prefix>/bin`, the launcher entry in `<prefix>/share/applications`, the icon in `<prefix>/share/icons/hicolor/scalable/apps` (prefix defaults to `/usr/local`), the udev rule in `/etc/udev/rules.d/60-edgepad-uinput.rules` and `uinput` in `/etc/modules-load.d/edgepad.conf`. Then it runs `modprobe uinput` and reloads udev. **Log out and back in once** so your session gets access to `/dev/uinput`. Start Edgepad from the launcher or run `edgepad`.

The app is a self-contained single file, so .NET need not be installed; its graphics libraries are inside it and unpack to `~/.net/` on first start. It still needs what the sections below list (BlueZ, `wpctl` or `pactl`, `xdg-utils`, and so on). A tray icon on GNOME needs the AppIndicator extension.

The licence notices for the bundled packages, the .NET runtime and the Lato font are in the archive's `licenses/` folder and are copied to `<prefix>/share/doc/edgepad`.

Uninstall with `sudo ./install.sh --uninstall` (same prefix, if you gave one). It removes those files; your settings in `~/.config/edgepad` stay.

## The tray, the trust question and Security

Edgepad draws its tray icon, the trust question and the Security window with Avalonia (built in C#, no XAML). Everything here was written from Avalonia's documentation and source, and none of it has been seen working. Report results on the issue tracker.

**The tray icon (UNVERIFIED).** Avalonia shows it through the StatusNotifierItem D-Bus interface, which KDE Plasma, XFCE, Cinnamon, MATE and similar desktops serve. **GNOME does not show it by default**: it needs the "AppIndicator and KStatusNotifierItem Support" extension (UNKNOWN until tested on a GNOME laptop). The menu rows are the Windows ones: the connection status, Security…, Open log, Documentation, Forget trusted phones, and Quit Edgepad with the version. Open log and Documentation call `xdg-open`, so `xdg-utils` must be installed. The icon is read from the Windows `edgepad.ico` by Avalonia's own decoder; if that fails the log says "The tray icon could not be loaded" and the tray has no picture (UNKNOWN until tested). Whether the menu's "Security… (Level N)" and Forget rows refresh when the menu opens, and not only when the status changes, also depends on the desktop (UNKNOWN).

**Without a tray.** The app still serves: phones that are already trusted connect, and the Bluetooth service does not depend on the UI. But the trust question and the Security window are then out of reach. There is no command-line way to open Security yet. A phone that is not trusted can still be asked about, because the trust question is a window and not a tray row, so a desktop with no tray can still trust a phone. Changing the security level or the PIN needs the tray, so on GNOME install the extension first. If Avalonia cannot open a display at all (an ssh session, no X11 or Wayland), the app says so on stderr, keeps serving trusted phones, and refuses every phone it would have had to ask about.

**The trust question.** A paired phone that is not trusted opens a window naming its Bluetooth address, with "Don't trust" as the default button and the Escape key. "Trust" is disabled for the first 2 seconds, so a click already in flight cannot land on it. No answer in 60 seconds, a window that cannot open, or any error is a no.

**Wayland and always-on-top.** The windows ask to be on top, but Wayland compositors may ignore that, and nothing relies on it: the question also appears in the status line and the log, and the window may open behind the one you are using (UNKNOWN per desktop). Dialogs that change security hold connections off while open (the same HoldOff the Windows and Mac apps use), because a connected phone is a keyboard.

**Fonts.** Lato is bundled with its OFL text (`Fonts/OFL.txt` inside the app), linked from the Windows project's font folder.

Keys, the pointer, scrolling, pinch zoom, typing and the gamepad work through virtual input devices. Volume, brightness, media, lock, capture and macros are below.

## Input: what it needs

Input goes through the kernel's `uinput` module as a virtual keyboard and mouse named `Edgepad`. It works the same on Wayland and X11, and needs no root, but your user must be able to open `/dev/uinput`. `install.sh` (above) sets this up. To do it by hand, as root:

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

## Volume, brightness, media, lock, capture and macros

Each of these runs a small tool or asks a service, so they depend on what is installed. Anything missing makes only that control do nothing (the log says so once); nothing else is affected.

| Needs | For | Without it |
| --- | --- | --- |
| `wpctl` (WirePlumber, PipeWire's session manager) or `pactl` (PulseAudio, or `pipewire-pulse`) | the volume and microphone dials, microphone mute, and the phone's dials following the laptop's own volume | `wpctl` is tried first and `pactl` is used when it is not installed; with neither, the dials do nothing |
| `loginctl` (systemd) | Lock (`loginctl lock-session`) | Lock does nothing |
| `xdg-open` (`xdg-utils`) | macros that open a web address, a folder or a document | those macros do nothing; macros that run a program do not need it |
| `wl-copy` (`wl-clipboard`) or `xclip` | optional: typing text that is not plain ASCII (see Input above), and the signature fallback (see Annotate and Sign) | that text does nothing |
| systemd-logind on the system bus and a `/sys/class/backlight/*` device | the brightness dial, which a desktop PC has none of | the dial does nothing and the phone is not told |
| A media player that exports MPRIS (Spotify, Firefox, Chromium, VLC and most others) | now playing, the media position dial, play/pause/next/previous | those buttons press the media keys instead, which the desktop sends to whatever it thinks is the active player |

The volume, microphone and brightness values on the phone are read twice a second, so a change made on the laptop reaches the dials within about half a second. When several players are open, the phone follows the one that is playing (else one that is paused, else the first); two playing at once keep whichever it followed first. The player's name on the phone is its D-Bus name, for example `Spotify`, not its display name.

Volume up, volume down and mute press the keyboard's volume keys, so the desktop's own on-screen display appears. The refresh-rate dial is not offered (Linux has no shared way to switch display modes). Annotate and Sign are below.

### Macros

Macros are a text file you edit by hand: there is no editor in this release. It is `~/.config/edgepad/macros.txt` (`$XDG_CONFIG_HOME/edgepad/macros.txt`), read when a phone connects. One macro per line, fields separated by a **tab**: name, target, arguments (optional). At most 15; a name is cut at 16 bytes and `/` in it becomes `-`. For example (tabs between the fields):

```
Browser	https://example.com
Notes	~/Documents/notes.txt
Terminal	gnome-terminal	--maximize
Screenshots	~/Pictures
```

A target that is a web address (`scheme://...` or `mailto:`), a folder, or a file that is not executable is opened with `xdg-open`, which ignores the arguments. Anything else is run as a program, by name from `PATH` or by path, with the arguments split on spaces (single or double quotes group a word; nothing else is interpreted). It is never run through a shell, so `;`, `$(...)`, `*` and `~` inside arguments are plain characters (only a leading `~/` in the target means your home folder). The phone sends only the slot number, never what to run.

**Macro icons (UNVERIFIED; nothing has run on hardware).** The phone asks for the icons when it opens the macro grid. An icon comes from the macro's own icon path if one is set, else from its target: a picture file (`.png`, `.svg`, `.jpg`) is the icon itself; a `.desktop` file gives its `Icon=`; a program (a path, or a name on `PATH`) is matched to a `.desktop` entry in `applications/` under `$XDG_DATA_HOME` and `$XDG_DATA_DIRS`, by file name or by `Exec=`, and that entry's `Icon=` is found as a path or as a name in the `hicolor` theme or `pixmaps`. Only `hicolor` is read, not the other themes, so a program that ships its icon only in another theme shows its name. A web address, folder or plain document has no icon and keeps its name. A file that cannot be read is logged and skipped, not reported as an error. Icons are sent at 128 px, and at 64 px when the 128 px PNG is larger than 24 KiB. UNKNOWN until tried on a Linux laptop: whether SVG rendering and the bundled Skia run inside the self-contained single file, and which desktops' icon names resolve.

### Desktop shortcuts (UNVERIFIED)

Task view, show desktop, search, notifications and switching desktops press a different shortcut on each desktop, chosen from `XDG_CURRENT_DESKTOP`. **Every one of these was written from memory of the default shortcuts and has not been seen working**; they also differ between versions and users can rebind them. A desktop other than GNOME or KDE drops those actions (the closing log line counts the dropped frames). The table lives in one file, `linux/src/Edgepad.Linux/Platform/Desktop.cs`; this is the list to try:

| Action | GNOME | KDE Plasma |
| --- | --- | --- |
| Task view | Super (tap) | Meta+W |
| Show desktop | Super+D | Meta+D |
| Search | Super+A | Alt+Space |
| Notifications | Super+V | Meta+N |
| Desktop left / right | Super+PageUp / Super+PageDown | Ctrl+Meta+Left / Ctrl+Meta+Right |
| Screenshot (region and whole screen) | Print | Print |
| Screen recording | Ctrl+Shift+Alt+R | Meta+Alt+R |
| App switcher, zoom reset | Alt+Tab (Alt held across swipes), Ctrl+0 on every desktop | |

Region and whole-screen both press Print, which opens the desktop's own screenshot tool (GNOME's screenshot UI, KDE's Spectacle) where you pick either; this is also tried on an unknown desktop. Recording is dropped on an unknown desktop.

## Annotate and Sign (best effort, in testing)

Annotate draws the phone's strokes on a transparent layer over the primary screen; Sign takes a pen signature without showing anything and saves it as a PNG. Both follow the Windows app: the same pen and highlighter widths, the same tools, the same crop (the ink plus 8 px, transparent). **None of it has been seen working on Linux.**

How it works. Avalonia 12.1.4 has only an X11 backend on Linux, so on a Wayland session the app runs under XWayland (`DISPLAY` must be set; it is on GNOME and KDE). When the tray starts, the app builds the layer hidden: a borderless, topmost (`_NET_WM_STATE_ABOVE`), never-activated window with a transparent (ARGB) background, and gives it an empty X input shape (libXext's `XShapeCombineRectangles`) so clicks pass through to the window below. Annotate is offered to the phone (TEXT 9 `annotate/capture`) only if all of that worked: the window is an X11 one, a compositor is running (without one the layer would cover the screen in black), and the X server took the input shape. Otherwise the phone gets `capture` only and hides the Annotate tile, and the log says why once (`Annotate is not offered (...)`). If showing the layer fails later, annotate is turned off for the rest of the run and the phone is told. A phone that disconnects, or Edgepad quitting, takes the layer down and throws its strokes away.

The phone is sent the primary screen's size in physical pixels (TEXT 10), again whenever Avalonia reports the screens changed.

A saved signature goes to `Edgepad/` in your Pictures folder: `$XDG_PICTURES_DIR` if set, else the `XDG_PICTURES_DIR` line of `~/.config/user-dirs.dirs`, else `~/Pictures`. It is named `signature-yyyyMMdd-HHmmss.png`, with `-2`, `-3` and on added rather than ever overwriting a file. It is then put on the clipboard as `image/png`, through Avalonia's X11 clipboard (Edgepad serves it while it runs), else `wl-copy --type image/png`, else `xclip -selection clipboard -t image/png`. The phone is told `saved`, `empty` (nothing drawn) or `failed` (the save or every clipboard route failed; the log says which).

| What | Why it is unknown | How to check |
| --- | --- | --- |
| The layer stays above other windows on GNOME Wayland | Mutter decides whether an XWayland window's above-state puts it over native Wayland windows. KDE is believed to honour it. | Start annotating with a native Wayland app (GNOME Text Editor, Files) focused and maximised: the strokes show over it. Then click that app: the strokes stay on top. |
| Clicks pass through under XWayland | The empty input shape is an X11 request. Whether Mutter and KWin pass the click on to a Wayland window underneath, and whether the window manager's frame around the layer catches it, is not known. | While annotating, click and type in the window below: it reacts, and the layer never takes focus (the window below keeps its focused title bar). |
| Without a compositor | On X11 with no compositor the layer cannot be transparent, so annotate should not be offered. | On a plain X11 window manager with no compositor, connect: the phone shows no Annotate tile and the log says `no compositor is running`. |
| Coordinates under fractional scaling | The layer is sized from the primary screen's pixels divided by the scale Avalonia gives the window; with 125% or 150% scaling under XWayland these may not match the screen. | At 150% scaling, draw a line from one corner to the opposite corner on the phone: it runs corner to corner on the laptop, not short or past the edge. |
| A second monitor, or a change of resolution | The layer follows the primary screen and is resized on each stroke; Avalonia's screen-change event is believed to fire on a resolution change. | Change the resolution or the primary monitor while connected, then annotate: the strokes land where the phone puts them, and the phone's surface takes the new shape. |
| A PNG on the clipboard on Wayland | Avalonia offers the image as X11 `image/png`; whether Mutter and KWin pass that to Wayland apps is not known, and `wl-copy` on GNOME has the problem described under Typing. | Sign on the phone and save, then paste into a native Wayland app that takes images (LibreOffice, GIMP): a transparent signature appears. The file in `~/Pictures/Edgepad/` is there either way. |

## Unverified

| What | Why it is unknown | How to check |
| --- | --- | --- |
| Network (Wi-Fi and USB phones) | Everything in the tray switch **Wi-Fi and USB connections** (off by default, `network.txt`) is unverified: that the tray toggle row shows its check mark and refreshes on every desktop; that the pairing window opens (and is not hidden behind other windows, Wayland ignores always-on-top); that the laptop's mDNS responder, which binds UDP 5353 with address reuse, coexists with Avahi or systemd-resolved's own listener on the same port; that a phone finds the laptop over Wi-Fi and over USB tethering (`usb0`/`enx*`, cdc_ether); and that ufw or firewalld lets it through. | Turn the switch on, then pair a phone: the tray row reads `Listening on 47321`, the log says `Listening for Wi-Fi and USB phones`, and the phone lists the laptop. If it does not, `ss -ltn sport = :47321` shows the TCP listener, `avahi-browse -rt _edgepad._tcp` shows the announcement, and the firewall below is the first suspect. |
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
| Every desktop shortcut above | Written from memory of GNOME's and KDE's defaults. | Press each action from the phone on each desktop; report the ones that do nothing, with the desktop and its version. |
| `SetBrightness` on `/org/freedesktop/login1/session/auto` | logind's documentation names `auto` as the caller's session and `SetBrightness` as available to unprivileged programs, but it was not run. | Drag the brightness dial: the panel changes. The log shows a failed call on the system bus once if it is refused. |
| A launched program is reaped | A macro starts its program with `Process.Start` and lets go of the handle, expecting the runtime to collect the child when it exits. | Run a macro for a short-lived program, then `ps -ef \| grep defunct`: nothing is listed. |
| A launched program outlives Edgepad | The child stays in Edgepad's process group and has no new session. | Run a macro for a long-running program, quit Edgepad from a terminal with Ctrl+C: the program may stop with it. |
| MPRIS `Position` and seeking | Read with `GetAll` on every poll, and `SetPosition` needs the player's track id. Players differ in whether they report position and allow seeking. | Play something in Spotify and Firefox: the timeline moves and the media dial seeks. |
| Media and mute keys with no MPRIS player | Pressed as virtual keys, so the desktop decides what receives them. | With a player that has no MPRIS, press play/pause on the phone. |

## Firewall for Wi-Fi and USB connections

Only needed if you turn on **Wi-Fi and USB connections** in the tray, and only if a firewall is running. Edgepad has no helper for this on Linux; run what matches yours as root:

```sh
# ufw
ufw allow 47321/tcp
ufw allow 5353/udp

# firewalld
firewall-cmd --permanent --add-port=47321/tcp --add-port=5353/udp && firewall-cmd --reload
```

47321 is the default; the tray row shows another number when that port was taken. 5353/udp is multicast DNS, which is how a phone finds the laptop. A USB-tethering network is often put in a stricter zone than your Wi-Fi, so allow it in that zone too. None of this has been tried on a real firewall.

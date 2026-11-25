# User guide

Everything about using Edgepad that the [README](../README.md) leaves out: install steps for each platform, connecting, the control surface, security levels, annotate, sign and capture, the Mac, troubleshooting and settings. The full documentation is also at [getedgepad.com/docs](https://getedgepad.com/docs).

## Install

Both apps come from the [latest release](https://github.com/akshit-bansal11/edgepad/releases/latest), under **Assets**. Each file is named for its release, so `Edgepad-5.0.0.exe` and `Edgepad-5.0.0.apk` are the two halves of 5.0.0; on a Mac the laptop half is `Edgepad-<version>-mac.zip`, from 3.4.0, and on Linux `Edgepad-<version>-linux-x64.tar.gz` (or `-linux-arm64.tar.gz` on an ARM laptop, from 5.0.0), from 4.0.0. Always install both from the same release: the two refuse each other at the handshake when their protocol versions differ, and say so. 3.0.0 moved the protocol from version 3 to version 4, so a 2.x app on either side will not talk to a 3.x one — update both halves together or neither connects. 4.0.0 moved it to version 5, and 5.0.0 moves it to version 6, so a 4.x app will not talk to a 5.x one either. Releases after 3.1.1 also carry `SHA256SUMS.txt` (signed from 5.0.0, see [UPDATES.md](UPDATES.md)), and `gh attestation verify Edgepad-<version>.apk --repo akshit-bansal11/edgepad` checks that a file was built by this repository's release workflow.

### Laptop (Windows 10 version 2004 or later, 64-bit)

1. Download `Edgepad-<version>.exe`. It is a single self-contained file; nothing else needs installing.
2. Run it. It is not code-signed, so SmartScreen asks first: choose **More info**, then **Run anyway**.
3. It lives in the system tray, and a left or a right click on its icon opens the same menu. The menu shows whether a phone is connected (**Waiting for your phone**, or **Connected to NAME · Bluetooth**), then **Update available: VERSION** when a newer release exists, a row for whatever needs doing (**Open Bluetooth settings** while no phone is trusted, a Wi-Fi or USB problem, **Allow through Windows Firewall…** while Wi-Fi and USB connections are on), then **Wi-Fi and USB connections**, **Share clipboard**, **Clipboard history**, **Send a file**, **Start with Windows**, **Check for updates daily**, **Macros…**, **Security…**, **Integrations…**, **Restart as administrator**, **Documentation**, **Open log** and **Quit Edgepad**. Forgetting a trusted phone is in **Security…**, per phone, with **Forget all…**. It follows Windows' light or dark app mode.

Running a newer release's exe asks the running copy to quit and takes its place. Running the same version again shows "Edgepad is already running. Find it in the taskbar corner." and exits. **Share clipboard** starts off; the first run asks about it together with **Let phones find this laptop?**, and offers **Start with Windows**, unticked (existing installs keep their value). From 3.3.0 the tray app asks GitHub once a day whether a newer release exists (**Check for updates daily** switches it off), and shows an **Update available** row at the top of its menu when one does.

**Optional, for the gamepad: ViGEmBus.** The gamepad works without it — every control can be bound to a keyboard key, and keys are what the pad sends on a laptop with no controller driver, which is the state most laptops are in. Install [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases/latest) and Edgepad plugs a virtual Xbox controller into Windows instead, so the sticks and triggers carry their full analog range and a game that only ever accepted a controller can be played from the phone. It is a third-party kernel driver, signed as Windows requires of one, and you install it yourself; Edgepad neither bundles nor installs it. You never have to guess which of the two you are in: the laptop answers every request for the controller, and the gamepad screen says **keyboard mode** and which of the three reasons it was, rather than being quietly dead. If you install ViGEmBus while a phone is connected, the gamepad screen finds it the next time that screen opens; no reconnect is needed.

### Laptop (Mac, macOS 13 or later)

One download covers Apple Silicon and Intel. The Mac app was compiled and unit-tested in CI before its first run on a real Mac, so expect rough edges and report them.

1. Download `Edgepad-<version>-mac.zip` and unzip it. It holds `Edgepad.app`; move that to Applications.
2. Open it. It is ad hoc signed and not notarised, because the project has no Apple Developer ID, so macOS refuses an app it does not recognise. Open **System Settings > Privacy & Security** and choose **Open Anyway** for Edgepad.
3. It lives in the menu bar, at the right; hold Command and drag to reorder its icon. The first launch shows a welcome alert that says what macOS is about to ask for, then it asks for **Bluetooth**, to listen for the paired phone, and for input control (**System Settings > Privacy & Security > Device Control and Data Access**, named Accessibility before macOS 27). Without it macOS silently discards every pointer move and key press the app sends, and the menu shows **Allow Edgepad to control this Mac…** until it is granted. On macOS 27 the system never lists the app on its own: in that pane click **+**, add Edgepad, and switch it on (including its Events switch where one is shown). If input still does nothing after you grant the permission, quit Edgepad from the menu bar and open it again.
4. The menu shows whether a phone is connected, then **Update available: VERSION** when a newer release exists, the **Allow Edgepad to control this Mac…** row while input is not granted, then **Wi-Fi connections** (there is no USB on a Mac), **Share clipboard**, **Start at login**, **Check for updates daily**, **Macros…**, **Security…**, **Documentation**, **Open log**, **About Edgepad** and **Quit Edgepad**. Forgetting a trusted phone is in **Security…**, per phone, with **Forget all**. **Share clipboard** starts off and the first run asks about it and offers **Start at login**, unticked. Opening the same version a second time shows "Edgepad is already running." and exits; a different version takes over. The app asks GitHub once at start and once a day whether a newer release exists, and downloads nothing.

Because the signature is ad hoc, macOS treats every new version as a different app, so the Device Control permission has to be granted again after each update: remove the old Edgepad entry in the list, then add the new one.

### Laptop (Linux, 64-bit)

**Still in testing; use it at your own risk.** [docs/LINUX.md](LINUX.md) lists everything that has not been seen working.

1. Download `Edgepad-<version>-linux-x64.tar.gz`, or `Edgepad-<version>-linux-arm64.tar.gz` on an ARM laptop, and check it against `SHA256SUMS.txt`. The arm64 build has never run on a device.
2. Extract it and run the installer as root:

   ```sh
   tar xzf Edgepad-<version>-linux-x64.tar.gz   # or -linux-arm64.tar.gz
   cd edgepad-<version>
   sudo ./install.sh            # or: sudo ./install.sh /opt/edgepad  to install under another prefix
   ```

3. **Log out and back in once**, so your session gets access to `/dev/uinput`. Then start Edgepad from the launcher, or run `edgepad`. Turn on **Start at login** in its menu to have it ready after every boot.

The tray menu follows the same order as the other laptops: status, **Update available: VERSION**, **Wi-Fi and USB connections**, **Share clipboard**, **Start at login**, **Check for updates daily**, **Macros…**, **Security…**, **Documentation**, **Open log**, **Quit Edgepad**. Starting the same version twice shows a notification and exits; a different version takes over.

`install.sh` only copies files and loads a module; it downloads nothing, and it ends by printing the next step. It puts `edgepad` in `<prefix>/bin` (the prefix defaults to `/usr/local`), the launcher entry in `<prefix>/share/applications`, the icon in `<prefix>/share/icons/hicolor/scalable/apps`, a udev rule in `/etc/udev/rules.d/60-edgepad-uinput.rules` and `uinput` in `/etc/modules-load.d/edgepad.conf`, then runs `modprobe uinput` and reloads udev. Uninstall with `sudo ./install.sh --uninstall` (same prefix, if you gave one); your settings in `~/.config/edgepad` stay.

The app is a self-contained single file, so .NET need not be installed. It does need:

- **BlueZ** (`bluetoothd`) for the Bluetooth link.
- **PipeWire's `wpctl`, or `pactl`**, for the volume dial.
- **systemd-logind** (`loginctl`) for Lock and for brightness, and **`xdg-open`** (`xdg-utils`) for macros that open a file, folder or web address and for the tray's Open log and Documentation.
- Optional: **`wl-copy`** (`wl-clipboard`) on Wayland or **`xclip`** on X11, for typing text that is not plain ASCII.
- **GNOME needs the AppIndicator extension** to show the tray icon. Without a tray host Edgepad opens a window with the same switches, **Macros…**, **Security…** and **Quit Edgepad**; closing it minimises it, and the app keeps serving phones.

On Wayland, the layer annotate draws on runs under XWayland and the compositor may ignore its always-on-top request, so annotate is best effort: Edgepad offers it to the phone only when it can work, and the phone hides the Annotate tile otherwise. The shortcuts behind task view, show desktop, search, notifications and switching desktops differ per desktop; the GNOME and KDE tables were written from memory and are unverified, and on any other desktop those actions do nothing. Macros are edited under **Macros…** in the tray menu.

### Phone (Android 12 or later)

1. Download `Edgepad-<version>.apk` on the phone and open it. Allow installing from this source if asked.
2. On first run, allow the **Nearby devices** permission. Edgepad uses it to see the laptops already paired over Bluetooth and, if you turn Wi-Fi and USB on, to find laptops on your network.
3. Every later release installs over the previous one; settings are kept. From 3.3.0 the app fetches them itself: **Settings › About and updates** says when a newer release exists, and a tap downloads and installs it. Update the laptop app to the same release.

## Use

1. Pair the phone with the laptop once, in Windows **Settings > Bluetooth & devices**, macOS **System Settings > Bluetooth** or your Linux desktop's Bluetooth settings, and confirm the code on both sides.
2. Keep Edgepad running in the laptop's tray, or on a Mac in the menu bar. On Linux the tray needs a desktop that shows StatusNotifier icons; GNOME needs the AppIndicator extension.
3. Open Edgepad on the phone and tap the laptop. The first time, the laptop asks **Trust this phone?**; choose **Trust** there (no answer within a minute counts as no). Every paired phone is asked about once, and each one you trust is remembered; the laptop's **Security…** window lists them, each with **Forget**, and has **Forget all…**. On the phone, a laptop's **Forget** removes it from the list and deletes its pairing code; Android's own Bluetooth pairing stays, so remove that in Bluetooth settings. One phone drives the laptop at a time: a trusted phone that connects takes over from the connected one, and the phone it replaced is refused for 30 seconds, so the two do not take the laptop back and forth. After that it can reconnect with Retry.
4. The phone remembers the laptop and reconnects when the app opens. **Disconnect** on Devices stops that until you tap the laptop again. If the link drops, the phone retries up to ten times, two seconds apart, and says so; **Stop trying** ends it early. With Stay connected on it keeps retrying while Edgepad is minimized, with a "Reconnecting to NAME…" notification. The phone stays connected to every laptop you connect, and you choose which one you control from Settings › Devices & connection or the Switch laptop dial.
5. **Share clipboard** and **Stay connected** start off. The phone asks about both on one screen after the first successful connection, and each laptop asks about the clipboard on its first run; an install that already chose keeps its choice. With Stay connected on, a notification keeps the link while Edgepad is minimized, with a Disconnect button.

### Wi-Fi and USB connections

Bluetooth stays the default. On Windows and Linux the tray **Wi-Fi and USB connections** switch, and on the Mac the menu-bar **Wi-Fi connections** switch (off until you turn it on; the first run asks "Let phones find this laptop?") makes the laptop listen on port 47321, or a free port the menu shows, and announce itself on the local network. A phone pairs once: it shows a pairing code and you type it into the window the laptop opens, one box per digit (paste fills them all); after that it reconnects without asking. On Windows, **Allow through Windows Firewall…** adds the inbound rule; on Linux a firewall may need `ufw allow 47321/tcp` and `ufw allow 5353/udp`. USB tethering is not supported on a Mac. The phone's half of this, finding the laptop from a phone and USB tethering have not been tried on real devices yet.

### The control surface

| Where | Touch | Laptop does |
| --- | --- | --- |
| A corner ruler | slide | turns that dial: clockwise raises |
| A corner ruler | tap | the dial's action: mute, play/pause, task view, reset zoom |
| Anywhere else, one finger | move / tap / hold still until it ticks, then move | pointer / left click / drag |
| Anywhere else, two fingers | hold still until the line turns solid, then draw | what that shape is bound to: an action, a macro, or the pad's own focus or lock |
| Two fingers | drag | scroll both axes |
| Two fingers | pinch / tap | zoom / right click, or what Settings assigns |
| Three or four fingers | tap, swipe up, down, left, right | what Settings assigns |
| Top centre | tap one of the four buttons | Settings, Type text, the lock, Controls (keyboard, gamepad, macros, annotate, capture) |
| Back | | leaves the surface; the link stays up |

Out of the box, three fingers left and right switch desktops, three up opens task view, three down shows the desktop, four fingers left and right walk the app switcher (Alt stays held while the fingers are down), and tapping searches or opens notifications. Every one of those is changed under **Surface settings › Trackpad**.

The lock in the middle of that row is the one button that changes the surface instead of opening a screen. One tap hides the dials and the media and leaves the trackpad; a second tap straight after locks the trackpad instead and puts the dials and the media back, so the phone can sit in a pocket or under a palm and answer only its rulers. Any later tap returns the whole surface. The mode is not stored: a phone that came back up silently locked would read as broken.

### Security levels and the PIN

Set in the laptop's tray or menu-bar menu under **Security…**, never from the phone. The level shows by name there: Trust only, PIN once per phone, PIN on every connection.

1. **Level 1, the default.** You say yes to a phone once, as above.
2. **Level 2.** Each phone also enters a six-digit PIN the first time it connects.
3. **Level 3.** The phone enters the PIN on every connection.

The phone asks on its own keypad, so no keyboard app sees the digits; it never stores the PIN, and the screen cannot be captured. A wrong answer shows the attempts left; after five wrong answers the laptop refuses that phone for 30 seconds, doubling with each further five, up to an hour. The counters are kept in memory, so quitting the laptop app resets them. Changing the PIN or the level makes every phone enter the PIN again.

**A forgotten PIN can only be reset on the laptop**, with **Reset PIN and forget phones…**. That removes the PIN, goes back to level 1 and forgets every trusted phone, so each one is asked **Trust this phone?** again. A six-digit PIN is only a million values, so what stops guessing is the lock-out; it does not protect against someone who can copy the laptop's settings file. The laptop stores only a salted hash of it.

### Controls, annotate, sign and capture

**Controls** on the surface opens a popup with the heading **Controls** and, beside it, the edit pencil. Its top row is Controls, Type, Lock, Settings. The tiles are Keyboard, Gamepad, Macros, Annotate, Capture, Clipboard, Presenter, Send a file, Stats and, with Developer mode on (Settings › Developer) and a tool connected on the laptop, Developer, which holds the AI and Git pages. Under them are chips: Edit, Hide dials and media, Black out when idle, Air mouse, Floating dials and Sleep when this ends. A laptop shows only what it can do, so a phone shows no Annotate tile when the laptop does not offer it. Each one is described below.

#### Annotate and sign

**Annotate** draws on the laptop's primary display, with the phone as an absolute pen. **Sign** is the same pad without the overlay; **Save signature** copies a transparent PNG to the clipboard and saves it as `signature-yyyyMMdd-HHmmss.png` in an `Edgepad` folder, never overwriting an earlier one:

| | Saved in | Notes |
| --- | --- | --- |
| Windows | `Pictures\Edgepad` | |
| Mac | `~/Pictures/Edgepad` | The clipboard gets the PNG and a TIFF. |
| Linux | `Pictures/Edgepad` (your `XDG_PICTURES_DIR`) | Annotate is best effort and in testing; see [docs/LINUX.md](LINUX.md). |

#### Capture

Capture presses the laptop's own screenshot shortcut; the phone has a trackpad under the buttons to drag out the region. **Save to: Laptop | Phone** chooses where the file ends up: with Phone, the laptop sends what its own tool just saved and deletes its copy only once the phone has saved it (Pictures/Edgepad or Movies/Edgepad) with a matching SHA-256. Images over 50 MB and videos over 200 MB stay on the laptop. Unverified on real laptops.

| | Region | Whole screen | Recording |
| --- | --- | --- | --- |
| Windows | Win+Shift+S | PrtScn | Win+Shift+R (needs a recent Snipping Tool; unverified) |
| Mac | Cmd+Shift+4 | Cmd+Shift+3 | Cmd+Shift+5 |
| Linux | Print | Print | Ctrl+Shift+Alt+R on GNOME, Meta+Alt+R on KDE (unverified) |

#### Send files

On the phone, **Controls › Send a file** picks one or several files, and the Android share sheet takes several. On the laptop, use the tray or menu-bar **Send a file** row (it picks several on Windows and Linux), Windows' Send to menu (several files), a file dropped on the Mac app, or on Linux `edgepad --send FILE` and the Send to phone desktop entry. Files go one after another, up to 2 GB each. The name is made safe, the SHA-256 is checked, an interrupted transfer resumes, and one file moves at a time per direction. Over Wi-Fi or USB the bytes use a second socket that can only attach to a phone that is already connected. It needs a connected laptop.

The phone shows a card with the file name, "2 of 5", a progress bar with percent and speed, and **Cancel**. At the end it says "Sent N files", or which files failed and why, with **Try again**, which re-sends only the failed ones. Windows shows the same as a toast near the tray; the Mac and Linux transfer cards have pause, stop and close. A file that fails does not stop the rest; **Cancel** or a lost link does.

#### Clipboard history

**Controls › Clipboard** lists the last copies from the laptop and from this phone, each with where it came from and how long ago; tap one to send it again. An empty list says **Nothing copied yet**. It records copies only while **Share clipboard** is on, and keeps text only, in memory only. On Windows, **Win+Alt+V** opens the same history as a panel: click or Enter pastes into the app you were in, Ctrl+Enter sends the item to the phone, and Esc closes it. The panel is also in the tray menu as **Clipboard history**. If another app owns Win+Alt+V, the tray row still opens the panel.

#### Presenter and laser pointer

**Controls › Presenter** always shows the laser pad, with Next slide and Previous slide as tall columns at the sides in landscape and a row below in portrait. The volume keys change slides only while this screen is open. The **Laser pointer** draws a bright red dot with a glow on the laptop (Windows, Mac and Linux) and leaves nothing behind; if the link drops, the laptop clears the dot. **Laser size** in Presenter settings has three steps, and a short red tail follows when **Show trail** is on.

#### Air mouse and Floating dials

**Controls › Air mouse**: tilt the phone to move the pointer while a finger rests on the trackpad. It works in any grip (flat, held like a remote, or upright) and stops the moment the finger lifts. The gyroscope is read only then. While it is on, a held finger does not drag; taps still click. With Air mouse on, the trackpad screen shows only the pad, a small "Air mouse · Off" pill at the top (tap it to turn Air mouse off and bring the dials, top row and media back) and two drag buttons, **Left** and **Right**: along the bottom edge upright, or down the right edge sideways. To drag a window, file or icon, hold one button and tilt, using the same thumb; lift it to drop. Resting a finger anywhere else on the pad only aims.

**Controls › Floating dials** shows the dials over other apps. It asks for Android's "draw over other apps" permission and needs a connected laptop and **Stay connected**.

#### Sleep when this ends

In the Controls popup while something plays: **Sleep the laptop** or **Shut the laptop down** when it ends, **Cancel** to disarm. It needs a media end time. Shutdown shows a 60 second cancellable countdown on the laptop. Windows, Mac and Linux.

#### Stats

**Controls › Stats** has two pages, System and Network, each with a device dropdown listing your laptops and this phone. System shows device info, processor, graphics card, memory, every disk, and the battery. Network shows current download and upload, ping and a speed test against Cloudflare, which runs only when you tap it. Turn on **System stats** in the laptop's **Integrations…** window. Readings run only while the page or a widget is on show, and a reading a laptop cannot give shows a dash. No page shows a Wi-Fi name.

#### Trackpad widgets

**Surface settings › Widgets** puts a clock, weather, system and network (and AI usage, once connected) behind the trackpad. They never take a touch. Weather uses Open-Meteo with a city you type on the laptop, and nothing else.

#### App profiles

**Settings › App profiles › Switch dials and gestures by app.** The laptop tells the phone which app is in front, only while this is on, and the name is not stored on the laptop. Off by default. Windows, Mac and Linux on X11; not on a Linux Wayland session.

#### Laptop notifications

**Settings › Show laptop notifications** on the phone, and the **Forward notifications to phone** row in the Linux tray. Off until both sides turn it on, sent only while connected, and never logged. Linux only: Windows waits for a check of its notification listener, and macOS has no public way to do it.

#### Quick Settings tile

Add **Edgepad** from your phone's Quick Settings edit screen. The stay-connected notification has play/pause and volume buttons.

#### Edit Controls

Tap **Edit**, the first chip under the tiles in the Controls popup. Tap a tile to hide it or show it again; hidden tiles are dimmed and say **Hidden**. Hold and drag a tile to move it. Tap **Done** when finished. If every tile is hidden, the popup says so and **Edit** brings them back.

#### For developers: AI screen and Git screen

Optional, off until you connect something in the laptop's **Integrations…** window (tray, or the menu-bar icon on a Mac). Their tiles appear in Controls only after that. Claude Code, Codex, Gemini CLI and OpenCode sessions are supported; usage limits are shown for Claude Code and Codex. Approval requests can be answered from a sheet over any screen, with the full command and Deny and Allow; no answer in 30 seconds, or any failure, leaves the question in the terminal. The Git page is read-only and lists repositories, issues and pull requests from GitHub, GitLab, Gitea, Forgejo, Codeberg, Bitbucket and Gitee. The sign-in is stored encrypted on the laptop and never sent to the phone. The step-by-step connect instructions for each tool and host are in the [README](../README.md#optional-extras).

#### What Edgepad uses

The phone links this page on first open and from **Settings › About and updates**. It lists every permission the apps ask for and every network destination, with what each is for; the same content is in [DATA-AND-PERMISSIONS.md](DATA-AND-PERMISSIONS.md).

#### Update in place

The phone's **Settings › About and updates** offers **Later** and **Skip this version**, and posts a notice when a newer release exists. Windows, Mac and Linux check the signed list of file hashes before replacing themselves; with no signing key in the build they open the release page instead. Details: [UPDATES.md](UPDATES.md). Phone and laptop must both be 5.0.0: a 5.0 phone and a 4.x laptop refuse each other at the handshake. On Windows the tray also has **Restart as administrator**, so input reaches elevated windows such as Task Manager.

### On a Mac

Pointer, click, drag, scroll, typing, the on-screen keyboard, the corner dials for volume, brightness and refresh rate, play/pause, next, previous and lock all work as on Windows. What differs is by design:

- **The keyboard maps by position.** The wire still carries Windows virtual-key codes, so Win is Command, Ctrl is Control and Alt is Option. Copy from the phone's keyboard is Win+C, not Ctrl+C. The Menu key has no Mac equivalent and is dropped.
- **Actions are macOS's default shortcuts.** If one was changed or switched off in **System Settings > Keyboard > Keyboard Shortcuts**, the gesture does whatever that shortcut now does, or nothing.

| Action | On a Mac |
| --- | --- |
| Task view | Mission Control (Control+Up) |
| Show desktop | F11 |
| Search | Spotlight (Command+Space) |
| Desktop left / right | Control+Left / Control+Right |
| App switcher | Command held, Tab |
| Zoom | Command+plus / Command+minus per notch |
| Zoom reset | Command+0 |
| Lock | Control+Command+Q |
| Notifications | nothing; macOS has no default shortcut |

- **Mute, volume and media keys go out as the Mac's own media keys**, so macOS shows its own readout. A level set from a dial goes through Core Audio and shows none.
- **Brightness** uses a private macOS framework, DisplayServices, loaded at run time. Where it is missing or there is no built-in display, the brightness dial has nothing to show and brightness up and down fall back to the brightness keys.
- **Levels changed on the Mac itself** reach the phone within half a second: the app polls twice a second rather than subscribing.
- **Trust.** Windows binds its socket so the link layer refuses unpaired devices. The Mac app checks that the phone is paired and explicitly authenticates its Bluetooth connection before starting a session. The socket settings the phone asks for are not a protection, since an attacker chooses their own socket. The laptop-side gates are that the phone is paired, that the link authenticates (on a Mac `requestAuthentication()` succeeds and the log says how long it took; on Windows the socket's `BluetoothEncryptionWithAuthentication` protection level), and the owner's yes. Every phone you trust has full keyboard-level control, so trust only your own. On both, **Forget** and **Forget all…** in the Security window hang up on the connected phone and takes no connection while it asks, nor for 30 seconds after a yes. The Mac's trust question shows the phone's own name in quotes beside the address, which is the part being trusted.

**Macros** are edited under **Macros…** in the menu bar: add, edit, remove, reorder, browse for an app or file, and an optional icon. Files, folders and web addresses are opened; for an `.app`, arguments are split on spaces with no quoting.

**Not in the Mac app yet:** now playing outside Music and Spotify, the on-screen level readout, and the virtual game controller (the gamepad screen says **keyboard mode**, because there is no driver; controls bound to keys work).

### When the laptop ignores the phone

The laptop app logs to `%LOCALAPPDATA%\Edgepad\edgepad.log` (also in the tray menu): connections, refusals, dropped frames, and input batches Windows refused because an elevated window had focus.

On a Mac the log is `~/Library/Logs/Edgepad/edgepad.log` (also **Open log** in the menu). The usual cause of a connected phone that moves nothing is that input control (**Device Control and Data Access**) has not been granted, or has to be granted again after an update: see the Mac install steps above.

If the phone says the laptop hung up before the handshake (an older phone app shows `read failed, socket might closed or timeout, RET:-1`) and the Mac log says `the link is not encrypted (mode 0)`, update the Mac app. The old encryption check can reject an encrypted connection on macOS 27; pairing again does not fix that check.

**Task Manager, and anything run as administrator, ignores the phone** unless Edgepad itself runs as administrator: Windows blocks input from an ordinary program into an elevated one. Quit Edgepad from its tray icon, then right-click the exe › **Run as administrator**. Macros then open programs as administrator too. **The lock screen, UAC prompts and Ctrl+Alt+Del cannot be reached at all:** they run on Windows' secure desktop, which no program can send input to, elevated or not.

### Settings

Settings is in sections, in this order. Each screen also keeps its own gear for what only applies to it.

1. **Surface** (also the surface's gear): Corners and Split corners, Trackpad (the gesture map, pointer and scroll speed, natural scrolling and on-screen hints, which are off by default), Shapes (every stroke you have drawn, with what it runs), Dial feel (slide sensitivity, dial length and height, haptic ticks, snapping to round numbers, with a live preview), Media layout, Control colour, Widgets and Look (a colour, gradient or image behind the surface, and a grid, dots or checker over it).
2. **Keyboard**: its own gear. Size picks one of six fixed boards (100, 96, 80, 75, 65 and 60%; 75% by default), Function keys picks "With Fn" or "Direct", and Keyboard text size sets the key labels. The keys themselves cannot be moved.
3. **Gamepad**: the same kind of canvas.
4. **Macros**.
5. **Presenter**: Laser size and Show trail.
6. **Capture & Annotate**: the pen colour, width and opacity (also on the toolbar).
7. **Clipboard & files**.
8. **Developer**.
9. **Devices & connection**.
10. **App appearance**.
11. **Language**: the app's 15 languages on Android 13 and later; older phones follow the system language.
12. **Help**.
13. **About and updates**: updates, and what the phone sends (only the update check and an update you choose), with a link to What Edgepad uses.

## How it works, in more words

The phone recognises gestures and sends small semantic frames: move the pointer by so much, press a button, scroll, run action 3, set control 0 to 55, here is the whole controller. The laptop owns the table of what each action does and executes it with the platform's input, audio, display and media APIs (Windows', or on a Mac CGEvent and Core Audio), so a macro slot can never be repointed from the phone and an id the laptop does not know is dropped rather than misexecuted. That table is a versioning seam, not a wall: the keyboard screen sends raw Windows virtual-key codes and the characters you type, and the laptop injects both directly, so a paired phone can do anything the laptop's own keyboard can. What keeps that safe is the Bluetooth pairing and the trusted phone above — [SECURITY.md](../SECURITY.md) sets out both boundaries in full. The laptop sends its own state back (volume, mute, brightness, what is playing) so the dials show real values.

The default transport is Bluetooth Classic RFCOMM (IOBluetooth on a Mac): an ordered, encrypted byte stream between two already-paired devices, with no server, no discovery and no network. The optional Wi-Fi and USB connections instead listen on port 47321 on the laptop, and a phone pairs once with a six-digit code. Frames are 2 to 258 bytes and go out in a single write per batch; a backlog of pointer moves collapses into one before it is sent, so a slow link catches up instead of lagging.

# What Edgepad uses

Everything Edgepad takes or needs, however big or small, so you know what access you are giving. Nothing here is used until a feature needs it. Wi-Fi network names, your location, contacts, photos and microphone are never asked for. The same text is on the phone: Settings, About, What Edgepad uses (and a link on the first screen of the first run). A test fails when the phone's manifest declares a permission this list lacks.

## Permissions on this phone

| Permission | What it is for | Asked? |
|---|---|---|
| `BLUETOOTH_CONNECT` | Connect to a laptop you already paired in Android's Bluetooth settings. Never scans for or reads other Bluetooth devices. | Yes, the first time you open Devices |
| `ACCESS_LOCAL_NETWORK` | Find laptops on your own Wi-Fi and talk to them. | Yes, before the first Wi-Fi search |
| `ACCESS_NETWORK_STATE` | Tell Wi-Fi, USB tethering and mobile data apart, to find laptops and show your connection. | No |
| `ACCESS_WIFI_STATE` | This phone's own Wi-Fi link speed, band and signal for the Network page. Never the network name, which would need Location. | No |
| `CHANGE_WIFI_MULTICAST_STATE` | Needed to find laptops on the Wi-Fi; Android also accepts it as a reason to let the connection service run in the background. | No |
| `INTERNET` | Carries the link to your laptop over Wi-Fi or USB. Edgepad itself uses the internet only to ask github.com for the newest version and to download an update you chose. | No |
| `REQUEST_INSTALL_PACKAGES` | Install an Edgepad update you chose to download. | Yes, the first time: allow installs from Edgepad |
| `UPDATE_PACKAGES_WITHOUT_USER_ACTION` | Android 12 and later: install that update without a second question. Edgepad only. | No |
| `FOREGROUND_SERVICE` | Keep the link alive while Edgepad is minimized, with a notification (Stay connected in Settings). | No |
| `FOREGROUND_SERVICE_CONNECTED_DEVICE` | Marks that service as one that keeps a connection to a device, your laptop. | No |
| `POST_NOTIFICATIONS` | The connection notice, file transfers, AI permission requests and, if you turn it on, your laptop's notifications. | Yes, once, Android 13 and later, after the introduction |
| `SYSTEM_ALERT_WINDOW` | Only for the floating dials you switch on yourself. | Android sends you to its settings page; Edgepad never asks otherwise |

## What goes between phone and laptop

From this phone to the laptop:

- **Your input.** Touches, keys, text you type, gamepad and pen strokes, and buttons such as volume and brightness. Only while a laptop is connected.
- **Clipboard.** Text or an image you copy, only if you turn on Share clipboard, and only when Edgepad is on screen, as Android allows.
- **Files and shares.** A file, text or image you send on purpose, or share to Edgepad. Sent after you tap Send.
- **Which pages are open.** Which dashboard pages the phone shows, so the laptop reads CPU, weather, AI or Git data only while one is on screen.
- **Your answers.** The PIN you type, and Approve or Deny for an AI tool's permission request.
- **Your switches.** Notifications, Developer mode, where captures are saved, pen size and colour, and whether to report the app in front.

From the laptop to this phone:

- **What the laptop is doing.** What is playing and in which app, volume, brightness, display size and refresh rates, the laptop's name and system, the battery of the laptop and of its Bluetooth devices, and the name of the audio output.
- **Macros and apps.** Macro names and icons, and a list of apps you can add as a macro. Never the paths or commands behind them.
- **Captures and files.** A screenshot or recording only if you chose Save to Phone, and files you send from the laptop.
- **Clipboard and notifications.** Copied text or an image if clipboard sharing is on, and the laptop's notifications (app, title, short text) if both notification switches are on. Never logged.
- **Dashboards.** While a dashboard page is open: computer load and disk use, weather, AI session names and states, AI usage and limits, and the titles of Git pull requests and issues.
- **Network page.** The laptop's connection type, band, link speed, signal, local address and gateway, and the result of a speed test. Never a Wi-Fi name.

Stays on this phone: the battery, memory, storage and connection figures on the System page are read on the phone, only while that page is open, and are never sent anywhere.

The wire format is in [PROTOCOL.md](PROTOCOL.md).

## What reaches the internet

A search of the phone, Windows, Mac and Linux source for web addresses found these hosts and no others (links in comments and documentation excepted).

| Host | From | When | What |
|---|---|---|---|
| `github.com` | phone and laptop apps | The update check (once a day unless you turn it off) and an update you choose to download | One request for the newest version; nothing about your device |
| `speed.cloudflare.com` | laptop | Only when you tap Test speed | Test data; nothing about you |
| `api.open-meteo.com`, `geocoding-api.open-meteo.com` | laptop | Only if the weather widget is on | The place you chose, to find its coordinates, then the forecast request |
| `github.com`, `api.github.com` | laptop | Only after you sign in to GitHub there | Sign-in and renewing the sign-in (the read-only GitHub App "Edgepad Dashboard"; no client secret, no notifications), then read-only requests. It sees only the repositories you install it on; **Choose repos on GitHub** opens that page in your browser |
| `gitlab.com`, or the GitLab server you enter | laptop | Only after you sign in to GitLab there | On gitlab.com: the device sign-in and renewing it (the "Edgepad" application, scopes `read_api` and `read_user` only, no client secret), then read-only requests. On a server you enter: a `read_api` token you paste, and read-only requests only; Edgepad never sends a sign-in request to your server. It never writes. |
| `codeberg.org`, or the Gitea or Forgejo server you enter | laptop | Only after you paste a token | Read-only requests |
| `api.bitbucket.org` | laptop | Only after you paste a token | Read-only requests |
| `gitee.com` | laptop | Only after you paste a token | Read-only requests |
| `getedgepad.com`, and the ViGEmBus page on `github.com` | your browser | Only when you tap a link | Nothing from Edgepad |

Edgepad never writes to a Git host: a built-in gate refuses every request that is not a read. There is no account, no analytics, no crash reporting and no advertising.

## On your laptop

**Windows**

- *Restart as administrator*: an optional tray row, behind Windows' own prompt, so Edgepad can type into windows that run as administrator.
- *Firewall rule*: one inbound allow rule for `Edgepad.exe`, so phones can connect over Wi-Fi or USB, added behind Windows' prompt. A no is not asked again at the next start.
- *Start at sign-in*: a per-user entry, only when you turn it on in the tray menu. No administrator rights.
- *Send to*: a shortcut in your own Send to folder.
- *Notification access*: only if you forward notifications; Windows asks.
- *Virtual controller*: the gamepad needs the ViGEmBus driver, which you install yourself. Edgepad only opens its download page.
- Sign-in tokens are kept encrypted for your Windows account.

**Mac**

- *Accessibility*: to type, click and press keys for the phone. macOS asks.
- *Bluetooth*: to listen for the phone you paired.
- *Local Network*: so the phone can find the Mac over Wi-Fi, when you turn on Wi-Fi connections.
- *Automation*: to ask Music or Spotify what is playing and to move it, and to sleep or shut down when you set that at the end of the media.
- *Files and folders*: Desktop, Documents and Downloads, to find a screenshot or recording you asked to receive on the phone.
- *Notifications and login item*: alerts for a phone asking to pair and for a new version; Start at login is a normal login item. Sign-in tokens go in your Keychain.
- Not requested: Input Monitoring and Screen Recording. Captures use macOS's own shortcuts.

**Linux**

- *Typing device*: `/dev/uinput`, opened for the person at the keyboard through a udev rule and a module setting that the installer adds once, as administrator.
- *Bluetooth*: BlueZ, on the system bus, to listen for the phone you paired.
- *Desktop services* on the session bus: notifications, media players (MPRIS), power (UPower), sleep and lock (login1), NetworkManager for the Network page, the desktop's dark-mode setting, display refresh rates, the tray, and the Secret Service for sign-in tokens.
- *Start at login*: an autostart entry in your own config folder, when you turn it on.

**AI tools, on any laptop.** Only with Developer mode on. Edgepad reads the local session and usage files of Claude Code, Codex, Gemini CLI and OpenCode, and connecting one adds a small hook or plug-in to that tool's own settings. Git tokens are kept encrypted on the laptop, in its own credential store, and are never sent to the phone.

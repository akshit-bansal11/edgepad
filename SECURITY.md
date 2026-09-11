# Security

Edgepad lets a phone drive a laptop, so its boundaries matter. This is what they are and how to report a hole in one.

## The model

- **Only paired devices can connect.** The laptop binds its Bluetooth socket with encryption and authentication, so the link layer refuses anything not already paired with the laptop through Windows.
- **Only one phone is trusted.** The first phone to complete the handshake is remembered by address; every other paired phone is refused until you choose **Forget trusted phone** in the tray menu.
- **The phone cannot send arbitrary input.** It sends pointer movement, buttons, scroll, zoom, text to type, and ids from a fixed table of actions and controls. The laptop owns that table. There is no frame for a key code, a scan code or a command.
- **Windows keeps its own line.** Injected input never reaches an elevated window, a UAC prompt or the lock screen; the laptop counts those refusals.
- **Nothing leaves the two devices.** No network, no server, no account, no telemetry. The laptop writes a local log of connections and errors and nothing else.
- **No secrets in the repository.** The APK signing key exists only in GitHub Actions secrets and on the machine that created it.

## Known limits

- The trusted phone's Bluetooth address is stored in plain text under `%APPDATA%\Edgepad`. Anyone who can write that file can change which phone is trusted; that person can already run programs as you.
- The laptop app is not code-signed. SmartScreen warns on first run. Verify the download came from this repository's Releases page.
- Text typed from the phone goes wherever the laptop's focus is, like a keyboard would.

## Reporting

Open a [security advisory](https://github.com/akshit-bansal11/edgepad/security/advisories/new) on GitHub, or email the maintainer if you would rather not use GitHub. Please do not open a public issue for something exploitable. Expect an acknowledgement within a week.

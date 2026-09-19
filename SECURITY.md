# Security

Edgepad lets a phone drive a laptop, so its boundaries matter. This is what they are and how to report a hole in one.

## The model

- **Only paired devices can connect.** The laptop binds its Bluetooth socket with encryption and authentication, so the link layer refuses anything not already paired with the laptop through Windows.
- **Only one phone is trusted.** The first phone to complete the handshake is remembered by address; every other paired phone is refused until you choose **Forget trusted phone** in the tray menu.
- **A paired phone is a trusted input device.** It sends pointer movement, buttons, scroll, zoom, key codes and text to type. The keyboard screen sends raw Windows virtual-key codes (`KEY`) and arbitrary characters (`TEXT` kind 3), which the laptop injects directly, so anything that could be done at the laptop's own keyboard can be done from the phone. The action and control ids are a fixed, laptop-owned table, which keeps the two halves independently versionable and lets an unknown id be dropped rather than misexecuted — it is **not** a containment boundary and should not be read as one. The trust boundary is the pairing plus trust-on-first-use above.
- **Windows keeps its own line.** Injected input never reaches an elevated window, a UAC prompt or the lock screen; the laptop counts those refusals.
- **Nothing leaves the two devices.** No network, no server, no account, no telemetry. The laptop writes a local log of connections and errors and nothing else.
- **No secrets in the repository.** The APK signing key exists only in GitHub Actions secrets and on the machine that created it.

## Known limits

- The trusted phone's Bluetooth address is stored in plain text under `%APPDATA%\Edgepad`. Anyone who can write that file can change which phone is trusted; that person can already run programs as you.
- The laptop app is not code-signed. SmartScreen warns on first run. Verify the download came from this repository's Releases page.
- Text typed from the phone goes wherever the laptop's focus is, like a keyboard would.
- Because the phone can press keys and type, it can reach anything a keyboard can — Win+R and a typed line is already arbitrary execution. Whoever holds the trusted phone has the laptop's keyboard. Use **Forget trusted phone** if you lose it.

## Reporting

Open a [security advisory](https://github.com/akshit-bansal11/edgepad/security/advisories/new) on GitHub, or email the maintainer if you would rather not use GitHub. Please do not open a public issue for something exploitable. Expect an acknowledgement within a week.

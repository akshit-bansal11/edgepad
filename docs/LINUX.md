# Linux app: what is not verified yet

The Linux laptop app (`linux/`) is built and unit-tested on GitHub Actions only. Nobody on the project has run it on a Linux laptop with a phone. Everything below was written from documentation, not seen working. Each item says how to check it. Report results on the issue tracker.

The app logs to `~/.local/share/Edgepad/edgepad.log`. Its settings live in `$XDG_CONFIG_HOME/edgepad/` (normally `~/.config/edgepad/`).

## Until the tray app lands (step 8.5)

There is no trust prompt yet. Every phone that is not already trusted is refused, and the log says so. To test, trust a phone by hand. Pair it in the desktop's Bluetooth settings first. Then put its address, upper case, on a line of its own in `~/.config/edgepad/trusted-phone.txt` (for example `AA:BB:CC:DD:EE:FF`; `bluetoothctl devices` lists it).

Input does nothing yet either: keys, pointer, actions and dials are received and dropped until steps 8.2 to 8.4. What can be checked today is the connection itself: the phone gets through the handshake, shows as connected, and the log names it.

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

# Edgepad for Linux

The Linux tray app: C# on .NET 10, Avalonia for the tray and its windows, BlueZ over D-Bus for Bluetooth and uinput for input. Like the Windows app it is built on [shared/Edgepad.Core](../shared/Edgepad.Core/README.md) and holds only what touches Linux. Setup on a desktop (the uinput rule, BlueZ) is in [docs/LINUX.md](../docs/LINUX.md); the design is under "The Linux app" in [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md#the-linux-app).

| Folder in `src/Edgepad.Linux` | What is in it |
| --- | --- |
| `Program.cs`, `Xdg.cs` | Start-up, and the XDG folders the settings and the instance lock live in. |
| `Bluetooth/` | The RFCOMM profile registered with BlueZ, and the session for one phone. |
| `Input/` | uinput devices for the pointer, keys and the virtual controller, the key map, and typing. |
| `Platform/` | `LinuxPlatform`, what each action does here, and the desktop around it: audio, backlight, MPRIS, logind, the clipboard, `.desktop` entries and icons, macros, captures, notifications, the single instance, the updater and the settings files. |
| `Native/` | libc and X11 calls. |
| `Dashboard/` | System stats from `/proc` and the GPU, the agent hook listener and the keyring sign-ins. |
| `Ui/` | The tray, every window and prompt, the drawing overlay and the signature export. `LinuxStrings.*.resx` hold the Linux-only UI words. |

`packaging/` holds the install script, the desktop entries and the uinput udev rule shipped in the release tarball.

Build and test with `pwsh scripts/check.ps1 -Only linux`. CI builds and tests it on Linux, and publishes an arm64 build too. More in [docs/DEVELOPING.md](../docs/DEVELOPING.md).

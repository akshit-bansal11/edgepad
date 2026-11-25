# Edgepad for Windows

The Windows tray app: C# on .NET 10, WinForms for the tray and its windows, WinRT for Bluetooth and the system media controls. Everything with the same shape on every OS lives in [shared/Edgepad.Core](../shared/Edgepad.Core/README.md); this project holds what touches Windows. The program is one tray icon, one instance per user, with one session per phone on its own thread ([docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)).

| Folder in `src/Edgepad` | What is in it |
| --- | --- |
| `Program.cs`, `TrayContext.cs` | Start-up (hook commands, Send to, the single-instance hand-over), and the tray: menu, status, run at login. |
| `Bluetooth/` | The RFCOMM server, and `Session`: the handshake, the trust check and the read loop for one phone. |
| `Network/` | Wi-Fi and USB: the firewall step, and which exe it last ran for. |
| `Dispatch/` | `WindowsPlatform`, what each action does on Windows, and `SessionHost`, which serves one phone at a time. |
| `Injection/` | `SendInput` for the pointer and keys, typing into Qt windows, and the Win32 declarations. |
| `Controls/` | Volume (Core Audio), brightness (WMI), refresh rate, what is playing and its artwork, batteries, the app in front. |
| `Gamepad/` | The virtual Xbox controller through ViGEmBus. |
| `Macros/` | Launching macros, their icons, the Start menu app list and the macro editor. |
| `Annotation/` | The drawing overlay and the signature export. |
| `Clipboard/`, `Transfer/` | Clipboard sync, and file transfers including Explorer's Send to. |
| `Dashboard/` | System stats (PDH, NVML), the agent hook listener and the saved Git sign-ins. |
| `Trust/`, `Ui/` | The Security window, and every other window, prompt and toast. |
| `Updates/`, `Startup/`, `Notifications/` | The updater and its setting, run at login and the single instance, and the notification access probe. |

`Ui/Strings.*.resx` hold the Windows-only UI words in every language; the shared ones are in the core.

Build and test with `pwsh scripts/check.ps1 -Only windows`, or `dotnet test --solution windows/Edgepad.slnx`. Run it with `dotnet run --project windows/src/Edgepad`. More in [docs/DEVELOPING.md](../docs/DEVELOPING.md).

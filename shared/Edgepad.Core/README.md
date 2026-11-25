# Edgepad.Core

The library the Windows and Linux laptop apps share: everything that has the same shape on every OS and can be tested on a CI runner with nothing attached. Plain `net10.0`, and every type is `internal`, with `InternalsVisibleTo` naming the two apps and the three test projects. The Mac app cannot reference .NET, so `macos/Sources/EdgepadCore` is a Swift port of the same pieces. Why the line sits where it does is in [docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md#the-laptop-apps-and-the-shared-core).

| Folder | What is in it |
| --- | --- |
| `Protocol/` | Frames, the codec, the id enums and the protocol version, checked against `protocol/frames.txt` and `actions.txt`. |
| `Net/` | Reassembling frames from a stream, the handshake, mDNS announcing, the TCP listener, and `Link/`, the encrypted EDGL1 network link. |
| `Trust/` | Trusted and paired phones, the PIN and the pairing lock, and `PrivateStorage`, which keeps those files to their owner. |
| `Dispatch/` | The dispatcher's routing and drop-and-count rules, and `IPlatform`, the per-OS table of what each action does. |
| `Controls/` | `Subscription`, the handle a watch returns, which ends the watch once when disposed. |
| `Macros/` | The macro list, launching and following macros, the app picker, and macro icons in chunks. |
| `Annotation/` | The stroke model that annotation, signing and the laser draw into. |
| `Blob/` | Blob transfers and clipboard sync, with the clipboard history panel's model. |
| `Transfer/` | File transfers over the second socket: send queue, sender, receiver, limits and safe file names. |
| `Capture/` | Finding a capture the OS tool saved, to send it to the phone. |
| `Dashboard/` | The phone's dashboard: system stats, AI agent usage and spend, and the Git hosts with their sign-ins. |
| `Hooks/` | The local channel agent plugins talk to the app over, and the permission broker that lets the phone answer an agent's approval. |
| `Media/`, `Power/`, `Foreground/`, `Notify/` | Cleaning media titles and the end-of-media action, batteries, the app in front, and the laptop notifications forwarded to the phone, with their rate limit. |
| `Updates/` | The update check, the decision, the download and its signature check. |
| `Log.cs`, `Localised.cs` | The log file, and filling localised strings. `CoreStrings.*.resx` hold the shared UI words in every language. |

Build and test it on its own with `pwsh scripts/check.ps1 -Only core`; the tests are in `shared/Edgepad.Core.Tests`.

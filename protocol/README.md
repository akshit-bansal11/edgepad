# protocol

The wire format and every shared table as plain-text fixtures. The Kotlin, C# and Swift test suites all read these files, so the phone and the three laptop apps cannot drift apart without a test failing. The prose behind them is [docs/PROTOCOL.md](../docs/PROTOCOL.md); the website's protocol tables are built from `frames.txt` and `actions.txt` too.

| File | What it pins |
| --- | --- |
| `frames.txt` | Every frame type as fields and golden bytes. Each codec must encode the fields to exactly those bytes and decode them back. |
| `actions.txt` | The action, control and TEXT kind ids, the handshake version and service id, and the other token tables. Each enum must match it exactly. |
| `keys.txt` | The Windows virtual-key codes the phone's keyboard sends. Every laptop must map each one. |
| `link-vectors.txt` | The EDGL1 network link's key derivation and crypto vectors. Written by the core test `LinkVectorsOutput`; do not edit by hand. |
| `dashboard-vectors.txt` | The dashboard's wire encoding, case by case. |
| `hook-vectors.txt` | The local hook channel between agent plugins and the laptop app. Laptop only; it never crosses the phone link. |
| `filenames.txt` | The file-name sanitiser every app holds a received name to, as `input \| expected` pairs. |
| `titles.txt` | How a media title is cleaned before the phone shows it. |
| `update-key.txt` | The public key update downloads are verified against. |

A change to a fixture is a change to every app at once. If it changes the wire format, it also moves the protocol version, which only happens in a major release (see Versions in `docs/PROTOCOL.md`).

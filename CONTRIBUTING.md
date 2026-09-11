# Contributing

Thanks for looking. Edgepad is small on purpose; the bar for a change is that it makes the product better for someone using it, and that it passes the same gate CI runs.

## Before you start

- Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The split between the two apps, and what the phone is and is not allowed to send, are deliberate.
- Open an issue for anything bigger than a fix, so the design can be talked through before code exists.

## Setting up

You need JDK 17 and the Android SDK for the phone app, and the .NET 10 SDK on Windows 10 version 2004 or later for the laptop app. Either half can be worked on alone: `pwsh scripts/check.ps1 -Only android` or `-Only windows` runs just that gate.

## Making a change

1. Branch from `main`.
2. Keep the two apps in step. A protocol change edits `protocol/frames.txt` or `protocol/actions.txt`, both codecs or both enums, and bumps the protocol version on both sides, all in one commit.
3. Add or change a test when you add logic, a branch or fix a bug. The recogniser, the dials and the codecs are pure and cheap to test; prefer testing there over anything that needs a device.
4. Run `pwsh scripts/check.ps1` until it is clean. It formats in place, then lints, builds and tests both halves with warnings as errors. Do not suppress a lint finding to get green.
5. Commit with a [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) subject (`feat:`, `fix:`, `docs:`, `ci:`, with an optional scope such as `feat(android):`) and a body that says what changed and why, including what you rejected.
6. Add a line under **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
7. Open a pull request. CI runs the non-mutating gate on both halves and builds both artifacts.

## Style

- No UI libraries on the phone, no third-party packages on the laptop beyond NAudio and System.Management. A new dependency needs a reason in the pull request.
- Constants are named. A number that appears twice is a constant.
- Doc comments say why, not what the next line already says.
- Nothing runs on the input path that could block: no logging, no allocation while drawing, no I/O on the session thread beyond the socket.

## Releasing

Maintainers only. Update the version table in `docs/PROTOCOL.md` if the protocol changed, move **Unreleased** in the changelog under the new version, then tag and push; the Release workflow does the rest. The signing key is not in the repository and must not be regenerated: a new key would stop installed phones from updating.

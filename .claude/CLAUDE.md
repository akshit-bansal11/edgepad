This project inherits every rule in `~/.claude/CLAUDE.md`. Rules below add to or override it. Nothing here restates it.

Edgepad was a mini project until 2026-09-17 and inherited `F:/projects/mini-projects/CLAUDE.md`. It no longer does. This file carries forward the handful of rules from that one that Edgepad still depends on, and nothing else.

## Notion is the source of truth for everything that is not code

The project page is `https://app.notion.com/p/3d8c6445b19f81b7b3aefee7a7f36dab`, a row in the **Projects** database under **Claude Second Brain**. It holds intent, architecture, decisions, traps already paid for, current state, open threads, next actions and the chat log. **Read it before planning or answering anything about this project.** The repo stores code and nothing else.

Where the two disagree, the repo wins on code and the Notion page wins on everything else. If a non-code fact is not on that page, you do not know it — ask rather than guess.

**This overrides the global router's §9.** Do not create `STATE.md`, `DECISIONS.md`, `DRIFT.md`, `OPEN_ITEMS.md`, `TECH-STACK.md` or `DIRECTORY-STRUCTURE.md` here. A claude.ai web or app chat has no filesystem, so pointing such a session at `STATE.md` gives it nothing. Files a mechanism actually reads from disk stay on disk: `README.md`, `LICENSE`, `.gitignore`, `.github/*`, `CONTRIBUTING.md`, `SECURITY.md`, `CHANGELOG.md`, `docs/*`, and this file.

At the end of a working session, append a dated entry to the page's "Chat log" and refresh "Current state", "Open threads" and "Next actions".

Never write secrets, API keys or credential values into Notion. Variable names only.

## Commits

Conventional Commits, with these trailers:

```
Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: <session url>
```

**No `Ref-ID:` trailer.** Edgepad's mini-project reference ID `yfu0idauhvj2nm9nkp0lcgkd7-zawm4drxxv` was retired on extraction and no id replaced it. Every commit through `1612ee2` still carries it, because history cannot be rewritten; `git log --grep=zawm4drxxv` finds them. Commits after the extraction carry no id.

## Nothing heavy runs locally

There is **no .NET SDK, no JDK, no Android SDK, no Gradle and no adb on this machine**, by choice. Both quality gates and both builds run on GitHub Actions. The owner may be gaming, so ask before starting anything long-running or memory-hungry locally.

Consequence, and budget for it: **the local gate cannot see Android lint or the unit tests.** Lint failures only CI can see are normal here, not a surprise. Where a local check is wanted, the pattern is a portable toolchain fetched into the session scratchpad (a portable .NET SDK via `dotnet-install.ps1 -InstallDir`, a portable Temurin JRE running the ktlint CLI); they die with the session.

## What is the owner's to do, not a session's

Hand over a command; never route around a refusal.

- **Creating the release signing key.** Use `scripts/new-signing-key.ps1`, which the owner runs. Refused twice on 2026-09-11: once with `openssl` into `.secrets/`, once as a throwaway test key in the scratchpad with .NET crypto.
- **Deleting a release or a tag.** `gh release delete <tag> --yes --cleanup-tag` is refused even with explicit authorisation. A tag with no release needs `git push origin --delete <tag>` as well, because `--cleanup-tag` cannot reach it; finish with `git fetch --prune --prune-tags`. This covers a draft release too: a session can create one by hand-running `release.yml` and cannot remove it afterwards.
- **Merging a pull request.** `gh pr merge` is refused as `[Merge Without Review]` — 2026-09-19, even on the owner's explicit instruction to merge.
- **Running `Edgepad.exe`.** The one item here that is not a refusal: it is the owner's standing instruction, 2026-09-11. A stuck copy holds the single-instance mutex and blocks the next one; clear it with `Get-Process Edgepad* | Stop-Process -Force`.

## Releases

Pushing a version tag triggers `.github/workflows/release.yml`, which runs both gates, builds a signed APK and a compressed self-contained exe, and attaches them under the stable names `Edgepad.apk` and `Edgepad.exe` so `releases/latest/download/<file>` always resolves. The wire protocol version moves only in a major release, and both halves of a release must then be installed together. It went to 4 in 3.0.0, for the gamepad's analog frame; before that it had been 3 since 0.6.0.

**No latency number may be written down anywhere until the app's own RTT readout reports one from the real devices.** None has ever been measured.

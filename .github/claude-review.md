# How Claude reviews an Edgepad pull request

Read by `.github/workflows/claude-review.yml`, `claude-issue.yml` and `claude-tldr.yml`. Edgepad has real daily
users: a bug that reaches a release reaches them. Nothing compiles in this job; CI builds and tests every half.
Your job is to catch what CI cannot, and what would make CI fail, before a human has to.

## What the repo is

- `android/`: the phone app, Kotlin, View-based (no Compose). Must stay light on low-end phones.
- `shared/Edgepad.Core/`: the laptop core in C#, used by `windows/` (WinForms) and `linux/` (Avalonia).
- `macos/`: Swift, its own port of the core.
- `protocol/` and `docs/PROTOCOL.md`: the wire format. Golden vectors in `protocol/*.txt` are read by the C#,
  Kotlin and Swift tests; a change to a frame must change all three codecs, the vectors and the doc together.
- `plugins/`: the Claude Code / Codex hooks. `site/`: the website (Next.js).

## How to hunt

A skim of the diff finds typos. The bugs that reach users hide in what the change did not touch. For each
changed function or type, do these before you list anything:

- **Trace it out.** Find every caller and every place that reads what it writes. A new parameter, return value,
  enum case or field is a bug wherever one of those still assumes the old shape.
- **Name the implicit state machine.** Most Edgepad code is one: connected / connecting / lost / reconnecting,
  paired or not, foreground or background, a mode flag, a "busy" boolean, a cached value plus its "loaded" flag.
  Write down its states and which events move it between them. Then ask: which transition did this change add or
  break? Can it reach a state with no way out? Is a flag set on one path and not cleared on the error, cancel or
  timeout path? Can the same event arrive twice, or out of order?
- **Walk every exit.** Each early return, `catch`, `finally`, cancellation and timeout: what is left half-done,
  still subscribed, still locked, or still holding a socket, file, timer or wake lock?
- **Ask "what if it runs twice, or at once?"** Re-entrancy, a callback after the screen or window closed, a
  reply for a request already timed out, two threads or coroutines touching the same field, an Android lifecycle
  restart (rotation, process death) mid-action, a retry that is not idempotent.
- **Feed it the boundaries.** 0, 1, max, negative, empty, null, unknown, very large; time going backwards or a
  clock change; unit mix-ups (ms vs s, px vs dp, bytes vs chars); integer overflow and float rounding; number and
  date formatting in another locale; a protocol frame from an older or newer version.
- **Then check your own findings.** For each one, find the line that proves it and the input that triggers it.
  If you cannot name both, drop it or say plainly it is a suspicion. If you fix something, review the fix the same
  way: a fix that adds a new bug is worse than a reported problem.

## Look for, in order

1. **Bugs**: wrong logic, off-by-one, null/empty input, a value that can be unknown shown as 0, races between
   threads, a resource never closed, a timer or listener never stopped, a state flag not reset on an error path.
2. **Edge cases**: empty lists, very long text, names with `:` `|` `/` or emoji, other languages (15 locales,
   Arabic is right to left), upright and sideways, light and dark, no laptop connected, link lost mid-action,
   a phone without the hardware, Windows without admin, Linux on Wayland and X11, a Mac without a battery.
3. **Things that fail CI** (each has cost a 15-minute round before):
   - Android has no `androidx.annotation` dependency: `@DrawableRes` and friends do not compile.
   - ktlint 1.8.0: once a call chain wraps, every `.call()` goes on its own line, including the first;
     `}.prop` stays on the brace line; every `when` entry braced if any is multiline, blank line between;
     a class body never starts with a blank line; imports sorted with java/javax/kotlin last; 120 columns.
   - Android lint: UnusedResources (a string orphaned in any of the 15 locales), NewApi, Typos.
   - Android strings need `\'` for apostrophes (French and Italian especially).
   - C# analyzers are errors: CA1068, CA1861, CA1822, CA1001, CA1859; nullable on with no `!`; `dotnet format`
     whitespace; CS0136 (a name reused from an outer `catch`); raw strings: `$$"""…}}"""` is CS9007.
   - A Kotlin parameter named `context` inside a View shadows `View.context`.
   - Swift: swift-format 120 columns, `Self.staticMember`, no `lock()`/`Thread.sleep` in async code,
     warnings are errors, test fakes must not reuse names already in Tests/.
   - A code fragment pasted above a file's package/import lines.
   - Dropped backslashes in string literals (`\n`, `\'`, Swift key paths `\.x`).
4. **Missing tests**: new logic, a new branch or a bug fix without a test. Tests deleted or weakened.
5. **Security and privacy**: tokens or secrets logged, sent to the phone, or written outside the secret store;
   input from the phone, a file or the network trusted without a length or format check; a new network
   destination or permission not listed on the "What Edgepad uses" page (`strings_uses.xml`).
6. **Promises**: no latency number written anywhere; no claim in copy that the code does not do.

Skip style opinions that no linter enforces.

## What CI already runs, so you don't

Every PR runs the real tools: `site.yml` runs Biome, ESLint, `tsc` and the Next build; `ci.yml` runs ktlint, Android
lint, `dotnet format`, the analyzers, swift-format and every test suite. Their results gate the merge. So:

- Don't hand-check formatting, whitespace, import order or line length. Keep to the CI traps listed above, which
  are listed because they are easy to miss in a diff; beyond them, leave formatting to the tools.
- Don't spend turns simulating a build or a linter, and don't write "I couldn't run X" or "not verified" lines.
  That nothing runs here is a given. Mention a tool only when you have a specific reason to expect it to fail.

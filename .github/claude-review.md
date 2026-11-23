# How Claude reviews an Edgepad pull request

Read by `.github/workflows/claude-review.yml` and `claude-issue.yml`. Edgepad has real daily users: a bug that
reaches a release reaches them. Nothing compiles in this job; CI builds and tests every half. Your job is to
catch what CI cannot, and what would make CI fail, before a human has to.

## What the repo is

- `android/`: the phone app, Kotlin, View-based (no Compose). Must stay light on low-end phones.
- `shared/Edgepad.Core/`: the laptop core in C#, used by `windows/` (WinForms) and `linux/` (Avalonia).
- `macos/`: Swift, its own port of the core.
- `protocol/` and `docs/PROTOCOL.md`: the wire format. Golden vectors in `protocol/*.txt` are read by the C#,
  Kotlin and Swift tests; a change to a frame must change all three codecs, the vectors and the doc together.
- `plugins/`: the Claude Code / Codex hooks. `site/`: the website (Next.js).

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

# Brand marks

Official marks used to name a product, taken unmodified. Android looks them up through `Logos.drawable(logoId)` in
`android/app/src/main/java/me/akshitbansal/edgepad/Logos.kt`, keyed by `LogoId` in
`shared/Edgepad.Core/Dashboard/DevProviders.cs`. An id without a mark falls back to the letter tile.

## Shipped

| Brand | Source | Usage rule | Repo paths |
| --- | --- | --- | --- |
| GitHub | Octicons `mark-github`, MIT, Copyright (c) 2021 GitHub Inc. (already in repo) | Name GitHub only; no recolour; never as Edgepad's own icon (https://brand.github.com/foundations/logo) | `android/app/src/main/res/drawable/ic_github.xml` |
| GitLab | https://about.gitlab.com/images/press/gitlab-logo-500-rgb.svg | Press-kit logomark; nominative-use terms not read, see https://handbook.gitlab.com/handbook/marketing/brand-and-product-marketing/brand/brand-activation/trademark-guidelines/ | `android/app/src/main/res/drawable/logo_gitlab.xml`, source `assets/brand/logos/gitlab.svg` |
| OpenCode | https://opencode.ai/brand/files/opencode-logo-dark-square.svg and `-light-square.svg` | Light on light, dark on dark; no recolour, gradient, shadow or glow; min 16x20 px; write "OpenCode"; no implied endorsement (https://opencode.ai/brand.md) | `android/app/src/main/res/drawable/logo_opencode.xml` (light theme), `android/app/src/main/res/drawable-night/logo_opencode.xml` (dark theme), sources `assets/brand/logos/opencode-light.svg`, `opencode-dark.svg` |

Laptop apps: no screen draws a provider logo yet and they have no image loader for one, so no laptop asset was added.
`assets/brand/logos/*.svg` are the unmodified sources for when the dev window needs them.

## Owner's to-do

No official download was reachable. Save each file at the path shown, then tell a session to add the `Logos.kt` row.

| Brand | Get it from | Save at |
| --- | --- | --- |
| Claude | marketing@anthropic.com (no public download; see https://www.anthropic.com/legal/trademark-guidelines) | `F:\projects\edgepad\android\app\src\main\res\drawable\logo_claude.xml` |
| Codex / OpenAI | https://openai.com/brand/ | `F:\projects\edgepad\android\app\src\main\res\drawable\logo_openai.xml` |
| Gemini | Google brand terms, formal approval needed | `F:\projects\edgepad\android\app\src\main\res\drawable\logo_gemini.xml` |
| Gitea | https://gitea.com/gitea/design | `F:\projects\edgepad\android\app\src\main\res\drawable\logo_gitea.xml` |
| Forgejo | https://codeberg.org/forgejo/governance | `F:\projects\edgepad\android\app\src\main\res\drawable\logo_forgejo.xml` |
| Gitee | none found | `F:\projects\edgepad\android\app\src\main\res\drawable\logo_gitee.xml` |
| Bitbucket | https://atlassian.design/foundations/logos | `F:\projects\edgepad\android\app\src\main\res\drawable\logo_bitbucket.xml` |

Save the original SVG too, at `F:\projects\edgepad\assets\brand\logos\<id>.svg`. Android needs a VectorDrawable, so a
session converts it.

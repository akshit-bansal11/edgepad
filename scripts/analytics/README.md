# Analytics digest

`.github/workflows/analytics.yml` runs `digest.py` every morning around 7 AM IST and mails a digest:
GitHub numbers plus every post in `posts.toml`, each with its change since the previous mail. It runs
on GitHub Actions minutes and uses no Claude usage.

## Setup

Add these under Settings → Secrets and variables → Actions → New repository secret.

| Secret | Needed | Where it comes from |
|---|---|---|
| `GMAIL_USER` | yes | the Gmail address that sends (and receives) the mail |
| `GMAIL_APP_PASSWORD` | yes | myaccount.google.com/apppasswords (needs 2-Step Verification on) |
| `MAIL_TO` | no | a different recipient, if wanted |
| `GH_TRAFFIC_TOKEN` | no | github.com/settings/personal-access-tokens → fine-grained, only this repo, Administration: Read-only. Adds views, clones and referrers |
| `YOUTUBE_API_KEY` | for YouTube | console.cloud.google.com → new project → enable "YouTube Data API v3" → Credentials → API key |
| `DEV_API_KEY` | no | dev.to/settings/extensions. Adds article views |
| `REDDIT_CLIENT_ID`, `REDDIT_CLIENT_SECRET` | if Reddit shows HTTP 403 | reddit.com/prefs/apps → "script" app |
| `PRODUCTHUNT_TOKEN` | no | producthunt.com/v2/oauth/applications → developer token |

Then Actions → Analytics digest → Run workflow. Tick "dry run" to see the digest in the run summary
without mailing it.

## Adding a post

Fill in its `url` in `posts.toml`. A post with a `live_from` date stays under "Upcoming" until that day.
Anything that cannot be read shows as unavailable with the reason, never as zero.

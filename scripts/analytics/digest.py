"""Morning analytics mail for Edgepad: GitHub plus every post listed in posts.toml.

Standard library only, so the workflow needs no install step. Every source is read on its own:
one that fails is reported as unavailable with the reason, never as a zero. Yesterday's numbers
come from the previous run's state file, so each figure can carry its change since then.

Usage: python3 digest.py --config posts.toml --state-in prev.json --state-out state.json [--dry-run]
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import html
import json
import os
import re
import smtplib
import sys
import tomllib
import urllib.error
import urllib.parse
import urllib.request
from email.message import EmailMessage
from http.client import HTTPException

IST = dt.timezone(dt.timedelta(hours=5, minutes=30))
UA = "edgepad-analytics/1.0 (+https://github.com/akshit-bansal11/edgepad)"
TIMEOUT = 20


class Unavailable(Exception):
    """A source could not be read. The message is shown in the mail as the reason."""


# --- HTTP -------------------------------------------------------------------------------------

def http(url: str, *, headers: dict | None = None, data: bytes | None = None, raw: bool = False):
    req = urllib.request.Request(url, data=data, headers={"User-Agent": UA, **(headers or {})})
    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT) as resp:
            body = resp.read()
    except urllib.error.HTTPError as e:
        raise Unavailable(f"HTTP {e.code}") from None
    except (OSError, HTTPException) as e:
        # URLError, timeouts, resets mid-body and TLS errors are all OSError; IncompleteRead is HTTPException.
        raise Unavailable(f"network: {getattr(e, 'reason', e) or type(e).__name__}") from None
    if raw:
        return body.decode("utf-8", "replace")
    try:
        return json.loads(body)
    except ValueError:
        raise Unavailable("response was not JSON (likely a bot block page)") from None


def gql(url: str, query: str, variables: dict, token: str | None = None) -> dict:
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    out = http(url, headers=headers, data=json.dumps({"query": query, "variables": variables}).encode())
    if out.get("errors"):
        raise Unavailable(out["errors"][0].get("message", "GraphQL error"))
    return out["data"]


# --- GitHub -----------------------------------------------------------------------------------

GITHUB_QUERY = """
query($owner: String!, $name: String!) {
  repository(owner: $owner, name: $name) {
    stargazerCount
    forkCount
    watchers { totalCount }
    openIssues: issues(states: OPEN) { totalCount }
    closedIssues: issues(states: CLOSED) { totalCount }
    openPRs: pullRequests(states: OPEN) { totalCount }
    mergedPRs: pullRequests(states: MERGED) { totalCount }
    discussions { totalCount }
    releases(first: 100) { nodes { releaseAssets(first: 50) { nodes { downloadCount } } } }
  }
}
"""


def read_github(repo: str) -> list[tuple[str, dict]]:
    owner, name = repo.split("/")
    token = os.environ.get("GITHUB_TOKEN")
    if not token:
        raise Unavailable("GITHUB_TOKEN not set")
    r = gql("https://api.github.com/graphql", GITHUB_QUERY, {"owner": owner, "name": name}, token)["repository"]
    downloads = sum(a["downloadCount"] for rel in r["releases"]["nodes"] for a in rel["releaseAssets"]["nodes"])
    sections = [("GitHub", {
        "stars": r["stargazerCount"],
        "forks": r["forkCount"],
        "watchers": r["watchers"]["totalCount"],
        "open issues": r["openIssues"]["totalCount"],
        "closed issues": r["closedIssues"]["totalCount"],
        "open PRs": r["openPRs"]["totalCount"],
        "merged PRs": r["mergedPRs"]["totalCount"],
        "discussions": r["discussions"]["totalCount"],
        "release downloads (all time)": downloads,
    })]

    # Traffic needs a token with Administration: read, which the workflow's own token cannot have.
    traffic_token = os.environ.get("GH_TRAFFIC_TOKEN")
    if traffic_token:
        h = {"Authorization": f"Bearer {traffic_token}", "Accept": "application/vnd.github+json"}
        base = f"https://api.github.com/repos/{repo}/traffic"
        try:
            views = http(f"{base}/views", headers=h)
            clones = http(f"{base}/clones", headers=h)
            refs = http(f"{base}/popular/referrers", headers=h)
            yday = (dt.datetime.now(dt.timezone.utc).date() - dt.timedelta(days=1)).isoformat()
            day = next((v for v in views.get("views", []) if v["timestamp"].startswith(yday)), None)
            traffic = {
                "views, last 14 days": views.get("count", 0),
                "unique visitors, last 14 days": views.get("uniques", 0),
                "clones, last 14 days": clones.get("count", 0),
            }
            if day is not None:  # GitHub's traffic lags; a day it has not counted yet is unknown, not 0
                traffic["views yesterday (UTC)"] = day.get("count", 0)
            for ref in refs[:6]:
                traffic[f"from {ref['referrer']}, 14 days"] = ref["count"]
            sections.append(("GitHub traffic", traffic))
        except Unavailable as e:
            sections.append(("GitHub traffic", {"_error": str(e)}))
        except (KeyError, IndexError, TypeError, ValueError, AttributeError) as e:
            sections.append(("GitHub traffic", {"_error": f"unexpected response ({type(e).__name__})"}))
    else:
        sections.append(("GitHub traffic", {"_error": "add secret GH_TRAFFIC_TOKEN to see views and referrers"}))
    return sections


# --- Posts ------------------------------------------------------------------------------------

def youtube(url: str) -> dict:
    key = os.environ.get("YOUTUBE_API_KEY")
    if not key:
        raise Unavailable("add secret YOUTUBE_API_KEY")
    m = re.search(r"(?:youtu\.be/|v=|shorts/|embed/)([\w-]{11})", url)
    if not m:
        raise Unavailable("no video id in the URL")
    q = urllib.parse.urlencode({"part": "statistics", "id": m.group(1), "key": key})
    items = http(f"https://www.googleapis.com/youtube/v3/videos?{q}").get("items", [])
    if not items:
        raise Unavailable("video not found (private or deleted?)")
    s = items[0]["statistics"]
    return {"views": int(s.get("viewCount", 0)), "likes": int(s.get("likeCount", 0)),
            "comments": int(s.get("commentCount", 0))}


_reddit_token: str | None = None


def reddit(url: str) -> dict:
    global _reddit_token
    path = urllib.parse.urlparse(url).path.rstrip("/")
    cid, secret = os.environ.get("REDDIT_CLIENT_ID"), os.environ.get("REDDIT_CLIENT_SECRET")
    if cid and secret:
        if _reddit_token is None:
            auth = base64.b64encode(f"{cid}:{secret}".encode()).decode()
            tok = http("https://www.reddit.com/api/v1/access_token",
                       headers={"Authorization": f"Basic {auth}"},
                       data=b"grant_type=client_credentials")
            _reddit_token = tok["access_token"]
        listing = http(f"https://oauth.reddit.com{path}?raw_json=1",
                       headers={"Authorization": f"Bearer {_reddit_token}"})
    else:
        try:
            listing = http(f"https://www.reddit.com{path}.json?raw_json=1")
        except Unavailable as e:
            raise Unavailable(f"{e}; Reddit blocks GitHub's servers without an API app") from None
    d = listing[0]["data"]["children"][0]["data"]
    if d.get("removed_by_category"):
        return {"_error": f"post removed ({d['removed_by_category']})"}
    return {"score": d["score"], "upvote %": round(d.get("upvote_ratio", 0) * 100), "comments": d["num_comments"]}


_dev_mine: list | None = None


def dev(url: str) -> dict:
    global _dev_mine
    path = urllib.parse.urlparse(url).path.strip("/")
    a = http(f"https://dev.to/api/articles/{path}")
    out = {"reactions": a.get("public_reactions_count", 0), "comments": a.get("comments_count", 0)}
    key = os.environ.get("DEV_API_KEY")
    if key:
        if _dev_mine is None:
            _dev_mine = http("https://dev.to/api/articles/me/all?per_page=1000", headers={"api-key": key})
        mine = next((m for m in _dev_mine if m.get("id") == a.get("id")), None)
        if mine and "page_views_count" in mine:
            out = {"views": mine["page_views_count"], **out}
    return out


def hn(url: str) -> dict:
    m = re.search(r"id=(\d+)", url)
    if not m:
        raise Unavailable("no item id in the URL")
    item = http(f"https://hacker-news.firebaseio.com/v0/item/{m.group(1)}.json")
    if not item:
        raise Unavailable("item not found")
    return {"points": item.get("score", 0), "comments": item.get("descendants", 0)}


def producthunt(url: str) -> dict:
    token = os.environ.get("PRODUCTHUNT_TOKEN")
    slug = urllib.parse.urlparse(url).path.rstrip("/").split("/")[-1]
    if not token:
        return page(url)
    d = gql("https://api.producthunt.com/v2/api/graphql",
            "query($slug: String!) { post(slug: $slug) { votesCount commentsCount } }", {"slug": slug}, token)
    if not d.get("post"):
        raise Unavailable("post not found")
    return {"upvotes": d["post"]["votesCount"], "comments": d["post"]["commentsCount"]}


def hashnode(url: str) -> dict:
    u = urllib.parse.urlparse(url)
    slug = u.path.strip("/").split("/")[-1]
    d = gql("https://gql.hashnode.com",
            "query($host: String!, $slug: String!) { publication(host: $host) {"
            " post(slug: $slug) { views reactionCount responseCount } } }",
            {"host": u.netloc, "slug": slug})
    post = (d.get("publication") or {}).get("post")
    if not post:
        raise Unavailable("post not found")
    return {"views": post["views"], "reactions": post["reactionCount"], "comments": post["responseCount"]}


_LD = re.compile(r'<script[^>]+application/ld\+json[^>]*>(.*?)</script>', re.S | re.I)


def page(url: str) -> dict:
    """Best effort for sites with no API: schema.org counters, which some pages publish for search engines."""
    text = http(url, headers={"Accept": "text/html"}, raw=True)
    found: dict = {}

    def walk(node):
        if isinstance(node, list):
            for n in node:
                walk(n)
        elif isinstance(node, dict):
            stats = node.get("interactionStatistic") or []
            for stat in stats if isinstance(stats, list) else [stats]:
                if not isinstance(stat, dict):
                    continue
                itype = stat.get("interactionType", "")
                if isinstance(itype, dict):
                    itype = itype.get("@type", "")
                kind = str(itype).split("/")[-1].replace("Action", "").lower()
                if kind and str(stat.get("userInteractionCount", "")).isdigit():
                    found[{"like": "likes", "comment": "comments", "watch": "views", "vote": "upvotes"}
                          .get(kind, kind)] = int(stat["userInteractionCount"])
            if str(node.get("commentCount", "")).isdigit():
                found.setdefault("comments", int(node["commentCount"]))
            agg = node.get("aggregateRating")
            if isinstance(agg, dict) and agg.get("ratingCount"):
                found.setdefault("ratings", int(agg["ratingCount"]))
            for v in node.values():
                if isinstance(v, (dict, list)):
                    walk(v)

    for block in _LD.findall(text):
        try:
            walk(json.loads(block))
        except ValueError:
            continue
    if not found:
        raise Unavailable("page is up, but it publishes no counts a script can read; check by hand")
    return found


READERS = {"youtube": youtube, "reddit": reddit, "dev": dev, "hn": hn,
           "producthunt": producthunt, "hashnode": hashnode, "page": page}


# --- Report -----------------------------------------------------------------------------------

def delta(now, before) -> str:
    if not isinstance(now, int) or not isinstance(before, int) or now == before:
        return ""
    return f"+{now - before}" if now > before else str(now - before)


def build(config: dict, prev: dict, today: dt.date):
    sections: list[tuple[str, str, dict]] = []  # (name, url, metrics)
    try:
        for name, metrics in read_github(config.get("repo", "akshit-bansal11/edgepad")):
            sections.append((name, "", metrics))
    except Unavailable as e:
        sections.append(("GitHub", "", {"_error": str(e)}))
    except (KeyError, IndexError, TypeError, ValueError, AttributeError) as e:
        sections.append(("GitHub", "", {"_error": f"unexpected response ({type(e).__name__})"}))

    manual, upcoming = [], []
    for post in config.get("post", []):
        name, kind, url = post["name"], post["kind"], post.get("url", "")
        live = post.get("live_from")
        if isinstance(live, str) and live:
            live = dt.date.fromisoformat(live)  # a bare TOML date is already a date
        if isinstance(live, dt.datetime):
            live = live.date()
        if live and live > today:
            upcoming.append(f"{name}: {live:%d %b %Y}")
        elif not url:
            upcoming.append(f"{name}: link not added yet" + (" (should be live now)" if live else ""))
        elif kind == "manual":
            manual.append((name, url))
        else:
            try:
                metrics = READERS[kind](url)
            except Unavailable as e:
                metrics = {"_error": str(e)}
            except (KeyError, IndexError, TypeError, ValueError, AttributeError) as e:
                metrics = {"_error": f"unexpected response ({type(e).__name__})"}
            sections.append((name, url, metrics))

    old = prev.get("metrics", {})
    # A source that failed today keeps its last good numbers, so tomorrow's change is still measured.
    state = {"date": today.isoformat(), "metrics": {
        n: old.get(n, {}) if "_error" in m else m for n, _, m in sections}}
    return sections, manual, upcoming, state, old, prev.get("date")


def render(sections, manual, upcoming, old, prev_date, today: dt.date) -> tuple[str, str, str]:
    stars = next((m.get("stars") for n, _, m in sections if n == "GitHub"), None)
    star_delta = delta(stars, old.get("GitHub", {}).get("stars"))
    subject = f"Edgepad daily, {today:%a %d %b}" + (f": {stars} stars" if stars is not None else "") + \
              (f" ({star_delta})" if star_delta else "")
    since = f"Changes are since {prev_date}." if prev_date else "First run: changes start showing tomorrow."

    txt = [subject, since, ""]
    h = [f"<div style='font-family:system-ui,sans-serif;max-width:640px'><h2 style='margin:0'>{html.escape(subject)}</h2>",
         f"<p style='color:#666;margin-top:4px'>{since}</p>"]
    for name, url, metrics in sections:
        title = f"<a href='{html.escape(url)}'>{html.escape(name)}</a>" if url else html.escape(name)
        h.append(f"<h3 style='margin:18px 0 4px'>{title}</h3>")
        txt.append(name + (f"  {url}" if url else ""))
        if "_error" in metrics:
            h.append(f"<p style='color:#a33;margin:0'>Unavailable: {html.escape(metrics['_error'])}</p>")
            txt.append(f"  unavailable: {metrics['_error']}")
            continue
        h.append("<table style='border-collapse:collapse'>")
        for k, v in metrics.items():
            d = delta(v, old.get(name, {}).get(k))
            colour = "#1a7f37" if d.startswith("+") else "#a33"
            h.append(f"<tr><td style='padding:1px 16px 1px 0'>{html.escape(k)}</td>"
                     f"<td style='text-align:right'><b>{v:,}</b></td>"
                     f"<td style='padding-left:8px;color:{colour}'>{d}</td></tr>")
            txt.append(f"  {k}: {v:,}" + (f" ({d})" if d else ""))
        h.append("</table>")
        txt.append("")
    if manual:
        h.append("<h3 style='margin:18px 0 4px'>Check by hand (no API)</h3><ul>")
        txt.append("Check by hand (no API)")
        for name, url in manual:
            h.append(f"<li><a href='{html.escape(url)}'>{html.escape(name)}</a></li>")
            txt.append(f"  {name}: {url}")
        h.append("</ul>")
        txt.append("")
    if upcoming:
        h.append("<h3 style='margin:18px 0 4px'>Upcoming or link missing</h3><ul style='color:#666'>")
        txt.append("Upcoming or link missing")
        for u in upcoming:
            h.append(f"<li>{html.escape(u)}</li>")
            txt.append(f"  {u}")
        h.append("</ul>")
    h.append("<p style='color:#999;font-size:12px'>Add or fix links in scripts/analytics/posts.toml.</p></div>")
    return subject, "\n".join(txt), "\n".join(h)


def send(subject: str, text: str, body_html: str) -> None:
    user, password = os.environ.get("GMAIL_USER"), os.environ.get("GMAIL_APP_PASSWORD")
    if not user or not password:
        sys.exit("GMAIL_USER and GMAIL_APP_PASSWORD must be set to send the mail")
    msg = EmailMessage()
    msg["Subject"], msg["From"], msg["To"] = subject, user, os.environ.get("MAIL_TO") or user
    msg.set_content(text)
    msg.add_alternative(body_html, subtype="html")
    with smtplib.SMTP_SSL("smtp.gmail.com", 465, timeout=30) as s:
        s.login(user, password)
        s.send_message(msg)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--config", required=True)
    ap.add_argument("--state-in")
    ap.add_argument("--state-out", required=True)
    ap.add_argument("--dry-run", action="store_true", help="print the mail instead of sending it")
    args = ap.parse_args()

    with open(args.config, "rb") as f:
        config = tomllib.load(f)
    prev = {}
    if args.state_in and os.path.exists(args.state_in):
        try:
            with open(args.state_in, encoding="utf-8") as f:
                prev = json.load(f)
        except ValueError:
            print("previous state is unreadable; this mail will have no changes", file=sys.stderr)
        if not isinstance(prev, dict):
            prev = {}
    today = dt.datetime.now(IST).date()

    sections, manual, upcoming, state, old, prev_date = build(config, prev, today)
    subject, text, body_html = render(sections, manual, upcoming, old, prev_date, today)

    # Only a dry run writes the run summary: on a public repo anyone can read it, and the traffic
    # figures are otherwise visible to repo admins only.
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary and args.dry_run:
        with open(summary, "a", encoding="utf-8") as f:
            f.write(f"```\n{text}\n```\n")
    if args.dry_run:
        print(text)
    else:
        send(subject, text, body_html)
    with open(args.state_out, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=1)


if __name__ == "__main__":
    main()

#!/usr/bin/env bash
# Opens the pull request that puts a manifest in the Windows Package Manager, using gh alone.
#
#   bash packaging/winget/submit.sh 3.2.0
#
# wingetcreate does the same and needs the .NET runtime, which this machine does not have. Nothing is
# cloned: winget-pkgs is very large, so the files are written to a fork through the GitHub API.
set -euo pipefail

version=${1:?usage: submit.sh <version>}
dir="$(dirname "$0")/$version"
id=AkshitBansal.Edgepad
title="New package: $id version $version"
branch="$id-$version"

[ -d "$dir" ] || { echo "No manifest folder at $dir" >&2; exit 1; }

user=$(gh api user --jq .login)
fork="$user/winget-pkgs"

# A fork that already exists is left alone; a new one takes a moment to appear.
gh repo fork microsoft/winget-pkgs --clone=false
until gh api "repos/$fork" --silent 2>/dev/null; do sleep 3; done
gh repo sync "$fork"

base=$(gh api "repos/$fork/git/ref/heads/master" --jq .object.sha)
gh api "repos/$fork/git/refs" -f ref="refs/heads/$branch" -f sha="$base" --silent

for file in "$dir"/*.yaml; do
  gh api -X PUT "repos/$fork/contents/manifests/a/AkshitBansal/Edgepad/$version/$(basename "$file")" \
    -f message="$title" -f branch="$branch" -f content="$(base64 -w0 "$file")" --silent
done

# The body is winget-pkgs' own checklist, ticked only where it is true. The install test and the licence
# agreement are left open: the first was not run, and the second is signed on the pull request itself.
gh pr create --repo microsoft/winget-pkgs --base master --head "$user:$branch" --title "$title" --body "## Description
Adds $id $version, the Windows half of https://github.com/akshit-bansal11/edgepad.

## Checklist
- [ ] Signed the Contributor License Agreement

## Manifest Checklist
- [x] Checked that there aren't other open pull requests for the same manifest update/change
- [x] This PR only modifies one (1) manifest
- [x] Validated manifest locally with \`winget validate --manifest <path>\`
- [ ] Tested manifest locally with \`winget install --manifest <path>\`
- [x] Manifest conforms to the 1.12 schema"

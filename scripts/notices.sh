#!/bin/sh
# Writes the licence notices of everything a .NET build ships, for the release jobs.
#
#   scripts/notices.sh <packages.lock.json> <rid> <out file>
#
# Every NuGet package the lock file resolves (all of them, not only those that reach the binary: a notice
# too many costs nothing, one too few breaks a licence), from the copy `dotnet restore` left in the NuGet
# cache: its licence expression, copyright and project URL from the .nuspec, then any licence or notices
# file the package carries. An MIT package that carries no licence file gets the MIT text under its own
# copyright line. Last comes the .NET runtime's own notices, which a self-contained build bundles.
# Anything missing fails the script, so a release cannot go out without its notices. Needs jq.
set -eu

lock=$1
rid=$2
out=$3
pkgs=${NUGET_PACKAGES:-$HOME/.nuget/packages}
# Git Bash on the Windows runner hands over Windows paths; find wants its own.
dotnet_root=${DOTNET_ROOT:+$(cygpath -u "$DOTNET_ROOT" 2>/dev/null || printf '%s' "$DOTNET_ROOT")}
list=$(mktemp)
trap 'rm -f "$list"' EXIT
# Written to a file and read back, not piped: a pipeline's loop runs in a subshell, where exit stops nothing.
# jq on Windows may end its lines with a carriage return.
jq -r '.dependencies[] | to_entries[] | select(.value.type != "Project") | "\(.key) \(.value.resolved)"' "$lock" |
    tr -d '\r' | sort -u >"$list"

# nuspec_field <nuspec> <element>: the element's text, first occurrence, on one line.
nuspec_field() {
    tr '\n' ' ' <"$1" | sed -n "s:.*<$2[^>]*>\([^<]*\)</$2>.*:\1:p" | head -n 1
}

{
    echo "Edgepad bundles the components below. Each is listed with its licence, as published by its makers."
    echo
    while read -r id ver; do
        dir="$pkgs/$(printf '%s' "$id" | tr 'A-Z' 'a-z')/$ver"
        spec="$dir/$(printf '%s' "$id" | tr 'A-Z' 'a-z').nuspec"
        if [ ! -f "$spec" ]; then
            echo "notices.sh: $id $ver is not in $pkgs; restore first" >&2
            exit 1
        fi
        licence=$(nuspec_field "$spec" license)
        [ -n "$licence" ] || licence=$(nuspec_field "$spec" licenseUrl)
        copyright=$(nuspec_field "$spec" copyright)
        echo "================================================================================"
        echo "$id $ver"
        echo "Licence: ${licence:-not stated}"
        [ -z "$copyright" ] || echo "$copyright"
        url=$(nuspec_field "$spec" projectUrl)
        [ -z "$url" ] || echo "$url"
        echo
        files=$(find "$dir" -maxdepth 1 -type f \( -iname 'licen[cs]e*' -o -iname '*notice*' \) | sort)
        if [ -n "$files" ]; then
            for f in $files; do
                cat "$f"
                echo
            done
        elif [ "$licence" = MIT ]; then
            echo "${copyright:-Copyright (c) the $id authors}"
            echo
            cat <<'EOF'
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
USE OR OTHER DEALINGS IN THE SOFTWARE.

EOF
        fi
    done <"$list"

    # The runtime pack's notices if restore fetched one, else the copy at the root of the SDK install.
    runtime=$(find "$pkgs/microsoft.netcore.app.runtime.$rid" ${dotnet_root:+"$dotnet_root"} -maxdepth 2 -type f \
        -iname 'third*party*notices*' 2>/dev/null | head -n 1)
    if [ -z "$runtime" ]; then
        echo "notices.sh: no .NET runtime notices under $pkgs or DOTNET_ROOT ('${DOTNET_ROOT:-}')" >&2
        exit 1
    fi
    echo "================================================================================"
    echo ".NET runtime (bundled by the self-contained build)"
    echo "Licence: MIT"
    echo "https://github.com/dotnet/runtime"
    echo
    cat "$runtime"
} >"$out"

echo "notices: $(grep -c '^================' "$out") components in $out"

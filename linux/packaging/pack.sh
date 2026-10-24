#!/bin/sh
# Builds the release archive from a `dotnet publish` folder: pack.sh <version> <publish-dir> <out-file>
# CI and the release job both call this, so the two archives cannot differ.
set -eu

version=$1
publish=$2
out=$3
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT
dir="$stage/edgepad-$version"
mkdir "$dir"

install -m 755 "$publish/Edgepad.Linux" "$dir/edgepad"
install -m 755 "$here/install.sh" "$dir/install.sh"
install -m 644 "$here/60-edgepad-uinput.rules" "$here/edgepad-modules.conf" "$here/edgepad.desktop" "$dir/"
install -m 644 "$root/assets/edgepad-light.svg" "$dir/edgepad.svg"

# Licences travel with the binary: the native libraries it bundles (Skia, HarfBuzz, FreeType, ...) and the
# embedded Lato font require their notices to ship with it. Each notices file is read from the NuGet cache
# that `dotnet publish` restored, at the version packages.lock.json resolves (else the newest one cached).
# A missing file fails the pack, so a release cannot ship without its licences.
lic="$dir/licenses"
mkdir "$lic"
pkgs=${NUGET_PACKAGES:-$HOME/.nuget/packages}
lock="$root/linux/src/Edgepad.Linux/packages.lock.json"

# native_notice <NuGet id> <archive file name>
native_notice() {
    id=$1
    lid=$(printf '%s' "$id" | tr 'A-Z' 'a-z')
    ver=""
    if [ -f "$lock" ]; then
        ver=$(grep -i -A4 "\"$id\": {" "$lock" | sed -n 's/.*"resolved": "\([^"]*\)".*/\1/p' | head -n 1)
    fi
    if [ -z "$ver" ]; then
        ver=$(ls -1 "$pkgs/$lid" 2>/dev/null | sort -V | tail -n 1)
    fi
    src="$pkgs/$lid/$ver/THIRD-PARTY-NOTICES.txt"
    if [ -z "$ver" ] || [ ! -f "$src" ]; then
        echo "pack.sh: no THIRD-PARTY-NOTICES.txt for $id (version '$ver') under $pkgs; run dotnet publish first" >&2
        exit 1
    fi
    install -m 644 "$src" "$lic/$2"
}

install -m 644 "$root/LICENSE" "$lic/edgepad-LICENSE.txt"
install -m 644 "$root/windows/src/Edgepad/Fonts/OFL.txt" "$lic/Lato-OFL.txt"
native_notice SkiaSharp.NativeAssets.Linux skiasharp-THIRD-PARTY-NOTICES.txt
native_notice HarfBuzzSharp.NativeAssets.Linux harfbuzzsharp-THIRD-PARTY-NOTICES.txt

# Owned by root in the archive, not by the CI runner's uid: `sudo tar x` would otherwise leave install.sh
# owned by that uid, which could then rewrite what root runs next. Sorted, dated to the commit and with no
# gzip timestamp, so one commit always packs to the same bytes.
mtime=${SOURCE_DATE_EPOCH:-$(git -C "$root" log -1 --format=%ct)}
tar -C "$stage" --owner=0 --group=0 --numeric-owner --mode=go-w --sort=name --mtime="@$mtime" -cf "$stage/archive.tar" "edgepad-$version"
gzip -n -c "$stage/archive.tar" >"$out"
tar -tzvf "$out"

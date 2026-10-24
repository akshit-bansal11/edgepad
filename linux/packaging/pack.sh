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

# Licences travel with the binary: the packages it bundles (Avalonia, Tmds.DBus, Skia, HarfBuzz, ...), the .NET
# runtime and the embedded Lato font require their notices to ship with it. scripts/notices.sh reads them from
# the NuGet cache that `dotnet publish` restored and fails when one is missing, so a release cannot ship
# without its licences.
lic="$dir/licenses"
mkdir "$lic"
install -m 644 "$root/LICENSE" "$lic/edgepad-LICENSE.txt"
install -m 644 "$root/windows/src/Edgepad/Fonts/OFL.txt" "$lic/Lato-OFL.txt"
sh "$root/scripts/notices.sh" "$root/linux/src/Edgepad.Linux/packages.lock.json" linux-x64 "$stage/notices.txt"
install -m 644 "$stage/notices.txt" "$lic/THIRD-PARTY-NOTICES.txt"

# Owned by root in the archive, not by the CI runner's uid: `sudo tar x` would otherwise leave install.sh
# owned by that uid, which could then rewrite what root runs next. Sorted, dated to the commit and with no
# gzip timestamp, so one commit always packs to the same bytes.
mtime=${SOURCE_DATE_EPOCH:-$(git -C "$root" log -1 --format=%ct)}
tar -C "$stage" --owner=0 --group=0 --numeric-owner --mode=go-w --sort=name --mtime="@$mtime" -cf "$stage/archive.tar" "edgepad-$version"
gzip -n -c "$stage/archive.tar" >"$out"
tar -tzvf "$out"

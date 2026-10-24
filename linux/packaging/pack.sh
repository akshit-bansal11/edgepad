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

tar -C "$stage" -czf "$out" "edgepad-$version"
tar -tzvf "$out"

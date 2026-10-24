#!/bin/sh
# Renders the app icons from the mark's SVG. Run by the `icons` CI job (needs librsvg2-bin, icoutils, icnsutils);
# nothing heavy runs on the owner's machine. The generated files are committed, so builds never depend on it.
#
#   scripts/icons.sh        run from anywhere; writes into the repo
#
# windows/src/Edgepad/edgepad.ico                       exe icon and Windows tray
# macos/Resources/edgepad.icns                          copied into Edgepad.app by macos/bundle.sh
# linux/src/Edgepad.Linux/Assets/edgepad-64.png         Linux tray (Skia does not decode an .ico)
# site/app/apple-icon.png                               180 px touch icon (site/app/icon.svg is hand-kept text)
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/assets/edgepad-light.svg"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

for n in 16 24 32 48 64 128 180 256 512; do
    rsvg-convert -w "$n" -h "$n" -o "$tmp/$n.png" "$src"
done

mkdir -p "$root/macos/Resources" "$root/linux/src/Edgepad.Linux/Assets"
icotool -c -o "$root/windows/src/Edgepad/edgepad.ico" "$tmp/16.png" "$tmp/24.png" "$tmp/32.png" "$tmp/48.png" "$tmp/64.png" "$tmp/128.png" "$tmp/256.png"
# png2icns (icnsutils 0.8.1) documents sizes up to 512, so there is no 1024 entry.
png2icns "$root/macos/Resources/edgepad.icns" "$tmp/16.png" "$tmp/32.png" "$tmp/128.png" "$tmp/256.png" "$tmp/512.png"
cp "$tmp/64.png" "$root/linux/src/Edgepad.Linux/Assets/edgepad-64.png"
cp "$tmp/180.png" "$root/site/app/apple-icon.png"

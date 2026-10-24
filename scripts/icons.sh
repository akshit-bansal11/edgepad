#!/bin/sh
# Renders the app icons from the mark's SVG. Run by the `icons` CI job (needs librsvg2-bin, icoutils, icnsutils);
# nothing heavy runs on the owner's machine. The generated files are committed, so builds never depend on it.
#
#   scripts/icons.sh        run from anywhere; writes into the repo
#
# windows/src/Edgepad/edgepad.ico                       exe icon and Windows tray
# macos/Resources/edgepad.icns                          copied into Edgepad.app by macos/bundle.sh
# linux/src/Edgepad.Linux/Assets/edgepad-64.png         Linux tray (Skia does not decode an .ico)
# linux/src/Edgepad.Linux/Assets/edgepad-64-dark.png    Linux tray on a dark desktop, from edgepad-dark.svg
# windows/src/Edgepad/Assets/tray-light.ico, tray-dark.ico   Windows tray, by the taskbar theme (16 to 48 px)
# site/app/apple-icon.png                               180 px touch icon (site/app/icon.svg is hand-kept text)
# assets/social-preview.png, site/public/social-preview.png   1280x640 card, from assets/social-preview.svg
# assets/brand/edgepad-{light,dark}-{256,512,1024}.png   the mark for press and documents
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
# The tray in both tiles, at the sizes a tray uses across DPI: Windows picks by the taskbar theme, Linux by the desktop's.
dark="$root/assets/edgepad-dark.svg"
for n in 20 40; do
    rsvg-convert -w "$n" -h "$n" -o "$tmp/$n.png" "$src"
    rsvg-convert -w "$n" -h "$n" -o "$tmp/d$n.png" "$dark"
done
for n in 16 24 32 48 64; do
    rsvg-convert -w "$n" -h "$n" -o "$tmp/d$n.png" "$dark"
done
mkdir -p "$root/windows/src/Edgepad/Assets"
icotool -c -o "$root/windows/src/Edgepad/Assets/tray-light.ico" "$tmp/16.png" "$tmp/20.png" "$tmp/24.png" "$tmp/32.png" "$tmp/40.png" "$tmp/48.png"
icotool -c -o "$root/windows/src/Edgepad/Assets/tray-dark.ico" "$tmp/d16.png" "$tmp/d20.png" "$tmp/d24.png" "$tmp/d32.png" "$tmp/d40.png" "$tmp/d48.png"
cp "$tmp/d64.png" "$root/linux/src/Edgepad.Linux/Assets/edgepad-64-dark.png"

# The social preview (the repo's card and the site's Open Graph image). Its text is Lato, so the job installs fonts-lato.
rsvg-convert -w 1280 -h 640 -o "$root/assets/social-preview.png" "$root/assets/social-preview.svg"
cp "$root/assets/social-preview.png" "$root/site/public/social-preview.png"

# The mark as PNGs for press and documents, both variants.
mkdir -p "$root/assets/brand"
for n in 256 512 1024; do
    rsvg-convert -w "$n" -h "$n" -o "$root/assets/brand/edgepad-light-$n.png" "$src"
    rsvg-convert -w "$n" -h "$n" -o "$root/assets/brand/edgepad-dark-$n.png" "$dark"
done

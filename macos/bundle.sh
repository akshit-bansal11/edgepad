#!/usr/bin/env bash
# Builds Edgepad.app and zips it.
#
#   macos/bundle.sh <version> <output dir>
#
# Leaves <output dir>/Edgepad.app and <output dir>/Edgepad-mac.zip. SwiftPM builds a bare executable and a
# menu bar app has to be a bundle: the Bluetooth permission prompt reads its reason from Info.plist, and
# start-at-login registers a bundle, not a file. There is no Xcode project to do this, on purpose: the
# package is plain text a CI runner can build, and this script is the whole of what Xcode would have added.
#
# The signature is ad hoc. Apple Silicon refuses to run unsigned code at all, so this is the least that
# launches; it is not a Developer ID and the app is not notarised, so Gatekeeper still has to be told to
# open it the first time. An ad hoc signature is also a different identity on every build, which is why
# macOS asks for the Accessibility permission again after each update.
set -euo pipefail

version="${1:?usage: bundle.sh <version> <output dir>}"
out="${2:?usage: bundle.sh <version> <output dir>}"

mkdir -p "$out"
out="$(cd "$out" && pwd)"
cd "$(dirname "$0")"

# Both architectures in one file, so one download runs on an Intel Mac and an Apple Silicon one.
build=(swift build -c release --arch arm64 --arch x86_64 -Xswiftc -warnings-as-errors)
"${build[@]}"
bin="$("${build[@]}" --show-bin-path)"

app="$out/Edgepad.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS"
cp "$bin/Edgepad" "$app/Contents/MacOS/Edgepad"

# CFBundleVersion takes numbers only, so a draft's "-draft.45" suffix stays on the display version alone.
cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>Edgepad</string>
  <key>CFBundleDisplayName</key>
  <string>Edgepad</string>
  <key>CFBundleIdentifier</key>
  <string>me.akshitbansal.edgepad</string>
  <key>CFBundleExecutable</key>
  <string>Edgepad</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleShortVersionString</key>
  <string>$version</string>
  <key>CFBundleVersion</key>
  <string>${version%%-*}</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSBluetoothAlwaysUsageDescription</key>
  <string>Edgepad uses Bluetooth to listen for the phone you have paired with this Mac.</string>
</dict>
</plist>
EOF

codesign --force --sign - "$app"
codesign --verify --strict "$app"

# ditto rather than zip: it keeps the bundle's symlinks and extended attributes, which is what the
# signature was computed over.
rm -f "$out/Edgepad-mac.zip"
ditto -c -k --keepParent "$app" "$out/Edgepad-mac.zip"
echo "bundle: $out/Edgepad-mac.zip"

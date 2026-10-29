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

# The version is written into Info.plist as it stands and comes from a tag name, which may hold anything a
# ref may: angle brackets included.
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]]; then
    echo "bundle.sh: '$version' is not a version like 3.4.0 or 3.4.0-draft.1" >&2
    exit 1
fi

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

# The icon is rendered from assets/edgepad-light.svg by scripts/icons.sh (the CI job `icons`) and committed.
# Missing, the build stops: a release must not go out with the generic icon.
mkdir -p "$app/Contents/Resources"
# macOS 26 draws the Dock and Finder icon from Assets.car, compiled from the Icon Composer file
# Resources/edgepad.icon: the white tile in light mode, the dark tile in dark mode. Older systems read the .icns.
# A car with no dark rendition fails the build, so the icon cannot quietly stop following the theme.
xcrun actool Resources/edgepad.icon --compile "$app/Contents/Resources" --app-icon edgepad \
    --platform macosx --target-device mac --minimum-deployment-target 13.0 \
    --output-partial-info-plist "$out/actool-partial.plist" >/dev/null
rm -f "$out/actool-partial.plist"
appearances=$(xcrun assetutil --info "$app/Contents/Resources/Assets.car" | grep -o '"Appearance" : "[^"]*"' | sort | uniq -c)
echo "Assets.car appearances: $appearances"
case "$appearances" in
    *Dark*) ;;
    *) echo "bundle.sh: Assets.car has no dark rendition of the icon" >&2; exit 1 ;;
esac
cp Resources/edgepad.icns "$app/Contents/Resources/edgepad.icns" || { echo "bundle.sh: macos/Resources/edgepad.icns is missing; run scripts/icons.sh (CI job icons) and commit it" >&2; exit 1; }

# CFBundleVersion takes numbers only, so a draft's "-draft.45" suffix stays on the display version alone.
# NSLocalNetworkUsageDescription and NSBonjourServices are for the Wi-Fi link's Bonjour listener. Whether macOS asks
# them of an app that only publishes a service is unverified; with them the Local Network prompt has a reason to show.
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
  <key>CFBundleIconFile</key>
  <string>edgepad</string>
  <key>CFBundleIconName</key>
  <string>edgepad</string>
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
  <key>NSLocalNetworkUsageDescription</key>
  <string>Edgepad lets your phone find this Mac and connect to it over Wi-Fi, when you turn Wi-Fi and USB connections on.</string>
  <key>NSBonjourServices</key>
  <array>
    <string>_edgepad._tcp</string>
  </array>
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

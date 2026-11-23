#!/bin/sh
# Screenshots every gallery screen in day and night, landscape and portrait, from a running emulator or phone
# that has the debug APK installed. Usage: scripts/gallery.sh [out-dir]; PNGs land in <out-dir>/shots (default ./shots).
# Fails if a landscape shot is not 2100x945, the size the design review compares against (800x360 dp).
set -eu

out="${1:-.}/shots"
mkdir -p "$out"
screens="surface controls keyboard media-layout widget-layout gamepad-layout rings presenter-sheet xfer-run xfer-paused stats-system stats-system-phone stats-network stats-network-phone ai-two ai-four git data-uses settings settings-capture"

adb shell wm size 945x2100
adb shell wm density 420
# Rotate the display itself: an activity's orientation request alone left the first shot upright.
adb shell settings put system accelerometer_rotation 0
# Android's one-time "Viewing full screen" hint would sit over every shot; mark it as already seen.
adb shell settings put secure immersive_mode_confirmations confirmed
# Errors and crashes land beside the PNGs, so a blank or home-screen shot comes with its reason.
adb logcat -c

for screen in $screens; do
  for night in true false; do
    for land in true false; do
      day=day
      [ "$night" = true ] && day=night
      shape=port
      [ "$land" = true ] && shape=land
      file="$out/$screen-$day-$shape.png"
      rotation=0
      [ "$land" = true ] && rotation=1
      adb shell settings put system user_rotation "$rotation"
      # The system's own day/night: the activity follows it like the app does.
      if [ "$night" = true ]; then adb shell cmd uimode night yes; else adb shell cmd uimode night no; fi
      adb shell am force-stop me.akshitbansal.edgepad
      adb shell am start -W -n me.akshitbansal.edgepad/.gallery.GalleryActivity \
        --es screen "$screen" --ez land "$land" > /dev/null
      sleep 3
      adb exec-out screencap -p > "$file"
      if [ "$land" = true ]; then
        # A PNG stores width and height as two big-endian 32-bit numbers at byte 16.
        size=$(od -An -tu1 -j16 -N8 "$file" | awk '{ print ($1*16777216+$2*65536+$3*256+$4) "x" ($5*16777216+$6*65536+$7*256+$8) }')
        echo "$file $size"
        if [ "$size" != "2100x945" ]; then
          echo "expected 2100x945, got $size" >&2
          bad=1
        fi
      fi
    done
  done
done
adb logcat -d -b crash > "$out/crash.txt" || true
adb logcat -d '*:E' > "$out/errors.txt" || true
# Every shot is taken first, so one wrong size still leaves the rest to review.
[ -z "${bad:-}" ]

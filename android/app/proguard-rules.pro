# Keep rules for the release build's R8 pass, on top of proguard-android-optimize.txt and the rules AAPT
# writes for everything the manifest names (the activity).
#
# What was checked, 2026-09-29, so the next change knows what it is changing:
# - The app reaches nothing of its own by reflection: no Class.forName, no getDeclaredMethod, no
#   Resources.getIdentifier. Every resource is reached through R, which the resource shrinker follows.
# - Nothing is inflated from XML: every view is built in code, so no View needs its constructors kept.
# - Enums stored in SharedPreferences are stored by name(), which R8 keeps meaning the source name even
#   when it renames or unboxes the enum, so a setting saved before this build still reads back.
# - The one wire format is written by hand in FrameCodec, byte by byte; nothing is serialised by reflection.

# Shrink and optimise, but keep every name. The source is public, so renaming would hide nothing, and it
# would turn every stack trace a user pastes into an issue into one that needs a mapping file to read, which
# no release keeps.
-dontobfuscate

# AndroidSVG draws the player logos. It ships no consumer rules of its own. Its reflection reaches only the
# platform (Canvas.save(int) on old Android, a charset by name), which R8 does not rename, so this is not
# known to be needed. It is kept whole anyway: it is small, a wrong rule here shows only as a blank logo on
# a phone, and no test runs the shrunk build.
-keep class com.caverock.androidsvg.** { *; }

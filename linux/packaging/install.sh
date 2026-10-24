#!/bin/sh
# Installs Edgepad from the unpacked release folder. Copies files and loads the uinput module; downloads nothing.
#
#   sudo ./install.sh [PREFIX]      install under PREFIX (default /usr/local)
#   sudo ./install.sh --uninstall [PREFIX]
#
# Under PREFIX: bin/edgepad, share/applications/edgepad.desktop, share/icons/hicolor/scalable/apps/edgepad.svg.
# Always under /etc: udev/rules.d/60-edgepad-uinput.rules and modules-load.d/edgepad.conf, because udev and
# systemd read only those folders.
set -eu

uninstall=0
if [ "${1:-}" = "--uninstall" ]; then
    uninstall=1
    shift
fi
prefix=${1:-/usr/local}
here=$(cd "$(dirname "$0")" && pwd)

if [ "$(id -u)" -ne 0 ]; then
    echo "install.sh: run it as root, for example: sudo $0" >&2
    exit 1
fi

bin="$prefix/bin/edgepad"
desktop="$prefix/share/applications/edgepad.desktop"
icon="$prefix/share/icons/hicolor/scalable/apps/edgepad.svg"
rule=/etc/udev/rules.d/60-edgepad-uinput.rules
modules=/etc/modules-load.d/edgepad.conf

reload_udev() {
    udevadm control --reload
    udevadm trigger --subsystem-match=misc --sysname-match=uinput
}

if [ "$uninstall" -eq 1 ]; then
    rm -f "$bin" "$desktop" "$icon" "$rule" "$modules"
    reload_udev
    echo "Removed: $bin $desktop $icon $rule $modules"
    echo "Left alone: your settings in ~/.config/edgepad, and the uinput module until the next reboot."
    exit 0
fi

install -D -m 755 "$here/edgepad" "$bin"
install -D -m 644 "$here/edgepad.desktop" "$desktop"
install -D -m 644 "$here/edgepad.svg" "$icon"
install -D -m 644 "$here/60-edgepad-uinput.rules" "$rule"
install -D -m 644 "$here/edgepad-modules.conf" "$modules"
modprobe uinput
reload_udev

echo "Installed: $bin $desktop $icon"
echo "Installed: $rule $modules"
echo "Loaded the uinput module and reloaded udev. Log out and back in once so your session gets access to /dev/uinput."

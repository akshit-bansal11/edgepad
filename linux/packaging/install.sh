#!/bin/sh
# Installs Edgepad from the unpacked release folder. Copies files and loads the uinput module; downloads nothing.
#
#   sudo ./install.sh [PREFIX]      install under PREFIX (default /usr/local)
#   sudo ./install.sh --uninstall [PREFIX]
#
# Under PREFIX: bin/edgepad, share/applications/edgepad.desktop and edgepad-send.desktop, share/icons/hicolor/scalable/apps/edgepad.svg,
# and the licence notices from licenses/ in share/doc/edgepad.
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
senddesktop="$prefix/share/applications/edgepad-send.desktop"
icon="$prefix/share/icons/hicolor/scalable/apps/edgepad.svg"
doc="$prefix/share/doc/edgepad"
rule=/etc/udev/rules.d/60-edgepad-uinput.rules
modules=/etc/modules-load.d/edgepad.conf

reload_udev() {
    # Not fatal under set -e: a system without udevadm (a container) still gets its files installed or removed.
    udevadm control --reload || echo "install.sh: udevadm control --reload failed; reboot to apply the uinput rule" >&2
    udevadm trigger --subsystem-match=misc --sysname-match=uinput || echo "install.sh: udevadm trigger failed; reboot to apply the uinput rule" >&2
}

if [ "$uninstall" -eq 1 ]; then
    rm -f "$bin" "$desktop" "$senddesktop" "$icon" "$rule" "$modules"
    rm -rf "$doc"
    reload_udev
    echo "Removed: $bin $desktop $senddesktop $icon $doc $rule $modules"
    echo "Left alone: your settings in ~/.config/edgepad, and the uinput module until the next reboot."
    exit 0
fi

install -D -m 755 "$here/edgepad" "$bin"
install -D -m 644 "$here/edgepad.desktop" "$desktop"
install -D -m 644 "$here/edgepad-send.desktop" "$senddesktop"
install -D -m 644 "$here/edgepad.svg" "$icon"
install -d -m 755 "$doc"
install -m 644 "$here"/licenses/* "$doc/"
install -D -m 644 "$here/60-edgepad-uinput.rules" "$rule"
install -D -m 644 "$here/edgepad-modules.conf" "$modules"
modprobe uinput || echo "install.sh: modprobe uinput failed (no uinput module in this kernel?); Edgepad cannot type until it loads" >&2
reload_udev

echo "Installed: $bin $desktop $senddesktop $icon $doc"
echo "Installed: $rule $modules"
echo "Loaded the uinput module and reloaded udev, unless a notice above says otherwise."
echo
echo "Next: log out and back in once, so your session gets access to /dev/uinput. Then start Edgepad from"
echo "your launcher, or run: edgepad. Turn on Start at login in its menu to have it ready after every boot."
echo "On GNOME, install the AppIndicator extension to see the tray icon (without it Edgepad opens a window)."
echo "Edgepad also needs BlueZ (bluetoothd) and, for volume, wpctl. Docs: https://getedgepad.com/docs"

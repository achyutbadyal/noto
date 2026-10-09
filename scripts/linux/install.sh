#!/bin/bash
# Installs Noto for the current user: ~/.local/share/noto plus a launcher in the applications menu.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
prefix="${PREFIX:-$HOME/.local/share}"

mkdir -p "$prefix" "$HOME/.local/share/applications" "$HOME/.local/bin"
rm -rf "$prefix/noto"
cp -a "$here/noto" "$prefix/noto"
sed "s#@PREFIX@#$prefix#g" "$here/noto.desktop" > "$HOME/.local/share/applications/noto.desktop"
# `noto --capture` is what a system shortcut should run on Wayland, where Noto can't grab a global hotkey.
ln -sf "$prefix/noto/Noto.Desktop" "$HOME/.local/bin/noto"
echo "Installed. Launch Noto from the menu, or run: noto"
echo "Optional (secure token storage): libsecret-tools + GNOME Keyring/KWallet. Notifications: libnotify-bin."

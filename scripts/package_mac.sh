#!/bin/bash
set -e

APP_NAME="Noto.app"
APP_DIR="dist/$APP_NAME"

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp -a dist/noto-desktop/. "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/Noto.Desktop"
cp src/Noto.Desktop/Assets/noto.icns "$APP_DIR/Contents/Resources/"
cp src/Noto.Desktop/Assets/Info.plist "$APP_DIR/Contents/"

echo "Successfully created $APP_DIR"

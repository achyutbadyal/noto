#!/bin/bash
set -e

APP_NAME="Noto.app"
APP_DIR="dist/$APP_NAME"

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp -a dist/noto-desktop/. "$APP_DIR/Contents/MacOS/"
chmod +x "$APP_DIR/Contents/MacOS/Noto.Desktop"

# The on-device AI helper is a separate Swift binary, not a .NET output, so dotnet publish never
# produces it. Bundle it when it has been built (`mise run ai:helper`), or the packaged app loses
# Apple Intelligence. Missing is a warning, not a failure: the app still works, the mode just
# reports itself unavailable.
HELPER="tools/noto-ai-helper/.build/release/noto-ai-helper"
if [[ -f "$HELPER" ]]; then
  cp "$HELPER" "$APP_DIR/Contents/MacOS/"
  chmod +x "$APP_DIR/Contents/MacOS/noto-ai-helper"
  echo "Bundled noto-ai-helper (on-device Apple Intelligence)"
else
  echo "note: noto-ai-helper isn't built, so $APP_NAME won't offer on-device AI." >&2
  echo "      Run 'mise run ai:helper' and package again to include it." >&2
fi

cp src/Noto.Desktop/Assets/noto.icns "$APP_DIR/Contents/Resources/"
cp src/Noto.Desktop/Assets/Info.plist "$APP_DIR/Contents/"

echo "Successfully created $APP_DIR"

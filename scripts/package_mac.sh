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
# produces it. It is bundled by default: built here when missing (needs macOS 26 SDK / Swift). If it can't
# be built the app still works and the mode reports itself unavailable, so locally that is a warning;
# release CI sets REQUIRE_AI_HELPER=1 so a shipped build can never silently lose Apple Intelligence.
HELPER="tools/noto-ai-helper/.build/release/noto-ai-helper"
if [[ ! -f "$HELPER" ]] && command -v swift >/dev/null; then
  echo "Building noto-ai-helper…"
  (cd tools/noto-ai-helper && swift build -c release) || echo "warning: noto-ai-helper failed to build" >&2
fi
if [[ -f "$HELPER" ]]; then
  cp "$HELPER" "$APP_DIR/Contents/MacOS/"
  chmod +x "$APP_DIR/Contents/MacOS/noto-ai-helper"
  echo "Bundled noto-ai-helper (on-device Apple Intelligence)"
elif [[ "${REQUIRE_AI_HELPER:-}" == "1" ]]; then
  echo "error: noto-ai-helper is required but could not be built" >&2
  exit 1
else
  echo "note: noto-ai-helper isn't built, so $APP_NAME won't offer on-device AI." >&2
fi

cp src/Noto.Desktop/Assets/noto.icns "$APP_DIR/Contents/Resources/"
cp src/Noto.Desktop/Assets/Info.plist "$APP_DIR/Contents/"

# Stamp the version into Info.plist (Finder's Get Info, About). VERSION comes from the release workflow;
# local builds read the same <Version> the assemblies get. The plist only accepts numeric x.y.z, so a
# prerelease suffix is dropped.
VERSION="${VERSION:-$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' Directory.Build.props)}"
PLIST_VERSION="${VERSION%%-*}"
plutil -replace CFBundleShortVersionString -string "$PLIST_VERSION" "$APP_DIR/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$PLIST_VERSION" "$APP_DIR/Contents/Info.plist"

# Ad-hoc sign (free, no developer account) so the bundle seal covers Info.plist and Resources, which
# were copied in after dotnet publish signed the executable. Recipients still need to clear the
# download quarantine flag (see README), since this is not a Developer ID signature.
codesign --force --deep --sign - "$APP_DIR"
codesign --verify --deep --strict "$APP_DIR"

echo "Successfully created $APP_DIR"

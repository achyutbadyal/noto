#!/usr/bin/env bash
#
# Builds noto-ai-helper — the on-device Apple Intelligence bridge for Noto's AI field suggestions —
# and installs it wherever the app can be launched from. AppleOnDeviceProvider looks for the binary
# next to the running app (AppContext.BaseDirectory), so every output layout needs its own copy:
#
#   src/Noto.Desktop/bin/*/net10.0/   'mise run desktop' (dotnet run)
#   dist/noto-desktop/                'mise run publish:desktop'
#   dist/Noto.app/Contents/MacOS/     'mise run publish:mac' / 'install:mac'
#
# Re-run this after any of those publish commands, since they rebuild their output directory.
#
# Requires macOS 26+ and a Swift toolchain (Xcode or the Command Line Tools).
# Run with: mise run ai:helper
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/tools/noto-ai-helper"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "noto-ai-helper only builds on macOS." >&2
  exit 1
fi

echo "Building noto-ai-helper…"
(cd "$src" && swift build -c release)

bin="$src/.build/release/noto-ai-helper"
installed=0
for dest in \
  "$root/src/Noto.Desktop/bin/Debug/net10.0" \
  "$root/src/Noto.Desktop/bin/Release/net10.0" \
  "$root/dist/noto-desktop" \
  "$root/dist/Noto.app/Contents/MacOS"; do
  if [[ -d "$dest" ]]; then
    cp "$bin" "$dest/noto-ai-helper"
    echo "installed → $dest/noto-ai-helper"
    installed=1
  fi
done

if [[ "$installed" -eq 0 ]]; then
  echo "No app output found yet. Run 'mise run build' (or publish:desktop) first, then re-run this." >&2
  exit 1
fi

echo
echo "Done. In Noto: Settings → AI suggestions → Apple Intelligence (on device)."
echo "If the app is already open, click Recheck there — no restart needed."

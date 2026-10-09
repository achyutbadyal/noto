#!/bin/bash
# Packages dist/noto-desktop (a linux-* self-contained publish) into dist/noto-linux-<rid>.tar.gz with a
# .desktop entry, the icon, and an install.sh that copies it under ~/.local.
set -e

RID="${TARGET_RID:-linux-x64}"
STAGE="dist/noto-linux"

rm -rf "$STAGE"
mkdir -p "$STAGE/noto"
cp -a dist/noto-desktop/. "$STAGE/noto/"
chmod +x "$STAGE/noto/Noto.Desktop"
cp src/Noto.App/Assets/noto.png "$STAGE/noto/noto.png"
cp scripts/linux/noto.desktop "$STAGE/noto.desktop"
cp scripts/linux/install.sh "$STAGE/install.sh"
chmod +x "$STAGE/install.sh"

tar -C "$STAGE" -czf "dist/noto-$RID.tar.gz" .
echo "Created dist/noto-$RID.tar.gz"

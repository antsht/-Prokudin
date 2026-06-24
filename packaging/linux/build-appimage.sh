#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?Usage: build-appimage.sh <version> <publish-dir> <output-dir>}"
PUBLISH_DIR="${2:?missing publish dir}"
OUTPUT_DIR="${3:?missing output dir}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ASSETS_DIR="$REPO_ROOT/assets"
APPDIR="$OUTPUT_DIR/Prokudin.AppDir"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp "$PUBLISH_DIR/Prokudin" "$APPDIR/usr/bin/Prokudin"
chmod +x "$APPDIR/usr/bin/Prokudin"

cp "$SCRIPT_DIR/prokudin.desktop" "$APPDIR/usr/share/applications/prokudin.desktop"
cp "$SCRIPT_DIR/prokudin.desktop" "$APPDIR/prokudin.desktop"
cp "$ASSETS_DIR/prokudin.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/prokudin.png"
cp "$ASSETS_DIR/prokudin.png" "$APPDIR/prokudin.png"

LINUXDEPLOY="${LINUXDEPLOY:-linuxdeploy-x86_64.AppImage}"
APPIMAGETOOL="${APPIMAGETOOL:-appimagetool-x86_64.AppImage}"

if [[ ! -f "$LINUXDEPLOY" ]]; then
  wget -q "https://github.com/linuxdeploy/linuxdeploy/releases/download/continuous/$LINUXDEPLOY"
  chmod +x "$LINUXDEPLOY"
fi

if [[ ! -f "$APPIMAGETOOL" ]]; then
  wget -q "https://github.com/AppImage/AppImageKit/releases/download/continuous/$APPIMAGETOOL"
  chmod +x "$APPIMAGETOOL"
fi

export ARCH=x86_64
export VERSION="$VERSION"

./"$LINUXDEPLOY" --appdir "$APPDIR" --desktop-file="$APPDIR/prokudin.desktop" --icon-file="$APPDIR/prokudin.png" --output appimage

APPIMAGE_FILE="$(find "$OUTPUT_DIR" -maxdepth 1 -name 'Prokudin-*.AppImage' -print -quit)"
if [[ -z "$APPIMAGE_FILE" ]]; then
  ./"$APPIMAGETOOL" "$APPDIR" "$OUTPUT_DIR/Prokudin-${VERSION}-linux-x64.AppImage"
  APPIMAGE_FILE="$OUTPUT_DIR/Prokudin-${VERSION}-linux-x64.AppImage"
else
  mv "$APPIMAGE_FILE" "$OUTPUT_DIR/Prokudin-${VERSION}-linux-x64.AppImage"
fi

echo "Created $OUTPUT_DIR/Prokudin-${VERSION}-linux-x64.AppImage"

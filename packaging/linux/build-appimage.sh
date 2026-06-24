#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?Usage: build-appimage.sh <version> <publish-dir> <output-dir>}"
PUBLISH_DIR="${2:?missing publish dir}"
OUTPUT_DIR="${3:?missing output dir}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
ASSETS_DIR="$REPO_ROOT/assets"
APPDIR="$OUTPUT_DIR/Prokudin.AppDir"
OUTPUT_APPIMAGE="$OUTPUT_DIR/Prokudin-${VERSION}-linux-x64.AppImage"

rm -rf "$APPDIR"
mkdir -p "$OUTPUT_DIR" "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

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
export APPIMAGE_EXTRACT_AND_RUN=1

# Build AppDir only. Do not use linuxdeploy --output appimage here: its plugin
# tries to execute the AppImage after packaging, which fails on CI without FUSE.
./"$LINUXDEPLOY" --appdir "$APPDIR" --desktop-file="$APPDIR/prokudin.desktop" --icon-file="$APPDIR/prokudin.png"

export VERSION="$VERSION"
./"$APPIMAGETOOL" --no-appstream "$APPDIR" "$OUTPUT_APPIMAGE"

echo "Created $OUTPUT_APPIMAGE"

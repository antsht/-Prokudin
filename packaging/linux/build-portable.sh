#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?Usage: build-portable.sh <version> <gui-publish-dir> <cli-publish-dir> <output-dir>}"
GUI_DIR="${2:?missing gui publish dir}"
CLI_DIR="${3:?missing cli publish dir}"
OUTPUT_DIR="${4:?missing output dir}"

mkdir -p "$OUTPUT_DIR"

GUI_ARCHIVE="$OUTPUT_DIR/Prokudin-${VERSION}-linux-x64-portable.tar.gz"
CLI_ARCHIVE="$OUTPUT_DIR/Prokudin-Cli-${VERSION}-linux-x64.tar.gz"

tar -C "$GUI_DIR" -czf "$GUI_ARCHIVE" Prokudin
tar -C "$CLI_DIR" -czf "$CLI_ARCHIVE" prokudin

cat > "$OUTPUT_DIR/README-portable.txt" <<EOF
Prokudin ${VERSION} (Linux x64 portable)

GUI:  tar -xzf Prokudin-${VERSION}-linux-x64-portable.tar.gz && chmod +x Prokudin && ./Prokudin
CLI:  tar -xzf Prokudin-Cli-${VERSION}-linux-x64.tar.gz && chmod +x prokudin && ./prokudin --help
EOF

echo "Created $GUI_ARCHIVE"
echo "Created $CLI_ARCHIVE"

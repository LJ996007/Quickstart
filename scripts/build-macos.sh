#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MACOS_DIR="$ROOT_DIR/macos/QuickstartMac"
SRC_DIR="$MACOS_DIR/QuickstartMac"
BUILD_DIR="$MACOS_DIR/build/Release"
APP_NAME="QuickstartMac"
DISPLAY_NAME="Quickstart"
BUNDLE_ID="ai.markl.QuickstartMac"
DEPLOYMENT_TARGET="${MACOSX_DEPLOYMENT_TARGET:-14.0}"
ARCHS="${ARCHS:-$(uname -m)}"
SDK_PATH="$(xcrun --show-sdk-path)"

if ! command -v swiftc >/dev/null 2>&1; then
  echo "error: swiftc not found. Install Xcode Command Line Tools or Xcode." >&2
  exit 1
fi

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR/objects" "$BUILD_DIR/$APP_NAME.app/Contents/MacOS" "$BUILD_DIR/$APP_NAME.app/Contents/Resources"

SOURCES=()
while IFS= read -r source_file; do
  SOURCES+=("$source_file")
done < <(find "$SRC_DIR" -name '*.swift' | sort)

BINARIES=()
for ARCH in $ARCHS; do
  OUT="$BUILD_DIR/objects/$APP_NAME-$ARCH"
  echo "Building $APP_NAME for $ARCH..."
  swiftc \
    -parse-as-library \
    -O \
    -target "$ARCH-apple-macosx$DEPLOYMENT_TARGET" \
    -sdk "$SDK_PATH" \
    -framework AppKit \
    "${SOURCES[@]}" \
    -o "$OUT"
  BINARIES+=("$OUT")
done

EXECUTABLE="$BUILD_DIR/$APP_NAME.app/Contents/MacOS/$APP_NAME"
if [ "${#BINARIES[@]}" -gt 1 ]; then
  lipo -create "${BINARIES[@]}" -output "$EXECUTABLE"
else
  cp "${BINARIES[0]}" "$EXECUTABLE"
fi
chmod +x "$EXECUTABLE"

python3 - "$SRC_DIR/Info.plist" "$BUILD_DIR/$APP_NAME.app/Contents/Info.plist" "$APP_NAME" "$BUNDLE_ID" <<'PY'
import sys
src, dst, executable, bundle_id = sys.argv[1:]
text = open(src, encoding='utf-8').read()
text = text.replace('$(EXECUTABLE_NAME)', executable)
text = text.replace('$(PRODUCT_BUNDLE_IDENTIFIER)', bundle_id)
text = text.replace('$(PRODUCT_NAME)', executable)
open(dst, 'w', encoding='utf-8').write(text)
PY

cat > "$BUILD_DIR/$APP_NAME.app/Contents/PkgInfo" <<'EOF'
APPL????
EOF

# Ad-hoc sign so Gatekeeper/quarantine checks and URL scheme registration behave better locally.
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$BUILD_DIR/$APP_NAME.app" >/dev/null
fi

ZIP_PATH="$BUILD_DIR/$DISPLAY_NAME-macOS.zip"
(
  cd "$BUILD_DIR"
  ditto -c -k --keepParent "$APP_NAME.app" "$ZIP_PATH"
)

echo "Built: $BUILD_DIR/$APP_NAME.app"
echo "Archive: $ZIP_PATH"

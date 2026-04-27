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
SDK_PATH="$(xcrun --sdk macosx --show-sdk-path)"
SWIFTC="$(xcrun --sdk macosx --find swiftc)"
MODULE_CACHE_DIR="$BUILD_DIR/module-cache"

if [ -z "$SWIFTC" ] || [ ! -x "$SWIFTC" ]; then
  echo "error: swiftc not found via xcrun. Install Xcode Command Line Tools or Xcode." >&2
  exit 1
fi

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR/objects" "$MODULE_CACHE_DIR" "$BUILD_DIR/$APP_NAME.app/Contents/MacOS" "$BUILD_DIR/$APP_NAME.app/Contents/Resources"

SOURCES=()
while IFS= read -r source_file; do
  SOURCES+=("$source_file")
done < <(find "$SRC_DIR" -name '*.swift' | sort)

BINARIES=()
for ARCH in $ARCHS; do
  OUT="$BUILD_DIR/objects/$APP_NAME-$ARCH"
  echo "Building $APP_NAME for $ARCH..."
  "$SWIFTC" \
    -O \
    -target "$ARCH-apple-macosx$DEPLOYMENT_TARGET" \
    -sdk "$SDK_PATH" \
    -module-cache-path "$MODULE_CACHE_DIR/$ARCH" \
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

# Use Developer ID signing for distributable builds, or ad-hoc signing for local builds.
# Example: APP_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" scripts/build-macos.sh
if command -v codesign >/dev/null 2>&1; then
  if [ -n "${APP_SIGN_IDENTITY:-}" ]; then
    codesign --force --deep --options runtime --timestamp --sign "$APP_SIGN_IDENTITY" "$BUILD_DIR/$APP_NAME.app"
    codesign --verify --deep --strict --verbose=2 "$BUILD_DIR/$APP_NAME.app"
  else
    codesign --force --deep --sign - "$BUILD_DIR/$APP_NAME.app" >/dev/null
    echo "warning: app was ad-hoc signed. Distribute with APP_SIGN_IDENTITY and notarization for Gatekeeper." >&2
  fi
fi

ZIP_PATH="$BUILD_DIR/$DISPLAY_NAME-macOS.zip"
(
  cd "$BUILD_DIR"
  ditto -c -k --keepParent "$APP_NAME.app" "$ZIP_PATH"
)

echo "Built: $BUILD_DIR/$APP_NAME.app"
echo "Archive: $ZIP_PATH"

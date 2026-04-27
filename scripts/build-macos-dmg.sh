#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MACOS_DIR="$ROOT_DIR/macos/QuickstartMac"
RELEASE_DIR="$MACOS_DIR/build/Release"
APP_NAME="QuickstartMac"
DISPLAY_NAME="Quickstart"
VOLUME_NAME="Quickstart Installer"
DMG_PATH="$RELEASE_DIR/$DISPLAY_NAME-macOS.dmg"
STAGE_DIR="$MACOS_DIR/build/dmg-stage"

# Build the .app first unless explicitly skipped.
if [ "${SKIP_APP_BUILD:-0}" != "1" ]; then
  ARCHS="${ARCHS:-arm64 x86_64}" "$ROOT_DIR/scripts/build-macos.sh"
fi

APP_PATH="$RELEASE_DIR/$APP_NAME.app"
if [ ! -d "$APP_PATH" ]; then
  echo "error: missing app bundle: $APP_PATH" >&2
  exit 1
fi

rm -rf "$STAGE_DIR" "$DMG_PATH"
mkdir -p "$STAGE_DIR"

# Copy the app and create the standard drag-to-Applications alias.
ditto "$APP_PATH" "$STAGE_DIR/$APP_NAME.app"
ln -s /Applications "$STAGE_DIR/Applications"

cat > "$STAGE_DIR/README.txt" <<'EOF'
Quickstart macOS 安装说明

1. 打开这个 DMG。
2. 将 QuickstartMac.app 拖到 Applications 文件夹。
3. 从 Applications 启动 QuickstartMac。
4. 程序会显示 Dock 图标，并保留 macOS 顶部菜单栏的 Quickstart 入口。

如果提示“无法验证开发者”，请在 Finder 中右键 QuickstartMac.app，选择“打开”；或到“系统设置 → 隐私与安全性”允许打开。
EOF

# Create a compressed read-only DMG.
hdiutil create \
  -volname "$VOLUME_NAME" \
  -srcfolder "$STAGE_DIR" \
  -ov \
  -format UDZO \
  "$DMG_PATH"

hdiutil verify "$DMG_PATH"

# Optional: sign and notarize the DMG for distribution.
# Example:
#   APP_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
#   DMG_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
#   NOTARY_PROFILE="notarytool-profile" \
#   scripts/build-macos-dmg.sh
if [ -n "${DMG_SIGN_IDENTITY:-${APP_SIGN_IDENTITY:-}}" ]; then
  codesign --force --sign "${DMG_SIGN_IDENTITY:-$APP_SIGN_IDENTITY}" "$DMG_PATH"
  codesign --verify --verbose=2 "$DMG_PATH"
fi

if [ -n "${NOTARY_PROFILE:-}" ]; then
  if [ -z "${APP_SIGN_IDENTITY:-}" ]; then
    echo "error: NOTARY_PROFILE requires APP_SIGN_IDENTITY so the app inside the DMG is Developer ID signed." >&2
    exit 1
  fi

  xcrun notarytool submit "$DMG_PATH" --keychain-profile "$NOTARY_PROFILE" --wait
  xcrun stapler staple "$DMG_PATH"
  xcrun stapler validate "$DMG_PATH"
fi

ls -lh "$DMG_PATH"
echo "Built DMG: $DMG_PATH"

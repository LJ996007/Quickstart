#!/usr/bin/env bash
# 在 macOS 上把 Quickstart.Mac（Avalonia/.NET）打包为 Quickstart.app。
# 用法：
#   scripts/build-mac-app.sh                # 默认 osx-arm64 / Release
#   RID=osx-x64 scripts/build-mac-app.sh    # Intel
set -euo pipefail

RID="${RID:-osx-arm64}"
CONFIG="${CONFIG:-Release}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/Quickstart.Mac/Quickstart.Mac.csproj"
OUT="$ROOT/Quickstart.Mac/build/publish-$RID"
APP="$ROOT/Quickstart.Mac/build/Quickstart.app"
EXEC="Quickstart"   # 与 csproj 的 AssemblyName 一致

echo "==> dotnet publish ($RID / $CONFIG)"
dotnet publish "$PROJ" -c "$CONFIG" -r "$RID" --self-contained true \
  -p:PublishSingleFile=false -p:UseAppHost=true -o "$OUT"

echo "==> 构建 .app bundle"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$OUT/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/$EXEC" 2>/dev/null || true

# 可选图标：尝试用 sips/iconutil 从 Assets/app.ico 生成 .icns
ICON_KEY=""
ICON_SRC="$ROOT/Quickstart.Mac/Assets/app.ico"
if [ -f "$ICON_SRC" ] && command -v sips >/dev/null && command -v iconutil >/dev/null; then
  TMP="$(mktemp -d)"
  if sips -s format png "$ICON_SRC" --out "$TMP/icon.png" >/dev/null 2>&1; then
    ICONSET="$TMP/icon.iconset"; mkdir -p "$ICONSET"
    for s in 16 32 64 128 256 512; do
      sips -z "$s" "$s" "$TMP/icon.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null 2>&1 || true
    done
    if iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Quickstart.icns" >/dev/null 2>&1; then
      ICON_KEY="Quickstart"
    fi
  fi
  rm -rf "$TMP"
fi

ICON_PLIST=""
[ -n "$ICON_KEY" ] && ICON_PLIST="<key>CFBundleIconFile</key><string>$ICON_KEY</string>"

echo "==> 写入 Info.plist"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Quickstart</string>
  <key>CFBundleDisplayName</key><string>Quickstart</string>
  <key>CFBundleIdentifier</key><string>com.quickstart.mac</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundleExecutable</key><string>$EXEC</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  $ICON_PLIST
  <key>CFBundleURLTypes</key>
  <array>
    <dict>
      <key>CFBundleURLName</key><string>Quickstart Protocol</string>
      <key>CFBundleURLSchemes</key><array><string>quickstart</string></array>
    </dict>
  </array>
</dict>
</plist>
PLIST

echo "==> ad-hoc 代码签名"
codesign --force --deep --sign - "$APP" 2>/dev/null || echo "（codesign 跳过/失败，本机运行仍可）"

echo "完成：$APP"
echo "运行：open \"$APP\"   （首次需在 系统设置→隐私与安全性→辅助功能 授权右键拖拽手势）"

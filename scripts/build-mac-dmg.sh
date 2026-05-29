#!/usr/bin/env bash
# 在 macOS 上把 Quickstart.app 打包为可分发的 .dmg。
# 先运行 scripts/build-mac-app.sh 生成 .app。
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/Quickstart.Mac/build/Quickstart.app"
DMG="$ROOT/Quickstart.Mac/build/Quickstart-macOS.dmg"

if [ ! -d "$APP" ]; then
  echo "未找到 $APP，请先运行 scripts/build-mac-app.sh" >&2
  exit 1
fi

echo "==> 构建 DMG"
rm -f "$DMG"
STAGE="$(mktemp -d)"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Quickstart" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"

echo "完成：$DMG"
echo "注意：ad-hoc 签名仅适合本机；分发到其它 Mac 需 Developer ID 签名 + 公证，否则 Gatekeeper 会拦截。"

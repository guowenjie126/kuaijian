#!/bin/zsh
set -euo pipefail

ROOT="${0:A:h}"
BUILD="$ROOT/build"
APP="$BUILD/快剪重建版.app"
CONTENTS="$APP/Contents"
MACOS="$CONTENTS/MacOS"
RES="$CONTENTS/Resources"

rm -rf "$APP"
mkdir -p "$MACOS" "$RES"

SDK="$(xcrun --sdk macosx --show-sdk-path)"
SWIFT="$(xcrun --find swiftc)"
TARGET="$(uname -m)-apple-macos13.0"

"$SWIFT" \
  -sdk "$SDK" \
  -target "$TARGET" \
  -parse-as-library \
  -framework SwiftUI \
  -framework AppKit \
  -framework Combine \
  -framework UniformTypeIdentifiers \
  -F "/Applications/快剪.app/Contents/Frameworks" \
  -framework Sparkle \
  -Xlinker -rpath -Xlinker "@loader_path/../Frameworks" \
  "$ROOT"/Sources/*.swift \
  -o "$MACOS/KuaiJianNative"

cp "$ROOT/Resources/Info.plist" "$CONTENTS/Info.plist"

# 复用正式版已有的应用图标，登录/注册页与 Dock 图标保持一致。
if [[ -f "/Applications/快剪.app/Contents/Resources/快剪.icns" ]]; then
  cp "/Applications/快剪.app/Contents/Resources/快剪.icns" "$RES/快剪.icns"
fi

if [[ -d "/Applications/快剪.app/Contents/Resources" ]]; then
  for resource in yt-dlp_macos yt-dlp-package; do
    [[ -e "/Applications/快剪.app/Contents/Resources/$resource" ]] && cp -R "/Applications/快剪.app/Contents/Resources/$resource" "$RES/"
  done
fi

if [[ -d "/Applications/快剪.app/Contents/Frameworks/Sparkle.framework" ]]; then
  mkdir -p "$CONTENTS/Frameworks"
  cp -R "/Applications/快剪.app/Contents/Frameworks/Sparkle.framework" "$CONTENTS/Frameworks/"
fi

codesign --force --deep --sign - "$APP"
echo "built: $APP"

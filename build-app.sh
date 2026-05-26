#!/usr/bin/env bash
set -euo pipefail

# Собирает ArkManager.app — обычный macOS-бандл (framework-dependent).
# Требует установленный .NET 10 runtime на машине (publish без --self-contained).
# Результат: dist/ArkManager.app — двойной клик из Finder / закинуть в /Applications.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/ArkManager.Desktop/ArkManager.App.csproj"
RID="osx-arm64"
CONFIG="Release"

APP_NAME="ArkManager"
EXECUTABLE="ArkManager.App"      # apphost-бинарь = имя сборки проекта
BUNDLE_ID="com.arkmanager.app"
VERSION="1.0.0"

DIST="$ROOT/dist"
APP="$DIST/$APP_NAME.app"
PUBLISH="$ROOT/src/ArkManager.Desktop/bin.noindex/$CONFIG/net10.0/$RID/publish"

echo "==> dotnet publish ($CONFIG / $RID / framework-dependent)"
dotnet publish "$PROJECT" -c "$CONFIG" -r "$RID" --self-contained false

echo "==> Сборка бандла $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Весь publish-вывод кладём в MacOS/ — apphost ищет .dll рядом с собой.
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

# Иконка: .ico -> .icns (best-effort, без неё бандл тоже валиден).
ICON_SRC="$ROOT/src/ArkManager.Desktop/Assets/AppIcon.png"
ICON_NAME="AppIcon"
if [[ -f "$ICON_SRC" ]]; then
  WORK="$(mktemp -d)"
  ICONSET="$WORK/$ICON_NAME.iconset"
  mkdir -p "$ICONSET"
  if sips -s format png "$ICON_SRC" --out "$WORK/icon.png" >/dev/null 2>&1; then
    gen() { sips -z "$1" "$1" "$WORK/icon.png" --out "$ICONSET/$2" >/dev/null 2>&1 || true; }
    gen 16   icon_16x16.png;     gen 32   icon_16x16@2x.png
    gen 32   icon_32x32.png;     gen 64   icon_32x32@2x.png
    gen 128  icon_128x128.png;   gen 256  icon_128x128@2x.png
    gen 256  icon_256x256.png;   gen 512  icon_256x256@2x.png
    gen 512  icon_512x512.png;   gen 1024 icon_512x512@2x.png
    if iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/$ICON_NAME.icns" 2>/dev/null; then
      echo "    иконка собрана"
    else
      echo "    iconutil failed — собираю без иконки"; ICON_NAME=""
    fi
  else
    echo "    sips не прочитал .ico — собираю без иконки"; ICON_NAME=""
  fi
else
  ICON_NAME=""
fi

echo "==> Info.plist"
ICON_KEY=""
[[ -n "$ICON_NAME" ]] && ICON_KEY="	<key>CFBundleIconFile</key>
	<string>$ICON_NAME</string>"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>$APP_NAME</string>
	<key>CFBundleDisplayName</key>
	<string>$APP_NAME</string>
	<key>CFBundleIdentifier</key>
	<string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key>
	<string>$VERSION</string>
	<key>CFBundleShortVersionString</key>
	<string>$VERSION</string>
	<key>CFBundleExecutable</key>
	<string>$EXECUTABLE</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>LSMinimumSystemVersion</key>
	<string>11.0</string>
$ICON_KEY
</dict>
</plist>
PLIST

# Ad-hoc подпись — на Apple Silicon без неё Gatekeeper ругается на «повреждённое».
echo "==> codesign (ad-hoc)"
codesign --force --deep --sign - "$APP" 2>/dev/null && echo "    подписано" || echo "    codesign недоступен — пропускаю"

echo ""
echo "Готово: $APP"
echo "Запуск:        open \"$APP\""
echo "В Applications: cp -R \"$APP\" /Applications/"

#!/usr/bin/env bash
set -euo pipefail

# Cross-OS build script. Runs on macOS or Linux hosts.
#   ./build.sh                        # all 3 targets
#   ./build.sh --target macos linux   # subset
#
# Phase 1: builds self-contained .NET bundles. Wine is NOT bundled yet —
# Mac/Linux outputs still require system wine. Phase 2 (Task 17/18) adds
# wine into the package.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/ArkManager.Desktop/ArkManager.App.csproj"
CONFIG="Release"
APP_NAME="ArkManager"
BUNDLE_ID="com.arkmanager.app"

# Read <Version> from Directory.Build.props.
VERSION=$(awk -F '[<>]' '/<Version>/{print $3; exit}' "$ROOT/Directory.Build.props")
[[ -z "$VERSION" ]] && { echo "Failed to read Version from Directory.Build.props"; exit 1; }

DIST="$ROOT/dist"
mkdir -p "$DIST"

# --- parse args --------------------------------------------------------------
TARGETS=()
if [[ $# -eq 0 ]]; then
  TARGETS=(macos linux windows)
else
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --target) shift; while [[ $# -gt 0 && "$1" != --* ]]; do TARGETS+=("$1"); shift; done ;;
      all) TARGETS=(macos linux windows); shift ;;
      *) echo "Unknown arg: $1"; exit 1 ;;
    esac
  done
fi

rid_of() {
  case "$1" in
    macos)   echo "osx-arm64" ;;
    linux)   echo "linux-x64" ;;
    windows) echo "win-x64" ;;
    *) echo "Unknown target: $1"; exit 1 ;;
  esac
}

publish_for() {
  local target="$1" rid; rid=$(rid_of "$target")
  echo "==> dotnet publish ($CONFIG / $rid / self-contained)" >&2
  dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" \
    --self-contained true \
    /p:PublishSingleFile=false \
    /p:PublishTrimmed=false >&2
  echo "$ROOT/src/ArkManager.Desktop/bin.noindex/$CONFIG/net10.0/$rid/publish"
}

# --- package: macOS ----------------------------------------------------------
package_macos() {
  local publish="$1"
  local app="$DIST/$APP_NAME-$VERSION-macos-arm64/$APP_NAME.app"
  rm -rf "$DIST/$APP_NAME-$VERSION-macos-arm64"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$publish/." "$app/Contents/MacOS/"

  # Icon (best-effort).
  local ICON_SRC="$ROOT/src/ArkManager.Desktop/Assets/AppIcon.png"
  local ICON_NAME=""
  if [[ -f "$ICON_SRC" ]]; then
    local WORK; WORK="$(mktemp -d)"
    local ICONSET="$WORK/AppIcon.iconset"; mkdir -p "$ICONSET"
    if sips -s format png "$ICON_SRC" --out "$WORK/icon.png" >/dev/null 2>&1; then
      gen() { sips -z "$1" "$1" "$WORK/icon.png" --out "$ICONSET/$2" >/dev/null 2>&1 || true; }
      gen 16   icon_16x16.png;     gen 32   icon_16x16@2x.png
      gen 32   icon_32x32.png;     gen 64   icon_32x32@2x.png
      gen 128  icon_128x128.png;   gen 256  icon_128x128@2x.png
      gen 256  icon_256x256.png;   gen 512  icon_256x256@2x.png
      gen 512  icon_512x512.png;   gen 1024 icon_512x512@2x.png
      iconutil -c icns "$ICONSET" -o "$app/Contents/Resources/AppIcon.icns" 2>/dev/null && ICON_NAME="AppIcon" || true
    fi
  fi
  local ICON_KEY=""
  [[ -n "$ICON_NAME" ]] && ICON_KEY="	<key>CFBundleIconFile</key>
	<string>$ICON_NAME</string>"

  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>$APP_NAME</string>
	<key>CFBundleDisplayName</key><string>$APP_NAME</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key><string>$VERSION</string>
	<key>CFBundleShortVersionString</key><string>$VERSION</string>
	<key>CFBundleExecutable</key><string>$APP_NAME</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>LSMinimumSystemVersion</key><string>11.0</string>
$ICON_KEY
</dict>
</plist>
PLIST

  # Ad-hoc sign so Gatekeeper accepts on Apple Silicon.
  codesign --force --deep --sign - "$app" 2>/dev/null || echo "    codesign skipped"

  ( cd "$DIST/$APP_NAME-$VERSION-macos-arm64" && zip -qr "../$APP_NAME-$VERSION-macos-arm64.zip" "$APP_NAME.app" )
  echo "    -> $DIST/$APP_NAME-$VERSION-macos-arm64.zip"
}

# --- package: Windows --------------------------------------------------------
package_windows() {
  local publish="$1"
  local out="$DIST/$APP_NAME-$VERSION-windows-x64"
  rm -rf "$out"; mkdir -p "$out"
  cp -R "$publish/." "$out/"
  ( cd "$DIST" && zip -qr "$APP_NAME-$VERSION-windows-x64.zip" "$APP_NAME-$VERSION-windows-x64" )
  echo "    -> $DIST/$APP_NAME-$VERSION-windows-x64.zip"
}

# --- package: Linux ----------------------------------------------------------
package_linux() {
  local publish="$1"
  local out="$DIST/$APP_NAME-$VERSION-linux-x64"
  rm -rf "$out"; mkdir -p "$out"
  cp -R "$publish/." "$out/"
  # Ensure the apphost has +x (publish output usually already has it on Unix).
  chmod +x "$out/$APP_NAME" 2>/dev/null || true
  ( cd "$DIST" && tar -czf "$APP_NAME-$VERSION-linux-x64.tar.gz" "$APP_NAME-$VERSION-linux-x64" )
  echo "    -> $DIST/$APP_NAME-$VERSION-linux-x64.tar.gz"
}

# --- run ---------------------------------------------------------------------
for t in "${TARGETS[@]}"; do
  echo ""
  echo "### Target: $t ###"
  publish_path=$(publish_for "$t")
  case "$t" in
    macos)   package_macos   "$publish_path" ;;
    linux)   package_linux   "$publish_path" ;;
    windows) package_windows "$publish_path" ;;
  esac
done

echo ""
echo "Done. Artifacts in $DIST"

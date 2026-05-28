#!/usr/bin/env bash
set -euo pipefail

# Cross-OS build script. Runs on macOS or Linux hosts.
#   ./build.sh                        # all 3 targets
#   ./build.sh --target macos linux   # subset
#
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

WINE_SOURCES="$ROOT/build/wine-sources.json"
WINE_CACHE="${ARKMANAGER_WINE_CACHE:-$HOME/.cache/ark-manager/wine}"
mkdir -p "$WINE_CACHE"

# json_field <key1> <key2>  e.g. json_field macos-arm64 url
json_field() {
  python3 -c "import json,sys; print(json.load(open('$WINE_SOURCES'))['$1']['$2'])"
}

ensure_wine() {
  # ensure_wine <macos-arm64|linux-x64> → echoes absolute path to the extracted wine root.
  local key="$1"
  local url sha extracted_dir
  url=$(json_field "$key" url)
  sha=$(json_field "$key" sha256)
  extracted_dir=$(json_field "$key" extractedWineDir)

  local cache_dir="$WINE_CACHE/${sha:0:12}"
  local root="$cache_dir/$extracted_dir"
  if [[ -d "$root" && (-x "$root/bin/wine64" || -x "$root/bin/wine") ]]; then
    echo "$root"
    return
  fi

  mkdir -p "$cache_dir"
  local archive="$cache_dir/wine.tar"
  echo "==> downloading wine for $key" >&2
  curl -L --fail --silent --show-error -o "$archive" "$url"

  local actual_sha
  actual_sha=$(shasum -a 256 "$archive" | awk '{print $1}')
  if [[ "$actual_sha" != "$sha" ]]; then
    echo "wine $key sha256 mismatch: expected $sha, got $actual_sha" >&2
    rm -f "$archive"
    exit 1
  fi

  echo "==> extracting wine for $key" >&2
  # Auto-detect tar compression by extension.
  case "$url" in
    *.tar.xz)  tar -xJf "$archive" -C "$cache_dir" ;;
    *.tar.gz)  tar -xzf "$archive" -C "$cache_dir" ;;
    *.tar.zst) tar --use-compress-program=zstd -xf "$archive" -C "$cache_dir" ;;
    *) echo "Unknown wine archive format: $url" >&2; exit 1 ;;
  esac
  rm -f "$archive"

  if [[ ! -x "$root/bin/wine64" && ! -x "$root/bin/wine" ]]; then
    echo "wine $key extracted but neither bin/wine64 nor bin/wine found in $root" >&2
    exit 1
  fi
  echo "$root"
}

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
  # Use -p: (not /p:) so the args survive MSYS / Git Bash on Windows runners,
  # which otherwise rewrites /-prefixed tokens as Unix paths and strips them.
  dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false >&2
  echo "$ROOT/src/ArkManager.Desktop/bin.noindex/$CONFIG/net10.0/$rid/publish"
}

# --- package: macOS ----------------------------------------------------------
package_macos() {
  local publish="$1"
  local app="$DIST/$APP_NAME-$VERSION-macos-arm64/$APP_NAME.app"
  rm -rf "$DIST/$APP_NAME-$VERSION-macos-arm64"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$publish/." "$app/Contents/MacOS/"

  local wine_root; wine_root=$(ensure_wine macos-arm64)
  mkdir -p "$app/Contents/Resources/wine"
  cp -R "$wine_root/." "$app/Contents/Resources/wine/"

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
  local zip_path="$DIST/$APP_NAME-$VERSION-windows-x64.zip"
  rm -f "$zip_path"
  if command -v zip >/dev/null 2>&1; then
    ( cd "$DIST" && zip -qr "$APP_NAME-$VERSION-windows-x64.zip" "$APP_NAME-$VERSION-windows-x64" )
  else
    # Windows GitHub runners run this script in Git Bash without `zip`.
    # PowerShell's Compress-Archive is always available on Windows.
    powershell -NoLogo -NoProfile -Command \
      "Compress-Archive -Path '$out' -DestinationPath '$zip_path' -Force"
  fi
  echo "    -> $zip_path"
}

# --- package: Linux ----------------------------------------------------------
package_linux() {
  local publish="$1"
  local out="$DIST/$APP_NAME-$VERSION-linux-x64"
  rm -rf "$out"; mkdir -p "$out"
  cp -R "$publish/." "$out/"

  local wine_root; wine_root=$(ensure_wine linux-x64)
  mkdir -p "$out/wine"
  cp -R "$wine_root/." "$out/wine/"

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

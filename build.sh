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

# --- wine LGPL compliance ----------------------------------------------------
# Wine is LGPL-2.1. We redistribute pre-built binaries, so every bundle ships the
# license text plus a notice pointing at the corresponding source. Wine runs as a
# separate process (not linked into the app) and lives in a replaceable folder,
# which satisfies the LGPL relink/replace clause by construction.
place_wine_licenses() {
  # place_wine_licenses <wine_dir> <macos-arm64|linux-x64>
  local wine_dir="$1" key="$2"
  cp "$ROOT/build/licenses/COPYING.LGPL-2.1" "$wine_dir/COPYING.LGPL-2.1"

  local bin_url wine_ver upstream_src builder build_src
  bin_url=$(json_field "$key" url)
  case "$key" in
    macos-arm64)
      wine_ver="Wine Stable 11.0_1 (based on Wine 11.0)"
      upstream_src="https://dl.winehq.org/wine/source/11.0/wine-11.0.tar.xz"
      builder="Gcenx/macOS_Wine_builds"
      build_src="https://github.com/Gcenx/macOS_Wine_builds" ;;
    linux-x64)
      wine_ver="lutris-wine 7.2-2 (based on Wine 7.2)"
      upstream_src="https://dl.winehq.org/wine/source/7.2/wine-7.2.tar.xz"
      builder="lutris/wine"
      build_src="https://github.com/lutris/wine" ;;
  esac

  cat > "$wine_dir/NOTICE.wine" <<NOTICE
This product bundles $wine_ver, licensed under the GNU Lesser General Public
License, version 2.1 — see COPYING.LGPL-2.1 in this folder.

Wine is a separate program, launched by ArkManager as its own process; it is not
linked into the ArkManager binary. You may replace the contents of this 'wine'
folder with your own build of Wine.

Bundled binary build:
  $bin_url

Corresponding source code:
  Wine source:    $upstream_src
  Build project:  $build_src ($builder)

Wine is free software — see <https://www.winehq.org/>. ArkManager itself is
MIT-licensed and is not derived from Wine.
NOTICE
}

# Third-party notices + embedded-font licenses (OFL-1.1). The fonts are compiled
# into the app on every platform, so this runs for all three targets.
place_app_licenses() {
  # place_app_licenses <dir>
  local dir="$1"
  cp "$ROOT/build/licenses/THIRD-PARTY-NOTICES.txt" "$dir/THIRD-PARTY-NOTICES.txt"
  cp "$ROOT/build/licenses/IBMPlex-OFL.txt"         "$dir/IBMPlex-OFL.txt"
  cp "$ROOT/build/licenses/ZillaSlab-OFL.txt"       "$dir/ZillaSlab-OFL.txt"
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

# Drop artifacts of OTHER versions from dist/ so it doesn't accumulate every past
# release locally. Keeps the current $VERSION (incl. other targets' archives from a
# previous run) and never touches non-ArkManager files. No-op on CI (fresh checkout).
prune_old_versions() {
  shopt -s nullglob
  local keep="$APP_NAME-$VERSION-" entry base
  for entry in "$DIST/$APP_NAME-"*; do
    base=$(basename "$entry")
    [[ "$base" == "$keep"* ]] && continue
    echo "==> removing stale artifact: $base" >&2
    rm -rf "$entry"
  done
  shopt -u nullglob
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
  place_wine_licenses "$app/Contents/Resources/wine" macos-arm64
  place_app_licenses "$app/Contents/Resources"

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

  # ditto, not zip: zip materialises the ~67 symlinks in the wine tree
  # (+100 MB, mangled layout). The ad-hoc seal still does not survive any
  # extractor -- that needs notarization.
  ditto -c -k --keepParent "$app" "$DIST/$APP_NAME-$VERSION-macos-arm64.zip"
  echo "    -> $DIST/$APP_NAME-$VERSION-macos-arm64.zip"
}

# --- package: Windows --------------------------------------------------------
package_windows() {
  local publish="$1"
  local out="$DIST/$APP_NAME-$VERSION-windows-x64"
  rm -rf "$out"; mkdir -p "$out"
  cp -R "$publish/." "$out/"
  place_app_licenses "$out"
  local zip_path="$DIST/$APP_NAME-$VERSION-windows-x64.zip"
  rm -f "$zip_path"
  if command -v zip >/dev/null 2>&1; then
    ( cd "$DIST" && zip -qr "$APP_NAME-$VERSION-windows-x64.zip" "$APP_NAME-$VERSION-windows-x64" )
  else
    # Windows GitHub runners run this script in Git Bash without `zip`.
    # PowerShell's Compress-Archive is always available on Windows, but it
    # needs native Windows paths (Git Bash MSYS paths like /d/a/... fail).
    local win_out win_zip
    win_out=$(cygpath -w "$out")
    win_zip=$(cygpath -w "$zip_path")
    powershell -NoLogo -NoProfile -Command \
      "Compress-Archive -Path '$win_out' -DestinationPath '$win_zip' -Force"
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
  place_wine_licenses "$out/wine" linux-x64
  place_app_licenses "$out"

  # Ensure the apphost has +x (publish output usually already has it on Unix).
  chmod +x "$out/$APP_NAME" 2>/dev/null || true
  ( cd "$DIST" && tar -czf "$APP_NAME-$VERSION-linux-x64.tar.gz" "$APP_NAME-$VERSION-linux-x64" )
  echo "    -> $DIST/$APP_NAME-$VERSION-linux-x64.tar.gz"
}

# --- run ---------------------------------------------------------------------
prune_old_versions

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

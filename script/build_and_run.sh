#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-run}"
APP_NAME="WiFiHealthConsole"
BUNDLE_ID="com.meyaomiao.WiFiHealthConsole"
MIN_SYSTEM_VERSION="14.0"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_FILE="$ROOT_DIR/VERSION"
if [[ ! -f "$VERSION_FILE" ]]; then
  echo "Product version file not found: $VERSION_FILE" >&2
  exit 1
fi
VERSION="$(/usr/bin/tr -d '[:space:]' < "$VERSION_FILE")"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Invalid product version in $VERSION_FILE: $VERSION" >&2
  exit 1
fi
BUILD_NUMBER="11"
DIST_DIR="$ROOT_DIR/dist"
APP_BUNDLE="$DIST_DIR/$APP_NAME.app"
APP_CONTENTS="$APP_BUNDLE/Contents"
APP_MACOS="$APP_CONTENTS/MacOS"
APP_RESOURCES="$APP_CONTENTS/Resources"
APP_BINARY="$APP_MACOS/$APP_NAME"
INFO_PLIST="$APP_CONTENTS/Info.plist"
APP_ICON_SOURCE="$ROOT_DIR/Assets/AppIcon.icns"
LEGAL_RESOURCE_FILES=(
  "LICENSE"
  "THIRD-PARTY-NOTICES.md"
  "CODE_SIGNING_POLICY.md"
)
LICENSES_SOURCE="$ROOT_DIR/licenses"

for relative_path in "${LEGAL_RESOURCE_FILES[@]}"; do
  source_path="$ROOT_DIR/$relative_path"
  if [[ ! -f "$source_path" ]]; then
    echo "Required legal file not found: $source_path" >&2
    exit 1
  fi
done
if [[ ! -d "$LICENSES_SOURCE" ]]; then
  echo "Required third-party license directory not found: $LICENSES_SOURCE" >&2
  exit 1
fi
if [[ -z "$(/usr/bin/find "$LICENSES_SOURCE" -type f -print -quit)" ]]; then
  echo "Required third-party license directory is empty: $LICENSES_SOURCE" >&2
  exit 1
fi

pkill -x "$APP_NAME" >/dev/null 2>&1 || true

cd "$ROOT_DIR"
swift build
BUILD_BINARY="$(swift build --show-bin-path)/$APP_NAME"

rm -rf "$APP_BUNDLE"
mkdir -p "$APP_MACOS" "$APP_RESOURCES"
cp "$BUILD_BINARY" "$APP_BINARY"
cp "$APP_ICON_SOURCE" "$APP_RESOURCES/AppIcon.icns"
for relative_path in "${LEGAL_RESOURCE_FILES[@]}"; do
  /usr/bin/ditto "$ROOT_DIR/$relative_path" "$APP_RESOURCES/$relative_path"
done
/usr/bin/ditto "$LICENSES_SOURCE" "$APP_RESOURCES/licenses"
chmod +x "$APP_BINARY"

cat >"$INFO_PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleName</key>
  <string>Wi-Fi 体检台</string>
  <key>CFBundleDisplayName</key>
  <string>Wi-Fi 体检台</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon.icns</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleVersion</key>
  <string>$BUILD_NUMBER</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>LSMinimumSystemVersion</key>
  <string>$MIN_SYSTEM_VERSION</string>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
  <key>NSLocationWhenInUseUsageDescription</key>
  <string>用于显示当前 Wi-Fi 名称、BSSID 并扫描附近信道，不会记录位置坐标。</string>
  <key>NSLocationUsageDescription</key>
  <string>用于显示当前 Wi-Fi 名称、BSSID 并扫描附近信道，不会记录位置坐标。</string>
</dict>
</plist>
PLIST

/usr/bin/codesign --force --sign - "$APP_BUNDLE" >/dev/null

open_app() {
  /usr/bin/open -n "$APP_BUNDLE"
}

case "$MODE" in
  run)
    open_app
    ;;
  --debug|debug)
    lldb -- "$APP_BINARY"
    ;;
  --logs|logs)
    open_app
    /usr/bin/log stream --info --style compact --predicate "process == \"$APP_NAME\""
    ;;
  --telemetry|telemetry)
    open_app
    /usr/bin/log stream --info --style compact --predicate "subsystem == \"$BUNDLE_ID\""
    ;;
  --verify|verify)
    open_app
    sleep 2
    pgrep -x "$APP_NAME" >/dev/null
    ;;
  *)
    echo "usage: $0 [run|--debug|--logs|--telemetry|--verify]" >&2
    exit 2
    ;;
esac

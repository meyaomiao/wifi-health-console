#!/usr/bin/env bash
set -euo pipefail

APP_EXECUTABLE="WiFiHealthConsole"
APP_DISPLAY_NAME="Wi-Fi 体检台"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION_FILE="$ROOT_DIR/VERSION"
if [[ ! -f "$VERSION_FILE" ]]; then
  echo "Product version file not found: $VERSION_FILE" >&2
  exit 1
fi
VERSION="$(/usr/bin/tr -d '[:space:]' < "$VERSION_FILE")"
BUILD_NUMBER="${BUILD_NUMBER:-10}"
BUNDLE_ID="${BUNDLE_ID:-com.meyaomiao.WiFiHealthConsole}"
MIN_SYSTEM_VERSION="14.0"
SIGNING_IDENTITY="${SIGNING_IDENTITY:-}"
NOTARYTOOL_PROFILE="${NOTARYTOOL_PROFILE:-}"

if [[ -z "$SIGNING_IDENTITY" ]]; then
  SIGNING_IDENTITY="$(
    /usr/bin/security find-identity -v -p codesigning 2>/dev/null |
      /usr/bin/awk -F'"' '/Developer ID Application:/{ print $2; exit }'
  )"
fi
SIGNING_IDENTITY="${SIGNING_IDENTITY:--}"

RELEASE_DIR="$ROOT_DIR/release"
SCRATCH_DIR="$ROOT_DIR/.build/release-universal"
APP_BUNDLE="$RELEASE_DIR/$APP_DISPLAY_NAME.app"
APP_CONTENTS="$APP_BUNDLE/Contents"
APP_MACOS="$APP_CONTENTS/MacOS"
APP_RESOURCES="$APP_CONTENTS/Resources"
APP_BINARY="$APP_MACOS/$APP_EXECUTABLE"
INFO_PLIST="$APP_CONTENTS/Info.plist"
APP_ICON_SOURCE="$ROOT_DIR/Assets/AppIcon.icns"
DMG_ROOT="$RELEASE_DIR/dmg-root"
DMG_PATH="$RELEASE_DIR/Wi-Fi-Health-Console-$VERSION-universal.dmg"
CHECKSUM_PATH="$DMG_PATH.sha256"

cd "$ROOT_DIR"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Invalid product version in $VERSION_FILE: $VERSION" >&2
  exit 1
fi
rm -rf "$APP_BUNDLE" "$DMG_ROOT" "$DMG_PATH" "$CHECKSUM_PATH" "$SCRATCH_DIR"
mkdir -p "$APP_MACOS" "$APP_RESOURCES" "$DMG_ROOT"

swift build \
  -c release \
  --arch arm64 \
  --arch x86_64 \
  --scratch-path "$SCRATCH_DIR"

BUILD_BINARY_DIR="$(swift build \
  -c release \
  --arch arm64 \
  --arch x86_64 \
  --scratch-path "$SCRATCH_DIR" \
  --show-bin-path)"

/usr/bin/ditto "$BUILD_BINARY_DIR/$APP_EXECUTABLE" "$APP_BINARY"
/usr/bin/ditto "$APP_ICON_SOURCE" "$APP_RESOURCES/AppIcon.icns"
chmod +x "$APP_BINARY"

cat >"$INFO_PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>zh_CN</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundleExecutable</key>
  <string>$APP_EXECUTABLE</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon.icns</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>$APP_DISPLAY_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$VERSION</string>
  <key>CFBundleVersion</key>
  <string>$BUILD_NUMBER</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key>
  <string>$MIN_SYSTEM_VERSION</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSLocationUsageDescription</key>
  <string>用于显示当前 Wi-Fi 名称、BSSID 并扫描附近信道，不会记录位置坐标。</string>
  <key>NSLocationWhenInUseUsageDescription</key>
  <string>用于显示当前 Wi-Fi 名称、BSSID 并扫描附近信道，不会记录位置坐标。</string>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
PLIST

/usr/bin/plutil -lint "$INFO_PLIST"

ARCHITECTURES="$(/usr/bin/lipo -archs "$APP_BINARY")"
if [[ "$ARCHITECTURES" != *"arm64"* || "$ARCHITECTURES" != *"x86_64"* ]]; then
  echo "Universal build failed; found architectures: $ARCHITECTURES" >&2
  exit 1
fi

if [[ "$SIGNING_IDENTITY" == "-" ]]; then
  /usr/bin/codesign --force --deep --options runtime --sign - "$APP_BUNDLE"
  echo "Warning: using ad hoc signing; Gatekeeper will not treat this as a notarized public release." >&2
else
  /usr/bin/codesign \
    --force \
    --deep \
    --options runtime \
    --timestamp \
    --sign "$SIGNING_IDENTITY" \
    "$APP_BUNDLE"
fi

/usr/bin/codesign --verify --deep --strict --verbose=2 "$APP_BUNDLE"

/usr/bin/ditto "$APP_BUNDLE" "$DMG_ROOT/$APP_DISPLAY_NAME.app"
/bin/ln -s /Applications "$DMG_ROOT/Applications"

/usr/bin/hdiutil create \
  -volname "$APP_DISPLAY_NAME $VERSION" \
  -srcfolder "$DMG_ROOT" \
  -ov \
  -format UDZO \
  "$DMG_PATH"

if [[ "$SIGNING_IDENTITY" != "-" ]]; then
  /usr/bin/codesign \
    --force \
    --timestamp \
    --sign "$SIGNING_IDENTITY" \
    "$DMG_PATH"
  /usr/bin/codesign --verify --strict --verbose=2 "$DMG_PATH"
fi

/usr/bin/hdiutil verify "$DMG_PATH"

if [[ -n "$NOTARYTOOL_PROFILE" ]]; then
  if [[ "$SIGNING_IDENTITY" != Developer\ ID\ Application:* ]]; then
    echo "NOTARYTOOL_PROFILE requires a Developer ID Application signing identity." >&2
    exit 1
  fi
  /usr/bin/xcrun notarytool submit \
    "$DMG_PATH" \
    --keychain-profile "$NOTARYTOOL_PROFILE" \
    --wait
  /usr/bin/xcrun stapler staple "$DMG_PATH"
  /usr/bin/xcrun stapler validate "$DMG_PATH"
  /usr/bin/codesign --verify --strict --verbose=2 "$DMG_PATH"
  /usr/sbin/spctl -a -vv -t open --context context:primary-signature "$DMG_PATH"
fi

# Stapling modifies the DMG, so final verification and checksum must run last.
/usr/bin/hdiutil verify "$DMG_PATH"
(
  cd "$RELEASE_DIR"
  /usr/bin/shasum -a 256 "$(basename "$DMG_PATH")" | \
    /usr/bin/tee "$(basename "$CHECKSUM_PATH")"
)

rm -rf "$DMG_ROOT"

echo "App: $APP_BUNDLE"
echo "DMG: $DMG_PATH"
echo "SHA-256: $CHECKSUM_PATH"
echo "Architectures: $ARCHITECTURES"

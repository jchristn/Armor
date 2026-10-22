#!/usr/bin/env bash
# Publish and install the self-contained menu-bar app and its terminal console.
# Usage: scripts/macos/install.sh [net8.0|net10.0]
set -euo pipefail
[ "$(uname -s)" = Darwin ] || { echo "This installer requires macOS." >&2; exit 1; }
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TFM="${1:-net10.0}"
case "$TFM" in net8.0|net10.0) ;; *) echo "Unsupported framework: $TFM" >&2; exit 1 ;; esac
[ "$#" -le 1 ] || { echo "Usage: $0 [net8.0|net10.0]" >&2; exit 1; }
case "$(uname -m)" in
    arm64) RID=osx-arm64 ;;
    x86_64) RID=osx-x64 ;;
    *) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 1 ;;
esac
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/src/Directory.Build.props" | head -n 1)"
[ -n "$VERSION" ] || { echo "Cannot determine Armor version." >&2; exit 1; }
STAGE="$REPO_ROOT/artifacts/macos-app/$RID/$TFM"
APP="$STAGE/Armor.app"
DESTINATION="$HOME/Applications/Armor.app"
rm -rf "$STAGE"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$STAGE/Armor.iconset"
# The agent's Open action and the TUI's agent auto-start resolve each other
# beside their executable, so both publishes must share Contents/MacOS.
for PROJECT in Armor.Agent Armor.Tui; do
    dotnet publish "$REPO_ROOT/src/$PROJECT/$PROJECT.csproj" -c Release -f "$TFM" \
        -p:TargetFrameworks="$TFM" -r "$RID" --self-contained true -o "$APP/Contents/MacOS"
done
for SIZE in 16 32 128 256 512; do
    sips -z "$SIZE" "$SIZE" "$REPO_ROOT/assets/logo.png" --out "$STAGE/Armor.iconset/icon_${SIZE}x${SIZE}.png" >/dev/null
    DOUBLE=$((SIZE * 2))
    sips -z "$DOUBLE" "$DOUBLE" "$REPO_ROOT/assets/logo.png" --out "$STAGE/Armor.iconset/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil --convert icns "$STAGE/Armor.iconset" --output "$APP/Contents/Resources/Armor.icns"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
    <key>CFBundleName</key><string>Armor</string>
    <key>CFBundleDisplayName</key><string>Armor</string>
    <key>CFBundleIdentifier</key><string>com.jchristn.armor</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleExecutable</key><string>Armor.Agent</string>
    <key>CFBundleIconFile</key><string>Armor.icns</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>LSUIElement</key><true/>
</dict></plist>
PLIST
chmod +x "$APP/Contents/MacOS/Armor.Agent" "$APP/Contents/MacOS/Armor.Tui"
codesign --force --deep --sign - "$APP"
mkdir -p "$HOME/Applications"
rm -rf "$DESTINATION"
cp -R "$APP" "$DESTINATION"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
    -f "$DESTINATION" >/dev/null 2>&1 || true
echo "Installed $DESTINATION. Open it in Finder for the menu-bar agent; choose Open to launch the console."

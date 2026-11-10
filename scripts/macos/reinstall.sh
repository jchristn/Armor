#!/usr/bin/env bash
# Rebuild Armor from this checkout and replace the install (~/Applications/Armor.app).
# Works whether or not Armor is installed. The new build is staged first, so a failed build
# leaves the existing install untouched. Refuses while Armor is running; stop it with
# kill-armor.sh first (update.sh stops and restarts it for you). Configuration and backups under
# ~/.armor (or ARMOR_HOME) are left alone.
# macOS version: the install is an app bundle (self-contained publish, icon, Info.plist, ad-hoc
# signature) staged under artifacts/macos-app/<rid>/<framework>, copied to ~/Applications and
# registered with Launch Services; uninstall.sh removes the app and the staging. Refuses while the
# .pkg installer's LaunchAgent (com.joelchristner.armor) is loaded.
# Usage: ./scripts/macos/reinstall.sh [net8.0|net10.0]   (default net10.0)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/macos/reinstall.sh [net8.0|net10.0]"
    echo "Rebuilds Armor and replaces ~/Applications/Armor.app. Defaults to net10.0."
}

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

TFM=net10.0
case "${1:-}" in
    -h|--help) usage; exit 0 ;;
    net8|net8.0) TFM=net8.0; shift ;;
    net10|net10.0) TFM=net10.0; shift ;;
    net*) die "Unsupported framework: $1. Supported frameworks: net8.0, net10.0." ;;
esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download"
for TOOL in sips iconutil codesign; do
    command -v "$TOOL" >/dev/null 2>&1 || die "$TOOL was not found on PATH. It ships with macOS in /usr/bin; make sure /usr/bin is on PATH."
done
case "$(uname -m)" in
    arm64) RID=osx-arm64 ;;
    x86_64) RID=osx-x64 ;;
    *) die "Unsupported macOS architecture: $(uname -m)" ;;
esac
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/src/Directory.Build.props" | head -n 1)"
[ -n "$VERSION" ] || die "Cannot determine the Armor version from src/Directory.Build.props."

DEST="$HOME/Applications/Armor.app"
STAGE="$REPO_ROOT/artifacts/macos-app/$RID/$TFM"
APP="$STAGE/Armor.app"
LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
PKG_LABEL=com.joelchristner.armor

# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }
# True when Armor.app is installed.
installed() { [ -e "$DEST" ]; }
# Publishes both executables into a staged Armor.app with its icon, Info.plist and ad-hoc signature.
build_app() {
    rm -rf "$STAGE"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$STAGE/Armor.iconset"
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
}

echo "Armor reinstall (macOS, $TFM)"
echo "[1/5] Checking for running Armor processes..."
# The .pkg installer's LaunchAgent (com.joelchristner.armor, /Applications/Armor.app, KeepAlive=true)
# restarts its agent whenever it exits. These scripts do not manage that install; kill-armor.sh
# unloads it until the next login.
if launchctl print "gui/$(id -u)/$PKG_LABEL" >/dev/null 2>&1; then
    die "The packaged Armor (LaunchAgent $PKG_LABEL from the .pkg installer, /Applications/Armor.app) is loaded, and launchd keeps its agent running. Run scripts/macos/kill-armor.sh to unload it until the next login (or remove the package), then try again."
fi
if armor_running; then
    die "Armor is running. Quit the agent from its menu-bar menu and close the console (or run scripts/macos/kill-armor.sh), then try again."
fi
echo "[2/5] Building Armor.Agent and Armor.Tui ($TFM, $RID)..."
build_app
echo "[3/5] Removing the existing install..."
if installed; then
    "$LSREGISTER" -u "$DEST" >/dev/null 2>&1 || true
    rm -rf "$DEST"
    echo "Removed $DEST."
else
    rm -rf "$DEST"
    echo "Armor is not installed; continuing."
fi
echo "[4/5] Installing to $DEST..."
mkdir -p "$HOME/Applications"
cp -R "$APP" "$DEST"
"$LSREGISTER" -f "$DEST" >/dev/null 2>&1 || true
echo "[5/5] Verifying the install..."
for EXE in Armor.Agent Armor.Tui; do
    [ -x "$DEST/Contents/MacOS/$EXE" ] || die "$EXE is missing from $DEST/Contents/MacOS after the install."
done
echo "Installed Armor.Agent and Armor.Tui ($TFM) to $DEST."
echo "Open it in Finder for the menu-bar agent (choose Open for the console), or register the agent for login with scripts/macos/run-at-startup.sh."
echo "Done."

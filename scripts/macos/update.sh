#!/usr/bin/env bash
# Update Armor from source: stop Armor, pull the latest code (git pull --ff-only; on failure it
# warns and rebuilds the checkout as-is), rebuild, replace the install, and start the agent again
# if it was running, the same way it ran before.
# Works whether or not Armor is installed. The new build is staged first and the old install is
# kept aside until the new one is verified; if any step fails, the old install is put back and the
# agent is restarted as it was. Configuration and backups under ~/.armor (or ARMOR_HOME) are kept.
# macOS version: the install is ~/Applications/Armor.app (see install.sh); the agent restarts
# through the launchd agent from run-at-startup.sh, or by opening the app. Refuses while the
# .pkg installer's LaunchAgent (com.joelchristner.armor) is loaded.
# Usage: ./scripts/macos/update.sh [net8.0|net10.0]   (default net10.0)
set -euo pipefail

# Everything runs from main, which bash reads in full before running it, so a git pull that
# rewrites this file cannot change the commands while they run.
main() {
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/macos/update.sh [net8.0|net10.0]"
    echo "Pulls, rebuilds and reinstalls Armor into ~/Applications/Armor.app, restarting the agent if it was running. Defaults to net10.0."
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
command -v git >/dev/null 2>&1 || die "git was not found on PATH. Install the Xcode Command Line Tools: xcode-select --install"

DEST="$HOME/Applications/Armor.app"
STAGE="$REPO_ROOT/artifacts/macos-app/$RID/$TFM"
APP="$STAGE/Armor.app"
PREVIOUS="$HOME/Applications/Armor.app.previous"
LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
LABEL=com.jchristn.armor.agent
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
PKG_LABEL=com.joelchristner.armor

# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }
# Asks this user's Armor processes (only the agent with "agent") to exit, waits up to 5 seconds,
# then force-kills what is left. Fails if something could not be stopped.
stop_armor() {
    _check=armor_running; _names="Armor.Agent Armor.Tui"; _pattern='Armor\.(Agent|Tui)\.dll'
    if [ "${1:-}" = agent ]; then _check=agent_running; _names=Armor.Agent; _pattern='Armor\.Agent\.dll'; fi
    if ! "$_check"; then
        echo "No Armor processes are running."
        return 0
    fi
    _signal() {
        for _name in $_names; do pkill "$1" -u "$(id -u)" -x "$_name" >/dev/null 2>&1 || true; done
        pkill "$1" -u "$(id -u)" -f "$_pattern" >/dev/null 2>&1 || true
    }
    _signal -TERM
    for _ in 1 2 3 4 5; do
        "$_check" || return 0
        sleep 1
    done
    echo "Some Armor processes did not exit; force-killing them."
    _signal -KILL
    sleep 1
    if "$_check"; then
        die "Some Armor processes could not be stopped. Check: pgrep -u $(id -u) -fl Armor"
    fi
}
# True when Armor.app is installed.
installed() { [ -e "$DEST" ]; }
# True when the launchd agent from run-at-startup.sh is loaded (KeepAlive may restart the agent).
launchd_loaded() { launchctl print "gui/$(id -u)/$LABEL" >/dev/null 2>&1; }
# Unloads it and waits until launchd has let go of the label (bootstrap fails while it lingers).
launchd_unload() {
    launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || launchctl unload "$PLIST" 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        launchd_loaded || return 0
        sleep 0.5
    done
}
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
# Starts the agent again the way it ran before this script stopped it.
restart_agent() {
    if [ "$LAUNCHD_WAS_RUNNING" -eq 1 ] && [ -f "$PLIST" ]; then
        launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || launchctl load -w "$PLIST"
        echo "Restarted the agent through launchd."
    elif [ "$LAUNCHD_WAS_RUNNING" -eq 1 ] || [ "$AGENT_WAS_RUNNING" -eq 1 ]; then
        [ -x "$DEST/Contents/MacOS/Armor.Agent" ] || { echo "Error: Cannot restart the agent: $DEST is missing." >&2; return 1; }
        open "$DEST"
        echo "Restarted the agent."
    else
        echo "The agent was not running; leaving it stopped."
    fi
}

STOPPED=0
VERIFIED=0
LAUNCHD_WAS_RUNNING=0
AGENT_WAS_RUNNING=0
# On failure after Armor was stopped: put the old install back if the new one was not verified,
# then start the agent again as it ran before.
on_exit() {
    _code=$?
    [ "$_code" -ne 0 ] && [ "$STOPPED" -eq 1 ] || return 0
    echo "The update failed; restoring the previous state." >&2
    if [ "$VERIFIED" -eq 0 ] && [ -e "$PREVIOUS" ]; then
        rm -rf "$DEST" || true
        mv "$PREVIOUS" "$DEST" || true
        "$LSREGISTER" -f "$DEST" >/dev/null 2>&1 || true
        echo "Restored the previous install at $DEST." >&2
    fi
    restart_agent >&2 || true
}
trap on_exit EXIT
# The .pkg installer's LaunchAgent (com.joelchristner.armor, /Applications/Armor.app, KeepAlive=true)
# restarts its agent whenever it exits. These scripts do not manage that install; kill-armor.sh
# unloads it until the next login.
if launchctl print "gui/$(id -u)/$PKG_LABEL" >/dev/null 2>&1; then
    die "The packaged Armor (LaunchAgent $PKG_LABEL from the .pkg installer, /Applications/Armor.app) is loaded, and launchd keeps its agent running. Run scripts/macos/kill-armor.sh to unload it until the next login (or remove the package), then try again."
fi

echo "Armor update (macOS, $TFM)"
echo "[1/6] Stopping Armor..."
# Unload the launchd agent first, otherwise KeepAlive could respawn the agent mid-install.
if launchd_loaded; then
    if agent_running; then LAUNCHD_WAS_RUNNING=1; fi
    launchd_unload
    echo "Unloaded the launchd agent."
elif agent_running; then
    AGENT_WAS_RUNNING=1
fi
STOPPED=1
stop_armor
echo "[2/6] Pulling the latest source..."
git -C "$REPO_ROOT" pull --ff-only || echo "Warning: git pull failed; rebuilding the checkout as-is." >&2
echo "[3/6] Building Armor.Agent and Armor.Tui ($TFM, $RID)..."
build_app
echo "[4/6] Replacing the install at $DEST..."
rm -rf "$PREVIOUS"
if installed; then
    "$LSREGISTER" -u "$DEST" >/dev/null 2>&1 || true
    mv "$DEST" "$PREVIOUS"
    echo "Moved the existing install aside."
else
    rm -rf "$DEST"
    echo "Armor is not installed; continuing."
fi
mkdir -p "$HOME/Applications"
cp -R "$APP" "$DEST"
"$LSREGISTER" -f "$DEST" >/dev/null 2>&1 || true
echo "[5/6] Verifying the install..."
for EXE in Armor.Agent Armor.Tui; do
    [ -x "$DEST/Contents/MacOS/$EXE" ] || die "$EXE is missing from $DEST/Contents/MacOS after the install."
done
VERIFIED=1
rm -rf "$PREVIOUS"
echo "Installed Armor.Agent and Armor.Tui ($TFM) to $DEST."
echo "[6/6] Restarting the agent..."
restart_agent
STOPPED=0
echo "Done."
exit 0
}

main "$@"

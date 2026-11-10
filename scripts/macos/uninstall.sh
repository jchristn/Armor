#!/usr/bin/env bash
# Remove what install.sh and run-at-startup.sh created: the launchd login agent, the Finder app ~/Applications/Armor.app (unregistered from Launch
# Services) and the build staging under artifacts/macos-app.
# Succeeds with a message when nothing is installed. Refuses while Armor is running; stop it with
# kill-armor.sh first. Configuration, the database and logs under ~/.armor (or ARMOR_HOME) are
# kept; reset.sh at the repository root removes them.
# Usage: ./scripts/macos/uninstall.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/macos/uninstall.sh"
    echo "Removes the login registration and ~/Applications/Armor.app; keeps ~/.armor."
}

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

case "${1:-}" in -h|--help) usage; exit 0 ;; esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"

DEST="$HOME/Applications/Armor.app"
PREVIOUS="$HOME/Applications/Armor.app.previous"
LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
LABEL=com.jchristn.armor.agent
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
PKG_LABEL=com.joelchristner.armor
ARMOR_DIR="${ARMOR_HOME:-$HOME/.armor}"

# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }
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

echo "Armor uninstall (macOS)"
echo "[1/3] Checking for running Armor processes..."
# The .pkg installer's LaunchAgent (com.joelchristner.armor, /Applications/Armor.app, KeepAlive=true)
# restarts its agent whenever it exits. These scripts do not manage that install; kill-armor.sh
# unloads it until the next login.
if launchctl print "gui/$(id -u)/$PKG_LABEL" >/dev/null 2>&1; then
    die "The packaged Armor (LaunchAgent $PKG_LABEL from the .pkg installer, /Applications/Armor.app) is loaded, and launchd keeps its agent running. Run scripts/macos/kill-armor.sh to unload it until the next login (or remove the package), then try again."
fi
if armor_running; then
    die "Armor is running. Quit the agent from its menu-bar menu and close the console (or run scripts/macos/kill-armor.sh), then try again."
fi
echo "[2/3] Removing the login registration..."
registered=0
if launchd_loaded; then
    launchd_unload
    registered=1
fi
if [ -f "$PLIST" ]; then
    rm -f "$PLIST"
    registered=1
fi
if [ "$registered" -eq 1 ]; then
    echo "Removed the launchd agent $PLIST."
fi
if [ "$registered" -eq 0 ]; then
    echo "The agent is not registered to run at login."
fi
echo "[3/3] Removing the install..."
rm -rf "$REPO_ROOT/artifacts/macos-app" "$PREVIOUS"
if installed; then
    "$LSREGISTER" -u "$DEST" >/dev/null 2>&1 || true
    rm -rf "$DEST"
    echo "Removed $DEST."
else
    echo "Armor is not installed; nothing to remove."
fi
echo "Armor's configuration, database and logs in $ARMOR_DIR were kept; run reset.sh at the repository root to remove them."
echo "Done."

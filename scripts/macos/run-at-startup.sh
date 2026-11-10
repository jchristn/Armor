#!/usr/bin/env bash
# Register the Armor agent (the menu-bar scheduler in ~/Applications/Armor.app) as a launchd user
# agent so it starts at login, and start it now unless it is already running. No sudo needed; the
# plist lives in ~/Library/LaunchAgents. launchd restarts the agent if it crashes and leaves it
# stopped after a clean Exit from the menu. Re-running refreshes the registration. Needs the app
# from install.sh.
# Usage: ./scripts/macos/run-at-startup.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/macos/run-at-startup.sh"
    echo "Registers ~/Applications/Armor.app's agent to start at login and starts it now."
}

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

case "${1:-}" in -h|--help) usage; exit 0 ;; esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"

DEST="$HOME/Applications/Armor.app"
AGENT_BIN="$DEST/Contents/MacOS/Armor.Agent"
LABEL=com.jchristn.armor.agent
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
PKG_PLIST=/Library/LaunchAgents/com.joelchristner.armor.plist
# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }
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

[ -x "$AGENT_BIN" ] || die "Armor is not installed at $DEST. Run scripts/macos/install.sh first."

echo "Armor start at login (macOS)"
if [ -f "$PKG_PLIST" ]; then
    echo "Note: the .pkg installer's LaunchAgent ($PKG_PLIST) also starts an agent at login; only one agent runs at a time." >&2
fi
mkdir -p "$HOME/Library/LaunchAgents"
cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>$AGENT_BIN</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <dict>
        <key>SuccessfulExit</key>
        <false/>
    </dict>
</dict>
</plist>
PLIST
echo "Registered $AGENT_BIN to start at login (launchd agent $PLIST)."
if agent_running; then
    echo "The agent is already running."
else
    # bootstrap fails on a label launchd still holds, so unload any old copy and wait for it first.
    # bootstrap is the modern verb; fall back to load -w on older macOS.
    if launchd_loaded; then launchd_unload; fi
    launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || launchctl load -w "$PLIST"
    echo "Started the agent now; look for the Armor icon in the menu bar."
fi
echo "Stop it with scripts/macos/kill-armor.sh; undo the registration with scripts/macos/remove-from-startup.sh."

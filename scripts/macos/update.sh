#!/bin/sh
# Update Armor from source: pull the latest code, rebuild, and republish the
# agent and the TUI side by side into dist/ (the tray's Open action needs both
# executables in the same folder). Restarts the agent if it was running.
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
LABEL="com.jchristn.armor.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
cd "$REPO_ROOT"

# If the launchd agent is loaded, boot it out first — otherwise KeepAlive would
# respawn the agent the moment we kill it, mid-publish.
SERVICE_WAS_LOADED=0
if launchctl print "gui/$(id -u)/$LABEL" >/dev/null 2>&1; then
    SERVICE_WAS_LOADED=1
    launchctl bootout "gui/$(id -u)/$LABEL"
fi

# Stop both Armor processes: a running agent keeps Armor.Core.dll open, and
# publishing over it would silently ship stale binaries.
AGENT_WAS_RUNNING=0
if pgrep -x Armor.Agent >/dev/null 2>&1; then
    AGENT_WAS_RUNNING=1
fi
pkill -x Armor.Tui 2>/dev/null || true
pkill -x Armor.Agent 2>/dev/null || true

git pull --ff-only || echo "git pull failed; rebuilding the checkout as-is." >&2

dotnet publish src/Armor.Agent -c Release -f net10.0 -o dist
dotnet publish src/Armor.Tui   -c Release -f net10.0 -o dist

echo "Published Armor.Agent and Armor.Tui to $REPO_ROOT/dist."

if [ "$SERVICE_WAS_LOADED" = "1" ]; then
    launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || launchctl load -w "$PLIST"
    echo "Restarted the agent via launchd."
elif [ "$AGENT_WAS_RUNNING" = "1" ]; then
    "$REPO_ROOT/dist/Armor.Agent" >/dev/null 2>&1 &
    echo "Restarted the agent."
fi

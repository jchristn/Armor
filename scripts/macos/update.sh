#!/bin/sh
# Update Armor from source: pull the latest code, rebuild, and republish the
# Finder app in ~/Applications, with the agent and TUI side by side inside
# the bundle. Restarts the agent if it was running.
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
LABEL="com.jchristn.armor.agent"
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

"$SCRIPT_DIR/install.sh" "$@"

if [ "$SERVICE_WAS_LOADED" = "1" ]; then
    "$SCRIPT_DIR/run-at-startup.sh"
    echo "Restarted the agent via launchd."
elif [ "$AGENT_WAS_RUNNING" = "1" ]; then
    open -a "$HOME/Applications/Armor.app"
    echo "Restarted the agent."
fi

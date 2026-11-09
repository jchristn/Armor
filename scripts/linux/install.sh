#!/bin/sh
# Build Armor from the current checkout and publish the agent and the TUI side
# by side into dist/ (the tray's Open action and the TUI's agent auto-start
# need both executables in the same folder). Stops a running agent first, so
# the publish can't ship stale binaries, and restarts it afterwards.
# Configuration and backups under ~/.armor are left alone.
# Usage: scripts/linux/install.sh [net8.0|net10.0]
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TFM="${1:-net10.0}"
case "$TFM" in net8.0|net10.0) ;; *) echo "Unsupported framework: $TFM" >&2; exit 1 ;; esac
[ "$#" -le 1 ] || { echo "Usage: $0 [net8.0|net10.0]" >&2; exit 1; }
command -v dotnet >/dev/null 2>&1 || {
    echo "dotnet was not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download" >&2
    exit 1
}
cd "$REPO_ROOT"

# Stop the systemd user service first, otherwise Restart=on-failure could
# respawn the agent mid-publish.
SERVICE_WAS_ACTIVE=0
if command -v systemctl >/dev/null 2>&1 && systemctl --user is-active --quiet armor-agent 2>/dev/null; then
    SERVICE_WAS_ACTIVE=1
    systemctl --user stop armor-agent
fi
AGENT_WAS_RUNNING=0
if pgrep -x Armor.Agent >/dev/null 2>&1; then
    AGENT_WAS_RUNNING=1
fi
pkill -x Armor.Tui 2>/dev/null || true
pkill -x Armor.Agent 2>/dev/null || true

for PROJECT in Armor.Agent Armor.Tui; do
    dotnet publish "src/$PROJECT/$PROJECT.csproj" -c Release -f "$TFM" -p:TargetFrameworks="$TFM" -o dist
done
chmod +x dist/Armor.Agent dist/Armor.Tui

echo "Installed Armor.Agent and Armor.Tui ($TFM) to $REPO_ROOT/dist."

if [ "$SERVICE_WAS_ACTIVE" = "1" ]; then
    systemctl --user start armor-agent
    echo "Restarted the armor-agent systemd user service."
elif [ "$AGENT_WAS_RUNNING" = "1" ]; then
    nohup "$REPO_ROOT/dist/Armor.Agent" >/dev/null 2>&1 &
    echo "Restarted the agent."
fi
echo "Run the console with dist/Armor.Tui, or register the agent for login with"
echo "the run-at-startup script for your platform (scripts/linux, scripts/wsl2, ...)."

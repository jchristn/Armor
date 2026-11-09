#!/usr/bin/env sh
# Update Armor from source: pull, rebuild, and republish the agent and TUI into dist/, restarting
# the agent if it was running. WSL 1 counterpart of scripts/linux/update.sh. Uses the Linux .NET
# SDK inside the distro, and restarts the agent through the Windows Startup launcher when one is
# registered, so it outlives this terminal.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET 10 SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_require_cmd git "Install git inside WSL (for example: sudo apt install git)."
cd "$REPO_ROOT"

AGENT_BIN="$REPO_ROOT/dist/Armor.Agent"
LAUNCHER=armor-agent

# A systemd user service exists only if scripts/linux/run-at-startup.sh was used on a
# systemd-enabled WSL 2 distro (never on WSL 1, which has no systemd). Stop it so Restart=on-failure can't respawn the agent mid-publish.
SERVICE_WAS_ACTIVE=0
if wsl_has_systemd && systemctl --user is-active --quiet armor-agent 2>/dev/null; then
    SERVICE_WAS_ACTIVE=1
    systemctl --user stop armor-agent
fi

# A running agent keeps Armor.Core.dll open; publishing over it would ship stale binaries.
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

if [ "$SERVICE_WAS_ACTIVE" = "1" ]; then
    systemctl --user start armor-agent
    echo "Restarted the armor-agent systemd user service."
elif [ "$AGENT_WAS_RUNNING" = "1" ]; then
    VBS="$(wsl_startup_dir 2>/dev/null)/$LAUNCHER-wsl.vbs" || VBS=""
    WSCRIPT="$(PATH="$WSL_ORIGINAL_PATH" command -v wscript.exe 2>/dev/null || echo /mnt/c/Windows/System32/wscript.exe)"
    if [ -n "$VBS" ] && [ -f "$VBS" ] && [ -x "$WSCRIPT" ] \
        && (cd /mnt/c 2>/dev/null || true; "$WSCRIPT" "$(wslpath -w "$VBS")") >/dev/null 2>&1; then
        echo "Restarted the agent through the Windows Startup launcher."
    else
        nohup "$AGENT_BIN" >/dev/null 2>&1 &
        echo "Restarted the agent."
    fi
fi

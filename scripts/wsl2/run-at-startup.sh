#!/usr/bin/env sh
# Register the Armor agent to start at Windows login, inside this distro. WSL 2 counterpart of
# scripts/linux/run-at-startup.sh. Nothing runs systemd user services or desktop autostart
# entries when Windows starts, so this installs a hidden launcher in the Windows Startup folder
# (no admin rights) that runs dist/Armor.Agent through wsl.exe and keeps the distro up while it
# runs. The agent is an Avalonia GUI app and needs WSLg; WSLg shows no tray icon, but the agent
# still runs your schedules. Run scripts/wsl2/install.sh first.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

AGENT_BIN="$REPO_ROOT/dist/Armor.Agent"
LAUNCHER=armor-agent

# -f, not -x: a checkout on a Windows drive (DrvFs) may not report executable bits.
if [ ! -f "$AGENT_BIN" ]; then
    echo "Armor.Agent not found at $AGENT_BIN." >&2
    echo "Run scripts/$WSL_FLAVOR/install.sh first to build and publish it." >&2
    exit 1
fi
if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    wsl_log "Warning: no DISPLAY or WAYLAND_DISPLAY is set, so WSLg looks unavailable. The agent is"
    wsl_log "a GUI app and may fail to start. Update WSL (wsl --update) and check guiApplications=true"
    wsl_log "in %UserProfile%\\.wslconfig."
fi

wsl_startup_install "$LAUNCHER" "$AGENT_BIN"
echo "The agent will start at your next Windows login."

if pgrep -x Armor.Agent >/dev/null 2>&1; then
    echo "The agent is already running."
else
    # Start it through the launcher so it runs exactly as it will at login and keeps the
    # distro alive after this terminal closes. Fall back to a plain background start.
    VBS="$(wsl_startup_dir)/$LAUNCHER-wsl.vbs"
    WSCRIPT="$(PATH="$WSL_ORIGINAL_PATH" command -v wscript.exe 2>/dev/null || echo /mnt/c/Windows/System32/wscript.exe)"
    if [ -x "$WSCRIPT" ] && (cd /mnt/c 2>/dev/null || true; "$WSCRIPT" "$(wslpath -w "$VBS")") >/dev/null 2>&1; then
        echo "Started the agent now."
    else
        nohup "$AGENT_BIN" >/dev/null 2>&1 &
        echo "Started the agent now. It stops when this distro shuts down (after the last WSL"
        echo "window closes) until the launcher starts it at the next login."
    fi
fi
echo "Stop it with: pkill -x Armor.Agent   Undo with: scripts/$WSL_FLAVOR/remove-from-startup.sh"

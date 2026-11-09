#!/usr/bin/env sh
# Register the Armor agent to start at Windows login, inside this distro. WSL 1 counterpart of
# scripts/linux/run-at-startup.sh. The agent is an Avalonia GUI (tray) app and WSL 1 has no WSLg
# display, so by default this stops with an explanation: use scripts/windows/run-at-startup.bat, or
# move the distro to WSL 2 and use scripts/wsl2/run-at-startup.sh. Set ARMOR_WSL1_DISPLAY_OK=1 to
# register anyway when an X server runs on Windows and your login profile exports DISPLAY. The
# launcher is a hidden script in the Windows Startup folder (no admin rights) that runs
# dist/Armor.Agent through wsl.exe. Run scripts/wsl/install.sh first.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

if [ "$(wsl_version)" = 1 ] && [ "${ARMOR_WSL1_DISPLAY_OK:-0}" != 1 ]; then
    wsl_die "WSL 1 has no GUI support, and Armor.Agent is a GUI (tray) app. Run scripts\\windows\\run-at-startup.bat on Windows, or move this distro to WSL 2 (wsl --set-version ${WSL_DISTRO_NAME:-<distro>} 2) and use scripts/wsl2/run-at-startup.sh. Set ARMOR_WSL1_DISPLAY_OK=1 if you run an X server and export DISPLAY in your login profile."
fi

AGENT_BIN="$REPO_ROOT/dist/Armor.Agent"
LAUNCHER=armor-agent

# -f, not -x: a checkout on a Windows drive (DrvFs) may not report executable bits.
if [ ! -f "$AGENT_BIN" ]; then
    echo "Armor.Agent not found at $AGENT_BIN." >&2
    echo "Run scripts/$WSL_FLAVOR/install.sh first to build and publish it." >&2
    exit 1
fi
if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ]; then
    wsl_log "Warning: DISPLAY is not set. The agent is a GUI app and fails to start without a display."
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

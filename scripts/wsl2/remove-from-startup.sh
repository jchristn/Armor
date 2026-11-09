#!/usr/bin/env sh
# Deregister the Armor agent from starting at login. WSL 2 counterpart of
# scripts/linux/remove-from-startup.sh. Removes the Windows Startup launcher that
# run-at-startup.sh installs, plus any systemd user service or desktop autostart entry that
# scripts/linux/run-at-startup.sh may have created in this distro earlier. A running agent keeps
# running; WSLg shows no tray icon to exit it from, so stop it with: pkill -x Armor.Agent
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

SERVICE="$HOME/.config/systemd/user/armor-agent.service"
DESKTOP="$HOME/.config/autostart/armor-agent.desktop"
REMOVED=0

if wsl_startup_remove armor-agent; then
    REMOVED=1
fi

if [ -f "$SERVICE" ]; then
    if wsl_has_systemd; then
        systemctl --user disable --now armor-agent 2>/dev/null || true
    fi
    rm -f "$SERVICE"
    if wsl_has_systemd; then
        systemctl --user daemon-reload 2>/dev/null || true
    fi
    echo "Removed the armor-agent systemd user service."
    REMOVED=1
fi

if [ -f "$DESKTOP" ]; then
    rm -f "$DESKTOP"
    echo "Removed $DESKTOP."
    REMOVED=1
fi

if [ "$REMOVED" = "0" ]; then
    echo "The agent is not registered to run at login; nothing to do."
elif pgrep -x Armor.Agent >/dev/null 2>&1; then
    echo "The agent is still running. Stop it with: pkill -x Armor.Agent"
fi

#!/bin/sh
# Deregister the Armor agent from starting at login. Removes whichever
# mechanism run-at-startup.sh installed: the systemd --user service (which also
# stops a running agent) and/or the desktop autostart entry.
set -eu

SERVICE="$HOME/.config/systemd/user/armor-agent.service"
DESKTOP="$HOME/.config/autostart/armor-agent.desktop"
REMOVED=0

if [ -f "$SERVICE" ]; then
    systemctl --user disable --now armor-agent 2>/dev/null || true
    rm "$SERVICE"
    systemctl --user daemon-reload 2>/dev/null || true
    echo "Removed the armor-agent systemd user service (and stopped it if running)."
    REMOVED=1
fi

if [ -f "$DESKTOP" ]; then
    rm "$DESKTOP"
    echo "Removed $DESKTOP. A running agent keeps running until you exit it from"
    echo "the tray menu."
    REMOVED=1
fi

if [ "$REMOVED" = "0" ]; then
    echo "The agent is not registered to run at login; nothing to do."
fi

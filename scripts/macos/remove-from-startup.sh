#!/bin/sh
# Deregister the Armor agent from starting at login: unload the launchd user
# agent (which also stops a running agent) and delete its plist.
set -eu

LABEL="com.jchristn.armor.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null \
    || launchctl unload -w "$PLIST" 2>/dev/null \
    || true

if [ -f "$PLIST" ]; then
    rm "$PLIST"
    echo "Removed $PLIST; the agent no longer starts at login (and was stopped if running)."
else
    echo "No launch agent installed at $PLIST; nothing to do."
fi

#!/bin/sh
# Remove what install.sh and run-at-startup.sh put in place: the login
# registration (systemd user service or desktop autostart entry), any running
# Armor processes, and the published copy in dist/. Configuration, the
# database and backups are kept; reset.sh at the repository root wipes ~/.armor.
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

sh "$SCRIPT_DIR/remove-from-startup.sh"

pkill -x Armor.Tui 2>/dev/null || true
pkill -x Armor.Agent 2>/dev/null || true

if [ -d "$REPO_ROOT/dist" ]; then
    rm -rf "$REPO_ROOT/dist"
    echo "Removed $REPO_ROOT/dist."
else
    echo "Nothing published at $REPO_ROOT/dist."
fi
echo "Armor's configuration and database under ~/.armor were kept; run reset.sh to remove them."

#!/usr/bin/env bash
# Refresh the Finder app, then run its terminal console in the current terminal.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$SCRIPT_DIR/install.sh" "$@"
exec "$HOME/Applications/Armor.app/Contents/MacOS/Armor.Tui"

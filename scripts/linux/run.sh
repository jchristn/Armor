#!/bin/sh
# Refresh the published copy in dist/, then run its terminal console in the
# current terminal. Usage: scripts/linux/run.sh [net8.0|net10.0]
set -eu
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
sh "$SCRIPT_DIR/install.sh" "$@"
exec "$REPO_ROOT/dist/Armor.Tui"

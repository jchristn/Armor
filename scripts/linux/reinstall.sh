#!/bin/sh
# Rebuild and replace the published copy in dist/, preserving Armor
# configuration and backups (~/.armor). Usage: scripts/linux/reinstall.sh [net8.0|net10.0]
set -eu
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
exec sh "$SCRIPT_DIR/install.sh" "$@"

#!/usr/bin/env bash
# Rebuild and replace the Finder app, preserving Armor configuration and backups.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/install.sh" "$@"

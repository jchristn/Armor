#!/usr/bin/env sh
# Remove the Windows Startup launcher, stop Armor and delete dist/. WSL 1 counterpart of
# scripts/linux/uninstall.sh; configuration and backups under ~/.armor are kept.
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

# The WSL remove-from-startup also clears the launcher; the linux uninstall then stops
# Armor and removes dist/ (its own remove-from-startup step finds nothing left to do).
sh "$SCRIPT_DIR/remove-from-startup.sh"
wsl_run "$SCRIPT_DIR/../linux/uninstall.sh" "$@"

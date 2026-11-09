#!/usr/bin/env sh
# Refresh dist/, then run the terminal console here. WSL 1 counterpart of scripts/linux/run.sh.
# The console runs fine on WSL 1. It also tries to auto-start the agent, a GUI (tray) app that
# cannot start without a display (WSL 1 has no WSLg), so scheduled backups need WSL 2 or Windows.
# Usage: scripts/wsl/run.sh [net8.0|net10.0]
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET 10 SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_run "$SCRIPT_DIR/../linux/run.sh" "$@"

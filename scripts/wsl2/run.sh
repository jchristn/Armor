#!/usr/bin/env sh
# Refresh dist/, then run the terminal console here. WSL 2 counterpart of scripts/linux/run.sh.
# The console auto-starts the agent, which is a GUI (tray) app and needs WSLg.
# Usage: scripts/wsl2/run.sh [net8.0|net10.0]
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl2
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET 10 SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_run "$SCRIPT_DIR/../linux/run.sh" "$@"

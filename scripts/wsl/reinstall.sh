#!/usr/bin/env sh
# Rebuild and replace dist/, keeping ~/.armor. WSL 1 counterpart of scripts/linux/reinstall.sh.
# Usage: scripts/wsl/reinstall.sh [net8.0|net10.0]
set -eu
SCRIPT_DIR="$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)"
REPO_ROOT="$(CDPATH='' cd -- "$SCRIPT_DIR/../.." && pwd)"
WSL_FLAVOR=wsl
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"
wsl_require_cmd dotnet "Install the .NET 10 SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"
wsl_run "$SCRIPT_DIR/../linux/install.sh" "$@"

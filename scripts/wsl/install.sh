#!/usr/bin/env bash
# Build Armor from this checkout and install it: publish the agent and the console (TUI) side by side into
# dist/ at the repository root (the agent and the console find each other beside
# their executable). Fails if Armor is already installed; use reinstall.sh to replace it.
# Refuses while Armor is running; stop it with kill-armor.sh first. Configuration and backups
# under ~/.armor (or ARMOR_HOME) are left alone.
# The build is staged under artifacts/install/<framework>.
# WSL 1 version: uses the Linux dotnet inside this distro (never the Windows one) and installs
# into this distro; use scripts\windows\install.bat for the Windows install.
# Usage: ./scripts/wsl/install.sh [net8.0|net10.0]   (default net10.0)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/wsl/install.sh [net8.0|net10.0]"
    echo "Builds Armor and installs dist/. Defaults to net10.0."
}

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

TFM=net10.0
case "${1:-}" in
    -h|--help) usage; exit 0 ;;
    net8|net8.0) TFM=net8.0; shift ;;
    net10|net10.0) TFM=net10.0; shift ;;
    net*) die "Unsupported framework: $1. Supported frameworks: net8.0, net10.0." ;;
esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
wsl_require_cmd dotnet "Install the .NET SDK inside WSL: https://learn.microsoft.com/dotnet/core/install/linux"

DEST="$REPO_ROOT/dist"
STAGE="$REPO_ROOT/artifacts/install/$TFM"

# A checkout on a Windows drive can hold the Windows build in dist/; never delete that from WSL.
if [ -e "$DEST/Armor.Agent.exe" ]; then
    die "$DEST holds the Windows build (Armor.Agent.exe). Manage that copy with the scripts\\windows scripts from Windows, or clone the repository into the Linux file system for WSL."
fi

# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }
# True when a published agent or console is in dist/.
installed() { [ -e "$DEST/Armor.Agent" ] || [ -e "$DEST/Armor.Tui" ]; }

echo "Armor install (WSL 1, $TFM)"
echo "[1/5] Checking for running Armor processes..."
if armor_running; then
    die "Armor is running. Close the console and stop the agent (run scripts/wsl/kill-armor.sh), then try again."
fi
echo "[2/5] Checking for an existing install..."
if installed; then
    die "Armor is already installed at $DEST. Run scripts/wsl/reinstall.sh to replace it."
fi
echo "[3/5] Building Armor.Agent and Armor.Tui ($TFM)..."
rm -rf "$STAGE"
for PROJECT in Armor.Agent Armor.Tui; do
    dotnet publish "$REPO_ROOT/src/$PROJECT/$PROJECT.csproj" -c Release -f "$TFM" -p:TargetFrameworks="$TFM" -o "$STAGE"
done
echo "[4/5] Installing to $DEST..."
rm -rf "$DEST"
mv "$STAGE" "$DEST"
echo "[5/5] Verifying the install..."
for EXE in Armor.Agent Armor.Tui; do
    [ -f "$DEST/$EXE" ] || die "$EXE is missing from $DEST after the publish."
    chmod +x "$DEST/$EXE" 2>/dev/null || true # DrvFs checkouts may not keep exec bits
done
echo "Installed Armor.Agent and Armor.Tui ($TFM) to $DEST."
echo "Start the console with $DEST/Armor.Tui (it starts the agent too), or register the agent for login with scripts/wsl/run-at-startup.sh."
echo "Done."

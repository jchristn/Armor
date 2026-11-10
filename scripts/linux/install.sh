#!/usr/bin/env bash
# Build Armor from this checkout and install it: publish the agent and the console (TUI) side by side into
# dist/ at the repository root (the agent and the console find each other beside
# their executable). Fails if Armor is already installed; use reinstall.sh to replace it.
# Refuses while Armor is running; stop it with kill-armor.sh first. Configuration and backups
# under ~/.armor (or ARMOR_HOME) are left alone.
# The build is staged under artifacts/install/<framework>.
# Usage: ./scripts/linux/install.sh [net8.0|net10.0]   (default net10.0)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/linux/install.sh [net8.0|net10.0]"
    echo "Builds Armor and installs dist/. Defaults to net10.0."
}

if [ "$(uname -s)" != Linux ]; then
    die "This script is for Linux. Use scripts/macos on macOS or scripts\\windows on Windows."
fi
# A Microsoft kernel alone is not WSL: Docker Desktop containers on the WSL 2 backend have one too.
if grep -qi microsoft /proc/version 2>/dev/null \
    && { [ -n "${WSL_DISTRO_NAME:-}" ] || [ -d /run/WSL ] || ls /proc/sys/fs/binfmt_misc/WSLInterop* >/dev/null 2>&1; }; then
    die "This is WSL. Use the same script in scripts/wsl2 (WSL 2) or scripts/wsl (WSL 1); they handle the Windows PATH, line endings and Windows startup."
fi

TFM=net10.0
case "${1:-}" in
    -h|--help) usage; exit 0 ;;
    net8|net8.0) TFM=net8.0; shift ;;
    net10|net10.0) TFM=net10.0; shift ;;
    net*) die "Unsupported framework: $1. Supported frameworks: net8.0, net10.0." ;;
esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1"
command -v dotnet >/dev/null 2>&1 || die "dotnet was not found on PATH. Install the .NET SDK: https://learn.microsoft.com/dotnet/core/install/linux"

DEST="$REPO_ROOT/dist"
STAGE="$REPO_ROOT/artifacts/install/$TFM"

# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }
# True when a published agent or console is in dist/.
installed() { [ -e "$DEST/Armor.Agent" ] || [ -e "$DEST/Armor.Tui" ]; }

echo "Armor install (Linux, $TFM)"
echo "[1/5] Checking for running Armor processes..."
if armor_running; then
    die "Armor is running. Quit the agent from its tray menu and close the console (or run scripts/linux/kill-armor.sh), then try again."
fi
echo "[2/5] Checking for an existing install..."
if installed; then
    die "Armor is already installed at $DEST. Run scripts/linux/reinstall.sh to replace it."
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
    chmod +x "$DEST/$EXE"
done
echo "Installed Armor.Agent and Armor.Tui ($TFM) to $DEST."
echo "Start the console with $DEST/Armor.Tui (it starts the agent too), or register the agent for login with scripts/linux/run-at-startup.sh."
echo "Done."

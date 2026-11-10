#!/usr/bin/env bash
# Remove what install.sh and run-at-startup.sh created: the login registration (the Windows Startup launcher, plus any armor-agent systemd user
# service or desktop autostart entry left by scripts/linux), the published copy in dist/ and any
# leftover build staging under artifacts/install.
# Succeeds with a message when nothing is installed. Refuses while Armor is running; stop it with
# kill-armor.sh first. Configuration, the database and logs under ~/.armor (or ARMOR_HOME) are
# kept; reset.sh at the repository root removes them.
# WSL 2 version: the Windows install, if any, is separate (scripts\windows\uninstall.bat).
# Usage: ./scripts/wsl2/uninstall.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/wsl2/uninstall.sh"
    echo "Removes the login registration and the published copy in dist/; keeps ~/.armor."
}

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl2
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

case "${1:-}" in -h|--help) usage; exit 0 ;; esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"

DEST="$REPO_ROOT/dist"
SERVICE="$HOME/.config/systemd/user/armor-agent.service"
DESKTOP="$HOME/.config/autostart/armor-agent.desktop"
ARMOR_DIR="${ARMOR_HOME:-$HOME/.armor}"

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

echo "Armor uninstall (WSL 2)"
echo "[1/3] Checking for running Armor processes..."
if armor_running; then
    die "Armor is running. Close the console and stop the agent (run scripts/wsl2/kill-armor.sh), then try again."
fi
echo "[2/3] Removing the login registration..."
registered=0
if wsl_startup_remove armor-agent; then
    registered=1
fi
if [ -f "$SERVICE" ] || [ -L "$HOME/.config/systemd/user/graphical-session.target.wants/armor-agent.service" ] \
    || [ -L "$HOME/.config/systemd/user/default.target.wants/armor-agent.service" ]; then
    systemctl --user disable --now armor-agent >/dev/null 2>&1 || true
    rm -f "$SERVICE" "$HOME/.config/systemd/user/graphical-session.target.wants/armor-agent.service" \
        "$HOME/.config/systemd/user/default.target.wants/armor-agent.service"
    systemctl --user daemon-reload >/dev/null 2>&1 || true
    echo "Removed the armor-agent systemd user service."
    registered=1
fi
if [ -f "$DESKTOP" ]; then
    rm -f "$DESKTOP"
    echo "Removed the desktop autostart entry."
    registered=1
fi
if [ "$registered" -eq 0 ]; then
    echo "The agent is not registered to run at login."
fi
echo "[3/3] Removing the install..."
rm -rf "$REPO_ROOT/artifacts/install"
if installed; then
    rm -rf "$DEST"
    echo "Removed $DEST."
else
    echo "Armor is not installed; nothing to remove."
fi
echo "Armor's configuration, database and logs in $ARMOR_DIR were kept; run reset.sh at the repository root to remove them."
echo "Done."

#!/usr/bin/env bash
# Stop this user's running Armor processes: the agent (Armor.Agent) and the console (Armor.Tui),
# including copies started with dotnet run or dotnet <dll>.
# Stops an armor-agent systemd user service left by scripts/linux first, so Restart=on-failure
# cannot respawn the agent. The Windows Startup launcher starts the agent again at the next
# Windows login. WSL 2 version: stops processes in this distro only; use
# scripts\windows\kill-armor.bat for Windows ones.
# Asks the processes to exit, waits up to 5 seconds, then force-kills what is left. Succeeds when
# nothing is running; fails if something could not be stopped.
# Usage: ./scripts/wsl2/kill-armor.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/wsl2/kill-armor.sh"
    echo "Stops the running Armor agent and console."
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

SERVICE="$HOME/.config/systemd/user/armor-agent.service"
# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }
# Asks this user's Armor processes (only the agent with "agent") to exit, waits up to 5 seconds,
# then force-kills what is left. Fails if something could not be stopped.
stop_armor() {
    _check=armor_running; _names="Armor.Agent Armor.Tui"; _pattern='Armor\.(Agent|Tui)\.dll'
    if [ "${1:-}" = agent ]; then _check=agent_running; _names=Armor.Agent; _pattern='Armor\.Agent\.dll'; fi
    if ! "$_check"; then
        echo "No Armor processes are running."
        return 0
    fi
    _signal() {
        for _name in $_names; do pkill "$1" -u "$(id -u)" -x "$_name" >/dev/null 2>&1 || true; done
        pkill "$1" -u "$(id -u)" -f "$_pattern" >/dev/null 2>&1 || true
    }
    _signal -TERM
    for _ in 1 2 3 4 5; do
        "$_check" || return 0
        sleep 1
    done
    echo "Some Armor processes did not exit; force-killing them."
    _signal -KILL
    sleep 1
    if "$_check"; then
        die "Some Armor processes could not be stopped. Check: pgrep -u $(id -u) -fl Armor"
    fi
}

echo "Stopping Armor processes..."
# Stop the service whenever its unit exists: one in "activating (auto-restart)" would respawn the agent.
if [ -f "$SERVICE" ] && command -v systemctl >/dev/null 2>&1; then
    if systemctl --user is-active --quiet armor-agent 2>/dev/null; then
        echo "Stopping the armor-agent systemd user service (it starts again at the next login)."
    fi
    systemctl --user stop armor-agent >/dev/null 2>&1 || true
fi
if ! armor_running; then
    echo "No Armor processes are running."
    exit 0
fi
stop_armor
echo "Done."

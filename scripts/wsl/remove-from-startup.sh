#!/usr/bin/env bash
# Deregister the Armor agent from starting at login: removes the Windows Startup launcher that run-at-startup.sh installs for this distro, plus any
# armor-agent systemd user service or desktop autostart entry left by scripts/linux.
# When something was registered, it also stops the running agent, the same on every platform
# (the console is left alone). Succeeds when nothing is registered.
# WSL 1 version: the Windows registration, if any, is separate (scripts\windows\remove-from-startup.bat).
# Usage: ./scripts/wsl/remove-from-startup.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/wsl/remove-from-startup.sh"
    echo "Stops the Armor agent from starting at login, and stops it."
}

# shellcheck disable=SC2034 # read by _wsl-common.sh
WSL_FLAVOR=wsl
# shellcheck source=_wsl-common.sh
. "$SCRIPT_DIR/_wsl-common.sh"
wsl_require
wsl_sanitize_path
wsl_check_repo "$REPO_ROOT"

case "${1:-}" in -h|--help) usage; exit 0 ;; esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"

SERVICE="$HOME/.config/systemd/user/armor-agent.service"
DESKTOP="$HOME/.config/autostart/armor-agent.desktop"
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
    echo "The agent is not registered to run at login; nothing to do."
    exit 0
fi
if agent_running; then
    stop_armor agent
    echo "Stopped the running agent."
fi

#!/usr/bin/env bash
# Deregister the Armor agent from starting at login: unloads the launchd user agent that run-at-startup.sh installs and deletes its plist.
# When something was registered, it also stops the running agent, the same on every platform
# (the console is left alone). Succeeds when nothing is registered.
# Usage: ./scripts/macos/remove-from-startup.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck disable=SC2034 # not every script needs REPO_ROOT
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/macos/remove-from-startup.sh"
    echo "Stops the Armor agent from starting at login, and stops it."
}

if [ "$(uname -s)" != Darwin ]; then
    die "This script is for macOS. Use scripts/linux on Linux, scripts/wsl2 (or scripts/wsl) on WSL, or scripts\\windows on Windows."
fi

case "${1:-}" in -h|--help) usage; exit 0 ;; esac
[ "$#" -eq 0 ] || die "Unexpected argument: $1 (this script takes no arguments)"

LABEL=com.jchristn.armor.agent
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
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
# True when the launchd agent from run-at-startup.sh is loaded (KeepAlive may restart the agent).
launchd_loaded() { launchctl print "gui/$(id -u)/$LABEL" >/dev/null 2>&1; }
# Unloads it and waits until launchd has let go of the label (bootstrap fails while it lingers).
launchd_unload() {
    launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || launchctl unload "$PLIST" 2>/dev/null || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        launchd_loaded || return 0
        sleep 0.5
    done
}

registered=0
if launchd_loaded; then
    launchd_unload
    registered=1
fi
if [ -f "$PLIST" ]; then
    rm -f "$PLIST"
    registered=1
fi
if [ "$registered" -eq 1 ]; then
    echo "Removed the launchd agent $PLIST."
fi
if [ "$registered" -eq 0 ]; then
    echo "The agent is not registered to run at login; nothing to do."
    exit 0
fi
if agent_running; then
    stop_armor agent
    echo "Stopped the running agent."
fi

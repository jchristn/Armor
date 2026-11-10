#!/usr/bin/env bash
# Rebuild Armor from this checkout, replace the install, and run the console in this terminal.
# Stops a running Armor first, so the install is never replaced while it runs, and starts the
# agent again the way it ran before; otherwise the console starts the agent itself.
# Works whether or not Armor is installed. The new build is staged first and the old install is
# kept aside until the new one is verified; if any step fails, the old install is put back and the
# agent is restarted as it was. Configuration and backups under ~/.armor (or ARMOR_HOME) are kept.
# WSL 1 version: uses the Linux dotnet inside this distro (never the Windows ones). The
# agent restarts through the Windows Startup launcher from run-at-startup.sh when one is
# registered (an armor-agent systemd user service left by scripts/linux also counts).
# Usage: ./scripts/wsl/run.sh [net8.0|net10.0]   (default net10.0)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/wsl/run.sh [net8.0|net10.0]"
    echo "Rebuilds and reinstalls Armor into dist/, then runs the console. Defaults to net10.0."
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
# A framework-dependent app finds a .NET outside the default locations (for example ~/.dotnet from
# dotnet-install.sh) only through DOTNET_ROOT; derive it from the dotnet on PATH and pass it on.
if [ -z "${DOTNET_ROOT:-}" ] && command -v dotnet >/dev/null 2>&1; then
    DOTNET_DIR="$(dirname "$(readlink -f "$(command -v dotnet)")")"
    if [ -d "$DOTNET_DIR/host/fxr" ]; then
        DOTNET_ROOT="$DOTNET_DIR"
        export DOTNET_ROOT
    fi
fi

DEST="$REPO_ROOT/dist"
STAGE="$REPO_ROOT/artifacts/install/$TFM"
PREVIOUS="$REPO_ROOT/artifacts/install/previous"
SERVICE="$HOME/.config/systemd/user/armor-agent.service"

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
# True when a published agent or console is in dist/.
installed() { [ -e "$DEST/Armor.Agent" ] || [ -e "$DEST/Armor.Tui" ]; }
# Starts the agent again the way it ran before this script stopped it.
restart_agent() {
    if [ "$SERVICE_WAS_ACTIVE" -eq 1 ]; then
        systemctl --user start armor-agent
        echo "Restarted the armor-agent systemd user service."
    elif [ "$AGENT_WAS_RUNNING" -eq 1 ] && wsl_startup_exists armor-agent; then
        wsl_startup_run armor-agent
        echo "Restarted the agent through the Windows Startup launcher."
    elif [ "$AGENT_WAS_RUNNING" -eq 1 ]; then
        [ -f "$DEST/Armor.Agent" ] || { echo "Error: Cannot restart the agent: $DEST/Armor.Agent is missing." >&2; return 1; }
        nohup "$DEST/Armor.Agent" >/dev/null 2>&1 &
        echo "Restarted the agent. It stops when this distro shuts down; run-at-startup.sh keeps it running."
    else
        echo "The agent was not running; the console starts it."
    fi
}

STOPPED=0
VERIFIED=0
SERVICE_WAS_ACTIVE=0
AGENT_WAS_RUNNING=0
# On failure after Armor was stopped: put the old install back if the new one was not verified,
# then start the agent again as it ran before.
on_exit() {
    _code=$?
    [ "$_code" -ne 0 ] && [ "$STOPPED" -eq 1 ] || return 0
    echo "The run failed; restoring the previous state." >&2
    if [ "$VERIFIED" -eq 0 ] && [ -e "$PREVIOUS" ]; then
        rm -rf "$DEST" || true
        mv "$PREVIOUS" "$DEST" || true
        echo "Restored the previous install at $DEST." >&2
    fi
    restart_agent >&2 || true
}
trap on_exit EXIT

echo "Armor run (WSL 1, $TFM)"
echo "[1/6] Stopping Armor..."
# Stop the service whenever its unit exists: one in "activating (auto-restart)" would respawn the
# agent mid-publish even though it is not active.
if [ -f "$SERVICE" ] && command -v systemctl >/dev/null 2>&1; then
    if systemctl --user is-active --quiet armor-agent 2>/dev/null; then SERVICE_WAS_ACTIVE=1; fi
    systemctl --user stop armor-agent >/dev/null 2>&1 || true
    if [ "$SERVICE_WAS_ACTIVE" -eq 1 ]; then echo "Stopped the armor-agent systemd user service."; fi
fi
if [ "$SERVICE_WAS_ACTIVE" -eq 0 ] && agent_running; then
    AGENT_WAS_RUNNING=1
fi
STOPPED=1
stop_armor
echo "[2/6] Building Armor.Agent and Armor.Tui ($TFM)..."
rm -rf "$STAGE"
for PROJECT in Armor.Agent Armor.Tui; do
    dotnet publish "$REPO_ROOT/src/$PROJECT/$PROJECT.csproj" -c Release -f "$TFM" -p:TargetFrameworks="$TFM" -o "$STAGE"
done
echo "[3/6] Replacing the install at $DEST..."
rm -rf "$PREVIOUS"
if installed; then
    mv "$DEST" "$PREVIOUS"
    echo "Moved the existing install aside."
else
    rm -rf "$DEST"
    echo "Armor is not installed; continuing."
fi
mv "$STAGE" "$DEST"
echo "[4/6] Verifying the install..."
for EXE in Armor.Agent Armor.Tui; do
    [ -f "$DEST/$EXE" ] || die "$EXE is missing from $DEST after the publish."
    chmod +x "$DEST/$EXE" 2>/dev/null || true # DrvFs checkouts may not keep exec bits
done
VERIFIED=1
rm -rf "$PREVIOUS"
echo "Installed Armor.Agent and Armor.Tui ($TFM) to $DEST."
echo "[5/6] Restarting the agent..."
restart_agent
STOPPED=0
echo "[6/6] Starting the console..."
if [ -z "${DISPLAY:-}" ]; then
    echo "Note: DISPLAY is not set. The console works, but WSL 1 has no WSLg and the agent is a GUI app, so scheduled backups do not run. Start an X server on Windows and set DISPLAY, or use the scripts\\windows scripts." >&2
fi
trap - EXIT
exec "$DEST/Armor.Tui"

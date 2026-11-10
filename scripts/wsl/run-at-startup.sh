#!/usr/bin/env bash
# Register the Armor agent (the scheduler, dist/Armor.Agent) to start at Windows login inside this
# distro, and start it now unless it is already running. systemd user services and desktop
# autostart entries do not run at Windows login, so this installs a hidden launcher in the Windows
# Startup folder that runs the agent through wsl.exe (no admin rights needed); the launcher keeps
# the distro running while the agent runs. Removes any armor-agent systemd user service or desktop
# autostart entry left by scripts/linux, so the agent does not start twice. Re-running refreshes
# the launcher. Needs the published copy from install.sh.
# WSL 1 version: WSL 1 has no WSLg, so the agent (a GUI app) needs an X server on Windows that
# starts before it, with DISPLAY set; the launcher passes this shell's DISPLAY to the agent.
# Usage: ./scripts/wsl/run-at-startup.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/wsl/run-at-startup.sh"
    echo "Registers dist/Armor.Agent to start at login and starts it now."
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

# A framework-dependent app finds a .NET outside the default locations (for example ~/.dotnet from
# dotnet-install.sh) only through DOTNET_ROOT; derive it from the dotnet on PATH and pass it on.
if [ -z "${DOTNET_ROOT:-}" ] && command -v dotnet >/dev/null 2>&1; then
    DOTNET_DIR="$(dirname "$(readlink -f "$(command -v dotnet)")")"
    if [ -d "$DOTNET_DIR/host/fxr" ]; then
        DOTNET_ROOT="$DOTNET_DIR"
        export DOTNET_ROOT
    fi
fi
AGENT_BIN="$REPO_ROOT/dist/Armor.Agent"
SERVICE="$HOME/.config/systemd/user/armor-agent.service"
DESKTOP="$HOME/.config/autostart/armor-agent.desktop"
# True when this user's agent or console is running (apphost launches, or dotnet <dll>).
armor_running() {
    pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
        || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
# True when this user's agent is running.
agent_running() { pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -f 'Armor\.Agent\.dll' >/dev/null 2>&1; }

# -f rather than -x: a checkout on a Windows drive (DrvFs) may not report executable bits.
[ -f "$AGENT_BIN" ] || die "Armor is not installed at $REPO_ROOT/dist. Run scripts/wsl/install.sh first."
if [ -z "${DISPLAY:-}" ]; then
    die "WSL 1 has no WSLg, so it cannot show the Armor agent. Run scripts\\windows\\run-at-startup.bat from Windows instead, move this distro to WSL 2 (wsl --set-version ${WSL_DISTRO_NAME:-<distro>} 2) and use scripts/wsl2, or start an X server on Windows and set DISPLAY."
fi
# Install the system packages the agent needs (INSTALL_SYSTEM_PACKAGES=0 only lists them).
wsl_install_deps "Armor.Agent" icu gui browser
wsl_check_icu
wsl_check_libs error "Armor.Agent" "$WSL_AVALONIA_LIBS"

echo "Armor start at login (WSL 1)"
# Replace anything scripts/linux/run-at-startup.sh may have created, so the agent does not start twice.
if [ -f "$SERVICE" ] || [ -L "$HOME/.config/systemd/user/graphical-session.target.wants/armor-agent.service" ] \
    || [ -L "$HOME/.config/systemd/user/default.target.wants/armor-agent.service" ]; then
    systemctl --user disable --now armor-agent >/dev/null 2>&1 || true
    rm -f "$SERVICE" "$HOME/.config/systemd/user/graphical-session.target.wants/armor-agent.service" \
        "$HOME/.config/systemd/user/default.target.wants/armor-agent.service"
    systemctl --user daemon-reload >/dev/null 2>&1 || true
    echo "Removed the old armor-agent systemd user service."
fi
if [ -f "$DESKTOP" ]; then
    rm -f "$DESKTOP"
    echo "Removed the old desktop autostart entry."
fi

# The launcher starts from a fresh wsl.exe login, so pass the environment the agent needs explicitly.
set -- env
if [ -n "${DOTNET_ROOT:-}" ]; then set -- "$@" "DOTNET_ROOT=$DOTNET_ROOT"; fi
# WSL 1 has no WSLg; the agent draws on the X server named by DISPLAY.
set -- "$@" "DISPLAY=$DISPLAY"
wsl_startup_install armor-agent "$@" "$AGENT_BIN"
echo "Registered $AGENT_BIN to start in WSL ($WSL_DISTRO_NAME) at Windows login (Windows Startup launcher)."

if agent_running; then
    echo "The agent is already running."
else
    # Start it through the same launcher, so it keeps running after this terminal closes.
    wsl_startup_run armor-agent
    echo "Started the agent now."
fi
echo "The X server must be running before the agent starts at login; without it the agent exits and your schedules do not run."
echo "Stop it with scripts/wsl/kill-armor.sh; undo the registration with scripts/wsl/remove-from-startup.sh."

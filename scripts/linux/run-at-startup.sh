#!/usr/bin/env bash
# Register the Armor agent (the tray scheduler, dist/Armor.Agent) to start at login, and start it
# now unless it is already running. Uses a systemd --user service tied to the desktop session
# (graphical-session.target; restarts it on failure) when this session activates that target, and
# otherwise a desktop autostart entry. Only one of the two is kept. Without a display (DISPLAY
# unset) the agent is registered but not started now. No root needed. Re-running refreshes the
# registration. Needs the published copy from install.sh.
# Usage: ./scripts/linux/run-at-startup.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
die() { echo "Error: $*" >&2; exit 1; }
usage() {
    echo "Usage: scripts/linux/run-at-startup.sh"
    echo "Registers dist/Armor.Agent to start at login and starts it now."
}

if [ "$(uname -s)" != Linux ]; then
    die "This script is for Linux. Use scripts/macos on macOS or scripts\\windows on Windows."
fi
# A Microsoft kernel alone is not WSL: Docker Desktop containers on the WSL 2 backend have one too.
if grep -qi microsoft /proc/version 2>/dev/null \
    && { [ -n "${WSL_DISTRO_NAME:-}" ] || [ -d /run/WSL ] || ls /proc/sys/fs/binfmt_misc/WSLInterop* >/dev/null 2>&1; }; then
    die "This is WSL. Use the same script in scripts/wsl2 (WSL 2) or scripts/wsl (WSL 1); they handle the Windows PATH, line endings and Windows startup."
fi
# shellcheck source=../common/native-libs.sh
. "$REPO_ROOT/scripts/common/native-libs.sh"

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

[ -f "$AGENT_BIN" ] || die "Armor is not installed at $REPO_ROOT/dist. Run scripts/linux/install.sh first."
# Install the system packages the agent needs (INSTALL_SYSTEM_PACKAGES=0 only lists them).
native_install_deps "Armor.Agent" icu gui
native_check_icu
native_check_libs error "Armor.Agent" "$NATIVE_AVALONIA_LIBS"

echo "Armor start at login (Linux)"
# systemd only when this desktop session activates graphical-session.target (GNOME, KDE Plasma and
# other systemd-integrated sessions); otherwise the unit would never start, so use autostart.
if command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1 \
    && systemctl --user is-active --quiet graphical-session.target 2>/dev/null; then
    mkdir -p "$(dirname "$SERVICE")"
    {
        echo "[Unit]"
        echo "Description=Armor backup agent (tray scheduler)"
        echo "# A GUI (tray) app: start it with the desktop session, which provides the display."
        echo "After=graphical-session.target"
        echo "PartOf=graphical-session.target"
        echo
        echo "[Service]"
        echo "ExecStart=\"$AGENT_BIN\""
        if [ -n "${DOTNET_ROOT:-}" ]; then echo "Environment=\"DOTNET_ROOT=$DOTNET_ROOT\""; fi
        echo "Restart=on-failure"
        echo "RestartSec=5"
        echo
        echo "[Install]"
        echo "WantedBy=graphical-session.target"
    } > "$SERVICE"
    # An older unit was wanted by default.target; drop that link so it is not started twice.
    rm -f "$HOME/.config/systemd/user/default.target.wants/armor-agent.service"
    systemctl --user daemon-reload
    systemctl --user enable armor-agent
    echo "Registered $AGENT_BIN to start at login (systemd user service armor-agent)."
    # One mechanism only, so the agent does not start twice.
    if [ -f "$DESKTOP" ]; then
        rm -f "$DESKTOP"
        echo "Removed the old desktop autostart entry."
    fi
    if agent_running; then
        echo "The agent is already running."
    else
        systemctl --user start armor-agent
        echo "Started the agent now."
    fi
    echo "Check it with: systemctl --user status armor-agent"
    echo "The service starts with your desktop session (graphical-session.target) and stops when you log out."
else
    mkdir -p "$(dirname "$DESKTOP")"
    {
        echo "[Desktop Entry]"
        echo "Type=Application"
        echo "Name=Armor Agent"
        echo "Comment=Armor backup agent (tray scheduler)"
        if [ -n "${DOTNET_ROOT:-}" ]; then
            echo "Exec=env \"DOTNET_ROOT=$DOTNET_ROOT\" \"$AGENT_BIN\""
        else
            echo "Exec=\"$AGENT_BIN\""
        fi
        echo "X-GNOME-Autostart-enabled=true"
    } > "$DESKTOP"
    echo "Registered $AGENT_BIN to start at login (desktop autostart entry $DESKTOP)."
    # One mechanism only, so the agent does not start twice.
    if [ -f "$SERVICE" ] || [ -L "$HOME/.config/systemd/user/graphical-session.target.wants/armor-agent.service" ] \
        || [ -L "$HOME/.config/systemd/user/default.target.wants/armor-agent.service" ]; then
        systemctl --user disable --now armor-agent >/dev/null 2>&1 || true
        rm -f "$SERVICE" "$HOME/.config/systemd/user/graphical-session.target.wants/armor-agent.service" \
            "$HOME/.config/systemd/user/default.target.wants/armor-agent.service"
        systemctl --user daemon-reload >/dev/null 2>&1 || true
        echo "Removed the old armor-agent systemd user service."
    fi
    if agent_running; then
        echo "The agent is already running."
    elif [ -z "${DISPLAY:-}" ]; then
        echo "No display (DISPLAY is unset), so the agent (an X11 tray app) was not started now; it starts at your next desktop login."
    else
        nohup "$AGENT_BIN" >/dev/null 2>&1 &
        echo "Started the agent now."
    fi
fi
echo "The tray icon needs StatusNotifierItem support (KDE Plasma has it built in; GNOME needs the"
echo "'AppIndicator and KStatusNotifierItem' extension). Without a tray, the agent still runs your schedules."
echo "Stop it with scripts/linux/kill-armor.sh; undo the registration with scripts/linux/remove-from-startup.sh."

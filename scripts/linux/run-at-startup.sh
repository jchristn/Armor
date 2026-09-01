#!/bin/sh
# Register the Armor agent (the tray/scheduler process) to start at login.
# Prefers a systemd --user service (works for desktops and headless servers,
# restarts on failure); falls back to a desktop autostart entry when
# systemd --user isn't available. No root needed. Expects the published layout
# from GETTING_STARTED.md: the agent and the TUI side by side in dist/ at the
# repository root (run update.sh first).
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
AGENT_BIN="$REPO_ROOT/dist/Armor.Agent"
SERVICE="$HOME/.config/systemd/user/armor-agent.service"
DESKTOP="$HOME/.config/autostart/armor-agent.desktop"

if [ ! -x "$AGENT_BIN" ]; then
    echo "Armor.Agent not found at $AGENT_BIN." >&2
    echo "Run scripts/linux/update.sh first to build and publish it." >&2
    exit 1
fi

if command -v systemctl >/dev/null 2>&1 && systemctl --user show-environment >/dev/null 2>&1; then
    mkdir -p "$(dirname "$SERVICE")"
    cat > "$SERVICE" <<EOF
[Unit]
Description=Armor backup agent

[Service]
ExecStart=$AGENT_BIN
Restart=on-failure

[Install]
WantedBy=default.target
EOF
    systemctl --user daemon-reload
    systemctl --user enable --now armor-agent
    echo "Installed and started the armor-agent systemd user service."
    echo "Check it with: systemctl --user status armor-agent"
    echo "On a headless server, also run 'loginctl enable-linger \$USER' once so it"
    echo "runs without anyone logged in."
else
    mkdir -p "$(dirname "$DESKTOP")"
    cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=Armor Agent
Comment=Armor backup agent (system-tray scheduler)
Exec=$AGENT_BIN
X-GNOME-Autostart-enabled=true
EOF
    echo "Installed $DESKTOP; the agent will start at your next desktop login."
    if pgrep -x Armor.Agent >/dev/null 2>&1; then
        echo "The agent is already running."
    else
        "$AGENT_BIN" >/dev/null 2>&1 &
        echo "Started the agent now."
    fi
fi

echo "Note: the tray icon needs StatusNotifierItem support (built into KDE Plasma;"
echo "GNOME needs the 'AppIndicator and KStatusNotifierItem' extension). Without a"
echo "tray, the agent still runs your schedules."

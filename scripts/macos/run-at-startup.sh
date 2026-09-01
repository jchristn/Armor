#!/bin/sh
# Register the Armor agent (the menu-bar scheduler) as a launchd user agent so
# it starts at login. No sudo needed; everything lives under
# ~/Library/LaunchAgents. Expects the published layout from GETTING_STARTED.md:
# the agent and the TUI side by side in dist/ at the repository root (run
# update.sh first).
set -eu

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
AGENT_BIN="$REPO_ROOT/dist/Armor.Agent"
LABEL="com.jchristn.armor.agent"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

if [ ! -x "$AGENT_BIN" ]; then
    echo "Armor.Agent not found at $AGENT_BIN." >&2
    echo "Run scripts/macos/update.sh first to build and publish it." >&2
    exit 1
fi

# KeepAlive/SuccessfulExit=false restarts the agent if it crashes, but leaves
# it stopped when it exits cleanly (the tray menu's Exit item).
mkdir -p "$HOME/Library/LaunchAgents"
cat > "$PLIST" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>$AGENT_BIN</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <dict>
        <key>SuccessfulExit</key>
        <false/>
    </dict>
</dict>
</plist>
EOF

# Load it now: bootstrap is the modern verb; fall back to load -w on older
# macOS. Boot out any previously loaded copy first so re-running is a refresh.
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || launchctl load -w "$PLIST"

echo "Installed $PLIST and started the agent."
echo "Look for the Armor icon in the menu bar."

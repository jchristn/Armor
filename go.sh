#!/usr/bin/env bash
# Build and launch the Armor TUI (POSIX). Mirrors go.bat for macOS/Linux users.
set -euo pipefail
cd "$(dirname "$0")"

# macOS: scripts/macos/run.sh stops Armor itself (through launchd first) and restarts the agent the
# way it ran, so hand off before killing anything here.
if [ "$(uname -s)" = Darwin ]; then
    exec ./scripts/macos/run.sh "$@"
fi

# Stop both running Armor processes first. The agent (the tray app) keeps
# Armor.Core.dll open; if it is left running, the net10 build cannot overwrite
# the DLL and silently ships a stale TUI. The TUI relaunches the agent on start.
# Only this user's processes, and only the executables (and dotnet <dll> launches): a bare
# 'pkill -f Armor.Tui' also kills any process whose command line merely mentions that path.
pkill -u "$(id -u)" -x Armor.Tui 2>/dev/null || true
pkill -u "$(id -u)" -x Armor.Agent 2>/dev/null || true
pkill -u "$(id -u)" -f 'Armor\.(Tui|Agent)\.dll' 2>/dev/null || true

cd src
dotnet build
cd Armor.Tui/bin/Debug/net10.0
./Armor.Tui

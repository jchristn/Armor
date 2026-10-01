#!/usr/bin/env bash
# Build and launch the Armor TUI (POSIX). Mirrors go.bat for macOS/Linux users.
set -euo pipefail
cd "$(dirname "$0")"

# Stop both running Armor processes first. The agent (the tray app) keeps
# Armor.Core.dll open; if it is left running, the net10 build cannot overwrite
# the DLL and silently ships a stale TUI. The TUI relaunches the agent on start.
pkill -f "Armor.Tui" 2>/dev/null || true
pkill -f "Armor.Agent" 2>/dev/null || true

cd src
dotnet build
cd Armor.Tui/bin/Debug/net10.0
./Armor.Tui

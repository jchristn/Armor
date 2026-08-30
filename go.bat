@echo off
cd /d "%~dp0"
REM Stop both running Armor processes first. The agent (the tray app) keeps
REM Armor.Core.dll open; if it is left running, the net10 build cannot overwrite
REM the DLL and silently ships a stale TUI. The TUI relaunches the agent on start.
taskkill /IM Armor.Tui.exe /F >nul 2>&1
taskkill /IM Armor.Agent.exe /F >nul 2>&1
cd src && dotnet build && cd Armor.Tui\bin\debug\net10.0 && armor.tui
cd /d "%~dp0"

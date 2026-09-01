@echo off
setlocal
REM Update Armor from source: pull the latest code, rebuild, and republish the
REM agent and the TUI side by side into dist\ (the tray's Open action needs both
REM executables in the same folder). Restarts the agent if it was running.

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"
cd /d "%REPO_ROOT%"

REM Remember whether the agent was running so we can restart it afterwards.
set "AGENT_WAS_RUNNING=0"
tasklist /FI "IMAGENAME eq Armor.Agent.exe" | find /I "Armor.Agent.exe" >nul && set "AGENT_WAS_RUNNING=1"

REM Stop both Armor processes: a running agent keeps Armor.Core.dll open, and
REM publishing over it would silently ship stale binaries.
taskkill /IM Armor.Tui.exe /F >nul 2>&1
taskkill /IM Armor.Agent.exe /F >nul 2>&1

git pull --ff-only
if errorlevel 1 echo git pull failed; rebuilding the checkout as-is.

dotnet publish src\Armor.Agent -c Release -f net10.0 -o dist || exit /b 1
dotnet publish src\Armor.Tui   -c Release -f net10.0 -o dist || exit /b 1

echo Published Armor.Agent.exe and Armor.Tui.exe to "%REPO_ROOT%\dist".

if "%AGENT_WAS_RUNNING%"=="1" (
    start "" "%REPO_ROOT%\dist\Armor.Agent.exe"
    echo Restarted the agent.
)
endlocal

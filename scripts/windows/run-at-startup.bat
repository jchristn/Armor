@echo off
setlocal
REM Register the Armor agent (the system-tray scheduler) to start at login for
REM the current user, via the HKCU Run registry key. No administrator rights
REM needed. Expects the published layout from GETTING_STARTED.md: the agent and
REM the TUI side by side in dist\ at the repository root (run update.bat first).

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"
set "AGENT_EXE=%REPO_ROOT%\dist\Armor.Agent.exe"

if not exist "%AGENT_EXE%" (
    echo Armor.Agent.exe not found at "%AGENT_EXE%".
    echo Run scripts\windows\update.bat first to build and publish it.
    exit /b 1
)

reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ArmorAgent /t REG_SZ /d "\"%AGENT_EXE%\"" /f >nul
if errorlevel 1 (
    echo Failed to write the Run registry key.
    exit /b 1
)
echo Registered "%AGENT_EXE%" to run at login.

tasklist /FI "IMAGENAME eq Armor.Agent.exe" | find /I "Armor.Agent.exe" >nul
if errorlevel 1 (
    start "" "%AGENT_EXE%"
    echo Started the agent now; look for the Armor icon in the system tray.
) else (
    echo The agent is already running.
)
endlocal

@echo off
setlocal
REM Build Armor from the current checkout and publish the agent and the TUI side
REM by side into dist\ (the tray's Open action and the TUI's agent auto-start
REM need both executables in the same folder). Stops a running agent first, so
REM the publish can't ship stale binaries, and restarts it afterwards.
REM Configuration and backups under %USERPROFILE%\.armor are left alone.
REM Usage: scripts\windows\install.bat [net8.0 or net10.0]


set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"
set "TFM=%~1"
if "%TFM%"=="" set "TFM=net10.0"
if /I not "%TFM%"=="net8.0" if /I not "%TFM%"=="net10.0" (
    echo Unsupported framework: %TFM%
    echo Usage: %~nx0 [net8.0^|net10.0]
    exit /b 1
)
if not "%~2"=="" (
    echo Usage: %~nx0 [net8.0^|net10.0]
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo dotnet was not found. Install the .NET 10 SDK: https://dotnet.microsoft.com/download
    exit /b 1
)

cd /d "%REPO_ROOT%"

set "AGENT_WAS_RUNNING=0"
tasklist /FI "IMAGENAME eq Armor.Agent.exe" | find /I "Armor.Agent.exe" >nul && set "AGENT_WAS_RUNNING=1"
taskkill /IM Armor.Tui.exe /F >nul 2>&1
taskkill /IM Armor.Agent.exe /F >nul 2>&1

dotnet publish src\Armor.Agent\Armor.Agent.csproj -c Release -f %TFM% -p:TargetFrameworks=%TFM% -o dist || exit /b 1
dotnet publish src\Armor.Tui\Armor.Tui.csproj -c Release -f %TFM% -p:TargetFrameworks=%TFM% -o dist || exit /b 1

echo Installed Armor.Agent.exe and Armor.Tui.exe (%TFM%) to "%REPO_ROOT%\dist".

if "%AGENT_WAS_RUNNING%"=="1" (
    start "" "%REPO_ROOT%\dist\Armor.Agent.exe"
    echo Restarted the agent.
)
echo Run the console with dist\Armor.Tui.exe, or register the agent for login with
echo scripts\windows\run-at-startup.bat.
endlocal

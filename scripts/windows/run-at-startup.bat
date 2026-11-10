@echo off
REM Register the Armor agent (the system-tray scheduler, dist\Armor.Agent.exe) to start at login for
REM the current user, via the HKCU Run registry key, and start it now unless it is already running.
REM No administrator rights needed. Re-running refreshes the registration. Needs the published copy
REM from install.bat.
REM Usage: scripts\windows\run-at-startup.bat
setlocal
for %%i in ("%~dp0..\..") do set "REPO_ROOT=%%~fi"
set "DEST=%REPO_ROOT%\dist"
set "ARG1=%~1"
if not defined ARG1 goto args_done
if /I "%ARG1%"=="-h" goto usage
if /I "%ARG1%"=="--help" goto usage
if /I "%ARG1%"=="/?" goto usage
goto unexpected_arg
:args_done
set "AGENT_EXE=%DEST%\Armor.Agent.exe"
if not exist "%AGENT_EXE%" goto not_installed

echo Armor start at login (Windows)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ArmorAgent /t REG_SZ /d "\"%AGENT_EXE%\"" /f >nul
if errorlevel 1 goto reg_failed
echo Registered %AGENT_EXE% to start at login (HKCU Run value ArmorAgent).
call :agent_running
if "%AGENT%"=="0" goto start_now
echo The agent is already running.
goto started
:start_now
start "" "%AGENT_EXE%"
echo Started the agent now; look for the Armor icon in the system tray.
:started
echo Stop it with scripts\windows\kill-armor.bat; undo the registration with scripts\windows\remove-from-startup.bat.
endlocal & exit /b 0

:not_installed
echo Error: Armor is not installed at %DEST%. Run scripts\windows\install.bat first. 1>&2
goto fail
:reg_failed
echo Error: Failed to write the Run registry value. 1>&2
goto fail

:unexpected_arg
echo Error: Unexpected argument: %1 ^(this script takes no arguments^) 1>&2
goto fail

:usage
echo Usage: scripts\windows\run-at-startup.bat
echo Registers dist\Armor.Agent.exe to start at login and starts it now.
endlocal & exit /b 0

:fail
endlocal & exit /b 1

REM Sets RUNNING=1 when this user's agent or console is running.
:armor_running
set "RUNNING=0"
tasklist /FI "IMAGENAME eq Armor.Agent.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "Armor.Agent.exe" >nul && set "RUNNING=1"
tasklist /FI "IMAGENAME eq Armor.Tui.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "Armor.Tui.exe" >nul && set "RUNNING=1"
exit /b 0

REM Sets AGENT=1 when this user's agent is running.
:agent_running
set "AGENT=0"
tasklist /FI "IMAGENAME eq Armor.Agent.exe" /FI "USERNAME eq %USERDOMAIN%\%USERNAME%" 2>nul | find /I "Armor.Agent.exe" >nul && set "AGENT=1"
exit /b 0

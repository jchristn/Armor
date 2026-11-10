@echo off
REM Remove what install.bat and run-at-startup.bat created: the login registration (the ArmorAgent
REM HKCU Run value), the published copy in dist\ and any leftover build staging under
REM artifacts\install. Succeeds with a message when nothing is installed. Refuses while Armor is
REM running; stop it with kill-armor.bat first.
REM Configuration, the database and logs under %USERPROFILE%\.armor (or ARMOR_HOME) are kept;
REM reset.bat at the repository root removes them.
REM Usage: scripts\windows\uninstall.bat
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
if exist "%DEST%\Armor.Agent" goto linux_build
set "ARMOR_DIR=%USERPROFILE%\.armor"
if defined ARMOR_HOME set "ARMOR_DIR=%ARMOR_HOME%"

echo Armor uninstall (Windows)
echo [1/3] Checking for running Armor processes...
call :armor_running
if "%RUNNING%"=="1" goto is_running
echo [2/3] Removing the login registration...
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ArmorAgent >nul 2>&1
if errorlevel 1 goto not_registered
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ArmorAgent /f >nul
if errorlevel 1 goto reg_failed
echo Removed the ArmorAgent Run registry value.
goto registration_done
:not_registered
echo The agent is not registered to run at login.
:registration_done
echo [3/3] Removing the install...
if exist "%REPO_ROOT%\artifacts\install" rmdir /s /q "%REPO_ROOT%\artifacts\install"
call :installed
if "%INSTALLED%"=="0" goto nothing_installed
rmdir /s /q "%DEST%"
if exist "%DEST%" goto dest_locked
echo Removed %DEST%.
goto removed
:nothing_installed
echo Armor is not installed; nothing to remove.
:removed
echo Armor's configuration, database and logs in %ARMOR_DIR% were kept; run reset.bat at the repository root to remove them.
echo Done.
endlocal & exit /b 0

:reg_failed
echo Error: Failed to delete the Run registry value. 1>&2
goto fail
:dest_locked
echo Error: Could not remove %DEST%; close anything using it and try again. 1>&2
goto fail

:is_running
echo Error: Armor is running. Exit the agent from its tray menu and close the console ^(or run scripts\windows\kill-armor.bat^), then try again. 1>&2
goto fail

:unexpected_arg
echo Error: Unexpected argument: %1 ^(this script takes no arguments^) 1>&2
goto fail

:linux_build
echo Error: %DEST% holds a Linux/WSL build ^(Armor.Agent without .exe^). Manage that copy with the scripts/wsl2 or scripts/wsl scripts inside WSL, or use a separate clone for Windows. 1>&2
goto fail

:usage
echo Usage: scripts\windows\uninstall.bat
echo Removes the login registration and the published copy in dist\; keeps %USERPROFILE%\.armor.
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

REM Sets INSTALLED=1 when a published agent or console is in dist\.
:installed
set "INSTALLED=0"
if exist "%DEST%\Armor.Agent.exe" set "INSTALLED=1"
if exist "%DEST%\Armor.Tui.exe" set "INSTALLED=1"
exit /b 0

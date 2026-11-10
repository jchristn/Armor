@echo off
REM Build Armor from this checkout and publish the agent and the console (TUI) side by side into
REM dist\ at the repository root (the tray's Open action and the console's agent auto-start find each
REM other beside their executable). Fails if Armor is already installed there; use reinstall.bat to
REM replace it. Refuses while Armor is running; stop it with kill-armor.bat first.
REM The build is staged under artifacts\install\<framework>. Configuration and backups under
REM %USERPROFILE%\.armor (or ARMOR_HOME) are left alone.
REM Usage: scripts\windows\install.bat [net8.0^|net10.0]   (default net10.0)
setlocal
for %%i in ("%~dp0..\..") do set "REPO_ROOT=%%~fi"
set "DEST=%REPO_ROOT%\dist"
set "TFM=net10.0"
set "ARG1=%~1"
if not defined ARG1 goto args_done
if /I "%ARG1%"=="-h" goto usage
if /I "%ARG1%"=="--help" goto usage
if /I "%ARG1%"=="/?" goto usage
if /I "%ARG1%"=="net8" (set "TFM=net8.0" & goto args_shift)
if /I "%ARG1%"=="net8.0" (set "TFM=net8.0" & goto args_shift)
if /I "%ARG1%"=="net10" (set "TFM=net10.0" & goto args_shift)
if /I "%ARG1%"=="net10.0" (set "TFM=net10.0" & goto args_shift)
if /I "%ARG1:~0,3%"=="net" goto bad_framework
goto unexpected_arg
:args_shift
shift
:args_done
if not "%~1"=="" goto unexpected_arg
where dotnet >nul 2>nul
if errorlevel 1 goto no_dotnet
if exist "%DEST%\Armor.Agent" goto linux_build
set "STAGE=%REPO_ROOT%\artifacts\install\%TFM%"

echo Armor install (Windows, %TFM%)
echo [1/5] Checking for running Armor processes...
call :armor_running
if "%RUNNING%"=="1" goto is_running
echo [2/5] Checking for an existing install...
call :installed
if "%INSTALLED%"=="1" goto already_installed
echo [3/5] Building Armor.Agent and Armor.Tui ^(%TFM%^)...
if exist "%STAGE%" rmdir /s /q "%STAGE%"
dotnet publish "%REPO_ROOT%\src\Armor.Agent\Armor.Agent.csproj" -c Release -f %TFM% -p:TargetFrameworks=%TFM% -o "%STAGE%"
if errorlevel 1 goto build_failed
dotnet publish "%REPO_ROOT%\src\Armor.Tui\Armor.Tui.csproj" -c Release -f %TFM% -p:TargetFrameworks=%TFM% -o "%STAGE%"
if errorlevel 1 goto build_failed
echo [4/5] Installing to %DEST%...
if exist "%DEST%" rmdir /s /q "%DEST%"
if exist "%DEST%" goto dest_locked
move "%STAGE%" "%DEST%" >nul
if errorlevel 1 goto move_failed
echo [5/5] Verifying the install...
if not exist "%DEST%\Armor.Agent.exe" goto verify_failed
if not exist "%DEST%\Armor.Tui.exe" goto verify_failed
echo Installed Armor.Agent.exe and Armor.Tui.exe ^(%TFM%^) to %DEST%.
echo Start the console with %DEST%\Armor.Tui.exe (it starts the agent too), or register the agent for login with scripts\windows\run-at-startup.bat.
echo Done.
endlocal & exit /b 0

:already_installed
echo Error: Armor is already installed at %DEST%. Run scripts\windows\reinstall.bat to replace it. 1>&2
goto fail

:is_running
echo Error: Armor is running. Exit the agent from its tray menu and close the console ^(or run scripts\windows\kill-armor.bat^), then try again. 1>&2
goto fail

:bad_framework
echo Error: Unsupported framework: %ARG1%. Supported frameworks: net8.0, net10.0. 1>&2
goto fail
:unexpected_arg
echo Error: Unexpected argument: %1 1>&2
goto fail
:no_dotnet
echo Error: dotnet was not found on PATH. Install the .NET SDK: https://dotnet.microsoft.com/download 1>&2
goto fail

:build_failed
echo Error: The build failed; the existing install, if any, was left in place. 1>&2
goto fail
:dest_locked
echo Error: Could not remove %DEST%; close anything using it and try again. 1>&2
goto fail
:move_failed
echo Error: Could not move the new build from %STAGE% to %DEST%. 1>&2
goto fail
:verify_failed
echo Error: Armor.Agent.exe or Armor.Tui.exe is missing from %DEST% after the publish. 1>&2
goto fail

:linux_build
echo Error: %DEST% holds a Linux/WSL build ^(Armor.Agent without .exe^). Manage that copy with the scripts/wsl2 or scripts/wsl scripts inside WSL, or use a separate clone for Windows. 1>&2
goto fail

:usage
echo Usage: scripts\windows\install.bat [net8.0^|net10.0]
echo Builds Armor and publishes Armor.Agent.exe and Armor.Tui.exe into dist\. Defaults to net10.0.
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

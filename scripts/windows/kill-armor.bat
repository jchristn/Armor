@echo off
REM Stop this user's running Armor processes in this session: the agent (Armor.Agent.exe) and the
REM console (Armor.Tui.exe), including copies started with "dotnet <dll>". Asks them to exit, waits up
REM to 5 seconds, then force-kills what is left (taskkill /T /F). Succeeds when nothing is running;
REM fails if something could not be stopped. The login registration is kept, so the agent starts
REM again at the next login (remove-from-startup.bat removes it).
REM Usage: scripts\windows\kill-armor.bat
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

echo Stopping Armor processes...
call :stop_armor
if errorlevel 1 goto fail
if "%STOPPED_NONE%"=="1" (endlocal & exit /b 0)
echo Done.
endlocal & exit /b 0

:unexpected_arg
echo Error: Unexpected argument: %1 ^(this script takes no arguments^) 1>&2
goto fail

:usage
echo Usage: scripts\windows\kill-armor.bat
echo Stops the running Armor agent and console.
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

REM Stops this user's Armor processes in this session (only the agent with "agent"), including copies
REM started with "dotnet <dll>": asks them to exit (taskkill /T), waits up to 5 seconds, then
REM force-kills what is left (/T /F). taskkill without /F alone does nothing to a console or windowless
REM process, hence the second pass. Sets STOPPED_NONE=1 when nothing was running; returns 1 on failure.
:stop_armor
set "STOPPED_NONE=0"
set "ARMOR_STOP_NAMES=Armor.Agent Armor.Tui"
set "ARMOR_STOP_DLL=Armor\.(Agent|Tui)\.dll"
if /I "%~1"=="agent" set "ARMOR_STOP_NAMES=Armor.Agent"
if /I "%~1"=="agent" set "ARMOR_STOP_DLL=Armor\.Agent\.dll"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$names = @($env:ARMOR_STOP_NAMES -split ' '); $dll = $env:ARMOR_STOP_DLL;" ^
  "$session = (Get-Process -Id $PID).SessionId;" ^
  "function Find-Armor { Get-CimInstance Win32_Process | Where-Object { $_.SessionId -eq $session } | Where-Object { ($names -contains [IO.Path]::GetFileNameWithoutExtension($_.Name)) -or ($_.Name -ieq 'dotnet.exe' -and $_.CommandLine -match $dll) } };" ^
  "$p = @(Find-Armor); if ($p.Count -eq 0) { Write-Output 'No Armor processes are running.'; exit 3 };" ^
  "foreach ($x in $p) { & taskkill.exe /PID $x.ProcessId /T 2>$null | Out-Null };" ^
  "for ($i = 0; $i -lt 5 -and @(Find-Armor).Count -gt 0; $i++) { Start-Sleep -Seconds 1 };" ^
  "$left = @(Find-Armor); if ($left.Count -gt 0) { Write-Output 'Some Armor processes did not exit; force-killing them.'; foreach ($x in $left) { & taskkill.exe /PID $x.ProcessId /T /F 2>$null | Out-Null }; Start-Sleep -Seconds 1 };" ^
  "$left = @(Find-Armor); if ($left.Count -gt 0) { [Console]::Error.WriteLine('Error: Some Armor processes could not be stopped: ' + (($left | ForEach-Object { $_.Name + ' (' + $_.ProcessId + ')' }) -join ', ')); exit 1 };" ^
  "exit 0"
if errorlevel 3 goto stop_armor_none
if errorlevel 1 exit /b 1
exit /b 0
:stop_armor_none
set "STOPPED_NONE=1"
exit /b 0

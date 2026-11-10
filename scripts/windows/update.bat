@echo off
REM Update Armor from source: stop Armor, pull the latest code (git pull --ff-only; on failure it
REM warns and rebuilds the checkout as-is), rebuild, replace the published copy in dist\, and start
REM the agent again if it was running. Works whether or not Armor is installed. The new build is
REM staged under artifacts\install\<framework> first and the old install is kept aside until the new
REM one is verified; if any step fails, the old install is put back and the agent is restarted as it
REM was. Configuration and backups under %USERPROFILE%\.armor (or ARMOR_HOME) are left alone.
REM Usage: scripts\windows\update.bat [net8.0^|net10.0]   (default net10.0)
REM cmd keeps reading a batch file while it runs, so a git pull that rewrites this file would corrupt
REM the rest of the run. This file only copies itself to %TEMP% and hands off to the copy, without
REM call, so cmd never returns to it; the copy runs everything below :main and deletes itself.
REM Only the temporary copy (named by ARMOR_UPDATE_COPY) runs the update itself; a stale variable from
REM an interrupted run does not make the original skip the hand-off.
if defined ARMOR_UPDATE_COPY if /I "%~f0"=="%ARMOR_UPDATE_COPY%" goto main
setlocal
for %%i in ("%~dp0..\..") do set "UPDATE_ROOT=%%~fi"
set "UPDATE_COPY=%TEMP%\armor-update-%RANDOM%%RANDOM%.bat"
REM %~dpn0.bat rather than %~f0: invoked as "update" (quoted, no extension), %~f0 has no .bat.
copy /y "%~dpn0.bat" "%UPDATE_COPY%" >nul
if errorlevel 1 (
    echo Error: Could not copy update.bat to the temporary folder. 1>&2
    endlocal & exit /b 1
)
endlocal & set "ARMOR_UPDATE_ROOT=%UPDATE_ROOT%" & set "ARMOR_UPDATE_COPY=%UPDATE_COPY%" & "%UPDATE_COPY%" %*

:main
setlocal
set "REPO_ROOT=%ARMOR_UPDATE_ROOT%"
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
where git >nul 2>nul
if errorlevel 1 goto no_git
if exist "%DEST%\Armor.Agent" goto linux_build
set "STAGE=%REPO_ROOT%\artifacts\install\%TFM%"
set "PREVIOUS=%REPO_ROOT%\artifacts\install\previous"
set "STOPPED=0"
set "VERIFIED=0"
set "AGENT_WAS_RUNNING=0"
set "NOT_RUNNING_MSG=The agent was not running; leaving it stopped."

echo Armor update (Windows, %TFM%)
echo [1/6] Stopping Armor...
call :agent_running
set "AGENT_WAS_RUNNING=%AGENT%"
set "STOPPED=1"
call :stop_armor
if errorlevel 1 goto fail
echo [2/6] Pulling the latest source...
git -C "%REPO_ROOT%" pull --ff-only
if errorlevel 1 echo Warning: git pull failed; rebuilding the checkout as-is. 1>&2
echo [3/6] Building Armor.Agent and Armor.Tui ^(%TFM%^)...
if exist "%STAGE%" rmdir /s /q "%STAGE%"
dotnet publish "%REPO_ROOT%\src\Armor.Agent\Armor.Agent.csproj" -c Release -f %TFM% -p:TargetFrameworks=%TFM% -o "%STAGE%"
if errorlevel 1 goto build_failed
dotnet publish "%REPO_ROOT%\src\Armor.Tui\Armor.Tui.csproj" -c Release -f %TFM% -p:TargetFrameworks=%TFM% -o "%STAGE%"
if errorlevel 1 goto build_failed
echo [4/6] Replacing the install at %DEST%...
if exist "%PREVIOUS%" rmdir /s /q "%PREVIOUS%"
call :installed
if "%INSTALLED%"=="0" goto swap_none
move "%DEST%" "%PREVIOUS%" >nul
if errorlevel 1 goto dest_locked
echo Moved the existing install aside.
goto swap_place
:swap_none
if exist "%DEST%" rmdir /s /q "%DEST%"
if exist "%DEST%" goto dest_locked
echo Armor is not installed; continuing.
:swap_place
move "%STAGE%" "%DEST%" >nul
if errorlevel 1 goto move_failed
echo [5/6] Verifying the install...
if not exist "%DEST%\Armor.Agent.exe" goto verify_failed
if not exist "%DEST%\Armor.Tui.exe" goto verify_failed
set "VERIFIED=1"
if exist "%PREVIOUS%" rmdir /s /q "%PREVIOUS%"
echo Installed Armor.Agent.exe and Armor.Tui.exe ^(%TFM%^) to %DEST%.
echo [6/6] Restarting the agent...
call :restart_agent
set "STOPPED=0"
echo Done.
set "CODE=0"
goto finish

:no_git
echo Error: git was not found on PATH. Install Git for Windows: https://git-scm.com/download/win 1>&2
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
echo Usage: scripts\windows\update.bat [net8.0^|net10.0]
echo Pulls, rebuilds and republishes Armor into dist\, restarting the agent if it was running. Defaults to net10.0.
set "CODE=0"
goto finish

:fail
if not "%STOPPED%"=="1" goto fail_done
echo The update failed; restoring the previous state. 1>&2
if "%VERIFIED%"=="1" goto fail_restart
if not exist "%PREVIOUS%" goto fail_restart
if exist "%DEST%" rmdir /s /q "%DEST%"
move "%PREVIOUS%" "%DEST%" >nul
if not errorlevel 1 echo Restored the previous install at %DEST%. 1>&2
:fail_restart
call :restart_agent 1>&2
:fail_done
set "CODE=1"
goto finish

REM The copy ends here: leave this batch context first, so deleting the running copy is safe, and
REM hand the exit code to the caller.
:finish
endlocal & set "ARMOR_UPDATE_ROOT=" & set "ARMOR_UPDATE_COPY=" & set "ARMOR_UPDATE_CODE=%CODE%"
(goto) 2>nul & del "%~f0" & set "ARMOR_UPDATE_CODE=" & cmd /c exit %ARMOR_UPDATE_CODE%

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

REM Sets INSTALLED=1 when a published agent or console is in dist\.
:installed
set "INSTALLED=0"
if exist "%DEST%\Armor.Agent.exe" set "INSTALLED=1"
if exist "%DEST%\Armor.Tui.exe" set "INSTALLED=1"
exit /b 0

REM Starts the agent again if this script stopped it.
:restart_agent
if not "%AGENT_WAS_RUNNING%"=="1" goto restart_agent_none
if not exist "%DEST%\Armor.Agent.exe" goto restart_agent_missing
start "" "%DEST%\Armor.Agent.exe"
echo Restarted the agent.
exit /b 0
:restart_agent_missing
echo Error: Cannot restart the agent: %DEST%\Armor.Agent.exe is missing. 1>&2
exit /b 1
:restart_agent_none
echo %NOT_RUNNING_MSG%
exit /b 0

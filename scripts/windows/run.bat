@echo off
setlocal
REM Refresh the published copy in dist\, then run its terminal console in the
REM current window. Usage: scripts\windows\run.bat [net8.0 or net10.0]

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"

call "%SCRIPT_DIR%install.bat" %*
if errorlevel 1 exit /b 1

"%REPO_ROOT%\dist\Armor.Tui.exe"
exit /b %errorlevel%

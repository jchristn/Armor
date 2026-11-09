@echo off
setlocal
REM Remove what install.bat and run-at-startup.bat put in place: the login
REM registration (HKCU Run value), any running Armor processes, and the
REM published copy in dist\. Configuration, the database and backups are kept;
REM reset.bat at the repository root wipes %USERPROFILE%\.armor.

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"

call "%SCRIPT_DIR%remove-from-startup.bat"
if errorlevel 1 exit /b 1

taskkill /IM Armor.Tui.exe /F >nul 2>&1
taskkill /IM Armor.Agent.exe /F >nul 2>&1

if exist "%REPO_ROOT%\dist\" (
    rmdir /s /q "%REPO_ROOT%\dist"
    if exist "%REPO_ROOT%\dist\" (
        echo Could not remove "%REPO_ROOT%\dist"; close anything using it and retry.
        exit /b 1
    )
    echo Removed "%REPO_ROOT%\dist".
) else (
    echo Nothing published at "%REPO_ROOT%\dist".
)
echo Armor's configuration and database were kept; run reset.bat to remove them.
endlocal

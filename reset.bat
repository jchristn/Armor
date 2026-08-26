@echo off
setlocal enabledelayedexpansion

REM ==========================================================================
REM reset.bat - Reset the local Armor installation to factory defaults.
REM
REM Armor keeps all of its local state under a single home directory
REM (ARMOR_HOME if set, otherwise %USERPROFILE%\.armor): the configuration
REM file (armor.json), the SQLite database (armor.db), the logs directory, and
REM the state directory (run locks and the machine-local key/password files).
REM Removing that directory returns Armor to a first-run state; the next launch
REM recreates it empty.
REM ==========================================================================

if defined ARMOR_HOME (
    set "ARMOR_DIR=%ARMOR_HOME%"
) else (
    set "ARMOR_DIR=%USERPROFILE%\.armor"
)

echo.
echo ==========================================================
echo   Armor - Reset to Factory Defaults
echo ==========================================================
echo.
echo WARNING: This is DESTRUCTIVE. It permanently deletes the local
echo Armor home directory and everything in it:
echo.
echo     %ARMOR_DIR%
echo.
echo That removes:
echo   - All backup policies, schedules, and storage targets
echo   - All encryption keys/passwords and cached (unattended) passwords
echo   - The Armor database, configuration, and logs
echo.
echo It does NOT delete backups already written to a storage target
echo (USB drive, S3, Azure, etc.). Password-protected backups stay
echo recoverable if you still know the password. Backups made with the
echo older key-file protection become UNRECOVERABLE once the local key
echo files are deleted.
echo.
set /p "CONFIRM=Type 'RESET' to confirm: "
echo.

if not "%CONFIRM%"=="RESET" (
    echo Aborted. No changes were made.
    exit /b 1
)

REM Refuse to operate on an obviously unsafe target, so a mis-set ARMOR_HOME
REM can never wipe a drive root or an entire user profile.
if "%ARMOR_DIR%"=="" goto :unsafe
if /I "%ARMOR_DIR%"=="%USERPROFILE%" goto :unsafe
if /I "%ARMOR_DIR%"=="%SystemDrive%\" goto :unsafe

echo [1/2] Stopping any running Armor processes...
taskkill /IM Armor.Tui.exe /F >nul 2>&1
taskkill /IM Armor.Agent.exe /F >nul 2>&1

echo [2/2] Removing the Armor home directory...
if exist "%ARMOR_DIR%" rd /s /q "%ARMOR_DIR%"

echo.
echo Factory reset complete.
echo.
echo To start Armor again (it will recreate a fresh, empty home directory):
echo   cd src ^&^& dotnet build ^&^& dotnet run --project Armor.Tui
echo.

endlocal
exit /b 0

:unsafe
echo Refusing to reset: "%ARMOR_DIR%" is not a safe Armor home directory.
echo Set ARMOR_HOME to a dedicated directory (for example %USERPROFILE%\.armor).
endlocal
exit /b 1

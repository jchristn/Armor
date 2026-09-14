@echo off
REM ============================================================================
REM Build Armor installers for THIS machine's OS (Windows) into
REM   installers\<version>\
REM
REM A single machine can only build its own OS's installers. Run this on Windows
REM for the .exe; run build-installers.sh on macOS (.dmg/.pkg) and on Linux
REM (.deb/.rpm/.AppImage). To build all three at once, push a v* tag and let CI
REM (.github/workflows/release.yml) fan out across all three runners.
REM ============================================================================
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "PUB=src\Armor.Publisher\bin\Release\net10.0\armor-publish.dll"

echo Building Armor.Publisher...
dotnet build src\Armor.Publisher\Armor.Publisher.csproj -c Release -v m
if errorlevel 1 exit /b 1

for /f "delims=" %%v in ('dotnet "%PUB%" --print-version') do set "VERSION=%%v"
set "OUT=installers\%VERSION%"

echo.
echo Version : %VERSION%
echo Output  : %OUT%
echo.

if not exist "%OUT%" mkdir "%OUT%"

echo Building Windows installer (Inno Setup)...
dotnet "%PUB%" --channel inno --output "%OUT%"
if %errorlevel% neq 0 (
  echo.
  echo Windows installer build failed. If the error was a missing 'iscc',
  echo install Inno Setup 6:  choco install innosetup
  exit /b 1
)

REM Drop the intermediate publish staging so only installers remain.
if exist "%OUT%\.staging" rmdir /s /q "%OUT%\.staging"

echo.
echo Done. Windows installers are in %OUT%
echo (Run build-installers.sh on macOS and Linux for the other two OSes.)
endlocal

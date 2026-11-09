@echo off
setlocal
REM Rebuild and replace the published copy in dist\, preserving Armor
REM configuration and backups (%USERPROFILE%\.armor).
REM Usage: scripts\windows\reinstall.bat [net8.0 or net10.0]
call "%~dp0install.bat" %*
exit /b %errorlevel%

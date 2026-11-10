@echo off
REM Pull the pinned images and recreate the Armor observability stack. Non-destructive: named
REM volumes (Prometheus, Tempo, Loki, Grafana data) are preserved. Stops at the first failing step,
REM so a failed pull leaves the running stack alone, like docker/update.sh.
REM Usage: docker\update.bat
setlocal
if not "%~1"=="" (
    echo Usage: docker\update.bat
    endlocal & exit /b 1
)
where docker >nul 2>nul
if errorlevel 1 (
    echo Error: docker was not found on PATH. Install Docker Desktop: https://docs.docker.com/desktop/ 1>&2
    endlocal & exit /b 1
)
cd /d "%~dp0"
docker compose pull
if errorlevel 1 goto fail
docker compose down
if errorlevel 1 goto fail
docker compose up -d
if errorlevel 1 goto fail
docker ps -a
endlocal & exit /b 0

:fail
echo Error: docker compose failed; see the output above. 1>&2
endlocal & exit /b 1

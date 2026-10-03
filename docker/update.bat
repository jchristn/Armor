@echo off
REM Pull the pinned images and recreate the Armor observability stack. Non-destructive: named
REM volumes (Prometheus, Tempo, Loki, Grafana data) are preserved.
cd /d "%~dp0"
docker compose pull
docker compose down
docker compose up -d
docker ps -a

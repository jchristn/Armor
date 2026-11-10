#!/bin/sh
# Pull the pinned images and recreate the Armor observability stack. Non-destructive: named volumes
# (Prometheus, Tempo, Loki, Grafana data) are preserved. Stops at the first failing step, so a
# failed pull leaves the running stack alone, like docker/update.bat.
# Usage: docker/update.sh
set -eu
if [ "$#" -ne 0 ]; then
    echo "Usage: docker/update.sh" >&2
    exit 1
fi
if ! command -v docker >/dev/null 2>&1; then
    echo "Error: docker was not found on PATH. Install Docker: https://docs.docker.com/get-docker/" >&2
    exit 1
fi
cd "$(dirname "$0")"
docker compose pull
docker compose down
docker compose up -d
docker ps -a

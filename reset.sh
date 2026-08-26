#!/usr/bin/env bash
# ==========================================================================
# reset.sh - Reset the local Armor installation to factory defaults (POSIX).
# Mirrors reset.bat for macOS/Linux users.
#
# Armor keeps all of its local state under a single home directory
# (ARMOR_HOME if set, otherwise ~/.armor): the configuration file
# (armor.json), the SQLite database (armor.db), the logs directory, and the
# state directory (which holds run locks and the machine-local key/password
# files). Removing that directory returns Armor to a first-run state; the
# next launch recreates it empty.
# ==========================================================================
set -euo pipefail

ARMOR_DIR="${ARMOR_HOME:-$HOME/.armor}"

echo
echo "=========================================================="
echo "  Armor - Reset to Factory Defaults"
echo "=========================================================="
echo
echo "WARNING: This is DESTRUCTIVE. It permanently deletes the local"
echo "Armor home directory and everything in it:"
echo
echo "    $ARMOR_DIR"
echo
echo "That removes:"
echo "  - All backup policies, schedules, and storage targets"
echo "  - All encryption keys/passwords and cached (unattended) passwords"
echo "  - The Armor database, configuration, and logs"
echo
echo "It does NOT delete backups already written to a storage target"
echo "(USB drive, S3, Azure, etc.). Password-protected backups stay"
echo "recoverable if you still know the password. Backups made with the"
echo "older key-file protection become UNRECOVERABLE once the local key"
echo "files are deleted."
echo
read -r -p "Type 'RESET' to confirm: " CONFIRM
echo
if [ "$CONFIRM" != "RESET" ]; then
  echo "Aborted. No changes were made."
  exit 1
fi

# Refuse to operate on an obviously unsafe target, so a mis-set ARMOR_HOME
# can never turn this into "rm -rf /" or wipe an entire home directory.
case "$ARMOR_DIR" in
  ""|"/"|"$HOME"|"$HOME/")
    echo "Refusing to reset: '$ARMOR_DIR' is not a safe Armor home directory."
    echo "Set ARMOR_HOME to a dedicated directory (for example ~/.armor)."
    exit 1
    ;;
esac

echo "[1/2] Stopping any running Armor processes..."
pkill -f "Armor.Tui" 2>/dev/null || true
pkill -f "Armor.Agent" 2>/dev/null || true

echo "[2/2] Removing the Armor home directory..."
rm -rf "$ARMOR_DIR"

echo
echo "Factory reset complete."
echo
echo "To start Armor again (it will recreate a fresh, empty home directory):"
echo "  cd src && dotnet build && dotnet run --project Armor.Tui"
echo

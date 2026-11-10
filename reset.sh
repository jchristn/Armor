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
# Under 'set -e' a bare read exits silently at end of input; treat that as "not confirmed".
read -r -p "Type 'RESET' to confirm: " CONFIRM || CONFIRM=""
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
# Stop a supervised agent through its supervisor first, so launchd KeepAlive or systemd
# Restart=on-failure cannot bring it back while its home directory is being deleted.
if [ "$(uname -s)" = Darwin ]; then
  launchctl bootout "gui/$(id -u)/com.jchristn.armor.agent" 2>/dev/null || true
  # The .pkg installer's LaunchAgent (KeepAlive) loads again at the next login.
  if launchctl print "gui/$(id -u)/com.joelchristner.armor" >/dev/null 2>&1; then
    launchctl bootout "gui/$(id -u)/com.joelchristner.armor" 2>/dev/null || true
    echo "Unloaded the packaged Armor LaunchAgent com.joelchristner.armor (it loads again at the next login)."
  fi
elif [ -f "$HOME/.config/systemd/user/armor-agent.service" ] && command -v systemctl >/dev/null 2>&1; then
  systemctl --user stop armor-agent >/dev/null 2>&1 || true
fi
# Only this user's processes, and only the executables (and dotnet <dll> launches): 'pkill -f
# Armor.Tui' would also kill any process whose command line merely mentions that path.
armor_running() {
  pgrep -u "$(id -u)" -x Armor.Agent >/dev/null 2>&1 || pgrep -u "$(id -u)" -x Armor.Tui >/dev/null 2>&1 \
    || pgrep -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' >/dev/null 2>&1
}
signal_armor() {
  pkill "$1" -u "$(id -u)" -x Armor.Agent 2>/dev/null || true
  pkill "$1" -u "$(id -u)" -x Armor.Tui 2>/dev/null || true
  pkill "$1" -u "$(id -u)" -f 'Armor\.(Agent|Tui)\.dll' 2>/dev/null || true
}
# Wait for them to exit: an agent still shutting down can write its database and logs back into
# the directory while it is being removed.
signal_armor -TERM
for _ in 1 2 3 4 5; do
  armor_running || break
  sleep 1
done
if armor_running; then
  signal_armor -KILL
  sleep 1
fi
if armor_running; then
  echo "Some Armor processes could not be stopped; nothing was removed. Check: pgrep -u $(id -u) -fl Armor"
  exit 1
fi

echo "[2/2] Removing the Armor home directory..."
rm -rf "$ARMOR_DIR"

echo
echo "Factory reset complete."
echo
echo "To start Armor again (it will recreate a fresh, empty home directory):"
echo "  cd src && dotnet build && dotnet run --project Armor.Tui"
echo

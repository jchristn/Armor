#!/usr/bin/env bash
# Remove what install.sh and run-at-startup.sh put in place: the launchd login
# agent, any running Armor processes, and ~/Applications/Armor.app (plus the
# build staging under artifacts/macos-app). Configuration, the database and
# backups are kept; reset.sh at the repository root wipes ~/.armor.
set -euo pipefail
[ "$(uname -s)" = Darwin ] || { echo "This uninstaller requires macOS." >&2; exit 1; }
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
DESTINATION="$HOME/Applications/Armor.app"

sh "$SCRIPT_DIR/remove-from-startup.sh"

pkill -x Armor.Tui 2>/dev/null || true
pkill -x Armor.Agent 2>/dev/null || true

if [ -d "$DESTINATION" ]; then
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
        -u "$DESTINATION" >/dev/null 2>&1 || true
    rm -rf "$DESTINATION"
    echo "Removed $DESTINATION."
else
    echo "No app installed at $DESTINATION."
fi
rm -rf "$REPO_ROOT/artifacts/macos-app"
echo "Armor's configuration and database under ~/.armor were kept; run reset.sh to remove them."

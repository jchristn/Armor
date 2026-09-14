#!/bin/sh
# ============================================================================
# Build Armor installers for THIS machine's OS into installers/<version>/
#
#   macOS  -> .dmg + .pkg          (via the 'dmg' channel)
#   Linux  -> .deb + .rpm + AppImage (via 'debrpm' and 'appimage' channels)
#
# A single machine can only build its own OS's installers. Run this on macOS and
# on Linux for those two; run build-installers.bat on Windows for the .exe. To
# build all three at once, push a v* tag and let CI (.github/workflows/release.yml)
# fan out across windows-latest, macos-latest, and ubuntu-latest.
# ============================================================================
set -eu

cd "$(dirname "$0")"
PUB="src/Armor.Publisher/bin/Release/net10.0/armor-publish.dll"

echo "Building Armor.Publisher..."
dotnet build src/Armor.Publisher/Armor.Publisher.csproj -c Release -v m

VERSION="$(dotnet "$PUB" --print-version)"
OUT="installers/$VERSION"
mkdir -p "$OUT"

echo
echo "Version : $VERSION"
echo "Output  : $OUT"
echo

OS="$(uname -s)"
case "$OS" in
    Darwin)
        echo "macOS detected: building .dmg + .pkg..."
        dotnet "$PUB" --channel dmg --output "$OUT"
        ;;
    Linux)
        echo "Linux detected: building .deb / .rpm..."
        dotnet "$PUB" --channel debrpm --output "$OUT"
        echo "Building AppImage..."
        dotnet "$PUB" --channel appimage --output "$OUT"
        ;;
    *)
        echo "Unsupported OS '$OS' for this script." >&2
        echo "Use build-installers.bat on Windows." >&2
        exit 1
        ;;
esac

# Drop the intermediate publish staging so only installers remain.
rm -rf "$OUT/.staging"

echo
echo "Done. Installers are in $OUT"
case "$OS" in
    Darwin) echo "(Run this on Linux and build-installers.bat on Windows for the other two OSes.)" ;;
    Linux)  echo "(Run this on macOS and build-installers.bat on Windows for the other two OSes.)" ;;
esac

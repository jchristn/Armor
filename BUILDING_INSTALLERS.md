# Building Installers

Each machine builds only its own OS's installers. Run the script for the platform you're on; to build
all three at once, use the CI path at the bottom.

Finished installers land in **`installers/<version>/`** (version comes from `<Version>` in
`src/Directory.Build.props`). Every file gets a matching `.sha256`.

## Prerequisites

- **.NET SDK 10** (all platforms).
- **Windows:** Inno Setup 6 — `choco install innosetup`
- **macOS:** nothing extra (`hdiutil`/`pkgbuild` are built in)
- **Linux:** `fpm` (`sudo gem install fpm`) and `appimagetool` on `PATH`

## Windows

```bat
build-installers.bat
```

Produces `installers\<version>\Armor-<version>-win-x64-setup.exe` (and `win-arm64`).

## macOS

```sh
sh build-installers.sh
```

Produces `installers/<version>/Armor-<version>-osx-{x64,arm64}.dmg` and `.pkg`.

## Linux

```sh
sh build-installers.sh
```

Produces `installers/<version>/`:
- `armor_<version>_{amd64,arm64}.deb`
- `armor-<version>-1.{x86_64,aarch64}.rpm`
- `Armor-<version>-{x86_64,aarch64}.AppImage`

## Build a single architecture (faster)

Skip the script and call the Publisher directly with `--rid`:

```sh
dotnet run --project src/Armor.Publisher -- --channel inno --rid win-x64 --output installers/0.2.0
```

## All three at once (CI)

Push a version tag; `.github/workflows/release.yml` fans out to `windows-latest`, `macos-latest`, and
`ubuntu-latest`, builds every installer, and attaches them to a GitHub Release.

```sh
git tag v<version> && git push origin --tags
```

The tag must match `<Version>` in `src/Directory.Build.props`.

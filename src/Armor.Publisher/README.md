# Armor.Publisher

A config-driven tool that turns Armor's build output into signed, downloadable installers **and**
native package-manager entries across Windows, macOS, and Linux. It reads
[`publisher.json`](../../publisher.json) (the single source of truth) and produces the requested
channel's output, publishing the target artifact self-contained (bundled .NET runtime), single-file.

Built to the `INSTALLERS.md` standard: a Publisher app inside a CI matrix, self-contained builds,
both installers and package managers, checksums per artifact, and startup/service registration wired
into the installers.

## Usage

```
dotnet run --project src/Armor.Publisher -- --channel <id> [options]
```

| Option | Description |
| --- | --- |
| `--channel <id>` | Channel to build (see table) |
| `--rid <rid>` | Build a single runtime instead of the channel default |
| `--version <v>` | Override the version (default: `<Version>` in `src/Directory.Build.props`) |
| `--framework <tfm>` | Target framework (default: first in `build.frameworks`) |
| `--config <path>` | Path to `publisher.json` (default: `./publisher.json`) |
| `--output <dir>` | Output directory (default: `./artifacts`) |
| `--list` | List channels and their status |

## Channels

| Channel | Payload | Host OS | Tool | Produces |
| --- | --- | --- | --- | --- |
| `inno` | agent | Windows | Inno Setup 6 (`iscc`) | signed `.exe` + Scheduled-Task startup |
| `dmg` | agent | macOS | `hdiutil`, `pkgbuild`, `codesign`, `notarytool` | notarized `.dmg` + `.pkg` (installs LaunchAgent) |
| `debrpm` | agent | Linux | `fpm` | `.deb` + `.rpm` with systemd unit |
| `appimage` | agent | Linux | `appimagetool` | `.AppImage` |
| `winget` | agent | any | — | `microsoft/winget-pkgs` manifests |
| `choco` | agent | any (pack: Win) | `choco` | `.nuspec` + install script → `.nupkg` |
| `scoop` | agent | any | — | bucket manifest (`bucket/armor.json`) |
| `brewcask` | agent | any | — | Homebrew cask `.rb` (references the `.pkg`) |
| `aptyum` | agent | Linux | `dpkg-dev`, `apt-utils`, `createrepo_c`, `gpg` | signed apt + yum repo trees |
| `snap` | agent | any (build: Linux) | `snapcraft` | `snapcraft.yaml` |
| `flatpak` | agent | any (build: Linux) | `flatpak-builder` | Flatpak manifest |
| `dotnettool` | tui | any | `dotnet pack` | global-tool `.nupkg` |
| `docker`, `helm` | tui | any | — | disabled (optional, not in the spec) |

Every produced artifact gets a `.sha256` sidecar; the release job writes an aggregate `SHA256SUMS`.
Package-manager channels reference the installers' checksums rather than a hand-copied hash, so run
the installer channel (or download its artifact into `--output`) before the matching PM channel.

Channels are **host-locked** where the packaging tool is: the tool refuses to run a channel on the
wrong OS. Full coverage runs through [`.github/workflows/release.yml`](../../.github/workflows/release.yml),
which fans out one job per OS on a `v*` tag and produces every installer and package-manager entry.

## End-user install commands (the compliance target)

```
# Windows
winget install JoelChristner.Armor
choco install armor
scoop bucket add armor https://github.com/jchristn/armor && scoop install armor

# macOS
brew install --cask armor            # via the jchristn/homebrew-armor tap

# Linux
sudo apt-get install armor           # from https://jchristn.github.io/armor/deb
sudo dnf install armor               # from https://jchristn.github.io/armor/rpm
sudo snap install armor
flatpak install armor

# Cross-platform CLI
dotnet tool install -g JoelChristner.Armor.Tui
```

## Destinations

Set in `publisher.json` under `destinations`. Most hosting is the existing repo:

- **Downloads** (`.exe/.dmg/.pkg/.deb/.rpm/.AppImage/.nupkg` + `SHA256SUMS`) → GitHub **Releases**.
- **apt + yum repos** → GitHub **Pages** (`gh-pages`) at `https://jchristn.github.io/armor/{deb,rpm}`.
- **Scoop bucket** → `bucket/` in this repo.
- **Homebrew cask** → the `jchristn/homebrew-armor` tap repo (one extra repo, for the standard
  `brew tap` UX).
- **winget / Chocolatey / Snap / Flathub / NuGet** → their central registries (submit-and-review).

## Secrets the CI reads (provided by you, once)

Stored as GitHub Actions secrets under the exact names in `publisher.json`'s `signing` block plus the
channel tokens. Until they exist, builds run **unsigned** and submissions report a pending state.

> **Current stance:** no code-signing certificates are in use. Windows ships unsigned (SmartScreen
> warning), macOS ships unsigned (Gatekeeper needs a per-install workaround), and Linux apt/yum repos
> are unsigned for now. End-user install steps and workarounds are in [`INSTALL.md`](../../INSTALL.md).
> The signing invocations stay wired behind `isConfigured: false`, so enabling any of them later is
> just adding the secret and flipping the flag — no code changes.

| Secret | Used for |
| --- | --- |
| `WINDOWS_CERT_PFX` (base64) + `WINDOWS_CERT_PASSWORD` | Authenticode-signing the `.exe` |
| `APPLE_DEVELOPER_ID` + `APPLE_NOTARY_PROFILE` | codesign + notarize the `.dmg`/`.pkg` |
| `GPG_PRIVATE_KEY` (+ `GPG_PASSPHRASE`) | signing apt/yum repo metadata |
| `NUGET_API_KEY` | pushing the dotnet global tool |
| `CHOCO_API_KEY` | pushing the Chocolatey package |
| tap/bucket write token, winget/Flathub/Snap creds | pushes and submissions |

To enable signing, provide the secrets and flip `signing.<os>.isConfigured` to `true` in the manifest.

## Local prerequisites

- **.NET SDK 10** (already required by the solution).
- **Inno Setup 6** for `inno`: `choco install innosetup`.
- macOS/Linux channels are intended to run on their native CI runners; running them locally needs the
  corresponding host and the tools listed above.

# Releasing Installers

How to publish a version of Armor with installers for Windows, macOS, and Linux on
[GitHub Releases](https://github.com/jchristn/Armor/releases).

You don't build the installers by hand. Pushing a `v<version>` tag runs
[`.github/workflows/release.yml`](.github/workflows/release.yml), which builds every installer on a
runner for its own OS and publishes them as a GitHub Release. Your part is to prepare the version, check
the build, push the tag, and check the result.

To build installers on your own machine (for testing, or for one OS only), see
[`BUILDING_INSTALLERS.md`](BUILDING_INSTALLERS.md).

---

## What a release contains

| OS | Built on | Files |
| --- | --- | --- |
| Windows | `windows-latest` (Inno Setup) | `Armor-<v>-win-x64-setup.exe`, `Armor-<v>-win-arm64-setup.exe` |
| macOS | `macos-latest` (`pkgbuild`, `productbuild`, `hdiutil`) | `Armor-<v>-osx-arm64.pkg`, `Armor-<v>-osx-arm64.dmg`, `Armor-<v>-osx-x64.pkg`, `Armor-<v>-osx-x64.dmg` |
| Linux | `ubuntu-latest` (`fpm`, `appimagetool`) | `armor_<v>_amd64.deb`, `armor_<v>_arm64.deb`, `armor-<v>-1.x86_64.rpm`, `armor-<v>-1.aarch64.rpm`, `Armor-<v>-x86_64.AppImage`, `Armor-<v>-aarch64.AppImage` |
| — | release job | `SHA256SUMS` (one line per file above) |

Each installer contains the agent (`Armor.Agent`) and the console (`Armor.Tui`), published
self-contained and single-file for `net10.0`, so users don't need .NET. What each installer does on
the user's machine is described in [`INSTALL.md`](INSTALL.md).

The release notes start with [`.github/release-install-notes.md`](.github/release-install-notes.md)
(the steps past the unsigned-build prompts and the download table, with `{version}` filled in),
followed by the version's section of [`CHANGELOG.md`](CHANGELOG.md) under **What's changed**. The
install steps come first because the macOS *"“Armor” Not Opened"* prompt is the first thing a new user
hits.

## The builds are unsigned

No code-signing certificates are in use, and that is intentional for now. Users see a warning the
first time they run Armor and have to approve it:

- **Windows:** SmartScreen shows *"Windows protected your PC"* → **More info → Run anyway**.
- **macOS:** the app is ad-hoc signed (the Publisher does this when Apple signing isn't configured),
  so Gatekeeper treats it as coming from an unidentified developer: the user clicks **Open Anyway** in
  **System Settings → Privacy & Security** (after clicking **Done** on *"“Armor” Not Opened"*). An
  unsigned `.dmg` also carries an *If Armor won't open.txt* note with those steps. Without the ad-hoc
  signature, macOS would call the download "damaged" and refuse to open it, so keep the ad-hoc step in
  place.
- **Linux:** packages install without prompting.

These steps are in the release notes, `INSTALL.md`, and the README. To turn on signing later, see
[Turning on code signing](#turning-on-code-signing).

---

## One-time setup

Nothing is needed beyond the repository itself:

- GitHub Actions must be enabled. The workflow asks for `contents: write`, which lets it create the
  release with the built-in `GITHUB_TOKEN`; no secrets are required for unsigned builds.
- For the commands below: the [GitHub CLI](https://cli.github.com/) (`gh auth login`) and push access to
  `main`.

---

## Releasing a version

The example releases `0.6.0`; substitute your version.

### 1. Choose the version

Armor uses [Semantic Versioning](https://semver.org/). While the version is `0.x`, bump the minor
version (`0.5.0` → `0.6.0`) for new features or behavior changes, and the patch version (`0.5.0` →
`0.5.1`) for fixes only. A version is released once; never reuse a number.

### 2. Update the version and the changelog

1. Set `<Version>` in [`src/Directory.Build.props`](src/Directory.Build.props). This one value names
   every installer and is checked against the tag.

   ```xml
   <Version>0.6.0</Version>
   ```

2. In [`CHANGELOG.md`](CHANGELOG.md), rename the entries under `## [Unreleased]` to a dated section for
   the version, and leave an empty `## [Unreleased]` above it:

   ```markdown
   ## [Unreleased]

   ## [0.6.0] - 2026-12-01

   ### Added
   - ...
   ```

   The heading must start with exactly `## [0.6.0]`: the workflow reads the release notes from it and
   **fails the release if the section is missing or empty.**

3. Update the version in the **Status** line near the top of [`README.md`](README.md).

### 3. (Optional) Build your own OS's installer locally

This catches packaging problems before CI does:

```sh
sh build-installers.sh        # macOS or Linux
build-installers.bat          # Windows
```

Install the result and check that the agent's tray / menu-bar icon appears and that the console opens
from it.

### 4. Commit and push to `main`

```sh
git add src/Directory.Build.props CHANGELOG.md README.md
git commit -m "Release 0.6.0"
git push origin main
```

Wait for the **Tests** workflow on that commit to pass:

```sh
gh run list --workflow tests.yml --limit 1
```

### 5. Dry run: build every installer without releasing

Run the release workflow by hand. On a branch (not a tag), it builds all three OSes and uploads the
installers as workflow artifacts, and the publishing jobs are skipped.

```sh
gh workflow run release.yml --ref main
gh run watch "$(gh run list --workflow release.yml --limit 1 --json databaseId -q '.[0].databaseId')"
```

All three build jobs (`windows`, `macos`, `linux`) must pass. To try the installers before releasing:

```sh
gh run download <run-id> --dir /tmp/armor-dryrun
```

### 6. Tag and push

```sh
git tag -a v0.6.0 -m "Armor 0.6.0"
git push origin v0.6.0
```

The tag must be `v` + the exact `<Version>`. If they don't match, the release job stops before
publishing anything.

### 7. Watch the release

```sh
gh run watch "$(gh run list --workflow release.yml --limit 1 --json databaseId -q '.[0].databaseId')"
```

The three build jobs run in parallel (about 10–15 minutes), then the `release` job checks the version,
builds the notes, computes `SHA256SUMS`, and publishes the release.

### 8. Check the release

```sh
gh release view v0.6.0
```

- [ ] The release is named **Armor 0.6.0** and its notes show the changelog section and the
      **Installing** section.
- [ ] It has 13 files: 2 Windows `.exe`, 2 `.pkg`, 2 `.dmg`, 2 `.deb`, 2 `.rpm`, 2 `.AppImage`, and
      `SHA256SUMS`. GitHub also adds two source-code archives on its own.
- [ ] `SHA256SUMS` verifies against the downloads:
      `gh release download v0.6.0 --dir /tmp/armor-rel && cd /tmp/armor-rel && shasum -a 256 -c SHA256SUMS`
- [ ] On at least one machine per OS, install from the release (going through the unsigned-build
      prompt) and confirm the agent icon appears and the console opens.
- [ ] The README's **Download** link (`/releases/latest`) opens this release.

---

## When something goes wrong

**A build job fails.** Nothing is published, because the `release` job waits for all three. Fix the
problem on `main`, then move the tag to the fixed commit and push it again:

```sh
git tag -d v0.6.0 && git push origin :refs/tags/v0.6.0
git tag -a v0.6.0 -m "Armor 0.6.0" && git push origin v0.6.0
```

For a flaky failure (a network timeout downloading a tool), use **Re-run failed jobs** on the run
instead (`gh run rerun <run-id> --failed`).

**The release job fails.** The log names the cause:

- *"Tag vX does not match <Version>..."*: the tag and `src/Directory.Build.props` disagree. Fix
  whichever is wrong, then re-tag as above.
- *"CHANGELOG.md has no '## [X]' section"*: add the section, commit, then re-tag as above.

**A published release is broken.** If nobody has downloaded it yet (for example, you caught it
immediately), delete the release and the tag, fix the problem, and release the same version again:

```sh
gh release delete v0.6.0 --cleanup-tag --yes
```

Once people may have downloaded it, don't replace its files. Release a patch version (`0.6.1`) with
the fix instead, so a version number always means the same files.

---

## Turning on code signing

The signing steps are already in the Publisher and the workflow; they are skipped while
`signing.<os>.isConfigured` is `false` in [`publisher.json`](publisher.json). To turn one on, add its
secrets to the repository (**Settings → Secrets and variables → Actions**) and set that flag to `true`:

| OS | Secrets | Removes |
| --- | --- | --- |
| Windows | `WINDOWS_CERT_PFX` (base64 `.pfx`), `WINDOWS_CERT_PASSWORD` | the SmartScreen prompt (once the certificate builds reputation) |
| macOS | `APPLE_DEVELOPER_ID`, `APPLE_NOTARY_PROFILE` (needs an Apple Developer ID) | the Gatekeeper prompt (signed and notarized) |

The macOS job also needs steps that import the Developer ID certificate into a keychain and store the
`notarytool` profile before the build; the workflow has a placeholder comment where they belong. After
turning signing on, update the unsigned-build notes in `.github/release-install-notes.md`,
`INSTALL.md`, and `README.md`. [`src/Armor.Publisher/README.md`](src/Armor.Publisher/README.md) lists
every secret.

## Package managers (not active)

`release.yml` also has jobs that publish apt/yum repositories to GitHub Pages, generate Scoop, winget,
and Homebrew manifests, push a `dotnet tool` package to NuGet, and pack a Chocolatey package. They have
not been run or tested, and their registries and tokens don't exist yet, so they are turned off. They
run on a tag only when the repository variable `ENABLE_PACKAGE_MANAGERS` is `true`:

```sh
gh variable set ENABLE_PACKAGE_MANAGERS --body true
```

Turn it on only after setting up those destinations (see `src/Armor.Publisher/README.md`), and do a
release with it on while you watch, since a failure in those jobs happens after the GitHub Release is
already published.

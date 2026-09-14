# Installing Armor

Armor ships as downloadable installers and through the common package managers for Windows, macOS,
and Linux. Builds are currently **unsigned**, so each platform has a one-time trust step noted below.
Every download has a matching `.sha256`, and each release includes a `SHA256SUMS` file you can verify.

---

## Windows

**Package managers**

```powershell
winget install JoelChristner.Armor
choco install armor
scoop bucket add armor https://github.com/jchristn/armor
scoop install armor
```

**Direct download:** grab `Armor-<version>-win-x64-setup.exe` from the
[latest release](https://github.com/jchristn/armor/releases/latest) and run it.

> **Unsigned-build note.** Windows SmartScreen may show *"Windows protected your PC."* Click
> **More info → Run anyway**. This happens because the installer isn't signed with an Authenticode
> certificate; it's expected. The installer registers Armor to start at logon (a Scheduled Task).

---

## macOS

**Homebrew** (recommended — handles the trust step for you):

```bash
brew install --cask --no-quarantine armor
```

The `--no-quarantine` flag is required because the app isn't notarized; without it, Gatekeeper will
block the app.

**Direct download:** grab `Armor-<version>-osx-arm64.pkg` (Apple Silicon) or `-osx-x64.pkg` (Intel)
from the [latest release](https://github.com/jchristn/armor/releases/latest). Because it's unsigned,
Gatekeeper blocks a normal double-click. Install it one of these ways:

```bash
# Option 1: install from Terminal (bypasses the Gatekeeper UI prompt)
sudo installer -pkg ~/Downloads/Armor-<version>-osx-arm64.pkg -target /

# Option 2: strip the quarantine flag, then open normally
xattr -dr com.apple.quarantine ~/Downloads/Armor-<version>-osx-arm64.pkg
```

Or open it once, then approve it under **System Settings → Privacy & Security → Open Anyway**. The
package installs Armor to `/Applications` and registers a LaunchAgent so it starts at login.

---

## Linux

The apt/yum repositories are currently **unsigned**, so the setup below marks them trusted explicitly.
(This changes once repository GPG signing is enabled.)

**Debian / Ubuntu (apt)**

```bash
echo 'deb [trusted=yes] https://jchristn.github.io/armor/deb/ ./' | sudo tee /etc/apt/sources.list.d/armor.list
sudo apt-get update
sudo apt-get install armor
```

**Fedora / RHEL (dnf/yum)**

```bash
sudo tee /etc/yum.repos.d/armor.repo <<'EOF'
[armor]
name=Armor
baseurl=https://jchristn.github.io/armor/rpm
enabled=1
gpgcheck=0
EOF
sudo dnf install armor
```

`gpgcheck=0` / `[trusted=yes]` are needed only while the repos are unsigned.

**Direct download** (`.deb` / `.rpm` from the [latest release](https://github.com/jchristn/armor/releases/latest)):

```bash
sudo dpkg -i armor_<version>_amd64.deb        # Debian/Ubuntu
sudo rpm -i armor-<version>-1.x86_64.rpm       # Fedora/RHEL
```

Both install a systemd service and enable it automatically.

**AppImage** (any distro):

```bash
chmod +x Armor-<version>-x86_64.AppImage
./Armor-<version>-x86_64.AppImage
```

**Snap / Flatpak:**

```bash
sudo snap install armor
flatpak install armor
```

---

## Cross-platform CLI (.NET tool)

If you have the .NET SDK:

```bash
dotnet tool install -g JoelChristner.Armor.Tui
```

---

## Verifying a download

```bash
# Linux/macOS
sha256sum -c SHA256SUMS            # or: shasum -a 256 -c SHA256SUMS
```

```powershell
# Windows
Get-FileHash .\Armor-<version>-win-x64-setup.exe -Algorithm SHA256
```

> Package-manager entries go live as each release is published and each registry submission clears its
> review (winget, Chocolatey, Snap, Flathub). Direct downloads are available immediately on every release.

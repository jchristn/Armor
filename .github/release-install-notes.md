## Installing

Download the installer for your platform from the assets below. Every file is listed with its SHA-256 in `SHA256SUMS`.

| Platform | File |
| --- | --- |
| Windows (x64) | `Armor-{version}-win-x64-setup.exe` |
| Windows (ARM64) | `Armor-{version}-win-arm64-setup.exe` |
| macOS (Apple Silicon) | `Armor-{version}-osx-arm64.pkg` or `.dmg` |
| macOS (Intel) | `Armor-{version}-osx-x64.pkg` or `.dmg` |
| Debian / Ubuntu | `armor_{version}_amd64.deb` (or `_arm64.deb`) |
| Fedora / RHEL | `armor-{version}-1.x86_64.rpm` (or `.aarch64.rpm`) |
| Any Linux | `Armor-{version}-x86_64.AppImage` (or `-aarch64.AppImage`) |

**These builds are not code-signed.** Your OS will warn you the first time. That is expected for this release:

- **Windows:** SmartScreen shows *"Windows protected your PC"*. Click **More info → Run anyway**.
- **macOS:** Gatekeeper blocks the first open. Open **System Settings → Privacy & Security**, scroll down, and click **Open Anyway** next to the Armor message (or run `xattr -dr com.apple.quarantine <file>` on the download first).
- **Linux:** no prompt. Install with `sudo apt install ./armor_{version}_amd64.deb` or `sudo dnf install ./armor-{version}-1.x86_64.rpm`, or `chmod +x` the AppImage and run it.

Full instructions: [INSTALL.md](https://github.com/jchristn/Armor/blob/main/INSTALL.md).

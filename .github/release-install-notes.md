## Installing

> [!IMPORTANT]
> **These builds are not code-signed, so Windows and macOS block the first launch.** This is expected. The steps to allow Armor are below.

### macOS: "Armor" Not Opened

macOS shows *"“Armor” Not Opened. Apple could not verify “Armor” is free of malware that may harm your Mac or compromise your privacy."* with **Move to Trash** and **Done** buttons. To open it anyway:

1. Click **Done**. Do not click Move to Trash.
2. Open **System Settings → Privacy & Security** and scroll down to **Security**.
3. Next to *"“Armor” was blocked to protect your Mac"*, click **Open Anyway**. The button only appears for about an hour after the blocked attempt; if it is missing, try to open Armor again first.
4. Click **Open Anyway** again in the dialog that follows and enter your password. macOS remembers the choice, so you only do this once.

Or skip the prompt in Terminal. Run the first command for the `.pkg` before you open it, or the second after you drag `Armor.app` from the `.dmg` to Applications:

```bash
xattr -dr com.apple.quarantine ~/Downloads/Armor-{version}-osx-arm64.pkg
xattr -dr com.apple.quarantine /Applications/Armor.app
```

On macOS 15 and later, Control-click → **Open** no longer bypasses this prompt; use the steps above.

### Windows: "Windows protected your PC"

SmartScreen shows *"Windows protected your PC"*. Click **More info**, then **Run anyway**. If your browser warns that the file *"isn't commonly downloaded"*, choose **Keep**.

### Linux

No prompt. Install with `sudo apt install ./armor_{version}_amd64.deb` or `sudo dnf install ./armor-{version}-1.x86_64.rpm`, or `chmod +x` the AppImage and run it.

### Which file to download

Every file is listed with its SHA-256 in `SHA256SUMS`.

| Platform | File |
| --- | --- |
| Windows (x64) | `Armor-{version}-win-x64-setup.exe` |
| Windows (ARM64) | `Armor-{version}-win-arm64-setup.exe` |
| macOS (Apple Silicon) | `Armor-{version}-osx-arm64.pkg` or `.dmg` |
| macOS (Intel) | `Armor-{version}-osx-x64.pkg` or `.dmg` |
| Debian / Ubuntu | `armor_{version}_amd64.deb` (or `_arm64.deb`) |
| Fedora / RHEL | `armor-{version}-1.x86_64.rpm` (or `.aarch64.rpm`) |
| Any Linux | `Armor-{version}-x86_64.AppImage` (or `-aarch64.AppImage`) |

Full instructions: [INSTALL.md](https://github.com/jchristn/Armor/blob/main/INSTALL.md).

# Installing Armor

Installers for Windows, macOS, and Linux are attached to every
[GitHub Release](https://github.com/jchristn/Armor/releases). The
[latest release](https://github.com/jchristn/Armor/releases/latest) is the one you want unless you need
an older version. Each installer bundles the .NET runtime, so you do not need .NET installed.

Every installer includes the two Armor programs:

- **The agent** (`Armor.Agent`): the tray / menu-bar icon that runs your scheduled backups.
- **The console** (`Armor.Tui`): the terminal dashboard you use to set up targets, passwords,
  policies, and schedules, and to restore. Opening the console starts the agent if it is not running.

> **The builds are not code-signed.** Windows and macOS warn you the first time you run them. The steps
> for getting past each warning are below. This is expected; it is not a sign that the download is
> damaged. To make sure it is the file that was published, check it against `SHA256SUMS`
> ([Verifying a download](#verifying-a-download)).

---

## Windows

Download `Armor-<version>-win-x64-setup.exe` (or `-win-arm64-setup.exe` on ARM PCs) and run it.

**Unsigned-build warnings:**

1. Your browser may say the file *"isn't commonly downloaded"*. Choose **Keep** (in Edge: **…** →
   **Keep** → **Show more** → **Keep anyway**).
2. SmartScreen shows *"Windows protected your PC"*. Click **More info**, then **Run anyway**.

The installer asks you to accept the license, installs to `C:\Program Files\Armor`, adds **Armor**
(the agent) and **Armor TUI** (the console) to the Start menu, and, if you leave the startup option
checked, starts the agent at logon through a Scheduled Task.

**Uninstall:** **Settings → Apps → Installed apps → Armor → Uninstall**. Your configuration and backup
records in `%USERPROFILE%\.armor` are kept.

---

## macOS

Use `Armor-<version>-osx-arm64.*` on Apple Silicon (M-series) Macs and `Armor-<version>-osx-x64.*` on
Intel Macs. Two formats are offered:

- **`.pkg` (recommended):** installs `/Applications/Armor.app`, starts the agent at login, and adds an
  `armor` command for the console in Terminal.
- **`.dmg`:** drag `Armor.app` to Applications. Nothing starts at login and there is no `armor` command;
  open the app to start the menu-bar agent and open the console from its menu.

### "Armor" Not Opened

Because the build is not signed by an Apple Developer ID, the first open is blocked with:

> **“Armor” Not Opened**
> Apple could not verify “Armor” is free of malware that may harm your Mac or compromise your privacy.
> [Move to Trash] [Done]

(Older macOS versions say *"“Armor” cannot be opened because it is from an unidentified developer"*.)
The same prompt appears for the `.pkg` itself, named after the file. To open it anyway:

1. Click **Done**. Do not click **Move to Trash**.
2. Open **System Settings → Privacy & Security** and scroll down to **Security**.
3. Next to *"“Armor” was blocked to protect your Mac"*, click **Open Anyway**. The button only appears
   for about an hour after the blocked attempt; if it isn't there, try to open Armor again, then come
   back.
4. Click **Open Anyway** in the dialog that follows and enter your password. macOS remembers this, so
   you only do it once.

On macOS 15 (Sequoia) and later, Control-click → **Open** no longer gets past this prompt; use the
steps above.

**Or skip the prompt from Terminal** by removing the quarantine flag macOS puts on downloads. For the
`.pkg`, run this before opening it:

```bash
xattr -dr com.apple.quarantine ~/Downloads/Armor-<version>-osx-arm64.pkg
```

For the `.dmg`, drag `Armor.app` to Applications first, then run:

```bash
xattr -dr com.apple.quarantine /Applications/Armor.app
```

You can also install the `.pkg` from Terminal, which skips the prompt:

```bash
sudo installer -pkg ~/Downloads/Armor-<version>-osx-arm64.pkg -target /
```

**Uninstall:**

```bash
launchctl bootout gui/$(id -u)/com.joelchristner.armor 2>/dev/null
sudo rm -rf /Applications/Armor.app /Library/LaunchAgents/com.joelchristner.armor.plist /usr/local/bin/armor
```

Your configuration and backup records in `~/.armor` are kept.

---

## Linux

Packages are built for x64 (`amd64` / `x86_64`) and ARM64 (`arm64` / `aarch64`).

**Debian / Ubuntu:**

```bash
sudo apt install ./armor_<version>_amd64.deb
```

**Fedora / RHEL:**

```bash
sudo dnf install ./armor-<version>-1.x86_64.rpm
```

Both install Armor under `/opt/armor`, add the `armor` (console) and `armor-agent` commands, add
**Armor** to your application menu, and start the agent at each desktop login (an `/etc/xdg/autostart`
entry, which you can turn off in your desktop's startup-applications settings). Installing through
`apt`/`dnf` as shown also installs the libraries the agent needs. To start the agent now without logging
out, pick **Armor** from the application menu or run `armor-agent &`.

The tray icon needs StatusNotifierItem support: KDE Plasma has it built in, and GNOME needs the
*AppIndicator and KStatusNotifierItem Support* extension. Without a tray the agent still runs your
schedules.

**Uninstall:** `sudo apt remove armor` or `sudo dnf remove armor`. Your configuration and backup records
in `~/.armor` are kept.

**AppImage (any distribution, no install):**

```bash
chmod +x Armor-<version>-x86_64.AppImage
./Armor-<version>-x86_64.AppImage          # the agent
./Armor-<version>-x86_64.AppImage tui      # the console
```

The AppImage does not set up login startup. It needs the same system libraries as the packages (ICU,
the X11 client libraries, and fontconfig), which desktop distributions already have.

---

## Verifying a download

Each release has a `SHA256SUMS` file listing every installer. Download it into the same folder as your
installer, then:

```bash
# Linux
sha256sum --ignore-missing -c SHA256SUMS
# macOS
shasum -a 256 --ignore-missing -c SHA256SUMS
```

```powershell
# Windows: compare the output with the line for your file in SHA256SUMS
Get-FileHash .\Armor-<version>-win-x64-setup.exe -Algorithm SHA256
```

---

## Package managers

winget, Chocolatey, Scoop, Homebrew, apt/yum repositories, Snap, Flatpak, and a `dotnet tool` package are
planned but **not published yet**. Use the installers above for now.

## Building from source

See [`GETTING_STARTED.md`](GETTING_STARTED.md) to build and run Armor from a checkout, and
[`BUILDING_INSTALLERS.md`](BUILDING_INSTALLERS.md) to build the installers yourself.

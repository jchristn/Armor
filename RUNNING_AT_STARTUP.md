# Running Armor at startup

The agent (`Armor.Agent`) is the always-on process: it owns the system-tray icon and runs
your schedules in the background even when the console is closed. For unattended backups
to actually happen, the agent has to be running — so you want it to start automatically
when you log in. This document covers how to do that on Windows, macOS, and Linux, both
with the ready-made scripts under [`scripts/`](scripts/) and by hand.

Everything below assumes the published layout from
[`GETTING_STARTED.md`](GETTING_STARTED.md): the agent and the TUI published **side by
side** into `dist/` at the repository root (macOS uses
`~/Applications/Armor.app/Contents/MacOS/`), so the tray's **Open** action can find the
console. If you haven't published yet, run the `install` script for your platform first —
it builds from source and publishes both executables into `dist/` on Windows, Linux and WSL,
or installs `~/Applications/Armor.app` on macOS.

## The scripts

Each platform directory (`windows`, `macos`, `linux`, `wsl`, `wsl2`) contains the same
eight scripts, with the same steps, messages and rules on every platform:

| Script | What it does |
|---|---|
| `install` | Builds the current checkout and publishes the agent and the console into `dist/` (`~/Applications/Armor.app` on macOS). Fails if Armor is already installed (use `reinstall`) |
| `reinstall` | Rebuilds and replaces the install, whether or not one exists. The build is staged first, so a failed build leaves the old install in place |
| `uninstall` | Removes the login registration and the install (and build staging); succeeds when nothing is installed. `~/.armor` is kept |
| `run` | Stops Armor, rebuilds and replaces the install, restarts the agent if it was running, then runs the console in this terminal |
| `update` | Stops Armor, runs `git pull --ff-only`, rebuilds and replaces the install, and restarts the agent the way it ran before |
| `run-at-startup` | Registers the agent to start at login and starts it now (unless it is already running, or there is no display on Linux) |
| `remove-from-startup` | Deregisters it from starting at login and, when something was registered, stops the running agent (the console is left alone); succeeds when nothing is registered |
| `kill-armor` | Stops your running agent and console (through launchd/systemd first, so they are not respawned); the login registration is kept |

`install`, `reinstall`, `run` and `update` take an optional `net8.0` or `net10.0` (the default);
every script accepts `-h`/`--help` and rejects anything else. `install`, `reinstall` and
`uninstall` refuse while Armor is running and point you at `kill-armor`, so they never replace or
delete a running app; `run` and `update` stop Armor themselves and start the agent again. If a
`run` or `update` step fails after Armor was stopped, the previous install is put back (the old
copy is kept aside until the new one is verified) and the agent is restarted the way it ran.
Process checks and kills only touch your own processes.
None of them need administrator/root rights — everything is registered per user.

```text
scripts/
├── windows/   *.bat
├── macos/     *.sh
├── linux/     *.sh
├── wsl/       *.sh  (WSL 1)
└── wsl2/      *.sh  (WSL 2)
```

On macOS, Linux and WSL they are bash scripts; run them directly (or with `bash`, not `sh`):

```bash
./scripts/linux/install.sh
./scripts/linux/run-at-startup.sh
```

## Windows

**Scripted:**

```bat
scripts\windows\install.bat           :: build from source and publish into dist\
scripts\windows\run-at-startup.bat    :: register at login and start the agent now
scripts\windows\remove-from-startup.bat
```

`run-at-startup.bat` writes an `ArmorAgent` value pointing at `dist\Armor.Agent.exe` into
the current user's Run key
(`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) — the standard per-user "run at
login" mechanism, no administrator rights required. `remove-from-startup.bat` deletes
that value and stops the running agent. `kill-armor.bat` stops the agent and console without
touching the registration.

**By hand**, any of these work:

- Press <kbd>Win+R</kbd>, type `shell:startup`, and drop a shortcut to
  `dist\Armor.Agent.exe` into the folder that opens.
- Add the registry value yourself:
  `reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ArmorAgent /t REG_SZ /d "\"C:\path\to\Armor\dist\Armor.Agent.exe\"" /f`
- Create a Task Scheduler task that runs `Armor.Agent.exe` "At log on".

## macOS

**Scripted:**

```bash
./scripts/macos/install.sh             # build and install ~/Applications/Armor.app
./scripts/macos/run-at-startup.sh      # install the launchd agent and start it now
./scripts/macos/remove-from-startup.sh
```

`run-at-startup.sh` installs a `launchd` user agent at
`~/Library/LaunchAgents/com.jchristn.armor.agent.plist` that launches `~/Applications/Armor.app/Contents/MacOS/Armor.Agent`
at login and restarts it if it crashes (quitting cleanly from the tray menu stays
quit). `remove-from-startup.sh` unloads and deletes the plist, which also stops the running
agent.

The `.pkg` installer registers its own LaunchAgent (`com.joelchristner.armor`, for
`/Applications/Armor.app`, with `KeepAlive`). The source scripts don't manage that install:
`install`, `reinstall`, `uninstall`, `run` and `update` refuse while it is loaded, and
`kill-armor.sh` (like `reset.sh`) unloads it until the next login.

**By hand**, either:

- Add `~/Applications/Armor.app` under *System Settings → General → Login Items*, or
- Write the plist yourself under `~/Library/LaunchAgents/` and load it with
  `launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.jchristn.armor.agent.plist`.

The icon appears as a status item in the menu bar at the top of the screen.

## Linux

**Scripted:**

```bash
./scripts/linux/install.sh             # build from source and publish into dist/
./scripts/linux/run-at-startup.sh      # register at login and start the agent now
./scripts/linux/remove-from-startup.sh
```

`run-at-startup.sh` prefers a **`systemd --user` service** tied to the desktop session
(`graphical-session.target`, which GNOME, KDE Plasma and other systemd-integrated sessions
activate): it starts with your desktop, restarts the agent on failure, and gives you
`systemctl --user status armor-agent` for free. When the session doesn't activate
`graphical-session.target` (or `systemd --user` isn't available), the script uses a
**desktop autostart entry** at `~/.config/autostart/armor-agent.desktop` instead.
`remove-from-startup.sh` removes whichever of the two is installed (including the unit's
enablement links, even when `systemctl` can't reach a user bus) and stops the running agent.
Without a display (`DISPLAY` unset, for example over SSH) the autostart entry is written but the
agent is not started now. When .NET lives outside the default locations (for example
`~/.dotnet`), the scripts derive `DOTNET_ROOT` and write it into the unit or autostart entry.

**By hand**, the systemd route looks like this:

```ini
# ~/.config/systemd/user/armor-agent.service
[Unit]
Description=Armor backup agent (tray scheduler)
After=graphical-session.target
PartOf=graphical-session.target

[Service]
ExecStart=/path/to/Armor/dist/Armor.Agent
Restart=on-failure
RestartSec=5

[Install]
WantedBy=graphical-session.target
```

Then `systemctl --user daemon-reload && systemctl --user enable --now armor-agent`.

Two Linux-specific notes:

- **Tray support:** the tray icon needs a desktop that speaks the StatusNotifierItem
  protocol. KDE Plasma has it built in; GNOME needs the "AppIndicator and
  KStatusNotifierItem" extension. Without a tray the agent still runs your schedules.
- **A display is required:** the agent is an Avalonia GUI app, so it runs inside a desktop
  session and stops when you log out. That is why the unit is bound to
  `graphical-session.target` rather than `default.target`, and why lingering
  (`loginctl enable-linger`) does not help.

## WSL

Use `scripts/wsl2` on WSL 2 and `scripts/wsl` on WSL 1, not `scripts/linux` (the Linux
scripts refuse to run under WSL, and each WSL directory refuses the other WSL version). They
use the Linux .NET SDK inside the distro (never the Windows one on the appended Windows
`PATH`) and stop early on CRLF checkouts. Nothing starts systemd or desktop autostart entries
when Windows logs in, so `run-at-startup.sh` puts a hidden launcher,
`armor-agent-wsl-<distro>.vbs`, in the Windows Startup folder that runs the agent in this
distro through `wsl.exe`, starts it now through the same launcher (so it outlives the
terminal), and removes any systemd unit or autostart entry the Linux scripts left behind. The
agent is a GUI (X11) app: on WSL 2 it needs WSLg (which sets `DISPLAY`) and shows no tray icon, so stop it with
`scripts/wsl2/kill-armor.sh`. WSL 1 has no WSLg, so `scripts/wsl/run-at-startup.sh` needs an X
server on Windows with `DISPLAY` set (and passes `DISPLAY` to the launcher); without one it
refuses and points you at `scripts\windows\run-at-startup.bat`. Clone into the Linux file
system (for example `~/src`), not `/mnt/c`. When a checkout on a Windows drive is shared, the
WSL scripts refuse to delete a Windows build (`dist\Armor.Agent.exe`) and the Windows scripts
refuse to delete a WSL build (`dist/Armor.Agent`).

## Linux and WSL: system packages

The agent is an Avalonia app, so it loads the X11 client libraries at startup; the console copies
with `wl-copy`, `xclip` or `xsel`; and every .NET program needs ICU. WSLg supplies only the display,
and the stock WSL images (like minimal and server installs) lack these packages. Building never
loads them, so without them Armor builds fine and then fails when it starts, with:

```text
System.DllNotFoundException: Unable to load shared library 'libICE.so.6'
Couldn't find a valid ICU package installed on the system
```

`install`, `reinstall`, `update` and `run-at-startup` on Linux and WSL install the missing packages
themselves (with `apt-get` or `dnf`, through `sudo`). Set `INSTALL_SYSTEM_PACKAGES=0` to have them
list the packages and stop instead. `run` only checks and warns. To install them by hand on
Debian/Ubuntu:

```bash
sudo apt install libx11-6 libice6 libsm6 libxext6 libxrandr2 libxi6 libxcursor1 libxfixes3 libfontconfig1
sudo apt install libicu74        # or the libicuNN your release has (apt-cache pkgnames libicu)
sudo apt install xclip           # copy to clipboard from the console
sudo apt install wslu xdg-utils  # WSL only: open links in the Windows browser
```

On Fedora: `sudo dnf install libX11 libICE libSM libXext libXrandr libXi libXcursor libXfixes fontconfig libicu xclip`.
If the agent still hits a missing library, it prints the same install commands and exits with code 1.

## Updating

`update` is the same on every platform, differing only in syntax:

1. Stops any running `Armor.Tui` and `Armor.Agent` — through launchd or systemd first when
   they supervise the agent, so it is not respawned — because a running agent keeps
   `Armor.Core.dll` open, and publishing over it would silently ship stale binaries.
2. Runs `git pull --ff-only` (if the pull fails — offline, local changes — it warns and
   rebuilds the checkout as-is).
3. Builds `Armor.Agent` and `Armor.Tui` (Release, `net10.0` unless you pass `net8.0`) into a
   staging folder, then replaces `dist/` (Windows, Linux, WSL) or
   `~/Applications/Armor.app` (macOS) with it.
4. Restarts the agent if it was running before the update, the same way: through launchd,
   the systemd user service or the WSL Startup launcher when one ran it, otherwise directly.

Your data is untouched throughout: configuration, database, and state all live under
`~/.armor`, and your backups live on your targets — neither is part of the build output.

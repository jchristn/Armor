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
console. If you haven't published yet, run the `update` script for your platform first —
it builds from source and publishes both executables into `dist/` on Windows/Linux
or installs `~/Applications/Armor.app` on macOS.

## The scripts

Each platform directory (`windows`, `macos`, `linux`, `wsl`, `wsl2`) contains the same
startup and update scripts:

| Script | What it does |
|---|---|
| `run-at-startup` | Registers `dist/Armor.Agent` to start at login (and starts it now) |
| `remove-from-startup` | Deregisters it from starting at login |
| `update` | Pulls the latest source, rebuilds, republishes into `dist/`, and restarts the agent if it was running |
| `install` / `reinstall` | Builds the current checkout and publishes into `dist/` (`~/Applications/Armor.app` on macOS); takes `net8.0` or `net10.0` |
| `run` | Runs `install`, then the console |
| `uninstall` | Removes the login registration, stops Armor, and deletes `dist/` (or the macOS app); `~/.armor` is kept |

None of them need administrator/root rights — everything is registered per user.

```text
scripts/
├── windows/   *.bat
├── macos/     *.sh
├── linux/     *.sh
├── wsl/       *.sh  (WSL 1)
└── wsl2/      *.sh  (WSL 2)
```

On macOS and Linux, run them with `sh` (or `chmod +x` them once and run them directly):

```bash
sh scripts/linux/update.sh
sh scripts/linux/run-at-startup.sh
```

## Windows

**Scripted:**

```bat
scripts\windows\update.bat            :: build from source and publish into dist\
scripts\windows\run-at-startup.bat    :: register at login and start the agent now
scripts\windows\remove-from-startup.bat
```

`run-at-startup.bat` writes an `ArmorAgent` value pointing at `dist\Armor.Agent.exe` into
the current user's Run key
(`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) — the standard per-user "run at
login" mechanism, no administrator rights required. `remove-from-startup.bat` deletes
that value; it leaves an already-running agent alone (use the tray menu's **Exit** to
stop it).

**By hand**, any of these work:

- Press <kbd>Win+R</kbd>, type `shell:startup`, and drop a shortcut to
  `dist\Armor.Agent.exe` into the folder that opens.
- Add the registry value yourself:
  `reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v ArmorAgent /t REG_SZ /d "\"C:\path\to\Armor\dist\Armor.Agent.exe\"" /f`
- Create a Task Scheduler task that runs `Armor.Agent.exe` "At log on".

## macOS

**Scripted:**

```bash
sh scripts/macos/update.sh             # build and install ~/Applications/Armor.app
sh scripts/macos/run-at-startup.sh     # install the launchd agent and start it now
sh scripts/macos/remove-from-startup.sh
```

`run-at-startup.sh` installs a `launchd` user agent at
`~/Library/LaunchAgents/com.jchristn.armor.agent.plist` that launches `~/Applications/Armor.app/Contents/MacOS/Armor.Agent`
at login and restarts it if it crashes (quitting cleanly from the tray menu stays
quit). `remove-from-startup.sh` unloads and deletes the plist — note that unloading also
stops a currently running agent.

**By hand**, either:

- Add `~/Applications/Armor.app` under *System Settings → General → Login Items*, or
- Write the plist yourself under `~/Library/LaunchAgents/` and load it with
  `launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/com.jchristn.armor.agent.plist`.

The icon appears as a status item in the menu bar at the top of the screen.

## Linux

**Scripted:**

```bash
sh scripts/linux/update.sh             # build from source and publish into dist/
sh scripts/linux/run-at-startup.sh     # register at login and start the agent now
sh scripts/linux/remove-from-startup.sh
```

`run-at-startup.sh` prefers a **`systemd --user` service** — it works for both desktop
sessions and headless servers, restarts the agent on failure, and gives you
`systemctl --user status armor-agent` for free. If `systemd --user` isn't available, the
script falls back to a **desktop autostart entry** at
`~/.config/autostart/armor-agent.desktop`. `remove-from-startup.sh` removes whichever of
the two is installed.

**By hand**, the systemd route looks like this:

```ini
# ~/.config/systemd/user/armor-agent.service
[Unit]
Description=Armor backup agent

[Service]
ExecStart=/path/to/Armor/dist/Armor.Agent
Restart=on-failure

[Install]
WantedBy=default.target
```

Then `systemctl --user daemon-reload && systemctl --user enable --now armor-agent`.

Two Linux-specific notes:

- **Tray support:** the tray icon needs a desktop that speaks the StatusNotifierItem
  protocol. KDE Plasma has it built in; GNOME needs the "AppIndicator and
  KStatusNotifierItem" extension. On a headless server there is no tray — the agent still
  runs your schedules; drive everything from the TUI.
- **Lingering (headless servers):** a `systemd --user` service normally starts at login
  and stops at logout. To have the agent run whenever the machine is up, without anyone
  logged in, enable lingering once: `loginctl enable-linger $USER`.

## WSL

Use `scripts/wsl2` on WSL 2 and `scripts/wsl` on WSL 1, not `scripts/linux`. They use the
Linux .NET SDK inside the distro (never the Windows one on the appended Windows `PATH`) and
stop early on CRLF checkouts. Nothing starts systemd or desktop autostart entries when
Windows logs in, so `run-at-startup.sh` puts a hidden launcher in the Windows Startup folder
that runs the agent through `wsl.exe`. The agent is a GUI app: on WSL 2 it needs WSLg (which
shows no tray icon; stop it with `pkill -x Armor.Agent`), and on WSL 1, which has no GUI,
`run-at-startup.sh` refuses and points you at `scripts\windows\run-at-startup.bat`. Clone
into the Linux file system (for example `~/src`), not `/mnt/c`.

## Updating

`update` is the same on every platform, differing only in syntax:

1. Stops any running `Armor.Tui` and `Armor.Agent` — a running agent keeps
   `Armor.Core.dll` open, and publishing over it would silently ship stale binaries.
2. Runs `git pull --ff-only` (if the pull fails — offline, local changes — it warns and
   rebuilds the checkout as-is).
3. Publishes `Armor.Agent` and `Armor.Tui` (Release, `net10.0`) side by side into `dist/`
   on Windows/Linux, or into `~/Applications/Armor.app` on macOS.
4. Restarts the agent if it was running before the update.

Your data is untouched throughout: configuration, database, and state all live under
`~/.armor`, and your backups live on your targets — neither is part of the build output.

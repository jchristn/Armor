# Getting started with Armor

This guide takes you from an empty machine to a working backup and a verified restore.
It covers building Armor from source, running the terminal console (TUI) and the
background agent that owns the system-tray icon, and a full round trip: back up a
folder, then restore it somewhere else and confirm the files came back intact.

Armor keeps everything it needs under a single directory, `~/.armor`, created on first
run: `armor.json` (configuration), `armor.db` (the SQLite state database), `logs/`, and
`state/`. Nothing is written anywhere else unless you point a backup target at it.

## What you need

- **.NET SDK 10** (it also builds the .NET 8 target Armor ships). Check with
  `dotnet --version`; you want `10.x`. Get it from <https://dotnet.microsoft.com/download>.
- **Git**, to clone the repository.
- A folder or drive to back up to. A second local folder is perfect for trying this out;
  in real use this is an external USB drive, a file share, or a cloud bucket.

The system-tray icon has one extra requirement per platform, covered in
[Installing the system tray](#installing-the-system-tray).

## Build from source

Clone the repository and build the solution:

```bash
git clone https://github.com/jchristn/Armor.git
cd Armor
dotnet build src/Armor.sln -c Release
```

That compiles the engine (`Armor.Core`), the terminal console (`Armor.Tui`), the
background agent (`Armor.Agent`), and the test projects. If you want to confirm your
build is healthy before trusting it with data, run the suite:

```bash
dotnet run --project src/Test.Automated -f net10.0 -c Release
```

You should see every case pass and an `OVERALL PASS` line at the end.

### Publish a runnable copy

For day-to-day use — and so the tray's **Open** action can find the console — publish
the agent and the TUI **into the same folder** so the two executables sit side by side:

```bash
dotnet publish src/Armor.Agent -c Release -f net10.0 -o dist
dotnet publish src/Armor.Tui   -c Release -f net10.0 -o dist
```

Now `dist/` contains `Armor.Agent` and `Armor.Tui` (with `.exe` extensions on Windows).
You can run either directly from there. During development you can also skip publishing
and use `dotnet run` as shown below, but then the agent and TUI live in separate build
folders and the tray's **Open** action won't be able to launch the console.

## Run the terminal console

The TUI is where you do everything by hand — set up backup targets and encryption
passwords, define policies, run backups, and restore. Start it with:

```bash
dotnet run --project src/Armor.Tui -f net10.0
```

or, from a published copy, run `dist/Armor.Tui` (double-click `Armor.Tui.exe` on Windows,
or run `./Armor.Tui` in a terminal).

You'll land on a full-screen **dashboard**: the Armor wordmark across the top, a nav
sidebar on the left, the selected section as a table in the middle, and an activity log
along the bottom (with a progress bar above it during a backup). The nav is ordered as a
setup checklist:

```
1 Backup targets      – where backups are stored
2 Passwords           – the encryption password(s) that protect your data
3 Policies            – what to back up, where, and how
4 Schedules           – when to run automatically
Runs                  – upcoming scheduled runs and anything in progress
Backup jobs           – every point-in-time you can restore
Recover               – restore from a target using only its location + password
```

Move the highlight with **↑/↓**. **Tab** (or **Enter** on a nav item) jumps into the
table; **Esc** returns to the nav. In a table, **Enter** runs that section's main action
(back up, validate, restore, enable/disable), **c** creates, **d** deletes, **r** shows a
policy's restore points, **F5** refreshes, and **F1** shows all shortcuts. When Armor asks
a question it opens a small dialog — type and press **Enter**, or **Escape** to cancel.
Press **Ctrl+Q** to quit.

The first launch creates `~/.armor` and logs where your configuration lives.

## A basic backup

We'll back up a folder to another local folder. Create a couple of test files first so
there's something real to protect:

```bash
mkdir -p ~/armor-demo/source ~/armor-demo/target
echo "the only backup that matters is the one you can restore" > ~/armor-demo/source/notes.txt
cp ~/armor-demo/source/notes.txt ~/armor-demo/source/notes-copy.txt
```

Now, in the TUI, follow the numbered nav sections top to bottom:

1. **Backup targets → `c`.** Name it (for example, `demo-target`) and enter the folder
   where backups are stored: `~/armor-demo/target`. Press **Enter** on it to validate —
   Armor writes a probe object, reads it back, and deletes it, so you learn the target is
   reachable before you rely on it. (Secret fields for network and cloud targets are
   encrypted at rest; a local folder needs no secret.)

2. **Passwords → `c`.** Give the encryption password a name (for example, `demo`) and
   choose a password (entered twice). Armor generates a random data key, wraps it with your
   password, and **caches the password locally** so backups can run unattended. The password
   is the only secret you need to restore on another machine — remember it. There is no key
   file to lose.

3. **Policies → `c`.** Name the policy (for example, `demo-policy`), enter the folder to
   back up (`~/armor-demo/source`), pick `demo-target` and the `demo` password when
   prompted, and choose **Full** as the backup type.

4. **Policies → Enter.** With `demo-policy` selected, press **Enter** to **run a backup
   now**. Armor chunks the files, deduplicates and compresses them, encrypts every chunk,
   and writes a manifest for this point in time. A progress bar tracks it in the bottom
   bar, and the activity log reports how many files it captured and how many chunks it
   wrote versus reused — because `notes-copy.txt` is identical to `notes.txt`, you'll see
   reuse on the second file.

You can confirm the run under **Backup jobs**: the job shows as `Completed`. It is now a
restore point.

## A basic restore

Restoring reads one manifest and rebuilds the files, checking every chunk against its
content hash on the way out. We'll restore to a fresh folder so you can compare against
the original.

1. **Backup jobs → Enter.** Pick the point-in-time you just created (rows show when, which
   policy, type, status, and file count) and press **Enter**. To browse just one policy's
   points instead, select the policy under **Policies** and press **r**.

2. If the password isn't already cached on this machine, Armor asks for it so it can unlock
   the data. (After a normal setup it's cached, so it won't ask.)

3. When asked for a **destination folder**, enter `~/armor-demo/restored`. (Leaving it
   blank restores each file to its original path — handy for a real recovery, but for this
   demo we want a separate folder to compare.)

Armor reports how many files and bytes it restored. Confirm the round trip from a shell:

```bash
diff -r ~/armor-demo/source ~/armor-demo/restored/home/<you>/armor-demo/source
```

Under a destination root, Armor recreates each file's full path beneath that root, so the
restored copy lives at the mirrored path shown above (substitute your actual home path).
A clean `diff` with no output means the restore is byte-for-byte identical to the source.

If you want assurance without writing files at all, there is also a verify path in the
engine that fetches and authenticates every chunk a point-in-time references; a future
menu item will surface it directly.

## Installing the system tray

The agent (`Armor.Agent`) is the always-on process. It shows Armor's icon in your system
tray and runs your schedules in the background even when the console is closed. Its tray
menu has four items: **About** (a small info window), **Open** (launches the TUI in a
terminal), **Status** (the agent's current state), and **Exit** (stops the agent).

Start it from your published copy:

```bash
# from the dist/ folder you published earlier
./Armor.Agent          # or double-click Armor.Agent.exe on Windows
```

The icon appears in the tray. **Open** looks for `Armor.Tui` next to `Armor.Agent`, which
is why publishing both into `dist/` matters.

Platform notes for the tray itself:

- **Windows** — works with no extra setup; the icon appears in the notification area.
- **macOS** — the icon appears as a status item in the menu bar at the top of the screen.
- **Linux** — you need a tray that speaks the StatusNotifierItem protocol. KDE Plasma has
  it built in; GNOME needs the "AppIndicator and KStatusNotifierItem" extension. On a
  headless server there is no tray — run the agent for its scheduler and drive everything
  from the TUI instead.

### Scheduled, unattended backups

A scheduled backup has no one to type a password, so Armor **caches the password on the
machine** (encrypted at rest with a machine-local key under `~/.armor/state/`) when you
create an encryption password. When a schedule comes due, the agent unlocks the data key
from the cached password and runs the backup — no key file, no prompt.

Create a schedule under **Schedules → `c`**: pick a policy, then answer a plain-English
frequency form (every N minutes/hours, every day, certain weekdays, a day of the month, or
an advanced raw cron) and a time of day. Armor builds the cron expression for you; the
**Schedules** and **Runs** views show the schedule in plain English and the next run time.
All times are UTC.

Policies whose password isn't cached on a machine are simply left for the next tick, so
they run the moment the password is available. The scheduler wakes on the interval in
`armor.json` (`SchedulerTickSeconds`, default 30).

### Disaster recovery on a fresh machine

Because every backup writes the password-wrapped data key and its parameters into a header
on the target itself, you can recover with only two things: **where the backup is** and
**the password**. On a brand-new install, open **Recover**, press **c** to point Armor at
the target location, press **Enter** and type the password, then browse the catalog of
backups found there and restore everything, a folder, or a single file — no local database
required.

### Start the agent automatically

To have the tray appear at login, register the agent with your platform's startup system.

- **Windows** — press `Win+R`, type `shell:startup`, and drop a shortcut to
  `Armor.Agent.exe` into the folder that opens. Or create a Task Scheduler task that runs
  it "At log on".
- **macOS** — add `Armor.Agent` under *System Settings → General → Login Items*, or install
  a `launchd` user agent under `~/Library/LaunchAgents`.
- **Linux** — for a desktop session, add an autostart entry at
  `~/.config/autostart/armor-agent.desktop`. For a headless scheduler, a `systemd --user`
  service is cleaner:

  ```ini
  # ~/.config/systemd/user/armor-agent.service
  [Unit]
  Description=Armor backup agent

  [Service]
  ExecStart=%h/dist/Armor.Agent
  Restart=on-failure

  [Install]
  WantedBy=default.target
  ```

  Then `systemctl --user enable --now armor-agent`.

## Where your data lives

- **Configuration and state:** `~/.armor/armor.json`, `~/.armor/armor.db`, `~/.armor/state/`
  (including the cached, machine-encrypted passwords under `~/.armor/state/keys/`).
- **Logs:** `~/.armor/logs/`.
- **Backed-up data:** on the backup target you configured, under a chunk store plus a
  per-run manifest, a small per-run info sidecar, and a repository header.

To move Armor to a new machine, press **x** in the TUI to export a self-backup — a zip of
your configuration, database, and state — then import it on the new machine, and every
policy, target, password envelope, and backup record comes back, ready to restore. But you
don't even need that: because each target carries an encrypted header with the
password-wrapped data key, your files remain recoverable from the **target plus the
password** alone, even if the local database is lost (see *Disaster recovery on a fresh
machine* above). That is the whole point: a backup you can actually restore.

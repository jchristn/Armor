# Getting started with Armor

This guide takes you from an empty machine to a working backup and a verified restore.
It covers building Armor from source, running the terminal console (TUI) and the
background agent that owns the system-tray icon, and a full round trip: back up a
folder, then restore it somewhere else and confirm the files came back intact.

Armor keeps everything it needs under a single directory, `~/.armor`, created on first
run: `armor.json` (configuration), `armor.db` (the SQLite state database), `logs/`, and
`state/`. Nothing is written anywhere else unless you point a storage target at it.

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

The TUI is where you do everything by hand — create keys, targets, and policies, and run
backups and restores. Start it with:

```bash
dotnet run --project src/Armor.Tui -f net10.0
```

or, from a published copy, run `dist/Armor.Tui` (double-click `Armor.Tui.exe` on Windows,
or run `./Armor.Tui` in a terminal).

You'll land on a full-screen console with a menu bar across the top: **Policies, Jobs,
Schedules, Targets, Keys, Restore, Maintenance, Help**. Move between menus with the
**Left/Right** arrows, open a menu with **Down** or **Enter**, move the highlight with
**Up/Down**, and choose an item with **Enter**. When Armor asks you a question it opens a
small dialog: type your answer and press **Enter**, or press **Escape** to cancel. When
it asks you to pick from a list, use **Up/Down** and **Enter**. To leave, open **Help →
Quit**.

The first launch creates `~/.armor` and prints where your configuration lives.

## A basic backup

We'll back up a folder to another local folder. Create a couple of test files first so
there's something real to protect:

```bash
mkdir -p ~/armor-demo/source ~/armor-demo/target
echo "the only backup that matters is the one you can restore" > ~/armor-demo/source/notes.txt
cp ~/armor-demo/source/notes.txt ~/armor-demo/source/notes-copy.txt
```

Now, in the TUI, work top to bottom:

1. **Keys → Create.** Give the key a name (for example, `demo-key`) and set a passphrase.
   Armor generates a random data key and stores only its wrapped form — your passphrase is
   never written to disk. Remember it; without it, or a key file, the data cannot be read.

2. **Targets → Create disk target.** Name it (for example, `demo-target`) and enter the
   destination directory: `~/armor-demo/target`. Secret fields for network and cloud
   targets are encrypted at rest, but a local folder needs no secret.

3. **Targets → Validate.** Pick `demo-target`. Armor writes a probe object, reads it back,
   and deletes it, then reports success — so you learn the target is reachable before you
   rely on it.

4. **Policies → Create.** Name the policy (for example, `demo-policy`), enter the include
   path `~/armor-demo/source`, then pick `demo-target` and `demo-key` when prompted, and
   choose **Full** as the backup type.

5. **Policies → Run backup.** Pick `demo-policy`, then enter the passphrase you set for
   `demo-key`. Armor chunks the files, deduplicates and compresses them, encrypts every
   chunk, and writes a manifest for this point in time. It reports how many files it
   captured and how many chunks it wrote versus reused — because `notes-copy.txt` is
   identical to `notes.txt`, you'll see reuse on the second file.

You can confirm the run under **Jobs → List**: the backup job shows as `Completed`.

## A basic restore

Restoring reads one manifest and rebuilds the files, checking every chunk against its
content hash on the way out. We'll restore to a fresh folder so you can compare against
the original.

1. **Restore → Restore a point-in-time.** Pick the backup job you just created (it's
   labeled with its type and completion time).

2. Enter the passphrase for `demo-key` when prompted, so Armor can unlock the data.

3. When asked for a **destination root**, enter `~/armor-demo/restored`. (Leaving it blank
   restores each file to its original path — handy for a real recovery, but for this demo
   we want a separate folder to compare.)

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

A scheduled backup has no one to type a passphrase, so the agent unlocks a policy's key
from a **key file** rather than a passphrase. To enable this, create the encryption key
with a key file (a future menu option; the engine and services already support it), and
place that key file at:

```
~/.armor/state/keys/<encryptionKeyId>.key
```

protected by normal filesystem permissions. When a schedule comes due, the agent reads the
key file, unlocks the data key, and runs the backup. Policies whose key isn't available
this way are simply left for the next tick, so they run the moment the key is provided.
The scheduler wakes on the interval in `armor.json` (`SchedulerTickSeconds`, default 30).

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

- **Configuration and state:** `~/.armor/armor.json`, `~/.armor/armor.db`, `~/.armor/state/`.
- **Logs:** `~/.armor/logs/`.
- **Backed-up data:** on the storage target you configured, under a chunk store plus a
  per-run manifest and a repository header.

To move Armor to a new machine, use **Maintenance → Export self-backup** to bundle your
configuration, database, and state into one zip, then import it on the new machine — every
policy, target, key envelope, and backup record comes back, ready to restore. And because
each target also carries an encrypted header with the wrapped data key, your files remain
recoverable from the target plus your passphrase or key file even if the local database is
lost. That is the whole point: a backup you can actually restore.

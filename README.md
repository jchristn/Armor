<p align="center">
  <img src="https://github.com/jchristn/Armor/blob/main/assets/logo.png?raw=true" width="150" alt="Armor logo" />
</p>

<h1 align="center">Armor</h1>

<p align="center"><em>Data protection for the paranoid.</em></p>

<p align="center"><em>Yes, I'm sick of paying for backup software that sucks.</em></p>

Armor is a cross-platform backup application for people who assume the worst about
their storage, their network, and their luck. It chunks your files, deduplicates
and compresses them, encrypts every block with AES-256-GCM, and writes the result
to whatever target you trust the least — a USB drive, a file share, or a cloud
bucket — in a form that only your password can reconstruct.

> **Status:** the engine and both applications are built and tested end to end. See
> [`archive/ARMOR_PLAN.md`](archive/ARMOR_PLAN.md) for the build plan and per-phase progress.

## Why Armor

A backup you cannot restore is a rumor. Armor is built around that sentence. Every
chunk written to a target is authenticated against its own content hash, so a
tampered or truncated block fails loudly on restore instead of quietly handing back
corrupt data. The same content-addressed store gives you deduplication for free:
identical data is stored once, no matter how many files or how many backup runs
contain it.

## How it works

Files are split into content-defined chunks (FastCDC), each identified by its
SHA-256 hash. Every chunk is compressed, then encrypted with AES-256-GCM under a
random per-repository data key. That data key is wrapped by a key derived from your
password (PBKDF2-HMAC-SHA256); the password is cached locally, encrypted at rest, so
scheduled backups run unattended. Per-run manifests map your files to their chunk lists,
so restoring a point in time reads that manifest and reassembles from the store. Manifests
are written and read as bounded, independently-encrypted segments, so a backup of millions
of files streams to and from the target without ever holding the whole file list in memory.
Because the wrapped key envelope and repository header travel with the data on the target,
a fresh machine can restore with nothing but the target and your password.

## Storage targets

Armor writes to local filesystem paths (including external USB drives), CIFS/SMB
shares, NFS exports, Amazon S3 and S3-compatible stores, Azure Blob Storage, and
Google Cloud Storage, all through the [Blobject](https://github.com/jchristn/Blobject)
library.

## Getting started

For a step-by-step walkthrough — building from source, running the console and the tray
agent, and a full backup-and-restore round trip — see
[`GETTING_STARTED.md`](GETTING_STARTED.md). The short version follows.

Armor keeps everything under `~/.armor`: a JSON configuration file, a SQLite database,
a `logs/` directory, and a `state/` directory. The first run creates them. Launch the
TUI — a dashboard with a nav sidebar ordered as a setup checklist — and work down it:

Keys are shown in brackets — for example, on the **Backup targets** screen press the <kbd>c</kbd>
key to create a target. The same <kbd>c</kbd> creates on every screen, and <kbd>Enter</kbd> runs
the screen's main action. A shortcut bar is always visible along the bottom, and <kbd>F1</kbd>
lists every key.

1. **Backup targets** — press <kbd>c</kbd> to add one. Point it at a folder — an external
   drive, a mounted share, or any path — then press <kbd>Enter</kbd> to validate: Armor writes a
   probe object, reads it back, and deletes it, so you learn immediately whether the target is
   reachable.
2. **Passwords** — press <kbd>c</kbd> to add one. Name an encryption password and choose a
   password. Armor generates a random data key, wraps it with your password, and caches the
   password locally so backups run unattended. The password is the only secret needed to
   restore — no key file.
3. **Policies** — press <kbd>c</kbd> to create one. Choose what to include, what to exclude, the
   backup target, the encryption password, and whether runs are full, incremental, or
   differential. A shared **global exclude list** — build output, package and tool caches,
   `AppData`, and the like — keeps the usual noise out of every policy that opts in; press
   <kbd>g</kbd> any time to manage it.
4. **Policies** — press <kbd>Enter</kbd> to run a backup now; a progress bar tracks it and the
   activity log reports how many chunks it wrote versus reused. Each run is a restore point.
5. **Backup jobs** — press <kbd>Enter</kbd> (or <kbd>r</kbd> on a policy) to pick a point-in-time
   and choose where to write. Restoring to a blank destination rebuilds the original tree there.

For unattended, scheduled backups, create a schedule (on **Schedules**, press <kbd>c</kbd> for a
plain-English frequency form). The agent (`Armor.Agent`) owns the tray icon and runs due
schedules on the interval in `armor.json`; opening the TUI starts it automatically when it is not
already running, and it keeps running in the tray after you close the TUI. Because a scheduled
run has no one to type a
password, the agent unlocks the data key from the password cached (encrypted at rest) under
`~/.armor/state/`.

## Storage-target setup

Disk targets need only a path. The network and cloud targets need their provider's
connection details, entered when you create the target; secret fields (passwords,
access keys, account keys, service-account JSON) are encrypted at rest under a local
key before they touch the database.

| Target | What you provide |
|---|---|
| Disk / USB | A local directory path |
| CIFS/SMB | Host, share, username, password |
| NFS | Host, share, protocol version (and uid/gid if needed) |
| Amazon S3 / compatible | Access key, secret key, region, bucket (endpoint and base URL for MinIO and friends) |
| Azure Blob | Account name, account key, endpoint, container |
| Google Cloud Storage | Project id, bucket, service-account JSON |

## Disaster recovery

Two independent paths bring Armor back:

- **From the self-backup zip.** Press <kbd>x</kbd> in the TUI to export a self-backup — your
  configuration, database, and state directory in one archive. On a new machine, drop the
  archive in and import it, and every policy, target, password envelope, and backup record
  returns — ready to restore your files.
- **From the target alone.** Each repository carries a header (`armor.repo.json`) with the
  password-wrapped data key and the parameters to unwrap it. Given only the **target and
  the password**, the data is recoverable even if the local database is gone — the TUI's
  **Recover** section browses the catalog on the target and restores full or partial.

## Building

Armor targets .NET 8 and .NET 10.

```bash
dotnet build src/Armor.sln
dotnet run --project src/Test.Automated        # Touchstone console runner
dotnet test src/Test.Xunit                     # xUnit adapter
dotnet test src/Test.Nunit                     # NUnit adapter
```

To exercise backup → enumerate → restore against a real target (a temp disk directory or an
object-storage bucket), use the CLI integration harness (`--help` lists every option):

```bash
# local disk (default)
dotnet run --project src/Test.Integration -f net10.0 -- --type disk
# S3 / MinIO, path-style, keep the data for inspection
dotnet run --project src/Test.Integration -f net10.0 -- --type s3 \
  --endpoint http://localhost:9000 --access-key A --secret-key B \
  --bucket armor-test --region us-east-1 --path-style --no-cleanup
```

## License

MIT — see [LICENSE.md](LICENSE.md).

## Attribution

<a href="https://www.flaticon.com/free-icons/armor" title="armor icons">Armor icons created by Smashicons - Flaticon</a>

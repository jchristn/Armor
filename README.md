<p align="center">
  <img src="https://github.com/jchristn/Armor/blob/main/assets/logo.png?raw=true" width="150" alt="Armor logo" />
</p>

<h1 align="center">Armor</h1>

<p align="center"><em>Data protection for the paranoid.</em></p>

Armor is a cross-platform backup application for people who assume the worst about
their storage, their network, and their luck. It chunks your files, deduplicates
and compresses them, encrypts every block with AES-256-GCM, and writes the result
to whatever target you trust the least — a USB drive, a file share, or a cloud
bucket — in a form that only your passphrase or key file can reconstruct.

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
passphrase (PBKDF2-HMAC-SHA256) and/or unwrapped by a key file — either can restore.
Per-run manifests map your files to their chunk lists, so restoring a point in time
reads one manifest and reassembles from the store. Because the wrapped key envelope
and repository header travel with the data on the target, a fresh machine can restore
with nothing but the target and your secret.

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
TUI and work top to bottom through the menu bar:

1. **Keys → Create.** Name an encryption key and set a passphrase. Armor generates a
   random data key and stores only its wrapped form.
2. **Targets → Create disk target.** Point it at a folder — an external drive, a
   mounted share, or any path. **Targets → Validate** writes a probe object, reads it
   back, and deletes it, so you learn immediately whether the target is reachable.
3. **Policies → Create.** Choose what to include, the storage target, the encryption
   key, and whether runs are full, incremental, or differential.
4. **Policies → Run backup.** Unlock the key with your passphrase and watch the run
   report how many chunks it wrote versus reused.
5. **Restore → Restore a point-in-time.** Pick a backup job, unlock, and choose where
   to write. Restoring to a blank destination rebuilds the original tree there.

For unattended, scheduled backups, run the agent (`Armor.Agent`). It owns the tray
icon and runs due schedules on the interval in `armor.json`. Because a scheduled run
has no one to type a passphrase, the agent unlocks a policy's key from a key file at
`~/.armor/state/keys/<keyId>.key` — provision the key with a key file and place it
there, protected by filesystem permissions.

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

- **From the self-backup zip.** Maintenance → Export self-backup writes your
  configuration, database, and state directory into one archive. On a new machine,
  drop the archive in and import it, and every policy, target, key envelope, and
  backup record returns — ready to restore your files.
- **From the target alone.** Each repository carries a header (`armor.repo.json`) with
  the wrapped data key and the parameters to unwrap it. Given the target and your
  passphrase or key file, the data is recoverable even if the local database is gone.

## Building

Armor targets .NET 8 and .NET 10.

```bash
dotnet build src/Armor.sln
dotnet run --project src/Test.Automated        # Touchstone console runner
dotnet test src/Test.Xunit                     # xUnit adapter
dotnet test src/Test.Nunit                     # NUnit adapter
```

## License

MIT — see [LICENSE.md](LICENSE.md).

## Attribution

<a href="https://www.flaticon.com/free-icons/armor" title="armor icons">Armor icons created by Smashicons - Flaticon</a>

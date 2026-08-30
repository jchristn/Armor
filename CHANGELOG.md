# Changelog

All notable changes to Armor are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Backup completion is announced — exactly once.** When a backup you run from the TUI finishes or
  fails, the TUI pops a modal with the same run statistics already written to the activity log — total
  runtime, files and bytes backed up, files skipped, and throughput on success, or the error on
  failure. Scheduled backups run by the tray agent (which has no on-screen modal) instead raise a
  single native desktop notification across operating systems — a Windows tray balloon, a macOS
  notification, or Linux `notify-send`. Each run produces one announcement in one place: a modal for
  interactive runs, a desktop notification for scheduled ones.

## [0.3.0] - 2026-08-29

Exclude visibility, restore progress, and a paste fix in the TUI.

### Added
- **Live progress for restores.** A restore now appears in the status workspace with the same
  live progress bar as a backup — file and byte counts and a percentage — and can be selected and
  canceled there. Both restore entry points are covered: restoring a policy's point-in-time and
  the **Recover** flow that browses a target directly. The engine reports progress against the
  backup point-in-time's own totals, so the bar is accurate from the first file (no pre-scan). The
  status region is renamed **"Backups & restores in progress"** to match.

### Changed
- **New policies pre-fill the global excludes.** Creating a policy now seeds its exclude editor
  with the shared global exclude list (build output, package/tool caches, `AppData`, OS metadata,
  …) instead of showing an empty list, so you can see and trim exactly what the policy will skip.
  The seeded rules become the policy's own, and the policy opts out of the shared list, so the
  editor's rules are authoritative — removing a shown rule actually un-excludes it rather than
  leaving a hidden global layer to re-apply it. The shared list and its <kbd>g</kbd> manager are
  unchanged for existing policies and the per-policy toggle.

### Fixed
- **Pasting into TUI prompts works.** Access keys, secret keys, passwords, and other values can
  now be pasted (Ctrl/Cmd+V) into the terminal prompts — for example when adding an S3 backup
  target. A bracketed paste was previously decoded but dropped before it reached the focused
  field. Fixed upstream by upgrading the TUIKit terminal-UI library to `0.9.0`.

## [0.2.0] - 2026-08-28

### Added
- **Shared global exclude list.** A machine-wide set of exclude rules — build output,
  package and tool caches (`node_modules`, `obj`, `.nuget`, `packages`, `.vs`), `AppData`,
  and other common noise — applied to every policy that opts in, with a per-policy toggle
  in the policy editor and a `g`-key manager (edit / restore defaults) in the TUI. Seeded
  with sensible defaults on first run.
- **Always-visible keyboard shortcut bar.** A persistent one-row footer lists the essential
  keys and always keeps `F1 Help` in view, so commands like global-exclude management are
  discoverable instead of hidden behind F1.

### Changed
- **Manifests are streamed as bounded, encrypted segments.** Instead of serializing a run's
  entire file list into a single in-memory JSON string and array, a manifest is now written
  to the target as a small header plus numbered, independently-compressed-and-encrypted
  segments — and read back the same way, one segment at a time (restore, verify, incremental
  baseline, retention, and the recovery catalog all stream). Peak memory stays flat
  regardless of file count, and legacy single-object manifests still read.
- **Scanning and backing up now overlap.** A run processes files while it is still scanning
  the source, so work begins as soon as the first batch lands instead of after a full
  enumeration. A durable scan-complete marker keeps resume correct: a run that crashed
  mid-scan discards its partial work list and re-scans, while one that finished scanning
  simply processes the complete list.

### Fixed
- **Out-of-memory on very large backups.** A backup of millions of files no longer fails at
  the manifest step with *"Insufficient memory to continue the execution of the program"* on
  a machine with ample free RAM — the failure was a single serialized manifest object
  exceeding .NET's ~2 GB object ceiling, now removed by segment streaming.
- **Directory-name exclude rules now prune reliably**, and a work-list row id can no longer
  be reused mid-run (the work list uses an autoincrementing id), closing a rare path where an
  overlapped scan could skip a file.

### Changed
- **Password-based encryption keys.** The TUI now provisions each encryption key from a
  user-chosen **password** (no key file). The password is cached on the machine, encrypted
  at rest, so both manual and scheduled backups run unattended, and the password alone
  recovers the data on a fresh install. Legacy key-file keys still unlock.
- **TUI redesigned as a dashboard.** A nav sidebar ordered as a setup checklist
  (backup targets → passwords → policies → schedules, then runs, restore points, recover),
  a live content table, an ASCII-art header, an in-flight progress bar, and an activity log.
  Long operations run in the background so the UI stays responsive.
- **Friendlier language.** "Storage targets" are now "Backup targets"; encryption keys are
  presented as "encryption passwords"; schedules are shown and confirmed in plain English
  rather than raw cron, via a guided frequency builder.

### Added
- **Recover from a target.** A recovery flow that reads the catalog directly off a backup
  target using only its location and the password — no local database — and restores
  everything, a folder, or a single file. Backed by a `RecoveryService`/`RecoverySession`
  in the core and a per-run encrypted metadata sidecar written on the target.
- **Backup progress reporting.** `BackupService`/`BackupEngine` accept an
  `IProgress<BackupProgress>`; the TUI shows a live completion bar during a backup.
- **Runs view and per-policy restore points.** A nav section listing upcoming scheduled
  runs and anything in progress, plus an `r` shortcut to browse and restore a selected
  policy's point-in-time backups.
- **Purge on target delete.** Deleting a backup target can also delete its stored backup
  data (removing the folder for disk targets), via `StorageTargetService.PurgeAsync`.
- **Referential-integrity guards.** The TUI blocks deletes that would dangle a reference
  (a password or target still used by a policy, a policy still used by a schedule).

### Added
- **Engine core.** Content-defined chunking (FastCDC), per-chunk compression
  (Deflate/Brotli, whichever is smaller), AES-256-GCM encryption with the chunk
  content hash bound in as associated data, and SHA-256 content addressing with
  deduplication.
- **Cryptography and keystore.** A random per-repository data key wrapped by a
  passphrase (PBKDF2-HMAC-SHA256) and/or a key file; either recovers the data key.
  Storage-target secrets are encrypted at rest under a local, owner-only key.
- **Backup engine.** Full, incremental, and differential backups producing a
  per-run manifest that lists every file with its ordered chunk references.
- **Restore engine.** Whole-tree, folder, and single-file restores to the original
  or an alternate root, plus a standalone verify. Corrupt or missing chunks abort
  loudly rather than producing wrong output.
- **Retention and garbage collection.** Retention-window pruning with mark-and-sweep
  chunk GC that keeps the newest point-in-time and never deletes a referenced chunk.
- **Scheduling.** Five-field cron parsing and evaluation, a cross-process per-policy
  run lock, and an agent scheduler loop.
- **Storage targets.** Local disk/USB, CIFS, NFS, Amazon S3 (and compatible), Azure
  Blob, and Google Cloud Storage via [Blobject](https://github.com/jchristn/Blobject),
  with connection validation.
- **Data layer.** SQLite with WAL and serialized writes, versioned idempotent
  migrations, domain-specific data-access methods, and per-policy state tables.
- **Self-backup.** Export and import of the configuration file, database, and state
  directory as a single zip.
- **Applications.** `Armor.Agent` (Avalonia system-tray icon + scheduler host) and
  `Armor.Tui` (TUIKit dashboard console).
- **Tests.** A Touchstone suite (console, xUnit, NUnit adapters) covering
  configuration, the data layer, cryptography, the chunk store, storage, the backup
  and restore engines, retention, scheduling, self-backup, and the service layer,
  with positive and negative cases, on .NET 8 and .NET 10.

# Changelog

All notable changes to Armor are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to
adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
  `Armor.Tui` (TUIKit menu-driven admin console).
- **Tests.** A Touchstone suite (console, xUnit, NUnit adapters) covering
  configuration, the data layer, cryptography, the chunk store, storage, the backup
  and restore engines, retention, scheduling, self-backup, and the service layer,
  with positive and negative cases, on .NET 8 and .NET 10.

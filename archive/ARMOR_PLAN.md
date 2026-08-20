# Armor — Build Plan

> **Armor** — *Data protection for the paranoid.*
> Repository: <https://github.com/jchristn/Armor>

This is the working plan for building Armor, a cross-platform cloud/local backup application written in C#. It is meant to be annotated as work proceeds: every task has a checkbox, and phases are ordered so that a developer can pick up where the last one left off. It follows the requirements in `c:\code\agents\requirements` and the app specification in `ARMOR.md`, deviating only where a single-user desktop tool genuinely differs from the multi-tenant HTTP-service assumptions in `BACKEND_ARCHITECTURE.md` (those deviations are called out explicitly in [§2](#2-where-we-deviate-from-backend_architecturemd)).

---

## 1. Decisions locked with the product owner

These were confirmed before planning and are treated as fixed. Changing any of them invalidates parts of this plan.

| Area | Decision |
|---|---|
| **On-target data layout** | Content-addressed chunk store: files are split into content-defined chunks, each chunk hashed, deduplicated, compressed, then encrypted. Per-run manifests map files → chunk lists; a chunk index tracks references. |
| **Encryption** | AES-256-GCM. A random per-repository data key is wrapped by a key-encryption-key that can be derived from a **passphrase** (KDF) **and/or** unwrapped by a **key file**. Both methods are supported per encryption-key entry; the user chooses at key-creation time. |
| **Scheduling** | A long-running **background agent process** owns the scheduler and runs backups even when the TUI is closed. The agent process also owns the system-tray icon. |
| **Tray icon** | Implemented with **Avalonia**'s cross-platform `TrayIcon`. Works on Windows, macOS, and Linux. |
| **TUI ↔ agent coupling** | **Shared in-process core.** The TUI and the agent both link `Armor.Core` and open the same SQLite database directly (WAL + serialized writes + a cross-process run lock). The agent's distinct job is scheduling and running due backups; the TUI can also trigger an immediate run in its own process. No local socket or HTTP API. |
| **Database** | **SQLite only, no multi-tenancy.** Single local database whose filename is defined in `armor.json`. The data layer keeps a clean driver/factory/interface shape but ships exactly one provider. |
| **Target frameworks** | Multi-target **`net8.0;net10.0`**, matching the TUIKit / Blobject / Touchstone sample projects. |
| **Storage-target testing** | Harnesses take **CLI arguments** (storage type, endpoint URLs, credentials, etc.). Supplied → run against that provider; not supplied → run against a **temporary directory and clean up**. Storage suites must exercise **both backup and restore** with byte-identical assertions. Offline CI stays green. |

---

## 2. Where we deviate from BACKEND_ARCHITECTURE.md

`BACKEND_ARCHITECTURE.md` is normative for backend HTTP services and assumes Watson 7, four database providers, and structural multi-tenancy. Armor is a single-user desktop application with no server. We adopt the parts that apply and drop the parts that do not. The deviations are deliberate.

**Dropped:** Watson 7 HTTP hosting, route registrars, the `AuthenticateRequest`/`Preflight`/`PostRouting` pipeline, request-history capture, multi-tenancy (`TenantId`/`UserId` columns, tenant-scoped queries and composite indexes), and the Mysql/Postgresql/SqlServer providers. None of these have meaning in a local backup tool, and carrying them would add a large, untested surface for no user benefit.

**Kept:** the strict C# code style in `CODE_STYLE.md` (verbatim — see [§16](#16-code-style-hard-rules)); `PrettyId` prefixed, K-sortable string identifiers generated through a central `IdGenerator` with prefixes in `Constants.cs`; a provider-neutral data-layer shape (`DatabaseDriverBase` → `SqliteDatabaseDriver`, `DatabaseDriverFactory`, `SchemaMigration` + a `schema_migrations` table, handwritten SQL in `Queries/` classes, domain-specific `*Methods` interfaces rather than a generic CRUD repository); structured schema instead of BLOB-with-an-index (no catch-all JSON columns for managed entities); strongly typed settings loaded from JSON and overridden by environment variables, validated and clamped on load; explicit SQLite write serialization; a thin `Program.cs` delegating to a bootstrapper; and the four-project Touchstone test layout from `BACKEND_TEST_ARCHITECTURE.md`.

---

## 3. Runtime and process model

Armor runs as **two cooperating processes over one shared database**, plus a shared engine library.

- **`Armor.Agent`** — the always-on process. Hosts the Avalonia tray icon and the scheduler. Evaluates schedules, runs due backups through `Armor.Core`, updates status, and prunes according to retention. Its tray menu exposes **About** (modal), **Open** (launches the TUI in a new terminal window), **Status: {status}**, and **Exit**. On Windows it builds as a `WinExe` so it has no stray console window; on macOS/Linux it runs headless with a tray/status item.
- **`Armor.Tui`** — the interactive admin console built on **TUIKit**. A menu-driven, modal-driven full-screen terminal app for every administrative function: policies, jobs, schedules, storage targets, encryption keys, restore, and the config/state backup facility. It can trigger an immediate backup in its own process.
- **`Armor.Core`** — the engine both processes link: configuration, SQLite data layer, chunk store, crypto/keystore, storage-target abstraction over Blobject, backup engine, restore engine, retention/GC, and the scheduler primitives.

Because two processes touch one SQLite file, the database opens in **WAL mode** with a busy timeout, and all writes route through a single `SemaphoreSlim` inside each process. Runs of the *same policy* are additionally guarded by a **cross-process lock** (an advisory lock row plus an OS file lock in the state directory) so a scheduled run and a TUI-triggered run can never execute the same policy concurrently. The tray's **Open** action shells out to the platform terminal (`wt`/`cmd` on Windows, `Terminal.app` via `open` on macOS, `x-terminal-emulator`/`$TERMINAL` on Linux) to launch `Armor.Tui`.

**Why not one binary:** Avalonia wants a windowed entry point (`WinExe` on Windows) while the TUI needs an attached console. Splitting them avoids the console-visibility conflict and keeps each process's lifetime independent.

---

## 4. Solution and repository layout

```
Armor/
├── ARMOR.md                     # product spec (exists)
├── ARMOR_PLAN.md                # this plan
├── README.md                    # see §15 (logo + attribution)
├── CHANGELOG.md
├── LICENSE.md                   # MIT
├── .gitignore
├── assets/
│   ├── logo.png                 # 512x512 app/README logo (exists)
│   └── logo.ico                 # Windows/app icon (exists)
└── src/
    ├── Armor.sln
    ├── Armor.Core/              # engine library (net8.0;net10.0)
    │   ├── Constants.cs
    │   ├── Configuration/       # ArmorSettings, loader, env overrides, paths
    │   ├── Database/
    │   │   ├── DatabaseDriverBase.cs
    │   │   ├── DatabaseDriverFactory.cs
    │   │   ├── DatabaseSettings.cs
    │   │   ├── SchemaMigration.cs
    │   │   ├── Interfaces/      # IPolicyMethods, IScheduleMethods, ...
    │   │   └── Sqlite/
    │   │       ├── SqliteDatabaseDriver.cs
    │   │       ├── Implementations/
    │   │       ├── Queries/
    │   │       ├── Sanitizer.cs
    │   │       └── Converters.cs
    │   ├── Enums/
    │   ├── Helpers/             # IdGenerator, path helpers, hashing helpers
    │   ├── Models/              # Policy, Schedule, StorageTarget, EncryptionKey,
    │   │                        # BackupJob, RestoreJob, Manifest, FileEntry, ...
    │   ├── Security/            # Keystore, KeyEnvelope, KDF, AES-GCM cipher, cred protection
    │   ├── Storage/             # IStorageRepository + Blobject adapters + factory
    │   ├── ChunkStore/          # chunker (FastCDC), compression, chunk read/write, index
    │   ├── Engine/              # BackupEngine, RestoreEngine, ChangeDetector, Retention/GC
    │   ├── Scheduling/          # schedule model, cron eval, run lock
    │   └── Backup/              # config+db+state zip export/import (self-backup)
    ├── Armor.Agent/            # tray + scheduler host (Exe / WinExe on Windows)
    ├── Armor.Tui/              # TUIKit admin app (console Exe)
    ├── Test.Shared/            # Touchstone descriptors (Touchstone.Core only)
    ├── Test.Automated/         # Touchstone console runner
    ├── Test.Xunit/             # Touchstone xUnit adapter
    └── Test.Nunit/             # Touchstone NUnit adapter
```

`Sanitizer.cs`/`Converters.cs` are included where the SQLite implementation needs them. Docker artifacts from `REPOSITORY_REQUIREMENTS.md` (items 2, 4, 9 — `.dockerignore`, `DOCKERHUB_README.md`, compose files) are **not applicable**: Armor ships as a desktop application, not a container image. See [open questions](#20-open-questions--risks).

---

## 5. On-target data layout (chunk store)

Everything written to a storage target lives under a repository root chosen per storage target. The layout is designed so a fresh machine can restore using only the target plus the passphrase or key file.

```
<target-root>/
├── armor.repo.json.enc        # repo header: format version, chunker params, KDF params,
│                              #   wrapped data-key envelope (mirrors the local keystore for DR)
├── chunks/
│   └── ab/abcdef...           # one object per chunk, sharded by first hash byte(s);
│                              #   payload = compressed-then-AES-256-GCM-encrypted chunk
├── manifests/
│   └── <policyId>/
│       └── <pointInTimeUtc>.manifest.json.enc   # encrypted per-run manifest
└── index.json.enc             # optional cached chunk index for target-only DR/verify
```

**Chunking.** Content-defined chunking (FastCDC) with configurable `MinSize`/`AvgSize`/`MaxSize` (defaults on the order of 256 KiB / 1 MiB / 4 MiB, all configurable — no magic constants). Content-defined boundaries make dedup resilient to insertions/deletions inside files. Each chunk's identity is its **SHA-256** hash; identical content produces identical chunk objects, which is the whole dedup mechanism.

**Compression.** Each chunk is compressed with a BCL codec (`System.IO.Compression`, Deflate/Brotli) and stored compressed only if the result is actually smaller; a per-chunk codec flag records `None`/`Deflate`/`Brotli` so incompressible data is never inflated. Determinism of the stored bytes for a given (content, codec) is covered by tests.

**Encryption.** Compression happens first, then AES-256-GCM. Each chunk object is framed as `[version][codec][nonce(96-bit)][tag(128-bit)][ciphertext]`, with the chunk's SHA-256 used as GCM associated data so a chunk cannot be silently swapped for another. A tampered or truncated chunk fails authentication on restore and aborts loudly.

**Manifests.** A manifest is a typed model: run metadata (policy id, point-in-time, backup type, base run for incr/diff) plus, per included file, its path, size, timestamps, attributes, and its ordered list of chunk hashes. Manifests are serialized to typed JSON, compressed, encrypted, and written under `manifests/<policyId>/`. Restore reads exactly one manifest to reconstruct a point in time; unchanged files in an incremental simply re-reference existing chunk hashes.

---

## 6. Encryption and key management

- **Cipher:** AES-256-GCM for chunks and manifests; unique random 96-bit nonce per encryption; 128-bit auth tag; associated data binds each ciphertext to its logical identity (chunk hash / manifest id).
- **Key hierarchy:** a random 256-bit **data key** encrypts repository data. The data key is wrapped by a **key-encryption-key (KEK)**. The KEK is either derived from a **passphrase** with **PBKDF2-HMAC-SHA256** (BCL `Rfc2898DeriveBytes` — no third-party dependency; salt and a high, documented iteration count recorded) or supplied by a **key file** (raw 256-bit material the user safeguards). An encryption-key entry may enable passphrase, key file, or both — both unwrap the same data key, so either can restore. The KDF name and its parameters are written into the repo header ([§6-agility](#6-encryption-and-key-management)), so a future move to Argon2id stays restorable without breaking existing repositories.
- **Envelope storage:** the wrapped data key, salt, and KDF parameters live in the local SQLite keystore **and** are mirrored into `armor.repo.json.enc` on each target. Disaster recovery therefore needs only the target plus the passphrase/key file — no surviving local state.
- **Passphrase handling:** passphrases are never persisted. In memory they are held only as long as needed and zeroed after use where the runtime allows.
- **Storage-target credentials** (S3 secret keys, Azure keys, CIFS passwords, etc.) are encrypted at rest under a **local data-protection key** stored in `~/.armor` with restricted permissions (`0600`). Platform keystores (Windows DPAPI, macOS Keychain, Linux Secret Service) are a documented future enhancement, not v1.

Negative behavior is a first-class requirement: wrong passphrase, wrong key file, tampered ciphertext, and truncated chunks must all fail with clear, specific exceptions and must never produce a partial or wrong restore.

---

## 7. Database schema and data layer

**Shape.** `DatabaseDriverBase` (abstract, `IDisposable` + `IAsyncDisposable`) exposes domain-specific method interfaces as protected-set properties; `SqliteDatabaseDriver` implements them with handwritten SQL in `Queries/` classes and thin `Implementations/`. `DatabaseDriverFactory.CreateAndInitializeAsync(...)` is the composition root. `InitializeAsync` applies versioned, idempotent migrations tracked in `schema_migrations`. Writes are serialized with a `SemaphoreSlim`; WAL mode + busy timeout handle the second process.

**Identifiers.** `PrettyId` K-sortable strings via `IdGenerator`, prefixes in `Constants.cs`:

| Entity | Prefix |
|---|---|
| Policy | `pol_` |
| Schedule | `sch_` |
| Storage target | `tgt_` |
| Encryption key | `key_` |
| Backup job / run (point-in-time) | `job_` |
| Restore job | `rst_` |
| Chunk index entry | `chk_` |

**Tables (initial):**
- `settings_meta` — schema/app metadata.
- `policies` — include roots, exclude patterns (wildcard + regex, files and dirs), min/max file size, backup type (full/incremental/differential), retention window, storage-target id, encryption-key id.
- `schedules` — cron-like spec, enabled flag, linked policy, next/last run.
- `storage_targets` — type (Disk/CIFS/NFS/S3/Azure/GCS) + provider settings; secret fields encrypted at rest.
- `encryption_keys` — keystore envelopes (wrapped data key, salt, KDF params, enabled methods).
- `backup_jobs` — one row per run/point-in-time: policy, type, base run, start/end, bytes/chunks written and reused, status, manifest key, error.
- `restore_jobs` — restore run history and status.
- `chunk_index` — chunk hash → size, codec, reference count (drives dedup and GC).
- **Per-policy state tables** — required by `ARMOR.md` ("a separate table per backup policy to track state"). Named `policy_state_<policyId-suffix>` and created on policy creation; each row tracks a source path's last-seen size, modified timestamp, archive-bit state, and last chunk set, so change detection is O(changed files).
- `schema_migrations` — applied versions.

Domain interfaces: `IPolicyMethods`, `IScheduleMethods`, `IStorageTargetMethods`, `IEncryptionKeyMethods`, `IBackupJobMethods`, `IRestoreJobMethods`, `IChunkIndexMethods`, `IPolicyStateMethods`. Each has positive and negative contract coverage (null args throw `ArgumentNullException`, not-found reads return `null`, enumerations return empty rather than throwing). Every `IEnumerable`-returning method has a `CancellationToken` async variant.

---

## 8. Configuration

`ArmorSettings` is a strongly typed model loaded from `~/.armor/armor.json`, then overridden by `ARMOR_*` environment variables, validated and clamped on load. Missing config on first run writes a default file and creates `~/.armor/` and `~/.armor/logs/`.

Keys include: database filename (default `~/.armor/armor.db`), state directory, log directory and logging switches, chunker defaults (min/avg/max), engine concurrency, agent scheduler tick interval, and default retention. Every tunable is a public member backed by a private field with a sensible default — no bare constants. Numeric ranges are clamped and documented in XML comments.

---

## 9. Storage targets (Blobject)

A single `IStorageRepository` abstraction wraps the Blobject client family so the engine is provider-agnostic:

| Target | Blobject client | Notes |
|---|---|---|
| Local path / USB | `Blobject.Disk` → `DiskBlobClient` | Also the deterministic test backend. |
| CIFS share | `Blobject.CIFS` → `CifsBlobClient` | host/username/password/share. |
| NFS export | `Blobject.NFS` → `NfsBlobClient` | host/uid/gid/share/version. |
| Amazon S3 / compatible | `Blobject.AmazonS3` → `AmazonS3BlobClient` | endpoint/ssl/base-url for MinIO etc. |
| Azure Blob | `Blobject.AzureBlob` → `AzureBlobClient` | account/key/endpoint/container. |
| Google Cloud Storage | `Blobject.GoogleCloud` → `GcpBlobClient` | project + service-account JSON. |

`StorageRepositoryFactory` builds the right client from a `StorageTarget` row. Large chunks and restores stream via `GetStreamAsync`/streamed `WriteAsync`; small control objects use the byte-array APIs. A **Validate Connection** operation (write a probe object, read it back, delete it, enumerate) backs the TUI's "validate connection" requirement and reports actionable errors.

---

## 10. Backup engine

The engine turns a policy run into manifest + chunks on a target.

1. **Enumerate** include roots; apply exclude patterns (wildcard and regex, files and directories) and min/max size filters.
2. **Change detection** decides which files need re-chunking. Primary signals are size and last-modified timestamp; on Windows the **archive bit** is also consulted (and optionally cleared after a successful backup). When signals are inconclusive, fall back to hashing. Per-policy state tables make this incremental.
3. **Backup type** selects the comparison baseline:
   - **Full** — every file is represented in the manifest; chunks already present on the target are still skipped by dedup, so a full backup is not a full re-upload.
   - **Incremental** — baseline is the previous run (full or incremental); only changed files produce new chunks; unchanged files re-reference existing chunk hashes.
   - **Differential** — baseline is the last full run.
4. **Chunk → compress → encrypt → write**, skipping any chunk hash already on the target; bump `chunk_index` reference counts.
5. **Write the manifest** for the point-in-time and record a `backup_jobs` row with byte/chunk written-vs-reused stats.

Runs are cancellable and stream progress to the caller (a TUIKit pane line handle in the TUI; status fields in the DB for the agent/tray). The per-policy cross-process lock is held for the duration.

---

## 11. Restore engine

Restore reconstructs data from a chosen backup job / point-in-time.

- **Scope:** the entire tree, a single folder, or a single file, selectable in the TUI.
- **Mechanism:** load and decrypt the run's manifest, resolve each target file's ordered chunk list, fetch/decrypt/decompress chunks, and reassemble to a chosen destination (in place or to an alternate root).
- **Chains:** incremental/differential restores read exactly the one manifest for the requested point-in-time; because manifests already reference all needed chunk hashes (whether created that run or earlier), no manual chain-walking of deltas is required at restore time.
- **Integrity:** every chunk is GCM-authenticated against its hash; a hash mismatch, failed tag, or missing chunk aborts the restore with a specific error rather than writing corrupt output. A standalone **Verify** operation walks a manifest and confirms every referenced chunk exists and authenticates, without writing files.

---

## 12. Retention and garbage collection

Retention windows on a policy determine which points-in-time survive. Pruning is a two-step, safety-first process: first remove expired `backup_jobs`/manifests, then **mark-and-sweep** chunks — a chunk is deleted only after confirming no surviving manifest references it. Reference counts in `chunk_index` drive the sweep, and the sweep never runs concurrently with a backup of the same repository. The invariant under test: after any prune, every remaining point-in-time still restores byte-identically.

---

## 13. Scheduler and agent

The agent wakes on a configurable tick, asks `IScheduleMethods` for due schedules, and runs each due policy through the backup engine under the cross-process lock. Schedules use a cron-like specification with enabled/disabled state and next/last-run bookkeeping. Backup and restore failures are logged to `~/.armor/logs` and surfaced through the tray status. The agent also drives periodic retention/GC.

---

## 14. System tray (Avalonia)

The tray lives in `Armor.Agent` using Avalonia's `TrayIcon` with `assets/logo.ico` (and PNG where a platform prefers it). Menu items:

- **About** — opens a small Avalonia modal (name, version, tagline, repo link).
- **Open** — spawns `Armor.Tui` in a platform-appropriate terminal window.
- **Status: {status}** — reflects live state (Idle, Running, Error, or last-run summary) read from the shared database/state.
- **Exit** — stops the scheduler and terminates the agent.

TUIKit has no OS-tray capability (confirmed against its source and docs), which is exactly why the tray is an Avalonia concern in the agent process rather than a TUI feature.

---

## 15. TUI (TUIKit)

A dock-shell layout: a `MenuBar` docked top, a `StatusBar` docked bottom, and a filled `main` region hosting the active screen. Menu actions open TUIKit modals (`ConfirmAsync`/`PromptAsync`/`SelectAsync`, `MessageModal`, custom `DialogModal`s) and drive `Form`/`ListView<T>`/`Table`/`FileBrowser` widgets. Long operations run on a `Task`, streaming progress into a pane line handle, and marshal host calls with `app.Post`.

Top-level menus map to the administrative functions in `ARMOR.md`:

- **Policies** — create, view, edit, delete backup policies (includes/excludes, size bounds, type, retention, target, key).
- **Jobs** — view backup jobs and points-in-time; run now; view stats and errors.
- **Schedules** — create/edit/delete/enable schedules.
- **Targets** — manage storage-target credentials; **validate connection**.
- **Keys** — manage encryption key material (passphrase and/or key file).
- **Restore** — browse snapshots, pick scope (all/folder/file), verify, and restore.
- **Maintenance** — export/import the Armor self-backup zip ([§16-config](#16-armor-self-backup)); trigger retention/GC.
- **Help** — About, Quit.

The TUI is exercised headlessly in tests via `HeadlessBackend` + `PumpInputOnce()`/`RenderOnce()` for deterministic screen assertions where practical; full interactive rendering is manual-smoke-tested.

### 16. Armor self-backup

A Maintenance action bundles `armor.json`, the SQLite database, and the state directory into a single `.zip` for moving Armor to a new machine; the inverse restores them and lets the user resume restoring files immediately. Both directions are round-trip tested.

---

## 17. Cross-platform concerns

Windows, macOS, and Linux are first-class. Path handling uses `Path.Combine` and platform-aware separators throughout; the archive bit is Windows-only and guarded behind an OS check with timestamp/size fallback elsewhere. Terminal-launch for the tray's **Open** action, file permission bits on `~/.armor` secrets, and Avalonia tray behavior each have per-platform handling isolated behind small abstractions so the engine core stays platform-neutral.

---

## 18. Testing strategy and coverage

The four-project Touchstone layout from `BACKEND_TEST_ARCHITECTURE.md`: descriptors live in `Test.Shared` (referencing `Touchstone.Core` and `Armor.Core` only, no console output), consumed identically by `Test.Automated` (console runner), `Test.Xunit`, and `Test.Nunit`. Every case has positive and negative variants — for a data-integrity product this is the point, not a nicety.

**Provider selection is driven by CLI arguments.** `Test.Automated` accepts arguments specifying the storage type, endpoint URL(s), credentials, bucket/share/container names, and any other material a provider needs (with the equivalent `BLOBJECT_TEST_*`-style environment variables for the xUnit/NUnit adapters). When a provider is supplied, the storage suites run against that real provider. When nothing is supplied, they run against a **temporary directory that is deleted afterward** — every case creates its own fixtures and cleans up, leaving no residue on disk or on a remote target. Regardless of provider, the storage suites **must exercise both BACKUP and RESTORE end-to-end** and assert byte-identical round-trips; a provider is not considered covered by a backup test alone.

Suites to build, each with negative cases:

- **Configuration** — load valid/missing/malformed; env overrides; clamping.
- **Identifiers** — prefix correctness, K-sortability, non-empty.
- **Chunking** — determinism (same bytes → same hashes), empty file, file below `MinSize`, exact boundary sizes, large file; boundary stability across inserts.
- **Compression** — round-trip for compressible, incompressible (must not inflate), and empty inputs; codec-flag correctness.
- **Crypto** — AES-GCM round-trip; wrong passphrase fails; wrong key file fails; tampered ciphertext fails the tag; truncated chunk fails; nonce uniqueness; KDF parameter round-trip; data-key wrap/unwrap under passphrase, key file, and both.
- **Keystore & credential protection** — envelope persistence; credential encrypt/decrypt at rest.
- **Data layer** — CRUD for every `*Methods` interface, positive and negative; per-policy state-table creation; migration idempotency; enumerations don't throw on empty.
- **Storage repository (Disk)** — write/read/exists/delete/enumerate; validate-connection success and failure.
- **Manifest** — serialize/deserialize/compress/encrypt round-trip.
- **Backup engine** — full/incremental/differential against a generated source tree; verify manifests and chunk reuse counts; exclude patterns (wildcard + regex) and size bounds; archive-bit and timestamp change detection, positive and negative.
- **Restore engine** — full point-in-time restores byte-identically; single file and folder; restore to alternate root; corrupted/missing chunk aborts loudly.
- **Retention/GC** — prune expired points; referenced chunks survive; unreferenced removed; surviving points still restore.
- **Concurrency** — serialized writes; cross-process run lock prevents double-runs of one policy.
- **Self-backup** — zip export/import round-trip.

**Coverage stance (stated honestly).** The goal is maximal deterministic coverage of `Armor.Core` — engine, chunk store, crypto, data layer, configuration, retention — measured with coverlet in offline CI. With no provider arguments supplied, the suite runs the full backup **and** restore path against a temporary directory, so the engine's real behavior is exercised out of the box, not mocked. **Measured result: 87.8% line / 67.3% branch on `Armor.Core`** across 95 console cases (100 through the xUnit adapter). The uncovered remainder is, by design or necessity: the **live cloud/CIFS/NFS provider construction arms** in `StorageRepositoryFactory` (need real credentials — run only when the `ARMOR_TEST_*` variables are supplied), **interactive UI shells** (`Armor.Agent` tray, `Armor.Tui` render loop), Windows-only **archive-bit** branches, and hard-to-force **error/cancellation** paths (I/O failures, unauthorized access, mid-run cancellation). Every data-integrity-critical path — chunking determinism, AES-GCM tamper rejection, full/incremental/differential backup→restore byte-identity, the retention safety invariant, and loud abort on corruption — is directly covered. Pushing the last ~12% would require fault-injection scaffolding for those error branches; it is tracked as follow-up rather than claimed.

---

## 19. Phased milestones

Each phase should build clean (zero warnings, `TreatWarningsAsErrors`, docs generated) and land its own tests before the next begins.

### Phase 0 — Repository scaffolding ✅
- [x] `Armor.sln` (classic format), `.gitignore`, `LICENSE.md` (MIT), `CHANGELOG.md`, `README.md` skeleton, shared `Directory.Build.props`.
- [x] `Armor.Core` project multi-targeting `net8.0;net10.0`, nullable enabled, warnings-as-errors, docs on — builds clean with zero warnings.
- [x] `Constants.cs` (entity ID prefixes), `IdGenerator` (PrettyId K-sortable), `Enums/` (`BackupTypeEnum`, `StorageTargetTypeEnum`).
- [x] Four Touchstone test projects wired; first `Identifier` suite (5 cases, positive + negative) passes through the console runner, xUnit, and NUnit on net8/net10.

### Phase 1 — Configuration and data layer ✅
- [x] `ArmorSettings` (+ `LoggingSettings`, `ChunkingSettings`) + `SettingsManager` load/save/env overrides (injectable reader)/validation/clamping; `ArmorPaths` + first-run `~/.armor` bootstrap.
- [x] `DatabaseDriverBase`, `DatabaseDriverFactory`, `SchemaMigration`, `SqliteDatabaseDriver` with WAL + busy timeout + `SemaphoreSlim` write serialization.
- [x] Migrations for all core tables (`SqliteMigrations`) + `schema_migrations` tracking; per-policy state tables created on demand.
- [x] All eight `*Methods` interfaces + SQLite implementations; handwritten SQL via `Sanitizer`, rows mapped via `Converters`.
- [x] Domain models (`Policy` + children, `Schedule`, `StorageTarget`, `EncryptionKey`, `BackupJob`, `RestoreJob`, `ChunkIndexEntry`, `PolicyStateEntry`) and domain exceptions.
- [x] Configuration suite (8 cases) + data-layer suite (11 cases), positive + negative — all green through the console runner. 24 cases total across Phases 0–1.

> **Note:** `Microsoft.Data.Sqlite` transitively pulls a `SQLitePCLRaw` native library flagged by NuGet audit (NU1903). It is kept as a visible warning (not build-blocking) and tracked for the Phase 8 hardening pass to pin a patched release.

### Phase 2 — Crypto and keystore ✅
- [x] `AesGcmCipher` (versioned nonce/tag/ciphertext frame, AAD-bound); `Pbkdf2KeyDeriver` (PBKDF2-HMAC-SHA256, BCL); `KeyMaterial` (data-key + key-file KEK derivation).
- [x] `Keystore` provision/unlock: one random data key wrapped by passphrase and/or key file; either recovers it. `ProvisionedKey` result. Algorithm/params carried on the `EncryptionKey` entry for format agility.
- [x] `CredentialProtector` — storage-target secrets encrypted at rest under a local, owner-only data-protection key.
- [x] Crypto suite (17 cases): GCM round-trip/empty/wrong-key/tamper/wrong-AAD/truncated/bad-key-length; PBKDF2 determinism; keystore passphrase/key-file/both round-trips and wrong-secret failures; credential protect/tamper. 41 cases total; all green.

> Repo-header mirroring onto the target (for disaster recovery) is written by the storage layer in Phase 3, where the target repository is created.

### Phase 3 — Chunk store and storage repository ✅
- [x] `FastCdc` content-defined chunker (deterministic gear table, sync + async); `Compressor` (Deflate/Brotli, keeps smallest); `Hasher` (SHA-256); `ChunkFramer` (compress→encrypt→frame, unframe with hash re-verification).
- [x] `Manifest`/`ManifestFileEntry`/`RepositoryHeader` models; `RepositoryKeys` key scheme (`chunks/ab/hash`, `manifests/{policy}/{job}`, `armor.repo.json`).
- [x] `IStorageRepository` + `BlobStorageRepository` (Blobject-backed, repository-root prefixing) + `StorageRepositoryFactory` for all six provider types; connection validation.
- [x] Chunk-store suite (10 cases: determinism, reassembly, empty/small-file edges, boundary resilience, compression, framer round-trip/tamper/wrong-hash) + storage suite (8 cases incl. a full chunk backup-and-restore and manifest round-trip on disk). 58 cases total; all green.
- [x] All Blobject adapters (Disk/CIFS/NFS/S3/Azure/GCS) build into the factory; disk is the deterministic default, other providers selected via `ARMOR_TEST_STORAGE_TYPE` + `ARMOR_TEST_*` env vars.

### Phase 4 — Backup and restore engines ✅
- [x] `FileEnumerator` + `ExcludeMatcher` (wildcard/regex, file/dir, directory pruning) + size bounds; `ChangeDetector` (size/timestamp + optional Windows archive bit, opt-in per policy).
- [x] `BackupEngine` (full/incremental/differential): baseline-manifest diffing, per-target dedup via chunk index, manifest + repo-header write, per-policy state updates, job stats, cancellation.
- [x] `ManifestCodec` (compress + encrypt manifests); `RestoreEngine` (all/folder/file scopes, alternate destination root) + standalone `VerifyAsync`.
- [x] Engine suite (11 cases): full/incremental/differential backup→restore byte-identity, dedup, single-file/folder restore, verify success + corruption failure, restore-abort on corrupted chunk, exclude patterns, size bounds, empty file. 69 console cases; 70/70 through xUnit + NUnit on net8/net10.
- [x] Fixed a parallel-runner flake by raising the minimum id length to 16 (collision-free at any allowed length).

### Phase 5 — Retention, scheduling, self-backup ✅
- [x] `RetentionManager` prune + mark-and-sweep GC: decrements per-manifest chunk refs, always keeps the newest point, deletes only chunks no surviving manifest references. Restore-safety invariant asserted (surviving point verifies + restores byte-identically after a pass).
- [x] `CronSchedule` (five-field parser + `Matches`/`NextOccurrenceUtc`); `RunLock` + `RunLockHandle` cross-process per-policy exclusion via an OS file lock.
- [x] `ConfigBackup` — config + database + state directory export to / import from a single zip.
- [x] Scheduling suite (5 cases), retention suite (2 cases incl. invariant), self-backup suite (2 cases). 78 console cases; 79/79 through xUnit + NUnit on net8/net10.

### Phase 6 — Agent and tray ✅
- [x] Service layer in `Armor.Core` (`ArmorContext`, `EncryptionKeyService`, `StorageTargetService` with credential protection, `BackupService`, `RestoreService`, `SchedulerService`) — 3 service integration tests (backup/restore, secret-at-rest, scheduler tick).
- [x] `Armor.Agent` (Avalonia, `WinExe`): `AgentHost` scheduler loop + retention (via `BackupService` `runRetention`), unattended key unlock from `state/keys/<keyId>.key`.
- [x] Avalonia tray with About / Open / Status / Exit and `logo.ico`; `AboutWindow`; `TerminalLauncher` for platform terminal launch of the TUI; live status wiring. Builds clean on net8/net10.

### Phase 7 — TUI ✅
- [x] `Armor.Tui` (TUIKit): dock-shell (menubar / main / status) with menu-bar navigation.
- [x] Menus and handlers for Policies (list/create/run backup), Jobs (list), Schedules (list/create), Targets (list/create disk/validate), Keys (list/create), Restore (point-in-time, alternate destination), Maintenance (export self-backup), Help/About — all driving the tested service layer. Builds clean on net8/net10.

> The TUI controller is a thin shell over the service layer, which is covered by the Service suite; interactive rendering is manual-smoke-tested, consistent with the coverage stance in [§18](#18-testing-strategy-and-coverage).

### Phase 8 — Hardening and docs ✅
- [x] Coverage pass: added a `Coverage` suite closing deterministic gaps (enumerations, updates, disposal, change detection, path resolution, exceptions, codecs, scheduler branches). **87.8% line / 67.3% branch on `Armor.Core`**; remaining gap is live-provider arms, UI shells, and error/cancellation branches (see the coverage stance in [§18](#18-testing-strategy-and-coverage)).
- [x] README enriched (logo + Flaticon attribution, getting-started walkthrough, provider-setup table, disaster-recovery paths); CHANGELOG rewritten for the 0.1.0 scope.
- [x] Whole solution builds clean on **net8.0 and net10.0** with zero compiler warnings (only the transitive NU1903 audit advisory remains, non-blocking).
- [x] CI workflow (`.github/workflows/tests.yml`): build + console runner + xUnit + NUnit on Ubuntu/Windows/macOS with .NET 8 and 10.
- [ ] Follow-up (not blocking): pin a patched `SQLitePCLRaw` when one ships (NU1903); fault-injection tests for error/cancellation branches; Argon2id option; distribution signing/notarization.

---

## 20. README, licensing, and housekeeping

- **README.md** — includes the logo and the required attribution. Use the logo at `assets/logo.png` and this exact attribution block:

  ```html
  <a href="https://www.flaticon.com/free-icons/armor" title="armor icons">Armor icons created by Smashicons - Flaticon</a>
  ```

  README content follows `WRITING_DOCUMENTS.md` voice (specific, owned, not template-shaped) and covers what Armor is, the threat/backup model, the chunk-store + encryption architecture, supported targets, and a getting-started + disaster-recovery walkthrough. Assets are referenced by explicit repo URLs (`https://github.com/jchristn/Armor/blob/main/assets/logo.png?raw=true`).
- **LICENSE.md** — MIT.
- **CHANGELOG.md** — seeded and maintained per release.
- **.gitignore** — standard .NET plus `~/.armor` artifacts that might land in the tree during dev.

---

## 21. Resolved decisions

These were the outstanding questions from planning. All are now settled.

1. **Docker artifacts — omitted.** `.dockerignore`, `DOCKERHUB_README.md`, and compose files are not produced; Armor ships as a desktop application, not a container image.
2. **Test provider selection — CLI-driven.** Harnesses accept CLI arguments (storage type, endpoint URLs, credentials, and any other required material). Supplied → test against that provider; not supplied → test against a temporary directory and clean up afterward. Storage suites must cover **both backup and restore**. See [§18](#18-testing-strategy-and-coverage).
3. **KDF — PBKDF2 (BCL), no Argon2 dependency.** Passphrase derivation uses PBKDF2-HMAC-SHA256 from the BCL with a high, documented iteration count. Fewer dependencies wins; Argon2id remains a future option enabled by the repo-header agility below.
4. **Format/algorithm agility — yes.** The repo header records the format version, cipher, KDF name, and KDF parameters so future format changes remain restorable.
5. **Compression — BCL only.** Deflate/Brotli from `System.IO.Compression`; no native/zstd dependency in v1.

### Deferred (not in scope for this plan)

6. **Distribution / code-signing.** macOS notarization and Windows code-signing are acknowledged and will be tackled later, before any public release.
```
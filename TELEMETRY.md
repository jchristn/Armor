# Telemetry

Armor emits metrics, traces, and logs so an operator can answer two questions from Grafana without
reading the source: **where did a backup's time go**, and **what failed**. This document is the
reference for every telemetry point: the source names, the metric and span catalogs, configuration, the
bundled Grafana stack, and recommended alerts.

## Contents

1. [How it fits together](#how-it-fits-together)
2. [Quick start](#quick-start)
3. [Sources and how to subscribe](#sources-and-how-to-subscribe)
4. [Configuration](#configuration)
5. [Metrics catalog](#metrics-catalog)
6. [Label values](#label-values)
7. [Spans catalog](#spans-catalog)
8. [Logs](#logs)
9. [Dashboards](#dashboards)
10. [Recommended alerts](#recommended-alerts)
11. [Cardinality, privacy, and cost](#cardinality-privacy-and-cost)
12. [Testing](#testing)

## How it fits together

Armor is both a library and an application, and the telemetry is split the same way.

- **`Armor.Core` (the engine) emits.** It records into one `System.Diagnostics.Metrics.Meter` and one
  `System.Diagnostics.ActivitySource`, both named **`Armor`**. It takes no exporter or SDK dependency;
  when nothing subscribes, every recording is a near-zero-cost no-op. All names live in
  `src/Armor.Core/Telemetry/TelemetryNames.cs`.
- **`Armor.Telemetry` (the host) exports.** The agent and the TUI each start exactly one
  `TelemetryHost` at their composition root. It wraps a [Radiant](https://www.nuget.org/packages/Radiant)
  0.1.2 host that subscribes to the `Armor` meter and activity source, adds .NET runtime and process
  metrics, and exports over OTLP (and optionally an in-process Prometheus endpoint and Loki). It also
  mirrors every Armor log line into the log pipeline, stamped with the active trace and span ids. Export
  is **off by default**; see [Configuration](#configuration).
- **Watson is not used.** Armor has no HTTP server, so there is no `Watson` meter to subscribe to; every
  unit of work (a scheduled tick, a backup, a restore) opens its own root span instead.

Because the agent and TUI run on the host (they are desktop processes, not containers), the bundled stack
is push-based:

```
Armor agent / TUI --OTLP--> OpenTelemetry Collector --> Prometheus (metrics)
   (127.0.0.1:4317)                                  --> Tempo      (traces)
                                                     --> Loki       (logs)
                                                Grafana reads all three
```

## Quick start

1. Start the stack (Docker Compose v2):

   ```
   docker compose -f docker/compose.yaml up -d
   ```

2. Turn export on, either in `~/.armor/armor.json`:

   ```json
   "Telemetry": { "Enabled": true }
   ```

   or with the environment variable `ARMOR_TELEMETRY_ENABLED=true`.

3. Restart the agent (quit it from the tray; opening the TUI relaunches it) and the TUI.

4. Open Grafana at `http://localhost:3000` (`admin` / `admin` locally) and the **Armor** folder.

| Service | URL | Credentials |
| --- | --- | --- |
| Grafana | `http://localhost:3000` | `admin` / `admin` (local default; set `GRAFANA_ADMIN_PASSWORD`) |
| Prometheus | `http://localhost:9090` | none |
| Tempo API | `http://localhost:3200` | none |
| Loki API | `http://localhost:3100` | none |
| OTLP (collector) | `127.0.0.1:4317` gRPC, `127.0.0.1:4318` HTTP | none |

Every port is published on `127.0.0.1` only. If a port collides with another stack, move it with
`ARMOR_GRAFANA_PORT`, `ARMOR_PROMETHEUS_PORT`, `ARMOR_TEMPO_PORT`, `ARMOR_LOKI_PORT`,
`ARMOR_OTLP_GRPC_PORT`, or `ARMOR_OTLP_HTTP_PORT` (and point `Telemetry.OtlpEndpoint` at the new OTLP
port). `docker/update.sh` (or `docker/update.bat`) pulls the pinned images and recreates the stack
without touching its volumes.

## Sources and how to subscribe

| Kind | Name | Defined in |
| --- | --- | --- |
| Meter | `Armor` | `TelemetryNames.MeterName` |
| ActivitySource | `Armor` | `TelemetryNames.ActivitySourceName` |

Both carry the Armor version (`0.2.0` today). The names are a public contract: dashboards and alerts
depend on them.

**Radiant** (what `Armor.Telemetry` does):

```csharp
RadiantSettings settings = new RadiantSettings("armor-agent");
settings.Sources.AddMeter("Armor");
settings.Sources.AddActivitySource("Armor");
using (RadiantHost host = RadiantHost.Start(settings)) { /* run */ }
```

**OpenTelemetry SDK directly:**

```csharp
using MeterProvider meters = Sdk.CreateMeterProviderBuilder().AddMeter("Armor").AddOtlpExporter().Build();
using TracerProvider traces = Sdk.CreateTracerProviderBuilder().AddSource("Armor").AddOtlpExporter().Build();
```

**Ad hoc, no code:** `dotnet-counters monitor --counters Armor -n Armor.Agent`.

## Configuration

The `Telemetry` section of `armor.json`. Defaults are safe for an end-user desktop: nothing is exported,
no port is opened, and no connection is attempted until `Enabled` is true.

| Key | Default | Notes |
| --- | --- | --- |
| `Enabled` | `false` | Master switch for export. Emission into the BCL meter and source is always on and costs nothing unobserved. |
| `ServiceName` | `armor` | Base `service.name`; each process appends its role: `armor-agent`, `armor-tui`. Becomes the Prometheus `job` label. |
| `OtlpEnabled` | `true` | Push metrics, traces, and logs over OTLP. |
| `OtlpEndpoint` | `http://127.0.0.1:4317` | Collector endpoint. Use `:4318` with `OtlpProtocol` `httpprotobuf`. |
| `OtlpProtocol` | `grpc` | `grpc` or `httpprotobuf`; anything else is treated as `grpc`. |
| `LogsEnabled` | `true` | Mirror Armor log lines into the log pipeline (OTLP, and Loki if enabled). |
| `PrometheusEnabled` | `false` | Serve an in-process `/metrics` scrape endpoint. |
| `PrometheusHostname` | `127.0.0.1` | Interface for the scrape endpoint (anonymous; keep it private). |
| `AgentPrometheusPort` | `9464` | Agent scrape port (1 to 65535). |
| `TuiPrometheusPort` | `9465` | TUI scrape port, distinct so both can run. |
| `LokiEnabled` | `false` | Also push logs straight to Loki (the bundled collector already forwards them). |
| `LokiEndpoint` | `http://127.0.0.1:3100/otlp` | Loki OTLP base endpoint. |
| `TraceSamplingRatio` | `1.0` | Parent-based root sampling, 0 to 1. |
| `MetricsExportIntervalMs` | `15000` | OTLP metric push interval, 1000 to 300000. |
| `TraceChunkOperations` | `false` | Give every chunk-level storage call its own client span. Off because a large backup makes millions of chunk calls; turn on briefly to diagnose chunk latency. Applies to any listener. |

Environment overrides (applied after `armor.json`):

| Variable | Sets |
| --- | --- |
| `ARMOR_TELEMETRY_ENABLED` | `Enabled` |
| `ARMOR_TELEMETRY_OTLP_ENDPOINT` | `OtlpEndpoint` |
| `ARMOR_TELEMETRY_OTLP_PROTOCOL` | `OtlpProtocol` |
| `ARMOR_TELEMETRY_PROMETHEUS_ENABLED` | `PrometheusEnabled` |
| `ARMOR_TELEMETRY_LOKI_ENABLED` | `LokiEnabled` |
| `ARMOR_TELEMETRY_TRACE_CHUNKS` | `TraceChunkOperations` |

Telemetry is best-effort. A bad endpoint or a busy port logs a warning and leaves Armor running without
export; a failing listener never affects a backup.

## Metrics catalog

Instrument names are dotted OpenTelemetry names. The Prometheus column is the name as it lands in
Prometheus through the bundled collector (verified against the running stack); label keys are rewritten
the same way (`armor.outcome` becomes `armor_outcome`, `error.type` becomes `error_type`). Every series
also carries `job` (`armor-agent` or `armor-tui`) and `instance` (the process instance id). Durations are
in seconds and histograms export buckets, so take quantiles in PromQL with `histogram_quantile`.

### Backup pipeline

| Prometheus name | Instrument | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- | --- |
| `armor_backup_jobs_total` | `armor.backup.jobs` | counter | `{job}` | `armor_backup_type`, `armor_outcome`, `error_type` (failures) | Engine backup jobs by type and outcome. |
| `armor_backup_duration_seconds` | `armor.backup.duration` | histogram | s | `armor_backup_type`, `armor_outcome` | End-to-end engine job duration. |
| `armor_backup_jobs_active` | `armor.backup.jobs.active` | up-down counter | `{job}` | `armor_backup_type` | Jobs running now. |
| `armor_backup_stage_total` | `armor.backup.stage` | counter | `{stage}` | `armor_stage`, `armor_outcome`, `error_type` (failures) | Stage executions. Stages: `prepare`, `open`, `header`, `baseline`, `scan`, `process`, `manifest`, `finalize`, `retention`. |
| `armor_backup_stage_duration_seconds` | `armor.backup.stage.duration` | histogram | s | `armor_stage`, `armor_outcome` | Stage duration. |
| `armor_backup_file_stage_duration_seconds` | `armor.backup.file.stage.duration` | histogram | s | `armor_stage` | Per-file and per-chunk work inside `process`: `queued` (wait for a worker), `read`, `hash`, `frame` (compress and encrypt), `dedupe_check`, `upload`, `commit`. |
| `armor_backup_files_total` | `armor.backup.files` | counter | `{file}` | `armor_file_outcome` | Files settled: `copied`, `unchanged`, `skipped`, `vanished`. |
| `armor_backup_files_scanned_total` | `armor.backup.files.scanned` | counter | `{file}` | none | Files found by the scan. |
| `armor_backup_bytes_total` | `armor.backup.bytes` | counter | By | `armor_bytes_kind` | `scanned`, `processed`, `stored`, `deduplicated`, `skipped`. |
| `armor_backup_chunks_total` | `armor.backup.chunks` | counter | `{chunk}` | `armor_chunk_outcome` | `written`, `deduplicated_run`, `deduplicated_target`, `deduplicated_baseline`. |
| `armor_backup_workers_active` | `armor.backup.workers.active` | up-down counter | `{worker}` | none | Copy workers busy on a file. |
| `armor_backup_workers_capacity` | `armor.backup.workers.capacity` | up-down counter | `{worker}` | none | Worker slots provisioned by running jobs. |
| `armor_backup_queue_depth` | `armor.backup.queue.depth` | up-down counter | `{file}` | none | Files waiting in the hand-off queue. |
| `armor_backup_queue_capacity` | `armor.backup.queue.capacity` | up-down counter | `{file}` | none | Queue capacity provisioned by running jobs. |
| `armor_backup_lock_rejections_total` | `armor.backup.lock.rejections` | counter | `{rejection}` | none | Requests refused because the policy was already running. |
| `armor_backup_last_success_timestamp_seconds` | `armor.backup.last_success.timestamp` | gauge | s | none | Unix time of the newest successful backup; seeded from the database at startup. |

### Restore, verify, retention

| Prometheus name | Instrument | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- | --- |
| `armor_restore_jobs_total` | `armor.restore.jobs` | counter | `{job}` | `armor_outcome`, `error_type` | Restore jobs. |
| `armor_restore_duration_seconds` | `armor.restore.duration` | histogram | s | `armor_outcome` | Restore duration. |
| `armor_restore_jobs_active` | `armor.restore.jobs.active` | up-down counter | `{job}` | none | Restores running now. |
| `armor_restore_stage_total` | `armor.restore.stage` | counter | `{stage}` | `armor_stage`, `armor_outcome`, `error_type` | Stages: `open`, `restore`, `finalize`. |
| `armor_restore_stage_duration_seconds` | `armor.restore.stage.duration` | histogram | s | `armor_stage`, `armor_outcome` | Restore stage duration. |
| `armor_restore_file_stage_duration_seconds` | `armor.restore.file.stage.duration` | histogram | s | `armor_stage` | `download`, `decrypt`, `write` per chunk. |
| `armor_restore_files_total` | `armor.restore.files` | counter | `{file}` | none | Files restored. |
| `armor_restore_bytes_total` | `armor.restore.bytes` | counter | By | none | Plaintext bytes restored. |
| `armor_verify_runs_total` | `armor.verify.runs` | counter | `{run}` | `armor_outcome`, `error_type` | Verification runs. |
| `armor_verify_duration_seconds` | `armor.verify.duration` | histogram | s | `armor_outcome` | Verification duration. |
| `armor_verify_chunks_total` | `armor.verify.chunks` | counter | `{chunk}` | `armor_chunk_outcome` | `verified`, `missing`, `corrupt`. |
| `armor_retention_runs_total` | `armor.retention.runs` | counter | `{run}` | `armor_outcome`, `error_type` | Retention runs. |
| `armor_retention_duration_seconds` | `armor.retention.duration` | histogram | s | `armor_outcome` | Retention duration. |
| `armor_retention_stage_total` | `armor.retention.stage` | counter | `{stage}` | `armor_stage`, `armor_outcome`, `error_type` | Stages: `prune`, `sweep`. |
| `armor_retention_stage_duration_seconds` | `armor.retention.stage.duration` | histogram | s | `armor_stage`, `armor_outcome` | Retention stage duration. |
| `armor_retention_jobs_pruned_total` | `armor.retention.jobs.pruned` | counter | `{job}` | none | Points-in-time pruned. |
| `armor_retention_chunks_deleted_total` | `armor.retention.chunks.deleted` | counter | `{chunk}` | none | Unreferenced chunks deleted. |

### Scheduler (background worker)

| Prometheus name | Instrument | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- | --- |
| `armor_scheduler_ticks_total` | `armor.scheduler.ticks` | counter | `{tick}` | `armor_outcome`, `error_type` | Scheduler ticks. |
| `armor_scheduler_tick_duration_seconds` | `armor.scheduler.tick.duration` | histogram | s | `armor_outcome` | Tick duration, including the backups it ran. |
| `armor_scheduler_decisions_total` | `armor.scheduler.decisions` | counter | `{decision}` | `armor_scheduler_decision` | Per-schedule decision each tick. |
| `armor_scheduler_schedules_pending` | `armor.scheduler.schedules.pending` | gauge | `{schedule}` | none | Schedules due on the last tick that were left due. |
| `armor_scheduler_last_tick_timestamp_seconds` | `armor.scheduler.last_tick.timestamp` | gauge | s | none | Unix time the last tick finished (agent liveness). |
| `armor_startup_reconciled_jobs_total` | `armor.startup.reconciled_jobs` | counter | `{job}` | none | Interrupted jobs closed out at startup. |

### Integrations

| Prometheus name | Instrument | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- | --- |
| `armor_storage_operations_total` | `armor.storage.operations` | counter | `{operation}` | `armor_storage_type`, `armor_storage_operation`, `armor_outcome`, `error_type` (failures) | Every storage-target call. |
| `armor_storage_operation_duration_seconds` | `armor.storage.operation.duration` | histogram | s | `armor_storage_type`, `armor_storage_operation`, `armor_outcome` | Storage call latency. |
| `armor_storage_operations_active` | `armor.storage.operations.active` | up-down counter | `{operation}` | `armor_storage_type` | Storage calls in flight. |
| `armor_storage_bytes_total` | `armor.storage.bytes` | counter | By | `armor_storage_type`, `armor_storage_direction` | Bytes `sent` and `received`. |
| `armor_key_operations_total` | `armor.key.operations` | counter | `{operation}` | `armor_key_operation`, `armor_outcome`, `error_type` | `provision`, `unlock_passphrase`, `unlock_keyfile`. |
| `armor_key_operation_duration_seconds` | `armor.key.operation.duration` | histogram | s | `armor_key_operation`, `armor_outcome` | Dominated by PBKDF2. |
| `armor_recovery_operations_total` | `armor.recovery.operations` | counter | `{operation}` | `armor_recovery_operation`, `armor_outcome`, `error_type` | Disaster recovery: `open`, `browse`, `list_folders`, `list_files`. |
| `armor_recovery_operation_duration_seconds` | `armor.recovery.operation.duration` | histogram | s | `armor_recovery_operation`, `armor_outcome` | Recovery operation duration. |
| `armor_errors_total` | `armor.errors` | counter | `{error}` | `armor_component`, `error_type` | Every counted failure, by component. |

### Process and configuration

| Prometheus name | Instrument | Type | Unit | Labels | Description |
| --- | --- | --- | --- | --- | --- |
| `armor_build_info` | `armor.build.info` | gauge (1) | none | `armor_version`, `armor_process`, `os_type`, `process_runtime_version` | Build and process identity. |
| `armor_config_scheduler_tick_interval_seconds` | `armor.config.scheduler.tick_interval` | gauge | s | none | Configured tick interval. |
| `armor_config_chunk_size_bytes` | `armor.config.chunk.size` | gauge | By | `armor_chunk_bound` (`min`, `avg`, `max`) | Configured chunk bounds. |
| `armor_config_trace_chunk_operations` | `armor.config.trace_chunk_operations` | gauge | none | none | 1 when per-chunk spans are on. |

The host also exports Radiant's runtime and process instruments (`dotnet_gc_*`, `dotnet_thread_pool_*`,
`dotnet_exceptions_total`, `dotnet_process_cpu_time_seconds_total`,
`dotnet_process_memory_working_set_bytes`, `process_uptime_seconds`, `process_thread_count`, and more).

## Label values

| Label | Values |
| --- | --- |
| `armor_outcome` | `success`, `failure`, `canceled` |
| `error_type` | The exception type name, for example `IOException`, `ArmorStorageException`, `TargetUnreachableException`, `PolicyAlreadyRunningException`, `ArmorCryptoException`; `ValidationMismatch` for a validation probe that read back different bytes. Bounded by the code base. |
| `armor_backup_type` | `full`, `incremental`, `differential` |
| `armor_storage_type` | `disk`, `amazon_s3`, `azure_blob`, `google_cloud`, `cifs`, `nfs` |
| `armor_storage_operation` | `validate`, `write_object`, `read_object`, `object_exists`, `delete_object`, `enumerate`, `write_chunk`, `read_chunk`, `chunk_exists`, `delete_chunk` |
| `armor_scheduler_decision` | `ran`; left due: `key_unavailable`, `target_unreachable`, `already_running`, `failed`; skipped forward: `policy_disabled`; also `disabled`, `initialized`, `not_due` |
| `armor_component` | `backup`, `source` (an unreadable file, skipped), `restore`, `verify`, `retention`, `scheduler`, `storage`, `key`, `recovery` |

Failures inside the engine are counted once by the engine (`armor_backup_jobs_total{armor_outcome="failure"}`
and `armor_errors_total{armor_component="backup"}`). A request refused before the engine starts (missing
policy or key, unreachable target, held run lock) is counted in `armor_errors_total{armor_component="backup"}`
and as a failed `prepare` stage where it fails there, not as a job.

## Spans catalog

All spans come from the `Armor` source. Status is set explicitly: `Ok` on success, `Error` with the
message and an `exception` event (type, message, stack) on failure; a cancellation leaves the status unset
and sets `armor.outcome=canceled`. Context flows through every background hand-off (the scan task and the
copy workers inherit the job span through the execution context), so one backup is one trace.

```
scheduler.tick                          (agent, one per tick)
└─ backup.run                           (BackupService; also a root when started from the TUI or tray)
   ├─ stage:prepare
   │  └─ <type> object_exists           (repository header probe)
   ├─ backup.job                        (BackupEngine)
   │  ├─ stage:open
   │  ├─ stage:header    └─ <type> write_object
   │  ├─ stage:baseline  └─ <type> read_object ...
   │  ├─ stage:scan                     (concurrent with stage:process)
   │  ├─ stage:process   └─ <type> write_chunk ... (only with TraceChunkOperations)
   │  ├─ stage:manifest  └─ <type> write_object ...
   │  └─ stage:finalize  └─ <type> write_object
   └─ stage:retention
      └─ retention.run
         ├─ stage:prune
         └─ stage:sweep
restore.job      ├─ stage:open ├─ stage:restore └─ stage:finalize
verify.job
recovery.open | recovery.browse | recovery.list_folders | recovery.list_files
key.operation
startup.reconcile
<type> <operation>                      (client spans, for example "amazon_s3 write_object")
```

| Span | Kind | Attributes |
| --- | --- | --- |
| `scheduler.tick` | internal | `armor.schedules.ran`, `armor.schedules.pending`, `armor.outcome` |
| `backup.run` | internal | `armor.policy.id`, `armor.job.id`, `armor.backup.type` (when overridden), `armor.storage_target.id`, `armor.storage.type` |
| `backup.job` | internal | `armor.policy.id`, `armor.job.id`, `armor.backup.type`, `armor.storage_target.id`, `armor.backup.resumed`, and on success `armor.files`, `armor.bytes`, `armor.chunks.written`, `armor.chunks.reused`, `armor.files.skipped` |
| `stage:<name>` | internal | `armor.stage`, `armor.outcome`; scan, process, baseline, and manifest add counts |
| `restore.job` | internal | `armor.job.id`, `armor.policy.id`, `armor.restore.scope`, `armor.restore_job.id`, `armor.files`, `armor.bytes` |
| `verify.job` | internal | `armor.job.id`, `armor.policy.id`, `armor.chunks.verified` |
| `retention.run` | internal | `armor.policy.id`, `armor.storage_target.id`, `armor.retention.days`, `armor.retention.jobs_pruned`, `armor.retention.chunks_deleted` |
| `recovery.<operation>` | internal | `armor.recovery.operation` |
| `key.operation` | internal | `armor.key.operation` |
| `startup.reconcile` | internal | `armor.jobs.reconciled` |
| `<type> <operation>` | client | `armor.storage.type`, `armor.storage.operation`, `armor.storage.key` (repository object key or chunk hash), `armor.storage.exists` (exists checks), `armor.storage.keys` (enumerations) |

Every span also carries `armor.outcome`, and failed spans carry `error.type`.

## Logs

Armor's own log (`~/.armor/logs`) is unchanged. With `LogsEnabled`, each line is also exported through the
OpenTelemetry log pipeline and lands in Loki under `service_name` (`armor-agent`, `armor-tui`) with
`severity_text` and `detected_level`. Lines written while a span is active (backup started and finished,
skipped files, failures) carry `trace_id` and `span_id`, so Grafana links a log line to its trace and a
span back to its logs. The agent's work is entirely background work, which is why logs are part of the
stack.

## Dashboards

Grafana provisions an **Armor** folder from `assets/grafana/` (datasource UIDs `prometheus`, `tempo`,
`loki`). Each dashboard has a **Process** variable (`job`) and links to the others.

| Dashboard | UID | Answers |
| --- | --- | --- |
| Armor / Overview | `armor-overview` | Is protection current? Time since last success, failures in 24h, schedules left due, scheduler liveness, errors by component, warning and error logs. Start here. |
| Armor / Backups | `armor-backups` | Where did the time go? Per-stage p95 and time share, per-file stage p95 and share (read vs hash vs frame vs upload vs queued), files, throughput, dedupe, worker and queue saturation, recent and failed backup traces. |
| Armor / Storage & Integrations | `armor-storage` | Is the target the cause? Calls, failures, p50/p95 by target and operation, bytes, in-flight calls, key and recovery operations, slow or failed client spans. |
| Armor / Restore, Verify & Retention | `armor-restore` | Restore stages and file stages, verification results (missing or corrupt chunks), retention prune and sweep. |
| Armor / Scheduler & Agent | `armor-scheduler` | Why didn't a schedule run? Decisions by reason, ticks, pending, last tick, reconciled jobs, configuration, the agent log. |
| Armor / Process & Runtime | `armor-runtime` | CPU, memory, GC, thread pool, exceptions, uptime, build info. |

A typical investigation: **Overview** shows a failed backup, **Backups** shows the `process` stage
failing with `IOException` and the `upload` file stage dominating, **Storage** shows
`amazon_s3 write_chunk` failures and a rising p95, and the failed `backup.run` trace in Tempo shows the
exact stage and exception, with its log lines one click away.

## Recommended alerts

These ship in `docker/prometheus-rules.yaml` and load into the bundled Prometheus. Thresholds assume
daily backups; tune them to your schedules.

| Alert | PromQL | Severity |
| --- | --- | --- |
| ArmorBackupFailed | `sum by (job, armor_backup_type, error_type) (increase(armor_backup_jobs_total{armor_outcome="failure"}[1h])) > 0` | warning |
| ArmorNoRecentBackup | `time() - max by (job) (armor_backup_last_success_timestamp_seconds) > 26 * 3600` for 15m | critical |
| ArmorSchedulesLeftDue | `max by (job) (armor_scheduler_schedules_pending) > 0` for 1h | warning |
| ArmorSchedulerStalled | `time() - max by (job) (armor_scheduler_last_tick_timestamp_seconds{job="armor-agent"}) > 600` for 5m | critical |
| ArmorAgentDown | `absent_over_time(armor_build_info{job="armor-agent"}[15m])` | critical |
| ArmorStorageErrors | `sum by (job, armor_storage_type, armor_storage_operation, error_type) (rate(armor_storage_operations_total{armor_outcome="failure"}[10m])) > 0` for 10m | warning |
| ArmorStorageSlow | `histogram_quantile(0.95, sum by (le, armor_storage_type, armor_storage_operation) (rate(armor_storage_operation_duration_seconds_bucket[10m]))) > 5` for 15m | warning |
| ArmorVerificationFoundDamage | `sum by (job, armor_chunk_outcome) (increase(armor_verify_chunks_total{armor_chunk_outcome=~"missing\|corrupt"}[1h])) > 0` | critical |
| ArmorSourceFilesSkipped | `sum by (job) (increase(armor_backup_files_total{armor_file_outcome="skipped"}[6h])) > 0` | info |

The bundled stack evaluates these but ships no Alertmanager; add one (or use Grafana alerting) to route
notifications.

## Cardinality, privacy, and cost

- **Bounded labels only.** Every metric label comes from a fixed set in `TelemetryNames` or is an
  exception type name. Policy, job, schedule, and target ids, object keys, and file paths never appear on
  a metric; ids and object keys appear only on spans. There is deliberately no per-policy metric: use the
  `backup.run` traces (tagged with `armor.policy.id`) to see a single policy.
- **No secrets or payloads.** No credential, password, key material, or file content is recorded
  anywhere. Span status messages carry exception messages, which can include a repository object key;
  log lines can include local file paths (for example a skipped unreadable file), exactly as the local
  log file already does. Keep the stack on a private network.
- **Cost.** Emission rides `System.Diagnostics` and is effectively free with no listener. With export on,
  per-chunk work records a few histogram points per chunk (tens of nanoseconds each, next to a SHA-256
  and an encryption of the same chunk) and no spans unless `TraceChunkOperations` is on.
- **Per process, reset on restart.** Counters restart with each process; Prometheus keeps the history.
  `armor_backup_last_success_timestamp_seconds` is re-seeded from the database at startup.

## Testing

`src/Test.Shared/TelemetrySuite.cs` (run by every test runner) proves emission with an in-memory BCL
listener (`TelemetryCapture`): the backup job, every stage and file stage, files, bytes, chunks, workers,
and queue; incremental dedupe; restore, verify, retention, scheduler, recovery, keys, and storage calls;
the span tree and parentage; failure paths (storage failure, cancellation, unreadable source, missing
chunk, refused requests, wrong password); the gauges; the settings and environment overrides; and the
exporter host, including a live Prometheus scrape of the in-process endpoint and an inert host when
disabled.

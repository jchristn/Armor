namespace Armor.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Diagnostics;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Service;

    /// <summary>
    /// The agent's background worker. It opens the shared runtime context and loops on the configured
    /// tick interval, running any due schedules. For unattended operation it unlocks data keys from
    /// key files placed under <c>&lt;state&gt;/keys/&lt;keyId&gt;.key</c>; policies whose key is not
    /// available by key file are left for the next tick (they run once a key is provided). Status
    /// changes are raised for the tray to display.
    /// </summary>
    public sealed class AgentHost
    {
        private readonly Dictionary<string, byte[]> _KeyCache = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private CancellationTokenSource? _Cts;
        private ArmorContext? _Context;
        private CancellationToken _Token;

        /// <summary>
        /// Raised when the agent's status text changes.
        /// </summary>
        public event Action<string>? StatusChanged;

        /// <summary>
        /// Start the background loop.
        /// </summary>
        public void Start()
        {
            _Cts = new CancellationTokenSource();
            CancellationToken token = _Cts.Token;
            _ = Task.Run(() => RunLoopAsync(token));
        }

        /// <summary>
        /// Stop the background loop.
        /// </summary>
        public void Stop()
        {
            _Cts?.Cancel();
        }

        /// <summary>
        /// Launch the TUI in a terminal window.
        /// </summary>
        public void LaunchTui()
        {
            TerminalLauncher.LaunchTui();
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            ArmorContext? context = null;
            try
            {
                context = await ArmorContext.CreateAsync(null, token, message => SetStatus(message)).ConfigureAwait(false);
                _Context = context;
                _Token = token;
                await new StartupMaintenance(context).ReconcileInterruptedBackupsAsync(token).ConfigureAwait(false);
                SchedulerService scheduler = new SchedulerService(context);
                int tickSeconds = context.Settings.SchedulerTickSeconds;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        SetStatus("Checking schedules");
                        ArmorContext current = context;
                        int ran = await scheduler.TickAsync(
                            policy => KeyProviderAsync(current, policy, token),
                            DateTime.UtcNow,
                            token,
                            (schedule, ex) => NotifyBackupFailed(null, ex),
                            (schedule, policy, job) => NotifyBackupCompleted(policy, job)).ConfigureAwait(false);
                        SetStatus(ran > 0 ? "Ran " + ran + " backup(s)" : "Idle");
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SetStatus("Error: " + ex.Message);
                    }

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(tickSeconds), token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetStatus("Fatal: " + ex.Message);
            }
            finally
            {
                _Context = null;
                context?.Dispose();
            }
        }

        /// <summary>
        /// List the configured backup policies, for the tray's "Back up now" menu. Returns an empty list
        /// until the runtime context has finished starting, or if the read fails.
        /// </summary>
        /// <returns>The policies, ordered as stored.</returns>
        public async Task<IReadOnlyList<Policy>> ListPoliciesAsync()
        {
            ArmorContext? context = _Context;
            if (context == null)
                return Array.Empty<Policy>();
            try
            {
                return await context.Database.Policies.ReadAllAsync(_Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return Array.Empty<Policy>();
            }
        }

        /// <summary>
        /// Start an interactive backup of a policy, triggered from the tray. It unlocks the policy's data
        /// key from its cached password (the same unattended path the scheduler uses); a policy whose
        /// password is not cached cannot run from the tray and is reported so the user runs it from the app
        /// instead. Completion, failure, already-running, and unreachable-target outcomes are all surfaced
        /// as a status update and a desktop notification. Safe to call from the UI thread; the work runs on
        /// a background task.
        /// </summary>
        /// <param name="policyId">Identifier of the policy to back up. Ignored when null or whitespace.</param>
        public void RunPolicyBackup(string policyId)
        {
            if (String.IsNullOrWhiteSpace(policyId))
                return;
            _ = Task.Run(() => RunPolicyBackupAsync(policyId));
        }

        private async Task RunPolicyBackupAsync(string policyId)
        {
            ArmorContext? context = _Context;
            if (context == null)
            {
                SetStatus("The agent is still starting; try again in a moment.");
                return;
            }

            CancellationToken token = _Token;
            string policyName = policyId;
            try
            {
                Policy? policy = await context.Database.Policies.ReadAsync(policyId, token).ConfigureAwait(false);
                if (policy == null)
                    return;
                policyName = policy.Name;

                if (String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
                {
                    SetStatus("Cannot back up '" + policyName + "': no encryption password is assigned.");
                    DesktopNotifier.Notify("Armor — cannot back up: " + policyName, "No encryption password is assigned to this policy.");
                    return;
                }

                byte[]? dataKey = await KeyProviderAsync(context, policy, token).ConfigureAwait(false);
                if (dataKey == null)
                {
                    SetStatus("Cannot back up '" + policyName + "' from the tray: its password is not cached.");
                    DesktopNotifier.Notify("Armor — cannot back up: " + policyName, "Its encryption password is not cached for unattended use. Open Armor to run it, or unlock the password first.");
                    return;
                }

                SetStatus("Backing up '" + policyName + "' (started from the tray).");
                BackupService service = new BackupService(context);
                BackupJob job = await service.RunAsync(policy.Id, dataKey, null, true, token).ConfigureAwait(false);
                NotifyBackupCompleted(policy, job);
            }
            catch (OperationCanceledException)
            {
                // The agent is shutting down; nothing to report.
            }
            catch (PolicyAlreadyRunningException)
            {
                SetStatus("A backup of '" + policyName + "' is already in progress.");
                DesktopNotifier.Notify("Armor — already running: " + policyName, "A backup of this policy is already in progress.");
            }
            catch (TargetUnreachableException ex)
            {
                SetStatus("Cannot back up '" + policyName + "': " + ex.Message);
                DesktopNotifier.Notify("Armor — cannot back up: " + policyName, ex.Message);
            }
            catch (Exception ex)
            {
                NotifyBackupFailed(policyName, ex);
            }
        }

        private void NotifyBackupCompleted(Policy policy, BackupJob job)
        {
            string summary = BackupJobSummary.OneLine(job);
            SetStatus("Backed up '" + policy.Name + "': " + summary);
            DesktopNotifier.Notify("Armor — backup complete: " + policy.Name, summary);
        }

        private void NotifyBackupFailed(string? policyName, Exception ex)
        {
            string title = String.IsNullOrEmpty(policyName) ? "Armor — backup failed" : "Armor — backup failed: " + policyName;
            SetStatus((String.IsNullOrEmpty(policyName) ? "A scheduled backup failed: " : "Backup of '" + policyName + "' failed: ") + ex.Message);
            DesktopNotifier.Notify(title, ex.Message);
        }

        private async Task<byte[]?> KeyProviderAsync(ArmorContext context, Policy policy, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
                return null;

            byte[]? cached;
            if (_KeyCache.TryGetValue(policy.EncryptionKeyId!, out cached))
                return cached;

            Armor.Core.Models.EncryptionKey? key = await context.Database.EncryptionKeys.ReadAsync(policy.EncryptionKeyId!, token).ConfigureAwait(false);
            EncryptionKeyService keyService = new EncryptionKeyService(context.Database);
            if (key == null || !keyService.CanUnlockUnattended(key, context.Paths))
                return null;

            byte[] dataKey = await keyService.UnlockUnattendedAsync(key.Id, context.Paths, context.CredentialProtector, token).ConfigureAwait(false);
            _KeyCache[key.Id] = dataKey;
            return dataKey;
        }

        private void SetStatus(string status)
        {
            StatusChanged?.Invoke(status);
        }
    }
}

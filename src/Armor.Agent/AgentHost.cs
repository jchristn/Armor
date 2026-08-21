namespace Armor.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
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
                context = await ArmorContext.CreateAsync(null, token).ConfigureAwait(false);
                SchedulerService scheduler = new SchedulerService(context);
                int tickSeconds = context.Settings.SchedulerTickSeconds;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        SetStatus("Checking schedules");
                        ArmorContext current = context;
                        int ran = await scheduler.TickAsync(policy => KeyProviderAsync(current, policy, token), DateTime.UtcNow, token).ConfigureAwait(false);
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
                context?.Dispose();
            }
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

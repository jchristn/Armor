namespace Armor.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Threading.Tasks;
    using Armor.Core.Enums;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Service;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Layout;

    /// <summary>
    /// Drives the Armor TUI. After a startup splash, it runs a modal menu loop: a main menu opens
    /// sub-menus and actions built from the built-in select and prompt dialogs, and an output pane keeps
    /// a running log behind the dialogs. Every operation — managing policies, targets, keys, and
    /// schedules, running backups, and restoring — is reachable from the menus.
    /// </summary>
    public sealed class TuiController
    {
        private readonly ArmorContext _Context;
        private readonly bool _ShowSplash;
        private TuiApplication? _App;
        private Pane? _Main;
        private Pane? _Status;

        /// <summary>
        /// Initializes a new instance of the <see cref="TuiController"/> class.
        /// </summary>
        /// <param name="context">The runtime context. Cannot be null.</param>
        /// <param name="showSplash">When true, the startup splash is shown before the menu. Default is true.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public TuiController(ArmorContext context, bool showSplash = true)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _ShowSplash = showSplash;
        }

        /// <summary>
        /// Configure the application: build the layout and panes, then start the splash and menu loop.
        /// </summary>
        /// <param name="app">The TUI application to configure. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Configure(TuiApplication app)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));

            _App = app;

            app.Layout = Layout.Create()
                .DockTop("title", 1)
                .DockBottom("status", 1)
                .Fill("main")
                .Build();

            Pane title = app.AddPane("title");
            title.WriteLine(" Armor — data protection for the paranoid");
            _Main = app.AddPane("main");
            _Status = app.AddPane("status");

            app.Bind("ctrl+q", app.Quit);

            SetStatus("Starting");
            _ = StartAsync();
        }

        private async Task StartAsync()
        {
            try
            {
                if (_ShowSplash)
                {
                    Version? assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                    string version = assemblyVersion != null
                        ? assemblyVersion.Major + "." + assemblyVersion.Minor + "." + assemblyVersion.Build
                        : "0.1.0";
                    ArmorSplashModal splash = new ArmorSplashModal("Armor", ArmorBanner.SplashLines(version));
                    await App().ShowAsync(splash).ConfigureAwait(false);
                }

                Info("Configuration: " + _Context.Paths.RootDirectory);
                Info("Choose an option from the menu. Press Ctrl+Q at any time to quit.");
                await RunMenuLoopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Info("Fatal: " + ex.Message);
            }
        }

        private async Task RunMenuLoopAsync()
        {
            bool running = true;
            while (running)
            {
                int choice = await App().SelectAsync(
                    "Armor — Main Menu",
                    "Policies",
                    "Storage targets",
                    "Encryption keys",
                    "Run a backup",
                    "Restore files",
                    "Backup jobs",
                    "Schedules",
                    "Maintenance",
                    "About",
                    "Quit").ConfigureAwait(false);

                try
                {
                    switch (choice)
                    {
                        case 0: await PoliciesMenuAsync().ConfigureAwait(false); break;
                        case 1: await TargetsMenuAsync().ConfigureAwait(false); break;
                        case 2: await KeysMenuAsync().ConfigureAwait(false); break;
                        case 3: await RunBackupAsync().ConfigureAwait(false); break;
                        case 4: await RestoreAsync().ConfigureAwait(false); break;
                        case 5: await ListJobsAsync().ConfigureAwait(false); break;
                        case 6: await SchedulesMenuAsync().ConfigureAwait(false); break;
                        case 7: await MaintenanceMenuAsync().ConfigureAwait(false); break;
                        case 8: await ShowAboutAsync().ConfigureAwait(false); break;
                        case 9: running = false; break;
                        default: break;
                    }
                }
                catch (Exception ex)
                {
                    Info("Error: " + ex.Message);
                    SetStatus("Error");
                }
            }

            App().Quit();
        }

        private async Task PoliciesMenuAsync()
        {
            while (true)
            {
                int choice = await App().SelectAsync("Policies", "List policies", "Create policy", "Run a backup", "Back").ConfigureAwait(false);
                if (choice == 0) await ListPoliciesAsync().ConfigureAwait(false);
                else if (choice == 1) await CreatePolicyAsync().ConfigureAwait(false);
                else if (choice == 2) await RunBackupAsync().ConfigureAwait(false);
                else return;
            }
        }

        private async Task TargetsMenuAsync()
        {
            while (true)
            {
                int choice = await App().SelectAsync("Storage targets", "List targets", "Create disk target", "Validate a target", "Back").ConfigureAwait(false);
                if (choice == 0) await ListTargetsAsync().ConfigureAwait(false);
                else if (choice == 1) await CreateDiskTargetAsync().ConfigureAwait(false);
                else if (choice == 2) await ValidateTargetAsync().ConfigureAwait(false);
                else return;
            }
        }

        private async Task KeysMenuAsync()
        {
            while (true)
            {
                int choice = await App().SelectAsync("Encryption keys", "List keys", "Create key", "Back").ConfigureAwait(false);
                if (choice == 0) await ListKeysAsync().ConfigureAwait(false);
                else if (choice == 1) await CreateKeyAsync().ConfigureAwait(false);
                else return;
            }
        }

        private async Task SchedulesMenuAsync()
        {
            while (true)
            {
                int choice = await App().SelectAsync("Schedules", "List schedules", "Create schedule", "Back").ConfigureAwait(false);
                if (choice == 0) await ListSchedulesAsync().ConfigureAwait(false);
                else if (choice == 1) await CreateScheduleAsync().ConfigureAwait(false);
                else return;
            }
        }

        private async Task MaintenanceMenuAsync()
        {
            while (true)
            {
                int choice = await App().SelectAsync("Maintenance", "Export self-backup", "Back").ConfigureAwait(false);
                if (choice == 0) await ExportSelfBackupAsync().ConfigureAwait(false);
                else return;
            }
        }

        private async Task ListPoliciesAsync()
        {
            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            Info("Policies (" + policies.Count + "):");
            foreach (Policy policy in policies)
                Info("  " + policy.Id + "  " + policy.Name + "  [" + policy.BackupType + ", retain " + policy.RetentionDays + "d, " + policy.IncludePaths.Count + " include path(s)]");
            SetStatus("Listed policies");
        }

        private async Task CreatePolicyAsync()
        {
            string? name = await PromptAsync("Policy name").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;

            string? include = await PromptAsync("Include path (file or folder)").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(include))
                return;

            StorageTarget? target = await PickTargetAsync("Select a storage target").ConfigureAwait(false);
            if (target == null)
                return;

            EncryptionKey? key = await PickKeyAsync("Select an encryption key").ConfigureAwait(false);
            if (key == null)
                return;

            int typeIndex = await App().SelectAsync("Backup type", "Full", "Incremental", "Differential").ConfigureAwait(false);
            if (typeIndex < 0)
                return;

            Policy policy = new Policy();
            policy.Name = name!;
            policy.IncludePaths.Add(include!);
            policy.StorageTargetId = target.Id;
            policy.EncryptionKeyId = key.Id;
            policy.BackupType = typeIndex == 1 ? BackupTypeEnum.Incremental : (typeIndex == 2 ? BackupTypeEnum.Differential : BackupTypeEnum.Full);

            await _Context.Database.Policies.CreateAsync(policy).ConfigureAwait(false);
            Info("Created policy " + policy.Id + " (" + policy.Name + ").");
            SetStatus("Policy created");
        }

        private async Task RunBackupAsync()
        {
            Policy? policy = await PickPolicyAsync("Select a policy to back up").ConfigureAwait(false);
            if (policy == null)
                return;
            if (String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
            {
                Info("Policy has no encryption key assigned.");
                return;
            }

            byte[]? dataKey = await UnlockAsync(policy.EncryptionKeyId!).ConfigureAwait(false);
            if (dataKey == null)
                return;

            Info("Backing up '" + policy.Name + "'...");
            SetStatus("Backing up");
            BackupService service = new BackupService(_Context);
            BackupJob job = await Task.Run(() => service.RunAsync(policy.Id, dataKey, null, true)).ConfigureAwait(false);
            Info("Backup " + job.Status + ": " + job.FileCount + " files, " + job.ChunksWritten + " chunks written, " + job.ChunksReused + " reused.");
            SetStatus("Backup " + job.Status);
        }

        private async Task ListJobsAsync()
        {
            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync().ConfigureAwait(false);
            Info("Backup jobs (" + jobs.Count + "):");
            foreach (BackupJob job in jobs)
                Info("  " + job.Id + "  " + job.BackupType + "  " + job.Status + "  files=" + job.FileCount + "  " + (job.CompletedUtc?.ToString("u") ?? "(running)"));
            SetStatus("Listed jobs");
        }

        private async Task ListSchedulesAsync()
        {
            List<Schedule> schedules = await _Context.Database.Schedules.ReadAllAsync().ConfigureAwait(false);
            Info("Schedules (" + schedules.Count + "):");
            foreach (Schedule schedule in schedules)
                Info("  " + schedule.Id + "  policy=" + schedule.PolicyId + "  '" + schedule.CronExpression + "'  " + (schedule.Enabled ? "enabled" : "disabled"));
            SetStatus("Listed schedules");
        }

        private async Task CreateScheduleAsync()
        {
            Policy? policy = await PickPolicyAsync("Select a policy to schedule").ConfigureAwait(false);
            if (policy == null)
                return;

            string? cron = await PromptAsync("Cron expression (min hour dom month dow)", "0 2 * * *").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(cron))
                return;

            Schedule schedule = new Schedule();
            schedule.PolicyId = policy.Id;
            schedule.CronExpression = cron!;
            await _Context.Database.Schedules.CreateAsync(schedule).ConfigureAwait(false);
            Info("Created schedule " + schedule.Id + " for policy " + policy.Name + ".");
            SetStatus("Schedule created");
        }

        private async Task ListTargetsAsync()
        {
            List<StorageTarget> targets = await _Context.Database.StorageTargets.ReadAllAsync().ConfigureAwait(false);
            Info("Storage targets (" + targets.Count + "):");
            foreach (StorageTarget target in targets)
                Info("  " + target.Id + "  " + target.Name + "  [" + target.Type + "]");
            SetStatus("Listed targets");
        }

        private async Task CreateDiskTargetAsync()
        {
            string? name = await PromptAsync("Target name").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;
            string? path = await PromptAsync("Local directory path").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(path))
                return;

            StorageTarget target = new StorageTarget();
            target.Name = name!;
            target.Type = StorageTargetTypeEnum.Disk;
            target.DiskPath = path!;

            StorageTargetService service = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
            await service.CreateAsync(target).ConfigureAwait(false);
            Info("Created disk target " + target.Id + " at " + path + ".");
            SetStatus("Target created");
        }

        private async Task ValidateTargetAsync()
        {
            StorageTarget? target = await PickTargetAsync("Select a target to validate").ConfigureAwait(false);
            if (target == null)
                return;

            SetStatus("Validating");
            StorageTargetService service = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
            bool ok = await Task.Run(() => service.ValidateAsync(target.Id)).ConfigureAwait(false);
            Info("Validation of '" + target.Name + "': " + (ok ? "succeeded" : "failed") + ".");
            SetStatus(ok ? "Target valid" : "Target invalid");
        }

        private async Task ListKeysAsync()
        {
            List<EncryptionKey> keys = await _Context.Database.EncryptionKeys.ReadAllAsync().ConfigureAwait(false);
            Info("Encryption keys (" + keys.Count + "):");
            foreach (EncryptionKey key in keys)
                Info("  " + key.Id + "  " + key.Name + "  [" + (key.UsesPassphrase ? "passphrase" : "") + (key.UsesKeyFile ? " keyfile" : "") + "]");
            SetStatus("Listed keys");
        }

        private async Task CreateKeyAsync()
        {
            string? name = await PromptAsync("Key name").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;
            string? passphrase = await PromptAsync("Passphrase").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(passphrase))
                return;

            EncryptionKeyService service = new EncryptionKeyService(_Context.Database);
            ProvisionedKey provisioned = await service.ProvisionAsync(name!, passphrase, null, 600000).ConfigureAwait(false);
            Info("Created encryption key " + provisioned.Key.Id + " (" + provisioned.Key.Name + ").");
            SetStatus("Key created");
        }

        private async Task RestoreAsync()
        {
            BackupJob? job = await PickJobAsync("Select a point-in-time to restore").ConfigureAwait(false);
            if (job == null)
                return;

            Policy? policy = await _Context.Database.Policies.ReadAsync(job.PolicyId).ConfigureAwait(false);
            if (policy == null || String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
            {
                Info("Cannot resolve the policy or key for this point-in-time.");
                return;
            }

            byte[]? dataKey = await UnlockAsync(policy.EncryptionKeyId!).ConfigureAwait(false);
            if (dataKey == null)
                return;

            string? destination = await PromptAsync("Destination root (blank to restore in place)").ConfigureAwait(false);

            RestoreJob restoreJob = new RestoreJob();
            restoreJob.BackupJobId = job.Id;
            restoreJob.Scope = RestoreScopeEnum.All;
            restoreJob.DestinationRoot = String.IsNullOrWhiteSpace(destination) ? null : destination;

            SetStatus("Restoring");
            RestoreService service = new RestoreService(_Context);
            RestoreJob done = await Task.Run(() => service.RunAsync(restoreJob, dataKey)).ConfigureAwait(false);
            Info("Restore " + done.Status + ": " + done.FilesRestored + " files, " + done.BytesRestored + " bytes.");
            SetStatus("Restore " + done.Status);
        }

        private async Task ExportSelfBackupAsync()
        {
            string defaultPath = Path.Combine(_Context.Paths.RootDirectory, "armor-selfbackup.zip");
            string? destination = await PromptAsync("Self-backup zip path", defaultPath).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(destination))
                return;

            string databaseFile = _Context.Settings.DatabaseFilename ?? _Context.Paths.DefaultDatabasePath;
            SetStatus("Exporting");
            await Task.Run(() => Armor.Core.Backup.ConfigBackup.ExportAsync(_Context.Paths.ConfigFilePath, databaseFile, _Context.Paths.StateDirectory, destination!)).ConfigureAwait(false);
            Info("Self-backup written to " + destination + ".");
            SetStatus("Self-backup exported");
        }

        private Task ShowAboutAsync()
        {
            Info("Armor 0.1.0 — Data protection for the paranoid.");
            Info("https://github.com/jchristn/Armor");
            Info("Configuration: " + _Context.Paths.RootDirectory);
            SetStatus("About");
            return Task.CompletedTask;
        }

        private async Task<byte[]?> UnlockAsync(string keyId)
        {
            string? passphrase = await PromptAsync("Passphrase to unlock the encryption key").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(passphrase))
                return null;

            try
            {
                EncryptionKeyService service = new EncryptionKeyService(_Context.Database);
                return await service.UnlockWithPassphraseAsync(keyId, passphrase!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Info("Unlock failed: " + ex.Message);
                return null;
            }
        }

        private async Task<Policy?> PickPolicyAsync(string title)
        {
            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            return await PickAsync(title, policies, policy => policy.Name + " (" + policy.Id + ")").ConfigureAwait(false);
        }

        private async Task<StorageTarget?> PickTargetAsync(string title)
        {
            List<StorageTarget> targets = await _Context.Database.StorageTargets.ReadAllAsync().ConfigureAwait(false);
            return await PickAsync(title, targets, target => target.Name + " [" + target.Type + "]").ConfigureAwait(false);
        }

        private async Task<EncryptionKey?> PickKeyAsync(string title)
        {
            List<EncryptionKey> keys = await _Context.Database.EncryptionKeys.ReadAllAsync().ConfigureAwait(false);
            return await PickAsync(title, keys, key => key.Name + " (" + key.Id + ")").ConfigureAwait(false);
        }

        private async Task<BackupJob?> PickJobAsync(string title)
        {
            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync().ConfigureAwait(false);
            return await PickAsync(title, jobs, job => job.BackupType + " " + (job.CompletedUtc?.ToString("u") ?? job.Id) + " (" + job.Id + ")").ConfigureAwait(false);
        }

        private async Task<T?> PickAsync<T>(string title, List<T> items, Func<T, string> label) where T : class
        {
            if (items.Count == 0)
            {
                Info("Nothing to choose from for: " + title);
                return null;
            }

            string[] options = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
                options[i] = label(items[i]);

            int index = await App().SelectAsync(title, options).ConfigureAwait(false);
            if (index < 0 || index >= items.Count)
                return null;
            return items[index];
        }

        private Task<string?> PromptAsync(string title, string initial = "")
        {
            return App().PromptAsync(title, initial);
        }

        private TuiApplication App()
        {
            if (_App == null)
                throw new InvalidOperationException("The application has not been configured.");
            return _App;
        }

        private void Info(string line)
        {
            _Main?.WriteLine(line);
        }

        private void SetStatus(string status)
        {
            _Status?.Clear();
            _Status?.WriteLine(" " + status);
        }
    }
}

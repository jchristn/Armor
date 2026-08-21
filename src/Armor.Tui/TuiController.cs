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
    using Armor.Tui.Widgets;
    using TUIKit.Content;
    using TUIKit.Hosting;

    /// <summary>
    /// Drives the Armor TUI as a persistent dashboard. A left nav lists the sections (policies,
    /// storage targets, encryption keys, backup jobs, schedules); the main pane shows the selected
    /// section as a live table; a header names the section and a status bar shows key hints and the
    /// current background activity. Navigation is non-blocking: long operations (backups, restores,
    /// validation) run on a background task and report progress back to the status bar, so the UI stays
    /// responsive while they run.
    /// </summary>
    public sealed class TuiController
    {
        private enum Section
        {
            Policies = 0,
            Targets = 1,
            Keys = 2,
            Jobs = 3,
            Schedules = 4,
            Recover = 5,
        }

        // Sentinel tag for the "back to locations" row shown while browsing a recovery catalog.
        private sealed class RecoverBackRow
        {
            public static readonly RecoverBackRow Instance = new RecoverBackRow();

            private RecoverBackRow()
            {
            }
        }

        private const int NavWidth = 20;

        private readonly ArmorContext _Context;
        private readonly bool _ShowSplash;

        private TuiApplication? _App;
        private Pane? _Log;
        private SectionTableView? _Nav;
        private SectionTableView? _Content;
        private Section _Current = Section.Policies;
        private bool _Busy;
        private RecoverySession? _RecoverSession;

        /// <summary>
        /// Initializes a new instance of the <see cref="TuiController"/> class.
        /// </summary>
        /// <param name="context">The runtime context. Cannot be null.</param>
        /// <param name="showSplash">When true, the startup splash is shown before the dashboard. Default is true.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public TuiController(ArmorContext context, bool showSplash = true)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _ShowSplash = showSplash;
        }

        /// <summary>
        /// Configure the application: build the dashboard layout, widgets, and key bindings, then start
        /// the splash and the initial section load.
        /// </summary>
        /// <param name="app">The TUI application to configure. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Configure(TuiApplication app)
        {
            if (app == null)
                throw new ArgumentNullException(nameof(app));

            _App = app;

            // Padding is applied inside the widget (region padding does not inset a custom widget's
            // draw surface): the nav is inset a column on each side, the content a column on the left
            // so it does not butt against the nav.
            _Nav = new SectionTableView(Array.Empty<string>(), new int[] { 1 }, false, padLeft: 1, padRight: 1);
            _Content = new SectionTableView(new[] { "Name" }, new int[] { 1 }, true, padLeft: 1, padRight: 0);

            // Top two-thirds: the configuration dashboard (nav sidebar + content table).
            // Bottom third: a scrolling activity log for status, results, and notifications.
            app.AddWidget("nav", _Nav, r => r.LeftAnchored(0, NavWidth).ProportionalHeight(0.0, 2.0 / 3.0));
            app.AddWidget("content", _Content, r => r.FillWidth(NavWidth, 0).ProportionalHeight(0.0, 2.0 / 3.0));
            _Log = app.AddPane("log", r => r.FillWidth().ProportionalHeight(2.0 / 3.0, 1.0 / 3.0));

            BuildNav();
            _Nav.SelectionChanged += () => Launch(LoadCurrentSectionAsync);
            _Nav.Activated += _ => app.Focus("content");
            _Content.Activated += tag => Launch(() => PrimaryActionAsync(tag));

            app.Bind("ctrl+q", app.Quit);
            app.Bind("escape", () => app.Focus("nav"));
            app.Bind("c", () => Launch(CreateInCurrentSectionAsync));
            app.Bind("d", () => Launch(DeleteSelectedAsync));
            app.Bind("f5", () => Launch(LoadCurrentSectionAsync));
            app.Bind("f1", () => Launch(ShowHelpAsync));
            app.Bind("x", () => Launch(ExportSelfBackupAsync));

            SetStatus("Armor started. Choose a section on the left; press F1 for help.");
            _ = StartAsync();
        }

        private void BuildNav()
        {
            List<TableRow> rows = new List<TableRow>
            {
                new TableRow(new[] { "Policies" }, Section.Policies),
                new TableRow(new[] { "Storage targets" }, Section.Targets),
                new TableRow(new[] { "Encryption keys" }, Section.Keys),
                new TableRow(new[] { "Backup jobs" }, Section.Jobs),
                new TableRow(new[] { "Schedules" }, Section.Schedules),
                new TableRow(new[] { "Recover" }, Section.Recover),
            };
            Nav().SetRows(rows, "No sections.");
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

                App().Focus("nav");
                await LoadCurrentSectionAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetStatus("Fatal: " + ex.Message);
            }
        }

        // ---- Section loading -------------------------------------------------

        private async Task LoadCurrentSectionAsync()
        {
            _Current = Nav().SelectedTag is Section section ? section : Section.Policies;
            switch (_Current)
            {
                case Section.Policies: await LoadPoliciesAsync().ConfigureAwait(false); break;
                case Section.Targets: await LoadTargetsAsync().ConfigureAwait(false); break;
                case Section.Keys: await LoadKeysAsync().ConfigureAwait(false); break;
                case Section.Jobs: await LoadJobsAsync().ConfigureAwait(false); break;
                case Section.Schedules: await LoadSchedulesAsync().ConfigureAwait(false); break;
                case Section.Recover: await LoadRecoverAsync().ConfigureAwait(false); break;
            }
        }

        private async Task LoadPoliciesAsync()
        {
            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "Name", "Type", "Retain", "Includes", "Enabled" }, new int[] { 5, 3, 2, 2, 2 });

            List<TableRow> rows = new List<TableRow>();
            foreach (Policy policy in policies)
            {
                rows.Add(new TableRow(new[]
                {
                    policy.Name,
                    policy.BackupType.ToString(),
                    policy.RetentionDays + "d",
                    policy.IncludePaths.Count.ToString(),
                    policy.Enabled ? "yes" : "no",
                }, policy));
            }

            Content().SetHeadings("Policies (" + policies.Count + ")", "↑↓ select · Enter run backup · c new · d delete · Tab/Esc nav · Ctrl+Q quit");
            Content().SetRows(rows, "No policies yet. Press 'c' to create one.");
        }

        private async Task LoadTargetsAsync()
        {
            List<StorageTarget> targets = await _Context.Database.StorageTargets.ReadAllAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "Name", "Type", "Location" }, new int[] { 3, 2, 6 });

            List<TableRow> rows = new List<TableRow>();
            foreach (StorageTarget target in targets)
            {
                rows.Add(new TableRow(new[]
                {
                    target.Name,
                    target.Type.ToString(),
                    String.IsNullOrWhiteSpace(target.DiskPath) ? "—" : target.DiskPath!,
                }, target));
            }

            Content().SetHeadings("Storage targets (" + targets.Count + ")", "↑↓ select · Enter validate · c new · d delete · Tab/Esc nav · Ctrl+Q quit");
            Content().SetRows(rows, "No storage targets yet. Press 'c' to create one.");
        }

        private async Task LoadKeysAsync()
        {
            List<EncryptionKey> keys = await _Context.Database.EncryptionKeys.ReadAllAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "Name", "Protection", "Created (UTC)" }, new int[] { 4, 3, 4 });

            List<TableRow> rows = new List<TableRow>();
            foreach (EncryptionKey key in keys)
            {
                string protection = (key.UsesPassphrase ? "password" : String.Empty)
                    + (key.UsesPassphrase && key.UsesKeyFile ? " + " : String.Empty)
                    + (key.UsesKeyFile ? "key file" : String.Empty);
                if (protection.Length == 0)
                    protection = "—";

                rows.Add(new TableRow(new[]
                {
                    key.Name,
                    protection,
                    key.CreatedUtc.ToString("u"),
                }, key));
            }

            Content().SetHeadings("Encryption keys (" + keys.Count + ")", "↑↓ select · Enter details · c new · d delete · Tab/Esc nav · Ctrl+Q quit");
            Content().SetRows(rows, "No encryption keys yet. Press 'c' to create one.");
        }

        private async Task LoadJobsAsync()
        {
            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "When (UTC)", "Type", "Status", "Files" }, new int[] { 4, 2, 2, 2 });

            List<TableRow> rows = new List<TableRow>();
            foreach (BackupJob job in jobs)
            {
                rows.Add(new TableRow(new[]
                {
                    job.CompletedUtc?.ToString("u") ?? "(running)",
                    job.BackupType.ToString(),
                    job.Status.ToString(),
                    job.FileCount.ToString(),
                }, job));
            }

            Content().SetHeadings("Backup jobs (" + jobs.Count + ")", "↑↓ select · Enter restore · F5 refresh · Tab/Esc nav · Ctrl+Q quit");
            Content().SetRows(rows, "No backups have run yet.");
        }

        private async Task LoadSchedulesAsync()
        {
            List<Schedule> schedules = await _Context.Database.Schedules.ReadAllAsync().ConfigureAwait(false);
            Dictionary<string, string> policyNames = await BuildPolicyNameMapAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "Policy", "Cron", "State", "Next run (UTC)" }, new int[] { 3, 3, 2, 4 });

            List<TableRow> rows = new List<TableRow>();
            foreach (Schedule schedule in schedules)
            {
                string policyName = policyNames.TryGetValue(schedule.PolicyId, out string? name) ? name : schedule.PolicyId;
                rows.Add(new TableRow(new[]
                {
                    policyName,
                    schedule.CronExpression,
                    schedule.Enabled ? "enabled" : "disabled",
                    schedule.NextRunUtc?.ToString("u") ?? "—",
                }, schedule));
            }

            Content().SetHeadings("Schedules (" + schedules.Count + ")", "↑↓ select · Enter enable/disable · c new · d delete · Tab/Esc nav · Ctrl+Q quit");
            Content().SetRows(rows, "No schedules yet. Press 'c' to create one.");
        }

        private async Task<Dictionary<string, string>> BuildPolicyNameMapAsync()
        {
            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Policy policy in policies)
                map[policy.Id] = policy.Name;
            return map;
        }

        // ---- Recover from a location ----------------------------------------

        private async Task LoadRecoverAsync()
        {
            _RecoverSession = null;
            List<StorageTarget> targets = await _Context.Database.StorageTargets.ReadAllAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "Location", "Type", "Path" }, new int[] { 3, 2, 6 });

            List<TableRow> rows = new List<TableRow>();
            foreach (StorageTarget target in targets)
            {
                rows.Add(new TableRow(new[]
                {
                    target.Name,
                    target.Type.ToString(),
                    String.IsNullOrWhiteSpace(target.DiskPath) ? "—" : target.DiskPath!,
                }, target));
            }

            Content().SetHeadings("Recover — choose where the backup is", "Enter open · c add a location · ↑↓ select · Tab/Esc nav");
            Content().SetRows(rows, "No locations yet. Press 'c' to add where your backup lives.");
        }

        private async Task RecoverPrimaryAsync(object? tag)
        {
            switch (tag)
            {
                case StorageTarget target: await OpenRecoveryAsync(target).ConfigureAwait(false); break;
                case RecoveryPoint point: await RestoreFromPointAsync(point).ConfigureAwait(false); break;
                case RecoverBackRow _: await LoadRecoverAsync().ConfigureAwait(false); break;
                default: break;
            }
        }

        private async Task OpenRecoveryAsync(StorageTarget target)
        {
            string? password = await PromptAsync("Password for the backup at '" + target.Name + "'").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(password))
                return;

            SetStatus("Opening backup at '" + target.Name + "'…");

            RecoverySession session;
            try
            {
                session = await new RecoveryService(_Context).OpenAsync(target.Id, password!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await NotifyAsync("Could not open backup", ex.Message).ConfigureAwait(false);
                return;
            }

            List<RecoveryPoint> points;
            try
            {
                points = await session.BrowseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await NotifyAsync("Could not read the catalog", ex.Message).ConfigureAwait(false);
                return;
            }

            if (points.Count == 0)
            {
                await NotifyAsync("Nothing to recover", "This location has no backups.").ConfigureAwait(false);
                return;
            }

            _RecoverSession = session;
            ShowRecoveryCatalog(target.Name, points);
            SetStatus("Opened '" + target.Name + "': " + points.Count + " backup" + (points.Count == 1 ? "" : "s") + " found.");
        }

        private void ShowRecoveryCatalog(string targetName, List<RecoveryPoint> points)
        {
            Content().SetColumns(new[] { "When (UTC)", "Type", "Files", "Size", "Policy" }, new int[] { 4, 2, 2, 2, 3 });

            List<TableRow> rows = new List<TableRow>();
            foreach (RecoveryPoint point in points)
            {
                rows.Add(new TableRow(new[]
                {
                    point.PointInTimeUtc.ToString("u"),
                    point.BackupType.ToString(),
                    point.FileCount.ToString(),
                    FormatBytes(point.TotalBytes),
                    point.PolicyName ?? point.PolicyId,
                }, point));
            }
            rows.Add(new TableRow(new[] { "‹ Back to locations", "", "", "", "" }, RecoverBackRow.Instance));

            Content().SetHeadings("Recover — " + targetName + " (" + points.Count + " backup" + (points.Count == 1 ? "" : "s") + ")", "Enter restore this point · Tab/Esc nav");
            Content().SetRows(rows, "No backups were found at this location.");
        }

        private async Task RestoreFromPointAsync(RecoveryPoint point)
        {
            RecoverySession? session = _RecoverSession;
            if (session == null)
                return;
            if (_Busy)
            {
                await NotifyAsync("Busy", "Another operation is still running.").ConfigureAwait(false);
                return;
            }

            int scope = await App().SelectAsync("How much do you want to restore?", "Everything", "A folder", "A single file").ConfigureAwait(false);
            if (scope < 0)
                return;

            RestoreScopeEnum restoreScope = RestoreScopeEnum.All;
            string? selector = null;

            if (scope == 1)
            {
                List<string> folders = await session.ListFoldersAsync(point).ConfigureAwait(false);
                string? folder = await PickStringAsync("Choose a folder to restore", folders).ConfigureAwait(false);
                if (folder == null)
                    return;
                restoreScope = RestoreScopeEnum.Folder;
                selector = folder;
            }
            else if (scope == 2)
            {
                List<string> files = await session.ListFilesAsync(point).ConfigureAwait(false);
                string? file = await PickStringAsync("Choose a file to restore", files).ConfigureAwait(false);
                if (file == null)
                    return;
                restoreScope = RestoreScopeEnum.File;
                selector = file;
            }

            string? destination = await PromptAsync("Destination folder (blank = original locations)").ConfigureAwait(false);

            RestoreJob restoreJob = new RestoreJob();
            restoreJob.Scope = restoreScope;
            restoreJob.SourceSelector = selector;
            restoreJob.DestinationRoot = String.IsNullOrWhiteSpace(destination) ? null : destination;

            _Busy = true;
            SetStatus("Restoring from " + point.PointInTimeUtc.ToString("u") + "…");

            _ = Task.Run(async () =>
            {
                try
                {
                    RestoreJob done = await session.RestoreAsync(point, restoreJob).ConfigureAwait(false);
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Restore " + done.Status + ": " + done.FilesRestored + " files, " + FormatBytes(done.BytesRestored) + ".");
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Restore failed: " + ex.Message);
                    });
                }
            });
        }

        private async Task<string?> PickStringAsync(string title, List<string> options)
        {
            if (options.Count == 0)
            {
                await NotifyAsync(title, "There is nothing to choose from.").ConfigureAwait(false);
                return null;
            }
            return await PickAsync(title, options, value => value).ConfigureAwait(false);
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? bytes + " B" : value.ToString("0.0") + " " + units[unit];
        }

        // ---- Primary (Enter) actions ----------------------------------------

        private async Task PrimaryActionAsync(object? tag)
        {
            if (_Current == Section.Recover)
            {
                await RecoverPrimaryAsync(tag).ConfigureAwait(false);
                return;
            }

            switch (tag)
            {
                case Policy policy: await RunBackupAsync(policy).ConfigureAwait(false); break;
                case StorageTarget target: await ValidateTargetAsync(target).ConfigureAwait(false); break;
                case EncryptionKey key: await ShowKeyDetailAsync(key).ConfigureAwait(false); break;
                case BackupJob job: await RestoreAsync(job).ConfigureAwait(false); break;
                case Schedule schedule: await ToggleScheduleAsync(schedule).ConfigureAwait(false); break;
                default: break;
            }
        }

        // ---- Create ----------------------------------------------------------

        private Task CreateInCurrentSectionAsync()
        {
            switch (_Current)
            {
                case Section.Policies: return CreatePolicyAsync();
                case Section.Targets: return CreateDiskTargetAsync();
                case Section.Keys: return CreateKeyAsync();
                case Section.Schedules: return CreateScheduleAsync();
                case Section.Recover: return CreateDiskTargetAsync();
                default: return NotifyAsync("Nothing to create", "This section has no create action.");
            }
        }

        private async Task CreatePolicyAsync()
        {
            List<StorageTarget> targets = await _Context.Database.StorageTargets.ReadAllAsync().ConfigureAwait(false);
            if (targets.Count == 0)
            {
                await NotifyAsync("Create policy", "No storage targets exist yet.", "Create one under 'Storage targets' first.").ConfigureAwait(false);
                return;
            }

            List<EncryptionKey> keys = await _Context.Database.EncryptionKeys.ReadAllAsync().ConfigureAwait(false);
            if (keys.Count == 0)
            {
                await NotifyAsync("Create policy", "No encryption keys exist yet.", "Create one under 'Encryption keys' first.").ConfigureAwait(false);
                return;
            }

            string? name = await PromptAsync("Policy name").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;

            string? include = await PromptAsync("Include path (file or folder)").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(include))
                return;

            StorageTarget? target = await PickAsync("Select a storage target", targets, t => t.Name + " [" + t.Type + "]").ConfigureAwait(false);
            if (target == null)
                return;

            EncryptionKey? key = await PickAsync("Select an encryption key", keys, k => k.Name + " (" + k.Id + ")").ConfigureAwait(false);
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
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Created policy '" + policy.Name + "'.");
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
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Created disk target '" + target.Name + "'.");
        }

        private async Task CreateKeyAsync()
        {
            string? name = await PromptAsync("Key name").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;

            string? password = await PromptAsync("Password for this key").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(password))
                return;

            string? confirm = await PromptAsync("Confirm password").ConfigureAwait(false);
            if (confirm == null)
                return;
            if (!String.Equals(password, confirm, StringComparison.Ordinal))
            {
                await NotifyAsync("Passwords do not match", "The two entries were different. Nothing was created.").ConfigureAwait(false);
                return;
            }

            // Password-protected, with the password cached locally so backups run unattended. The
            // password (not a file) is the recovery secret: it is all you need on a fresh machine.
            EncryptionKeyService service = new EncryptionKeyService(_Context.Database);
            ProvisionedKey provisioned = await service.ProvisionWithPasswordAsync(name!, password!, _Context.Paths, _Context.CredentialProtector, 600000).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Created key '" + provisioned.Key.Name + "'.");
            await NotifyAsync(
                "Key created",
                provisioned.Key.Name + " is protected by your password.",
                "Backups run unattended — the password is cached on this machine.",
                "Remember the password: with it alone you can restore on a fresh install of Armor.").ConfigureAwait(false);
        }

        private async Task CreateScheduleAsync()
        {
            Policy? policy = await PickPolicyAsync("Select a policy to schedule").ConfigureAwait(false);
            if (policy == null)
                return;

            (string Cron, string Description)? built = await BuildScheduleAsync().ConfigureAwait(false);
            if (built == null)
                return;

            Schedule schedule = new Schedule();
            schedule.PolicyId = policy.Id;
            schedule.CronExpression = built.Value.Cron;
            await _Context.Database.Schedules.CreateAsync(schedule).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Scheduled '" + policy.Name + "': " + built.Value.Description);
            await NotifyAsync("Schedule created", policy.Name + " — " + built.Value.Description, "Cron: " + built.Value.Cron, "Times are interpreted as UTC.").ConfigureAwait(false);
        }

        /// <summary>
        /// Walk the user through a friendly frequency picker and return the resulting cron expression
        /// (in the five-field minute/hour/day-of-month/month/day-of-week dialect the scheduler parses)
        /// together with a human-readable description, or null when the user cancels.
        /// </summary>
        private async Task<(string Cron, string Description)?> BuildScheduleAsync()
        {
            int frequency = await App().SelectAsync(
                "How often should this backup run?",
                "Every N minutes",
                "Every N hours",
                "Every day",
                "Certain days of the week",
                "A day of the month",
                "Advanced (raw cron)").ConfigureAwait(false);

            switch (frequency)
            {
                case 0:
                {
                    int? minutes = await PromptIntAsync("Run every how many minutes?", 1, 59, "15").ConfigureAwait(false);
                    if (minutes == null)
                        return null;
                    return ("*/" + minutes + " * * * *", "Every " + minutes + " minute" + (minutes == 1 ? "" : "s"));
                }

                case 1:
                {
                    int? hours = await PromptIntAsync("Run every how many hours?", 1, 23, "6").ConfigureAwait(false);
                    if (hours == null)
                        return null;
                    return ("0 */" + hours + " * * *", "Every " + hours + " hour" + (hours == 1 ? "" : "s") + ", on the hour");
                }

                case 2:
                {
                    (int Hour, int Minute)? time = await PromptTimeAsync().ConfigureAwait(false);
                    if (time == null)
                        return null;
                    return (time.Value.Minute + " " + time.Value.Hour + " * * *", "Every day at " + FormatTime(time.Value) + " UTC");
                }

                case 3:
                {
                    int choice = await App().SelectAsync(
                        "Which days of the week?",
                        "Weekdays (Mon–Fri)",
                        "Weekends (Sat & Sun)",
                        "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday").ConfigureAwait(false);
                    if (choice < 0)
                        return null;

                    string dow;
                    string dowDescription;
                    if (choice == 0)
                    {
                        dow = "1-5";
                        dowDescription = "weekday";
                    }
                    else if (choice == 1)
                    {
                        dow = "0,6";
                        dowDescription = "weekend day";
                    }
                    else
                    {
                        int day = choice - 2; // 0 = Sunday .. 6 = Saturday
                        dow = day.ToString();
                        dowDescription = DayName(day);
                    }

                    (int Hour, int Minute)? time = await PromptTimeAsync().ConfigureAwait(false);
                    if (time == null)
                        return null;
                    return (time.Value.Minute + " " + time.Value.Hour + " * * " + dow, "Every " + dowDescription + " at " + FormatTime(time.Value) + " UTC");
                }

                case 4:
                {
                    int? dayOfMonth = await PromptIntAsync("Day of the month", 1, 31, "1").ConfigureAwait(false);
                    if (dayOfMonth == null)
                        return null;
                    (int Hour, int Minute)? time = await PromptTimeAsync().ConfigureAwait(false);
                    if (time == null)
                        return null;
                    return (time.Value.Minute + " " + time.Value.Hour + " " + dayOfMonth + " * *", "Day " + dayOfMonth + " of each month at " + FormatTime(time.Value) + " UTC");
                }

                case 5:
                {
                    while (true)
                    {
                        string? raw = await PromptAsync("Cron (min hour day-of-month month day-of-week)", "0 2 * * *").ConfigureAwait(false);
                        if (String.IsNullOrWhiteSpace(raw))
                            return null;
                        try
                        {
                            Armor.Core.Scheduling.CronSchedule.Parse(raw!);
                            return (raw!.Trim(), "Custom schedule (" + raw!.Trim() + ")");
                        }
                        catch (Exception ex)
                        {
                            await NotifyAsync("That cron expression is not valid", ex.Message, "Try again, or leave it blank to cancel.").ConfigureAwait(false);
                        }
                    }
                }

                default:
                    return null;
            }
        }

        private async Task<int?> PromptIntAsync(string title, int min, int max, string initial)
        {
            while (true)
            {
                string? text = await PromptAsync(title + " (" + min + "–" + max + ")", initial).ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(text))
                    return null;
                if (int.TryParse(text!.Trim(), out int value) && value >= min && value <= max)
                    return value;
                await NotifyAsync("Enter a whole number", "Please enter a number between " + min + " and " + max + ".").ConfigureAwait(false);
            }
        }

        private async Task<(int Hour, int Minute)?> PromptTimeAsync()
        {
            while (true)
            {
                string? text = await PromptAsync("Time of day, 24-hour UTC (HH:MM)", "02:00").ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(text))
                    return null;

                string[] parts = text!.Trim().Split(':');
                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), out int hour)
                    && int.TryParse(parts[1].Trim(), out int minute)
                    && hour >= 0 && hour <= 23
                    && minute >= 0 && minute <= 59)
                {
                    return (hour, minute);
                }

                await NotifyAsync("Enter a valid time", "Use 24-hour HH:MM, for example 02:00 or 18:30.").ConfigureAwait(false);
            }
        }

        private static string FormatTime((int Hour, int Minute) time)
        {
            return time.Hour.ToString("D2") + ":" + time.Minute.ToString("D2");
        }

        private static string DayName(int dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case 0: return "Sunday";
                case 1: return "Monday";
                case 2: return "Tuesday";
                case 3: return "Wednesday";
                case 4: return "Thursday";
                case 5: return "Friday";
                case 6: return "Saturday";
                default: return "day " + dayOfWeek;
            }
        }

        // ---- Delete ----------------------------------------------------------

        private async Task DeleteSelectedAsync()
        {
            switch (Content().SelectedTag)
            {
                case Policy policy: await DeletePolicyAsync(policy).ConfigureAwait(false); break;
                case StorageTarget target: await DeleteTargetAsync(target).ConfigureAwait(false); break;
                case EncryptionKey key: await DeleteKeyAsync(key).ConfigureAwait(false); break;
                case Schedule schedule: await DeleteScheduleAsync(schedule).ConfigureAwait(false); break;
                default: break;
            }
        }

        private async Task DeleteKeyAsync(EncryptionKey key)
        {
            // A policy points at its key; deleting a referenced key would leave that policy dangling.
            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            List<string> dependents = new List<string>();
            foreach (Policy policy in policies)
            {
                if (String.Equals(policy.EncryptionKeyId, key.Id, StringComparison.Ordinal))
                    dependents.Add(policy.Name);
            }

            if (dependents.Count > 0)
            {
                await NotifyAsync(
                    "Cannot delete '" + key.Name + "'",
                    "It is the encryption key for " + Count(dependents.Count, "policy", "policies") + ":",
                    NameList(dependents),
                    "Delete or re-key " + (dependents.Count == 1 ? "that policy" : "those policies") + " first.").ConfigureAwait(false);
                return;
            }

            if (!await ConfirmAsync("Delete key '" + key.Name + "'? Backups already made with it will become unrecoverable.").ConfigureAwait(false))
                return;

            await _Context.Database.EncryptionKeys.DeleteAsync(key.Id).ConfigureAwait(false);
            TryDeleteCachedSecrets(key.Id);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Deleted key '" + key.Name + "'.");
        }

        private async Task DeleteTargetAsync(StorageTarget target)
        {
            // A policy points at its storage target; deleting a referenced target would strand it.
            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            List<string> dependents = new List<string>();
            foreach (Policy policy in policies)
            {
                if (String.Equals(policy.StorageTargetId, target.Id, StringComparison.Ordinal))
                    dependents.Add(policy.Name);
            }

            if (dependents.Count > 0)
            {
                await NotifyAsync(
                    "Cannot delete '" + target.Name + "'",
                    "It is the storage target for " + Count(dependents.Count, "policy", "policies") + ":",
                    NameList(dependents),
                    "Delete or repoint " + (dependents.Count == 1 ? "that policy" : "those policies") + " first.").ConfigureAwait(false);
                return;
            }

            if (!await ConfirmAsync("Delete storage target '" + target.Name + "'?").ConfigureAwait(false))
                return;

            await _Context.Database.StorageTargets.DeleteAsync(target.Id).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Deleted target '" + target.Name + "'.");
        }

        private async Task DeletePolicyAsync(Policy policy)
        {
            // A schedule points at its policy; block until the schedule is removed so it can't dangle.
            List<Schedule> schedules = await _Context.Database.Schedules.ReadAllAsync().ConfigureAwait(false);
            int scheduleCount = 0;
            foreach (Schedule schedule in schedules)
            {
                if (String.Equals(schedule.PolicyId, policy.Id, StringComparison.Ordinal))
                    scheduleCount++;
            }

            if (scheduleCount > 0)
            {
                await NotifyAsync(
                    "Cannot delete '" + policy.Name + "'",
                    "It still has " + Count(scheduleCount, "schedule", "schedules") + " attached.",
                    "Delete " + (scheduleCount == 1 ? "that schedule" : "those schedules") + " under 'Schedules' first.").ConfigureAwait(false);
                return;
            }

            // Backups resolve their key through the policy, so deleting it makes them unrestorable.
            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync().ConfigureAwait(false);
            int backupCount = 0;
            foreach (BackupJob job in jobs)
            {
                if (String.Equals(job.PolicyId, policy.Id, StringComparison.Ordinal))
                    backupCount++;
            }

            string prompt = "Delete policy '" + policy.Name + "'?";
            if (backupCount > 0)
                prompt += " Its " + Count(backupCount, "backup", "backups") + " will no longer be restorable.";

            if (!await ConfirmAsync(prompt).ConfigureAwait(false))
                return;

            await _Context.Database.Policies.DeleteAsync(policy.Id).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Deleted policy '" + policy.Name + "'.");
        }

        private async Task DeleteScheduleAsync(Schedule schedule)
        {
            // Nothing references a schedule, so it is always safe to delete.
            if (!await ConfirmAsync("Delete this schedule?").ConfigureAwait(false))
                return;

            await _Context.Database.Schedules.DeleteAsync(schedule.Id).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Deleted schedule.");
        }

        private static string Count(int n, string singular, string plural)
        {
            return n + " " + (n == 1 ? singular : plural);
        }

        private static string NameList(List<string> names)
        {
            const int max = 6;
            if (names.Count <= max)
                return "  " + String.Join(", ", names);
            List<string> shown = names.GetRange(0, max);
            return "  " + String.Join(", ", shown) + ", and " + (names.Count - max) + " more";
        }

        private void TryDeleteCachedSecrets(string keyId)
        {
            foreach (string path in new[] { _Context.Paths.PasswordFilePath(keyId), _Context.Paths.KeyFilePath(keyId) })
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup; an orphaned cached secret is harmless.
                }
            }
        }

        // ---- Long-running actions (background, non-blocking) -----------------

        private async Task RunBackupAsync(Policy policy)
        {
            if (_Busy)
            {
                await NotifyAsync("Busy", "Another operation is still running.").ConfigureAwait(false);
                return;
            }
            if (String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
            {
                await NotifyAsync("Cannot back up", "Policy '" + policy.Name + "' has no encryption key assigned.").ConfigureAwait(false);
                return;
            }

            byte[]? dataKey = await UnlockAsync(policy.EncryptionKeyId!).ConfigureAwait(false);
            if (dataKey == null)
                return;

            _Busy = true;
            SetStatus("Backing up '" + policy.Name + "'…");
            string policyId = policy.Id;
            string policyName = policy.Name;

            _ = Task.Run(async () =>
            {
                try
                {
                    BackupService service = new BackupService(_Context);
                    BackupJob job = await service.RunAsync(policyId, dataKey, null, true).ConfigureAwait(false);
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Backup " + job.Status + ": " + job.FileCount + " files, " + job.ChunksWritten + " new / " + job.ChunksReused + " reused chunks.");
                        if (_Current == Section.Jobs)
                            Launch(LoadCurrentSectionAsync);
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Backup of '" + policyName + "' failed: " + ex.Message);
                    });
                }
            });
        }

        private async Task ValidateTargetAsync(StorageTarget target)
        {
            if (_Busy)
            {
                await NotifyAsync("Busy", "Another operation is still running.").ConfigureAwait(false);
                return;
            }

            _Busy = true;
            SetStatus("Validating '" + target.Name + "'…");
            string targetId = target.Id;
            string targetName = target.Name;

            _ = Task.Run(async () =>
            {
                try
                {
                    StorageTargetService service = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
                    bool ok = await service.ValidateAsync(targetId).ConfigureAwait(false);
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Validation of '" + targetName + "': " + (ok ? "succeeded" : "failed") + ".");
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Validation of '" + targetName + "' failed: " + ex.Message);
                    });
                }
            });
        }

        private async Task RestoreAsync(BackupJob job)
        {
            if (_Busy)
            {
                await NotifyAsync("Busy", "Another operation is still running.").ConfigureAwait(false);
                return;
            }

            Policy? policy = await _Context.Database.Policies.ReadAsync(job.PolicyId).ConfigureAwait(false);
            if (policy == null || String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
            {
                await NotifyAsync("Cannot restore", "The policy or key for this backup could not be resolved.").ConfigureAwait(false);
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

            _Busy = true;
            SetStatus("Restoring…");

            _ = Task.Run(async () =>
            {
                try
                {
                    RestoreService service = new RestoreService(_Context);
                    RestoreJob done = await service.RunAsync(restoreJob, dataKey).ConfigureAwait(false);
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Restore " + done.Status + ": " + done.FilesRestored + " files, " + done.BytesRestored + " bytes.");
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        _Busy = false;
                        SetStatus("Restore failed: " + ex.Message);
                    });
                }
            });
        }

        private async Task ToggleScheduleAsync(Schedule schedule)
        {
            schedule.Enabled = !schedule.Enabled;
            await _Context.Database.Schedules.UpdateAsync(schedule).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Schedule " + (schedule.Enabled ? "enabled" : "disabled") + ".");
        }

        private Task ShowKeyDetailAsync(EncryptionKey key)
        {
            EncryptionKeyService service = new EncryptionKeyService(_Context.Database);
            bool unattended = service.CanUnlockUnattended(key, _Context.Paths);
            string protection = (key.UsesPassphrase ? "password" : String.Empty)
                + (key.UsesPassphrase && key.UsesKeyFile ? " and " : String.Empty)
                + (key.UsesKeyFile ? "key file" : String.Empty);
            return NotifyAsync(
                "Encryption key",
                key.Name + " (" + key.Id + ")",
                "Protection: " + (protection.Length == 0 ? "none" : protection),
                unattended
                    ? "The secret is cached here, so backups run unattended."
                    : "No cached secret here — you will be asked for the password.");
        }

        private async Task ExportSelfBackupAsync()
        {
            string defaultPath = Path.Combine(_Context.Paths.RootDirectory, "armor-selfbackup.zip");
            string? destination = await PromptAsync("Self-backup zip path", defaultPath).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(destination))
                return;

            string databaseFile = _Context.Settings.DatabaseFilename ?? _Context.Paths.DefaultDatabasePath;
            SetStatus("Exporting self-backup…");
            await Task.Run(() => Armor.Core.Backup.ConfigBackup.ExportAsync(_Context.Paths.ConfigFilePath, databaseFile, _Context.Paths.StateDirectory, destination!)).ConfigureAwait(false);
            SetStatus("Self-backup written to " + destination + ".");
            await NotifyAsync("Self-backup exported", "Written to " + destination + ".").ConfigureAwait(false);
        }

        private Task ShowHelpAsync()
        {
            return NotifyAsync(
                "Armor — keys",
                "↑ ↓            move selection",
                "Tab / Esc      switch nav / content",
                "Enter          section action (run / validate / restore / toggle)",
                "c              create   ·   d   delete",
                "F5 refresh · x export self-backup · Ctrl+Q quit");
        }

        // ---- Unlock ----------------------------------------------------------

        private async Task<byte[]?> UnlockAsync(string keyId)
        {
            EncryptionKey? key = await _Context.Database.EncryptionKeys.ReadAsync(keyId).ConfigureAwait(false);
            if (key == null)
            {
                await NotifyAsync("Cannot unlock", "Encryption key '" + keyId + "' was not found.").ConfigureAwait(false);
                return null;
            }

            EncryptionKeyService service = new EncryptionKeyService(_Context.Database);

            // Set-and-forget: unlock silently from the cached password when present.
            if (service.CanUnlockUnattended(key, _Context.Paths))
            {
                try
                {
                    return await service.UnlockUnattendedAsync(keyId, _Context.Paths, _Context.CredentialProtector).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await NotifyAsync("Unlock failed", ex.Message).ConfigureAwait(false);
                    return null;
                }
            }

            // No cached secret here (for example after a fresh install): ask for the password.
            if (key.UsesPassphrase)
            {
                string? password = await PromptAsync("Password to unlock '" + key.Name + "'").ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(password))
                    return null;

                try
                {
                    return await service.UnlockWithPassphraseAsync(keyId, password!).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await NotifyAsync("Unlock failed", "That password did not work for '" + key.Name + "'.").ConfigureAwait(false);
                    return null;
                }
            }

            await NotifyAsync("Cannot unlock", "'" + key.Name + "' has no cached secret and no password on this machine.").ConfigureAwait(false);
            return null;
        }

        // ---- Modal pickers / prompts ----------------------------------------

        private async Task<Policy?> PickPolicyAsync(string title)
        {
            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            return await PickAsync(title, policies, policy => policy.Name + " (" + policy.Id + ")").ConfigureAwait(false);
        }

        private async Task<T?> PickAsync<T>(string title, List<T> items, Func<T, string> label) where T : class
        {
            if (items.Count == 0)
            {
                await NotifyAsync(title, "There is nothing to choose from yet.", "Create one first.").ConfigureAwait(false);
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

        private Task<bool> ConfirmAsync(string message)
        {
            return App().ConfirmAsync(message, "Delete", "Cancel");
        }

        private Task NotifyAsync(string title, params string[] lines)
        {
            // Mirror the notification into the activity log so it persists after the modal is dismissed.
            SetStatus(lines.Length > 0 ? title + " — " + lines[0] : title);
            return App().ShowAsync(new ArmorSplashModal(title, lines, "Press any key to continue"));
        }

        // ---- Plumbing --------------------------------------------------------

        private void Launch(Func<Task> action)
        {
            _ = RunGuardedAsync(action);
        }

        private async Task RunGuardedAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
                try
                {
                    await NotifyAsync("Something went wrong", ex.Message).ConfigureAwait(false);
                }
                catch
                {
                    // Never let error reporting throw out of the loop callback.
                }
            }
        }

        private void Post(Action action)
        {
            App().Post(action);
        }

        private void SetStatus(string text)
        {
            _Log?.WriteLine(" " + DateTime.Now.ToString("HH:mm:ss") + "  " + text);
        }

        private TuiApplication App()
        {
            if (_App == null)
                throw new InvalidOperationException("The application has not been configured.");
            return _App;
        }

        private SectionTableView Nav()
        {
            if (_Nav == null)
                throw new InvalidOperationException("The application has not been configured.");
            return _Nav;
        }

        private SectionTableView Content()
        {
            if (_Content == null)
                throw new InvalidOperationException("The application has not been configured.");
            return _Content;
        }
    }
}

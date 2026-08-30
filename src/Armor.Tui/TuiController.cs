namespace Armor.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armor.Core.Enums;
    using Armor.Core.Exceptions;
    using Armor.Core.Models;
    using Armor.Core.Security;
    using Armor.Core.Service;
    using Armor.Tui.Widgets;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Layout;
    using TUIKit.Modals;
    using TUIKit.Theming;

    /// <summary>
    /// Drives the Armor TUI as a persistent dashboard. A left nav lists the sections in setup order
    /// (backup targets, encryption passwords, policies, schedules) followed by the operational views
    /// (runs, restore points, recovery); the main pane shows the selected section as a live table. A
    /// header shows the wordmark, a focusable status workspace shows in-flight backups (Tab to it to
    /// cancel a running job), and an activity log
    /// records results. Navigation is non-blocking: long operations (backups, restores, validation)
    /// run on a background task and report progress, so the UI stays responsive while they run.
    /// </summary>
    public sealed class TuiController
    {
        // Ordered to follow the setup workflow: pick where backups go, set a password, define what to
        // back up, schedule it — then the operational views (runs, restore points, recovery).
        private enum Section
        {
            Targets = 0,
            Keys = 1,
            Policies = 2,
            Schedules = 3,
            Runs = 4,
            Jobs = 5,
            Recover = 6,
        }

        // Sentinel tag for the "back to locations" row shown while browsing a recovery catalog.
        private sealed class RecoverBackRow
        {
            public static readonly RecoverBackRow Instance = new RecoverBackRow();

            private RecoverBackRow()
            {
            }
        }

        // Tag for an in-progress backup row in the Runs list, so pressing Enter on it can cancel that run.
        private sealed class RunningJobRow
        {
            public RunningJobRow(string jobId)
            {
                JobId = jobId;
            }

            public string JobId { get; }
        }

        // Value equality for exclude rules by their editable token form, so the list editor can reject a
        // duplicate rule regardless of the underlying ExcludePattern instance being a fresh object.
        private sealed class ExcludePatternComparer : IEqualityComparer<ExcludePattern>
        {
            public static readonly ExcludePatternComparer Instance = new ExcludePatternComparer();

            public bool Equals(ExcludePattern? x, ExcludePattern? y)
            {
                if (x == null || y == null)
                    return ReferenceEquals(x, y);
                return String.Equals(ToExcludeToken(x), ToExcludeToken(y), StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(ExcludePattern obj)
            {
                return obj == null ? 0 : ToExcludeToken(obj).ToLowerInvariant().GetHashCode();
            }
        }

        private const int NavWidth = 20;
        private const int LogHeight = 27;
        // Tall enough for the header line, a blank line, the three-line progress rectangle, and a
        // trailing blank line — so the rectangle is framed by a linebreak above and below.
        private const int StatusHeight = 10;

        private readonly ArmorContext _Context;
        private readonly bool _ShowSplash;

        private TuiApplication? _App;
        private ActivityLogView? _Log;
        private SectionTableView? _Nav;
        private SectionTableView? _Content;
        private Section _Current = Section.Targets;
        private bool _Busy;
        private RecoverySession? _RecoverSession;
        private JobStatusView? _JobView;
        private readonly List<JobEntry> _Jobs = new List<JobEntry>();
        private string? _ActivityText;

        // Distinguishes the two kinds of run that share the status workspace and job registry, so cancel
        // prompts and counters can word themselves correctly.
        private enum JobKind
        {
            Backup,
            Restore,
        }

        /// <summary>
        /// A backup or restore running under this TUI process: its display label, cancellation source, and
        /// the latest progress. All fields are read and written only on the UI thread (via <c>Post</c>), so
        /// no locking is needed. The <see cref="Id"/> is a process-local handle, distinct from the
        /// database job id, so the status view can address a run before its job row exists.
        /// </summary>
        private sealed class JobEntry
        {
            public JobEntry(string id, string label, string policyName, CancellationTokenSource cts, JobKind kind = JobKind.Backup)
            {
                Id = id;
                Label = label;
                PolicyName = policyName;
                Cts = cts;
                Kind = kind;
                // A restore knows its totals up front (from the backup record), so it never shows the
                // backup's "scanning" pre-phase.
                Scanning = kind == JobKind.Backup;
            }

            public JobKind Kind { get; }

            public string Id { get; }

            public string Label { get; }

            public string PolicyName { get; }

            public CancellationTokenSource Cts { get; }

            public int Percent { get; set; }

            public int FilesDone { get; set; }

            public int FilesTotal { get; set; }

            public long BytesDone { get; set; }

            public long BytesTotal { get; set; }

            public bool Cancelling { get; set; }

            // True while the run is still pre-scanning the source (before any file is copied). Seeded by
            // the constructor from the job kind: backups begin scanning, restores never scan.
            public bool Scanning { get; set; }
        }

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

            // Use the terminal's own background rather than painting one: the default theme is dark,
            // which shows up as an unwanted background color. A text style with the default background
            // emits no background color, so the terminal's native background shows through.
            app.Theme = new Theme(
                "armor",
                CellStyle.Default,
                CellStyle.Default.WithForeground(Color.FromPalette(6)),
                // Border: dark grey (palette 8, "bright black"), used for the framed log and activity areas.
                CellStyle.Default.WithForeground(Color.FromPalette(8)),
                CellStyle.Default.WithForeground(Color.FromPalette(8)),
                false,
                null, null, null, null, null, null);

            // Top banner: the ASCII-art wordmark plus tagline and project link. Its height tracks the
            // wordmark plus its box border. The band sits above the nav and workspace.
            string[] logoRows = ArmorBanner.WordmarkLines();
            HeaderBanner header = new HeaderBanner(logoRows, "Data protection for the paranoid", "https://github.com/jchristn/Armor");
            // One extra row below the wordmark; the banner only draws its logo rows, so this stays
            // blank and separates the header from the workspace beneath it.
            int headerHeight = logoRows.Length + 1;

            // Plain panes: no borders, no padding, no background fill.
            _Nav = new SectionTableView(Array.Empty<string>(), new int[] { 1 }, false);
            // A small left inset separates the workspace from the nav sidebar on its left.
            _Content = new SectionTableView(new[] { "Name" }, new int[] { 1 }, true, padLeft: 2);
            _Log = new ActivityLogView();
            _Log.Announce += text => Post(() => SetStatus(text));
            _JobView = new JobStatusView();

            // A persistent one-row key-hint bar pinned to the very bottom, so the essential shortcuts —
            // and the F1 pointer to the full list — are always visible instead of hidden behind F1.
            FooterHints footer = new FooterHints();

            // Application-shell layout, built region by region so the log and in-progress activity areas can
            // carry a border and left/right padding (the DockTop/DockBottom shortcuts create only plain,
            // unbordered regions). The constraints reproduce the same stack the dock helpers would: header
            // fixed at the top; hints, log, and status stacked up from the bottom; nav on the left; content
            // filling the middle. Vertical FromEnd offsets are cumulative from the bottom edge, so the regions
            // tile without overlap. Padding is 0 everywhere except the framed regions, which get one column of
            // left/right padding (no top/bottom) inside their border.
            const int FillMax = 1_000_000;
            AxisConstraint fullWidth = AxisConstraint.Stretch(0, 0, 1, FillMax);
            int bottomStack = 1 + LogHeight + StatusHeight; // hints + log + status
            AxisConstraint middleHeight = AxisConstraint.Stretch(headerHeight, bottomStack, 1, FillMax);

            app.Layout = Layout.Create()
                .Add("header", region => region.Horizontal(fullWidth).Vertical(AxisConstraint.Fixed(0, headerHeight)).WithPadding(0))
                .Add("hints", region => region.Horizontal(fullWidth).Vertical(AxisConstraint.FromEnd(0, 1)).WithPadding(0))
                .Add("log", region => region.Horizontal(fullWidth).Vertical(AxisConstraint.FromEnd(1, LogHeight))
                    .WithBorder(BorderStyle.Line, "Activity log").WithPadding(0).WithHorizontalPadding(1, 1))
                .Add("status", region => region.Horizontal(fullWidth).Vertical(AxisConstraint.FromEnd(1 + LogHeight, StatusHeight))
                    .WithBorder(BorderStyle.Line, "Backups & restores in progress").WithPadding(0).WithHorizontalPadding(1, 1))
                .Add("nav", region => region.Horizontal(AxisConstraint.Fixed(0, NavWidth)).Vertical(middleHeight).WithPadding(0))
                .Add("content", region => region.Horizontal(AxisConstraint.Stretch(NavWidth, 0, 1, FillMax)).Vertical(middleHeight).WithPadding(0))
                .Build();

            app.Bind("nav", _Nav);
            app.Bind("content", _Content);
            app.Bind("header", header);
            app.Bind("hints", footer);
            // Bind the status workspace as a focusable widget (not a plain pane) so Tab reaches it and
            // the user can step through and manage in-progress backups.
            app.Bind("status", _JobView);
            // Bind the activity log as a focusable widget (not a plain pane) so Tab reaches it and its
            // scroll/copy/clear shortcuts become available.
            app.Bind("log", _Log);

            // Mirror file-log messages (backup lifecycle, warnings, errors) into the on-screen log pane.
            Armor.Core.Diagnostics.ArmorLog.MessageLogged += OnLogMessage;

            BuildNav();
            _Nav.SelectionChanged += () => Launch(LoadCurrentSectionAsync);
            _Nav.Activated += _ => app.Focus("content");
            _Content.Activated += tag => Launch(() => PrimaryActionAsync(tag));
            _JobView.Activated += id => Launch(() => ManageJobAsync(id));

            app.Bind("ctrl+q", app.Quit);
            app.Bind("escape", () => app.Focus("nav"));
            app.Bind("c", () => Launch(CreateInCurrentSectionAsync));
            app.Bind("d", () => Launch(DeleteSelectedAsync));
            app.Bind("e", () => Launch(EditSelectedAsync));
            // The physical Insert and Delete keys mirror the c/create and d/delete shortcuts; F2 is the
            // conventional edit key and mirrors e/edit.
            app.Bind("insert", () => Launch(CreateInCurrentSectionAsync));
            app.Bind("delete", () => Launch(DeleteSelectedAsync));
            app.Bind("f2", () => Launch(EditSelectedAsync));
            app.Bind("r", () => Launch(RestorePointsForSelectedPolicyAsync));
            app.Bind("f5", () => Launch(LoadCurrentSectionAsync));
            app.Bind("f1", () => Launch(ShowHelpAsync));
            app.Bind("x", () => Launch(ExportSelfBackupAsync));
            app.Bind("s", () => Launch(ShowStatisticsAsync));
            app.Bind("g", () => Launch(ManageGlobalExcludesAsync));

            SetStatus("Armor started. Choose a section on the left; press F1 for help.");
            _ = StartAsync();
        }

        private void BuildNav()
        {
            List<TableRow> rows = new List<TableRow>
            {
                new TableRow(new[] { "1 Backup targets" }, Section.Targets),
                new TableRow(new[] { "2 Passwords" }, Section.Keys),
                new TableRow(new[] { "3 Policies" }, Section.Policies),
                new TableRow(new[] { "4 Schedules" }, Section.Schedules),
                new TableRow(new[] { "Runs" }, Section.Runs),
                new TableRow(new[] { "Backup jobs" }, Section.Jobs),
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
            _Current = Nav().SelectedTag is Section section ? section : Section.Targets;
            switch (_Current)
            {
                case Section.Policies: await LoadPoliciesAsync().ConfigureAwait(false); break;
                case Section.Targets: await LoadTargetsAsync().ConfigureAwait(false); break;
                case Section.Keys: await LoadKeysAsync().ConfigureAwait(false); break;
                case Section.Jobs: await LoadJobsAsync().ConfigureAwait(false); break;
                case Section.Schedules: await LoadSchedulesAsync().ConfigureAwait(false); break;
                case Section.Runs: await LoadRunsAsync().ConfigureAwait(false); break;
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

            Content().SetHeadings("Policies (" + policies.Count + ")", new[] { Hint("↑↓", "Select"), Hint("↵", "Back up now"), Hint("r", "Restore"), Hint("c", "Create"), Hint("e", "Edit"), Hint("d", "Delete"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
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

            Content().SetHeadings("Backup targets (" + targets.Count + ")", new[] { Hint("↑↓", "Select"), Hint("↵", "Validate"), Hint("c", "Create"), Hint("e", "Edit"), Hint("d", "Delete"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
            Content().SetRows(rows, "No backup targets yet. Press 'c' to add where backups are stored.");
        }

        private async Task LoadKeysAsync()
        {
            List<EncryptionKey> keys = await _Context.Database.EncryptionKeys.ReadAllAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "Name", "Protection", "Created" }, new int[] { 4, 3, 8 });

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
                    FormatTimestamp(key.CreatedUtc),
                }, key));
            }

            Content().SetHeadings("Encryption passwords (" + keys.Count + ")", new[] { Hint("↑↓", "Select"), Hint("↵", "Details"), Hint("c", "Create"), Hint("e", "Rename"), Hint("d", "Delete"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
            Content().SetRows(rows, "No encryption passwords yet. Press 'c' to create one.");
        }

        private async Task LoadJobsAsync()
        {
            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync().ConfigureAwait(false);
            Dictionary<string, string> policyNames = await BuildPolicyNameMapAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "When", "Policy", "Type", "Status", "Files" }, new int[] { 9, 3, 2, 2, 2 });

            List<TableRow> rows = new List<TableRow>();
            // Newest first — these are the point-in-time restore points.
            jobs.Reverse();
            foreach (BackupJob job in jobs)
            {
                string policyName = policyNames.TryGetValue(job.PolicyId, out string? name) ? name : job.PolicyId;
                rows.Add(new TableRow(new[]
                {
                    job.CompletedUtc.HasValue ? FormatTimestamp(job.CompletedUtc.Value) : "(running)",
                    policyName,
                    job.BackupType.ToString(),
                    job.Status.ToString(),
                    job.FileCount.ToString(),
                }, job));
            }

            Content().SetHeadings("Backup jobs — restore points (" + jobs.Count + ")", new[] { Hint("↑↓", "Select"), Hint("↵", "Restore"), Hint("F5", "Refresh"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
            Content().SetRows(rows, "No backups have run yet. Run a policy from 'Policies' to create a restore point.");
        }

        private async Task LoadSchedulesAsync()
        {
            List<Schedule> schedules = await _Context.Database.Schedules.ReadAllAsync().ConfigureAwait(false);
            Dictionary<string, string> policyNames = await BuildPolicyNameMapAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "Policy", "Schedule", "State", "Next run" }, new int[] { 3, 4, 2, 9 });

            List<TableRow> rows = new List<TableRow>();
            foreach (Schedule schedule in schedules)
            {
                string policyName = policyNames.TryGetValue(schedule.PolicyId, out string? name) ? name : schedule.PolicyId;
                rows.Add(new TableRow(new[]
                {
                    policyName,
                    DescribeCron(schedule.CronExpression),
                    schedule.Enabled ? "enabled" : "disabled",
                    schedule.NextRunUtc.HasValue ? FormatTimestamp(schedule.NextRunUtc.Value) : "—",
                }, schedule));
            }

            Content().SetHeadings("Schedules (" + schedules.Count + ")", new[] { Hint("↑↓", "Select"), Hint("↵", "Enable/disable"), Hint("c", "Create"), Hint("e", "Edit"), Hint("d", "Delete"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
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

        private async Task LoadRunsAsync()
        {
            List<Schedule> schedules = await _Context.Database.Schedules.ReadAllAsync().ConfigureAwait(false);
            Dictionary<string, string> policyNames = await BuildPolicyNameMapAsync().ConfigureAwait(false);
            Content().SetColumns(new[] { "When", "Policy", "What", "Status" }, new int[] { 9, 3, 4, 2 });

            List<TableRow> rows = new List<TableRow>();

            // Backups running right now (a live snapshot; the status workspace shows live progress). Each
            // carries its job handle so Enter here cancels it, the same as the status workspace does.
            foreach (JobEntry active in _Jobs)
                rows.Add(new TableRow(new[] { "now", active.PolicyName, active.Label, active.Cancelling ? "canceling" : "running" }, new RunningJobRow(active.Id)));
            if (_Busy && !String.IsNullOrEmpty(_ActivityText))
                rows.Add(new TableRow(new[] { "now", "—", _ActivityText!, "running" }, null));

            // Upcoming scheduled runs, soonest first.
            List<Schedule> ordered = new List<Schedule>(schedules);
            ordered.Sort((a, b) =>
            {
                DateTime ax = a.NextRunUtc ?? DateTime.MaxValue;
                DateTime bx = b.NextRunUtc ?? DateTime.MaxValue;
                return ax.CompareTo(bx);
            });

            foreach (Schedule schedule in ordered)
            {
                string policyName = policyNames.TryGetValue(schedule.PolicyId, out string? name) ? name : schedule.PolicyId;
                string when = schedule.Enabled && schedule.NextRunUtc.HasValue ? FormatTimestamp(schedule.NextRunUtc.Value) : "—";
                rows.Add(new TableRow(new[]
                {
                    when,
                    policyName,
                    DescribeCron(schedule.CronExpression),
                    schedule.Enabled ? "scheduled" : "paused",
                }, schedule));
            }

            Content().SetHeadings("Runs — upcoming & in progress", new[] { Hint("↑↓", "Select"), Hint("↵", "Cancel running"), Hint("F5", "Refresh"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
            Content().SetRows(rows, "Nothing running and nothing scheduled. Add a schedule under 'Schedules'.");
        }

        private async Task RestorePointsForSelectedPolicyAsync()
        {
            if (_Current != Section.Policies)
            {
                await NotifyAsync("Restore points", "Select a policy under 'Policies' first, then press 'r'.").ConfigureAwait(false);
                return;
            }
            if (!(Content().SelectedTag is Policy policy))
                return;

            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync().ConfigureAwait(false);
            List<BackupJob> points = new List<BackupJob>();
            foreach (BackupJob job in jobs)
            {
                if (String.Equals(job.PolicyId, policy.Id, StringComparison.Ordinal) && !String.IsNullOrEmpty(job.ManifestKey))
                    points.Add(job);
            }
            points.Reverse(); // newest first

            if (points.Count == 0)
            {
                await NotifyAsync("No restore points", "'" + policy.Name + "' has no completed backups yet.", "Press Enter on the policy to run one now.").ConfigureAwait(false);
                return;
            }

            BackupJob? chosen = await PickAsync(
                "Restore point for '" + policy.Name + "'",
                points,
                j => (j.CompletedUtc.HasValue ? FormatTimestamp(j.CompletedUtc.Value) : j.Id) + "  " + j.BackupType + "  " + j.FileCount + " files").ConfigureAwait(false);
            if (chosen == null)
                return;

            await RestoreAsync(chosen).ConfigureAwait(false);
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

            Content().SetHeadings("Recover — choose where the backup is", new[] { Hint("↑↓", "Select"), Hint("↵", "Open"), Hint("c", "Add location"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
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
            RecoveryService recovery = new RecoveryService(_Context);
            RecoverySession? session = null;

            // Same-machine recovery: try the password already cached for any policy that writes to this target
            // before asking. A wrong cached password (or a fresh install with none) falls through to the prompt.
            foreach (string cached in await CachedPasswordsForTargetAsync(target.Id).ConfigureAwait(false))
            {
                SetStatus("Opening backup at '" + target.Name + "'.");
                try
                {
                    session = await recovery.OpenAsync(target.Id, cached).ConfigureAwait(false);
                    break;
                }
                catch (ArmorCryptoException)
                {
                    // Cached password does not unlock this repository; try the next, then prompt.
                }
                catch (Exception ex)
                {
                    await NotifyAsync("Could not open backup", ex.Message).ConfigureAwait(false);
                    return;
                }
            }

            if (session == null)
            {
                string? password = await PromptAsync("Password for the backup at '" + target.Name + "'").ConfigureAwait(false);
                if (String.IsNullOrWhiteSpace(password))
                    return;

                SetStatus("Opening backup at '" + target.Name + "'.");
                try
                {
                    session = await recovery.OpenAsync(target.Id, password!).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await NotifyAsync("Could not open backup", ex.Message).ConfigureAwait(false);
                    return;
                }
            }

            List<RecoveryPoint> points;
            try
            {
                points = await session!.BrowseAsync().ConfigureAwait(false);
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

        /// <summary>
        /// Let the user pick a folder or file to restore by navigating the backup's captured tree in the
        /// hierarchical file selector — drilling in and out — rather than scrolling one flat list of every
        /// path. Returns the chosen scope and selector, or null when nothing was chosen. The scope is derived
        /// from what was actually picked (a folder or a file), so it stays correct regardless of the entry mode.
        /// </summary>
        private async Task<(RestoreScopeEnum Scope, string Selector)?> PickFromBackupAsync(RecoverySession session, RecoveryPoint point, bool showFiles, string title)
        {
            List<string> files;
            try
            {
                files = await session.ListFilesAsync(point).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await NotifyAsync("Could not read the backup contents", ex.Message).ConfigureAwait(false);
                return null;
            }

            if (files.Count == 0)
            {
                await NotifyAsync("Nothing to restore", "This backup point has no files.").ConfigureAwait(false);
                return null;
            }

            ManifestFileSystemProvider provider = new ManifestFileSystemProvider(files);
            FileSelectOptions options = new FileSelectOptions
            {
                Title = title,
                ShowFiles = showFiles,
                ShowHidden = true,
            };

            FileSelection? selection = await App().ShowAsync<FileSelection>(new FileSelectModal(options, provider)).ConfigureAwait(false);
            if (selection == null || selection.Includes.Count == 0)
                return null;

            string chosen = selection.Includes[0];
            RestoreScopeEnum scope = provider.IsDirectory(chosen) ? RestoreScopeEnum.Folder : RestoreScopeEnum.File;
            return (scope, chosen);
        }

        /// <summary>
        /// The distinct cached passwords for the encryption keys used by any policy that writes to the given
        /// target. Lets recovery open a target without prompting on the machine that made the backup.
        /// </summary>
        private async Task<List<string>> CachedPasswordsForTargetAsync(string targetId)
        {
            List<string> passwords = new List<string>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);
            EncryptionKeyService keyService = new EncryptionKeyService(_Context.Database);

            List<Policy> policies = await _Context.Database.Policies.ReadAllAsync().ConfigureAwait(false);
            foreach (Policy policy in policies)
            {
                if (!String.Equals(policy.StorageTargetId, targetId, StringComparison.Ordinal))
                    continue;
                if (String.IsNullOrEmpty(policy.EncryptionKeyId) || !seenKeys.Add(policy.EncryptionKeyId!))
                    continue;

                string? password = await keyService.TryReadCachedPasswordAsync(policy.EncryptionKeyId!, _Context.Paths, _Context.CredentialProtector).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(password))
                    passwords.Add(password!);
            }
            return passwords;
        }

        private void ShowRecoveryCatalog(string targetName, List<RecoveryPoint> points)
        {
            Content().SetColumns(new[] { "When", "Type", "Files", "Size", "Policy" }, new int[] { 9, 2, 2, 2, 3 });

            List<TableRow> rows = new List<TableRow>();
            foreach (RecoveryPoint point in points)
            {
                rows.Add(new TableRow(new[]
                {
                    FormatTimestamp(point.PointInTimeUtc),
                    point.BackupType.ToString(),
                    point.FileCount.ToString(),
                    FormatBytes(point.TotalBytes),
                    point.PolicyName ?? point.PolicyId,
                }, point));
            }
            rows.Add(new TableRow(new[] { "‹ Back to locations", "", "", "", "" }, RecoverBackRow.Instance));

            Content().SetHeadings("Recover — " + targetName + " (" + points.Count + " backup" + (points.Count == 1 ? "" : "s") + ")", new[] { Hint("↑↓", "Select"), Hint("↵", "Restore"), Hint("s", "Stats"), Hint("Esc", "Nav"), Hint("^Q", "Quit") });
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

            if (scope == 1 || scope == 2)
            {
                (RestoreScopeEnum Scope, string Selector)? picked = await PickFromBackupAsync(
                    session, point,
                    showFiles: scope == 2,
                    title: scope == 2 ? "Choose a file to restore" : "Choose a folder to restore").ConfigureAwait(false);
                if (picked == null)
                    return;
                restoreScope = picked.Value.Scope;
                selector = picked.Value.Selector;
            }

            (bool proceed, string? destinationRoot) = await AskRestoreDestinationAsync().ConfigureAwait(false);
            if (!proceed)
                return;

            RestoreJob restoreJob = new RestoreJob();
            restoreJob.Scope = restoreScope;
            restoreJob.SourceSelector = selector;
            restoreJob.DestinationRoot = destinationRoot;

            // Register the recover-flow restore in the status workspace with a live progress bar, the same
            // as a policy restore. Totals come from the recovery point up front, so there is no scan phase.
            string pointLabel = point.PolicyName ?? point.PolicyId;
            string jobId = "ui_" + Guid.NewGuid().ToString("N");
            CancellationTokenSource cts = new CancellationTokenSource();
            string label = "Restore from " + point.PointInTimeUtc.ToString("u");
            JobEntry entry = new JobEntry(jobId, label, pointLabel, cts, JobKind.Restore);
            entry.FilesTotal = point.FileCount > int.MaxValue ? int.MaxValue : (int)point.FileCount;
            entry.BytesTotal = point.TotalBytes;
            _Jobs.Add(entry);
            RefreshJobView();
            SetStatus("Restoring from " + point.PointInTimeUtc.ToString("u") + ".");

            int lastPercent = -1;
            int lastFilesDone = -1;
            IProgress<RestoreProgress> progress = new DelegateProgress<RestoreProgress>(p =>
            {
                int pct = p.BytesTotal > 0
                    ? (int)(p.BytesDone * 100 / p.BytesTotal)
                    : (p.FilesTotal > 0 ? p.FilesDone * 100 / p.FilesTotal : 0);
                pct = Math.Clamp(pct, 0, 100);

                if (pct == lastPercent && p.FilesDone == lastFilesDone)
                    return;
                lastPercent = pct;
                lastFilesDone = p.FilesDone;

                int filesDone = p.FilesDone;
                int filesTotal = p.FilesTotal;
                long bytesDone = p.BytesDone;
                long bytesTotal = p.BytesTotal;
                Post(() =>
                {
                    entry.Scanning = false;
                    entry.Percent = pct;
                    entry.FilesDone = filesDone;
                    entry.FilesTotal = filesTotal;
                    entry.BytesDone = bytesDone;
                    entry.BytesTotal = bytesTotal;
                    RefreshJobView();
                });
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    RestoreJob done = await session.RestoreAsync(point, restoreJob, cts.Token, progress).ConfigureAwait(false);
                    Post(() =>
                    {
                        FinishJob(entry);
                        SetStatus("Restore " + done.Status + ": " + done.FilesRestored + " files, " + FormatBytes(done.BytesRestored) + ".");
                    });
                }
                catch (OperationCanceledException)
                {
                    Post(() =>
                    {
                        FinishJob(entry);
                        SetStatus("Restore from " + point.PointInTimeUtc.ToString("u") + " canceled.");
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        FinishJob(entry);
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

        /// <summary>
        /// Format a UTC timestamp showing both UTC and the machine's local time (with its offset), so
        /// tables read clearly regardless of the viewer's timezone. Example:
        /// <c>2026-08-21 05:42Z · 22:42 -07:00</c>.
        /// </summary>
        private static string FormatTimestamp(DateTime utc)
        {
            DateTime asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return asUtc.ToString("yyyy-MM-dd HH:mm") + "Z · " + asUtc.ToLocalTime().ToString("HH:mm zzz");
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

        /// <summary>Shorthand for a keyboard-shortcut hint used in a section heading.</summary>
        private static KeyHint Hint(string key, string label)
        {
            return new KeyHint(key, label);
        }

        /// <summary>
        /// Build the run-statistics block for a completed backup: total runtime, files and bytes backed up,
        /// files and bytes skipped, and the per-second file and byte throughput. The same lines feed the
        /// activity log and the completion modal so the two always agree.
        /// </summary>
        /// <param name="job">The finished job.</param>
        /// <param name="runtime">The wall-clock duration of the run.</param>
        /// <returns>The statistics lines, one metric per line.</returns>
        private static List<string> BuildBackupStatistics(BackupJob job, TimeSpan runtime)
        {
            double seconds = runtime.TotalSeconds;
            string runtimeText = ((int)runtime.TotalHours).ToString("D2") + ":" + runtime.Minutes.ToString("D2") + ":" + runtime.Seconds.ToString("D2");

            // "Copied" figures reflect the work actually done this run: files that were read and written
            // (not reused wholesale from an incremental baseline) and the bytes actually written to the
            // target. For a full backup that is every file; the byte total is the stored (compressed and
            // encrypted) size that landed on the target.
            double filesPerSecond = seconds > 0 ? job.CopiedFiles / seconds : 0;
            double bytesPerSecond = seconds > 0 ? job.BytesWritten / seconds : 0;

            return new List<string>
            {
                "Total runtime   : " + runtimeText,
                "Files backed up : " + job.CopiedFiles.ToString("N0") + ", " + FormatBytes(job.BytesWritten),
                "Files skipped   : " + job.SkippedFiles.ToString("N0") + ", " + FormatBytes(job.SkippedBytes),
                "Files/second    : " + filesPerSecond.ToString("N1"),
                "Bytes/second    : " + FormatBytes((long)bytesPerSecond) + "/s",
            };
        }

        /// <summary>
        /// Write a run-statistics block to the activity log, each line prefixed with a dash.
        /// </summary>
        /// <param name="stats">The statistics lines from <see cref="BuildBackupStatistics"/>.</param>
        private void LogBackupStatistics(IReadOnlyList<string> stats)
        {
            foreach (string line in stats)
                SetStatus("- " + line);
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
                case RunningJobRow running: await ManageJobAsync(running.JobId).ConfigureAwait(false); break;
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
                case Section.Targets: return CreateTargetAsync();
                case Section.Keys: return CreateKeyAsync();
                case Section.Schedules: return CreateScheduleAsync();
                case Section.Recover: return CreateTargetAsync();
                default: return NotifyAsync("Nothing to create", "This section has no create action.");
            }
        }

        private async Task CreatePolicyAsync()
        {
            List<StorageTarget> targets = await _Context.Database.StorageTargets.ReadAllAsync().ConfigureAwait(false);
            if (targets.Count == 0)
            {
                await NotifyAsync("Create policy", "No backup targets exist yet.", "Add one under 'Backup targets' first.").ConfigureAwait(false);
                return;
            }

            List<EncryptionKey> keys = await _Context.Database.EncryptionKeys.ReadAllAsync().ConfigureAwait(false);
            if (keys.Count == 0)
            {
                await NotifyAsync("Create policy", "No encryption passwords exist yet.", "Create one under 'Encryption passwords' first.").ConfigureAwait(false);
                return;
            }

            string? name = await PromptAsync("Policy name").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;

            FileSelection? browse = await BrowseAsync().ConfigureAwait(false);
            if (browse == null || browse.Includes.Count == 0)
                return;

            // Seed the exclude editor with the shared global list so a new policy shows — and lets the user
            // trim — the default excludes (.git, bin, obj, node_modules, …) instead of an empty list. The
            // seeded rules become the policy's own, and the policy opts out of the global list below so the
            // visible rules are exactly what is applied: removing a shown rule actually un-excludes it,
            // rather than leaving a hidden global layer to re-apply it.
            List<ExcludePattern> globalSeed = await _Context.Database.GlobalExcludes.ReadAllAsync().ConfigureAwait(false);
            if (globalSeed.Count == 0)
                globalSeed = GlobalExcludeDefaults.Create();

            // The seeded global defaults, any extra name/wildcard/regex rules the user adds, plus the
            // browser's own excluded holes. A cancelled editor keeps the seeded defaults rather than
            // dropping them.
            List<ExcludePattern>? manual = await EditExcludePatternsAsync(globalSeed).ConfigureAwait(false);
            List<ExcludePattern> excludes = manual ?? globalSeed;
            excludes.AddRange(EncodeUiExcludes(browse.Excludes));

            StorageTarget? target = await PickAsync("Where should backups be stored?", targets, t => t.Name + " [" + t.Type + "]").ConfigureAwait(false);
            if (target == null)
                return;

            EncryptionKey? key = await PickAsync("Which encryption password?", keys, k => k.Name).ConfigureAwait(false);
            if (key == null)
                return;

            int typeIndex = await App().SelectAsync("Backup type", "Full", "Incremental", "Differential").ConfigureAwait(false);
            if (typeIndex < 0)
                return;

            Policy policy = new Policy();
            policy.Name = name!;
            foreach (string includePath in browse.Includes)
                policy.IncludePaths.Add(includePath);
            policy.ExcludePatterns = excludes;

            // The global defaults are now materialized as this policy's own (visible, editable) rules, so
            // opt out of the shared global list to avoid applying them twice and to keep the editor's rules
            // authoritative.
            policy.UseGlobalExcludes = false;
            policy.StorageTargetId = target.Id;
            policy.EncryptionKeyId = key.Id;
            policy.BackupType = typeIndex == 1 ? BackupTypeEnum.Incremental : (typeIndex == 2 ? BackupTypeEnum.Differential : BackupTypeEnum.Full);

            await _Context.Database.Policies.CreateAsync(policy).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Created policy '" + policy.Name + "'.");
        }

        private async Task CreateTargetAsync()
        {
            int typeIndex = await App().SelectAsync(
                "What kind of backup target?",
                "Local folder or USB drive",
                "Amazon S3 (or S3-compatible)",
                "Azure Blob Storage",
                "Google Cloud Storage",
                "CIFS / SMB share",
                "NFS export").ConfigureAwait(false);
            if (typeIndex < 0)
                return;

            string? name = await PromptAsync("Backup target name").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;

            StorageTarget target = new StorageTarget();
            target.Name = name!;

            // Collect the fields this target type needs. Any required field left blank cancels.
            switch (typeIndex)
            {
                case 0:
                {
                    target.Type = StorageTargetTypeEnum.Disk;
                    string? path = await PromptRequiredAsync("Folder where backups are stored").ConfigureAwait(false);
                    if (path == null) return;
                    target.DiskPath = path;
                    break;
                }
                case 1:
                {
                    target.Type = StorageTargetTypeEnum.AmazonS3;
                    string? accessKey = await PromptRequiredAsync("Access key").ConfigureAwait(false);
                    if (accessKey == null) return;
                    string? secretKey = await PromptRequiredAsync("Secret key").ConfigureAwait(false);
                    if (secretKey == null) return;
                    string? region = await PromptRequiredAsync("Region", "us-east-1").ConfigureAwait(false);
                    if (region == null) return;
                    string? bucket = await PromptRequiredAsync("Bucket").ConfigureAwait(false);
                    if (bucket == null) return;
                    target.AccessKey = accessKey;
                    target.SecretKey = secretKey;
                    target.Region = region;
                    target.Bucket = bucket;
                    string? endpoint = await PromptAsync("Custom endpoint for S3-compatible stores (blank for AWS)").ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(endpoint))
                        target.Endpoint = endpoint!.Trim();
                    break;
                }
                case 2:
                {
                    target.Type = StorageTargetTypeEnum.AzureBlob;
                    string? account = await PromptRequiredAsync("Account name").ConfigureAwait(false);
                    if (account == null) return;
                    string? accountKey = await PromptRequiredAsync("Account key").ConfigureAwait(false);
                    if (accountKey == null) return;
                    string? endpoint = await PromptRequiredAsync("Endpoint (e.g. https://acct.blob.core.windows.net)").ConfigureAwait(false);
                    if (endpoint == null) return;
                    string? container = await PromptRequiredAsync("Container").ConfigureAwait(false);
                    if (container == null) return;
                    target.AccountName = account;
                    target.AccountKey = accountKey;
                    target.Endpoint = endpoint;
                    target.Container = container;
                    break;
                }
                case 3:
                {
                    target.Type = StorageTargetTypeEnum.GoogleCloud;
                    string? project = await PromptRequiredAsync("Project id").ConfigureAwait(false);
                    if (project == null) return;
                    string? bucket = await PromptRequiredAsync("Bucket").ConfigureAwait(false);
                    if (bucket == null) return;
                    string? jsonPath = await PromptRequiredAsync("Path to service-account JSON file").ConfigureAwait(false);
                    if (jsonPath == null) return;
                    if (!File.Exists(jsonPath))
                    {
                        await NotifyAsync("File not found", "No file at " + jsonPath + ".", "Nothing was created.").ConfigureAwait(false);
                        return;
                    }
                    target.ProjectId = project;
                    target.Bucket = bucket;
                    target.CredentialJson = await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false);
                    break;
                }
                case 4:
                {
                    target.Type = StorageTargetTypeEnum.Cifs;
                    string? host = await PromptRequiredAsync("Host").ConfigureAwait(false);
                    if (host == null) return;
                    string? share = await PromptRequiredAsync("Share name").ConfigureAwait(false);
                    if (share == null) return;
                    string? user = await PromptRequiredAsync("Username").ConfigureAwait(false);
                    if (user == null) return;
                    string? password = await PromptRequiredAsync("Password").ConfigureAwait(false);
                    if (password == null) return;
                    target.Host = host;
                    target.ShareName = share;
                    target.Username = user;
                    target.Password = password;
                    break;
                }
                case 5:
                {
                    target.Type = StorageTargetTypeEnum.Nfs;
                    string? host = await PromptRequiredAsync("Host").ConfigureAwait(false);
                    if (host == null) return;
                    string? share = await PromptRequiredAsync("Export path / share").ConfigureAwait(false);
                    if (share == null) return;
                    string? version = await PromptRequiredAsync("NFS version", "V3").ConfigureAwait(false);
                    if (version == null) return;
                    target.Host = host;
                    target.ShareName = share;
                    target.NfsVersion = version;
                    break;
                }
                default:
                    return;
            }

            StorageTargetService service = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
            try
            {
                await service.CreateAsync(target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await NotifyAsync("Could not add backup target", ex.Message).ConfigureAwait(false);
                return;
            }
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Added backup target '" + target.Name + "' (" + target.Type + ").");

            // Optional validation step, then a success/result modal.
            bool validate = await ConfirmAsync("Test the connection to '" + target.Name + "' now?", "Test now", "Skip").ConfigureAwait(false);
            if (!validate)
            {
                await NotifyAsync("Backup target added", "'" + target.Name + "' was saved.", "Select it and press Enter to test the connection any time.").ConfigureAwait(false);
                return;
            }

            SetStatus("Testing '" + target.Name + "'.");
            try
            {
                bool ok = await Task.Run(() => service.ValidateAsync(target.Id)).ConfigureAwait(false);
                SetStatus("Test of '" + target.Name + "': " + (ok ? "succeeded" : "failed") + ".");
                await NotifyAsync(
                    ok ? "Backup target ready" : "Connection test failed",
                    ok ? "'" + target.Name + "' is reachable and writable." : "Could not write a test object to '" + target.Name + "'.",
                    ok ? "Backups can be stored here." : "Check the connection details, then test again with Enter.").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetStatus("Test of '" + target.Name + "' failed: " + ex.Message);
                await NotifyAsync("Connection test failed", ex.Message, "The target was saved; fix the details and test again with Enter.").ConfigureAwait(false);
            }
        }

        private async Task<string?> PromptRequiredAsync(string title, string initial = "")
        {
            string? value = await PromptAsync(title, initial).ConfigureAwait(false);
            return String.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private async Task CreateKeyAsync()
        {
            string? name = await PromptAsync("Name for this encryption password").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name))
                return;

            string? password = await PromptAsync("Password").ConfigureAwait(false);
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

            // The data is encrypted with this password; the password is cached locally so backups run
            // unattended, and it is the only secret needed to restore on a fresh machine.
            EncryptionKeyService service = new EncryptionKeyService(_Context.Database);
            ProvisionedKey provisioned = await service.ProvisionWithPasswordAsync(name!, password!, _Context.Paths, _Context.CredentialProtector, 600000).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Created encryption password '" + provisioned.Key.Name + "'.");
            await NotifyAsync(
                "Encryption password created",
                "'" + provisioned.Key.Name + "' is ready to use in a policy.",
                "Backups run unattended — this password is cached on this machine.",
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
            await NotifyAsync("Schedule created", "'" + policy.Name + "' will back up: " + built.Value.Description + ".", "All times are UTC.").ConfigureAwait(false);
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

        /// <summary>
        /// Turn a cron expression back into the plain-English phrasing the schedule builder produces,
        /// so users never have to read raw cron. Falls back to the raw expression for anything custom.
        /// </summary>
        private static string DescribeCron(string cron)
        {
            if (String.IsNullOrWhiteSpace(cron))
                return "—";

            string[] f = cron.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 5)
                return cron;

            string min = f[0], hour = f[1], dom = f[2], mon = f[3], dow = f[4];

            if (min.StartsWith("*/", StringComparison.Ordinal) && hour == "*" && dom == "*" && mon == "*" && dow == "*"
                && int.TryParse(min.Substring(2), out int everyMinutes))
                return "every " + everyMinutes + " minute" + (everyMinutes == 1 ? "" : "s");

            if (min == "0" && hour.StartsWith("*/", StringComparison.Ordinal) && dom == "*" && mon == "*" && dow == "*"
                && int.TryParse(hour.Substring(2), out int everyHours))
                return "every " + everyHours + " hour" + (everyHours == 1 ? "" : "s") + ", on the hour";

            if (int.TryParse(min, out int mm) && int.TryParse(hour, out int hh) && mon == "*"
                && mm >= 0 && mm <= 59 && hh >= 0 && hh <= 23)
            {
                string time = hh.ToString("D2") + ":" + mm.ToString("D2") + " UTC";
                if (dom == "*" && dow == "*")
                    return "every day at " + time;
                if (dom == "*" && dow != "*")
                    return "every " + DescribeDow(dow) + " at " + time;
                if (dom != "*" && dow == "*")
                    return "day " + dom + " of each month at " + time;
            }

            return cron;
        }

        private static string DescribeDow(string dow)
        {
            if (dow == "1-5")
                return "weekday";
            if (dow == "0,6")
                return "weekend day";
            if (int.TryParse(dow, out int d) && d >= 0 && d <= 6)
                return DayName(d);
            return "days " + dow;
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
                    "It is the encryption password for " + Count(dependents.Count, "policy", "policies") + ":",
                    NameList(dependents),
                    "Reassign or delete " + (dependents.Count == 1 ? "that policy" : "those policies") + " first.").ConfigureAwait(false);
                return;
            }

            if (!await ConfirmAsync("Delete encryption password '" + key.Name + "'? Backups already made with it will become unrecoverable.").ConfigureAwait(false))
                return;

            await _Context.Database.EncryptionKeys.DeleteAsync(key.Id).ConfigureAwait(false);
            TryDeleteCachedSecrets(key.Id);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Deleted encryption password '" + key.Name + "'.");
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
                    "It is the backup target for " + Count(dependents.Count, "policy", "policies") + ":",
                    NameList(dependents),
                    "Delete or repoint " + (dependents.Count == 1 ? "that policy" : "those policies") + " first.").ConfigureAwait(false);
                return;
            }

            if (!await ConfirmAsync("Remove backup target '" + target.Name + "' from Armor?").ConfigureAwait(false))
                return;

            // Offer to also destroy the backup data stored there.
            bool isDisk = target.Type == StorageTargetTypeEnum.Disk && !String.IsNullOrWhiteSpace(target.DiskPath);
            int dataChoice = await App().SelectAsync(
                "Also delete the backup data stored at '" + target.Name + "'? This cannot be undone.",
                "Keep the backup data",
                isDisk ? "Delete all data and remove the folder" : "Delete all backup data").ConfigureAwait(false);
            if (dataChoice < 0)
                return;
            bool purge = dataChoice == 1;

            if (purge)
            {
                try
                {
                    if (isDisk && Directory.Exists(target.DiskPath))
                        await Task.Run(() => Directory.Delete(target.DiskPath!, true)).ConfigureAwait(false);
                    else
                    {
                        StorageTargetService purgeService = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
                        await purgeService.PurgeAsync(target.Id).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    await NotifyAsync("Could not delete the backup data", ex.Message, "The target was left in place.").ConfigureAwait(false);
                    return;
                }
            }

            await _Context.Database.StorageTargets.DeleteAsync(target.Id).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus(purge
                ? "Deleted backup target '" + target.Name + "' and its backup data."
                : "Deleted backup target '" + target.Name + "' (backup data kept).");
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

        // ---- Edit ------------------------------------------------------------

        private async Task EditSelectedAsync()
        {
            switch (Content().SelectedTag)
            {
                case Policy policy: await EditPolicyAsync(policy).ConfigureAwait(false); break;
                case StorageTarget target: await EditTargetAsync(target).ConfigureAwait(false); break;
                case Schedule schedule: await EditScheduleAsync(schedule).ConfigureAwait(false); break;
                case EncryptionKey key: await EditKeyAsync(key).ConfigureAwait(false); break;
                default: await NotifyAsync("Nothing to edit", "Select a policy, backup target, schedule, or password first.").ConfigureAwait(false); break;
            }
        }

        private async Task EditPolicyAsync(Policy original)
        {
            // Work on a fresh copy read from the database, so Cancel simply discards it and the on-screen
            // row (a different instance) is untouched until the next reload.
            Policy? policy = await _Context.Database.Policies.ReadAsync(original.Id).ConfigureAwait(false);
            if (policy == null)
            {
                await NotifyAsync("Policy not found", "'" + original.Name + "' no longer exists.").ConfigureAwait(false);
                return;
            }

            List<StorageTarget> targets = await _Context.Database.StorageTargets.ReadAllAsync().ConfigureAwait(false);
            List<EncryptionKey> keys = await _Context.Database.EncryptionKeys.ReadAllAsync().ConfigureAwait(false);

            while (true)
            {
                SplitExcludes(policy.ExcludePatterns, out List<ExcludePattern> uiHoles, out List<ExcludePattern> manualRules);
                int choice = await App().SelectAsync(
                    "Edit policy '" + original.Name + "'",
                    "Name: " + policy.Name,
                    "Included paths: " + Count(policy.IncludePaths.Count, "item", "items") + (uiHoles.Count > 0 ? " (" + uiHoles.Count + " excluded)" : ""),
                    "Exclude patterns: " + Count(manualRules.Count, "rule", "rules"),
                    "Backup type: " + policy.BackupType,
                    "Storage target: " + FindTargetName(targets, policy.StorageTargetId),
                    "Encryption password: " + FindKeyName(keys, policy.EncryptionKeyId),
                    "Retention: " + (policy.RetentionDays <= 0 ? "keep all" : policy.RetentionDays + " days"),
                    "Parallel workers: " + policy.MaxParallelism + (policy.MaxParallelism == 1 ? " (serial)" : ""),
                    "Enabled: " + (policy.Enabled ? "yes" : "no"),
                    "Use global excludes: " + (policy.UseGlobalExcludes ? "yes" : "no"),
                    "Save changes",
                    "Cancel").ConfigureAwait(false);

                switch (choice)
                {
                    case 0:
                    {
                        string? name = await PromptAsync("Policy name", policy.Name).ConfigureAwait(false);
                        if (!String.IsNullOrWhiteSpace(name))
                            policy.Name = name!.Trim();
                        break;
                    }
                    case 1:
                    {
                        // Pre-check the current includes and pre-uncheck the browser-owned holes so the
                        // tree opens exactly as it was left; manual glob/regex rules are preserved as-is.
                        SplitExcludes(policy.ExcludePatterns, out List<ExcludePattern> uiExcludes, out List<ExcludePattern> manualExcludes);
                        List<string> excludedPaths = new List<string>();
                        foreach (ExcludePattern pattern in uiExcludes)
                        {
                            string? path = DecodeUiExcludePath(pattern);
                            if (!String.IsNullOrEmpty(path))
                                excludedPaths.Add(path!);
                        }

                        FileSelection? browse = await BrowseAsync(policy.IncludePaths, excludedPaths).ConfigureAwait(false);
                        if (browse != null && browse.Includes.Count > 0)
                        {
                            policy.IncludePaths.Clear();
                            policy.IncludePaths.AddRange(browse.Includes);

                            List<ExcludePattern> merged = new List<ExcludePattern>(manualExcludes);
                            merged.AddRange(EncodeUiExcludes(browse.Excludes));
                            policy.ExcludePatterns = merged;
                        }
                        break;
                    }
                    case 2:
                    {
                        // Only the user-typed name/wildcard/regex rules are edited here; the browser-owned
                        // path holes are preserved. Esc (null) leaves the rules unchanged.
                        SplitExcludes(policy.ExcludePatterns, out List<ExcludePattern> uiExcludes, out List<ExcludePattern> manualExcludes);
                        List<ExcludePattern>? edited = await EditExcludePatternsAsync(manualExcludes).ConfigureAwait(false);
                        if (edited != null)
                        {
                            List<ExcludePattern> merged = new List<ExcludePattern>(uiExcludes);
                            merged.AddRange(edited);
                            policy.ExcludePatterns = merged;
                        }
                        break;
                    }
                    case 3:
                    {
                        int typeIndex = await App().SelectAsync("Backup type", "Full", "Incremental", "Differential").ConfigureAwait(false);
                        if (typeIndex >= 0)
                            policy.BackupType = typeIndex == 1 ? BackupTypeEnum.Incremental : (typeIndex == 2 ? BackupTypeEnum.Differential : BackupTypeEnum.Full);
                        break;
                    }
                    case 4:
                    {
                        StorageTarget? target = await PickAsync("Where should backups be stored?", targets, t => t.Name + " [" + t.Type + "]").ConfigureAwait(false);
                        if (target != null)
                            policy.StorageTargetId = target.Id;
                        break;
                    }
                    case 5:
                    {
                        EncryptionKey? key = await PickAsync("Which encryption password?", keys, k => k.Name).ConfigureAwait(false);
                        if (key != null)
                            policy.EncryptionKeyId = key.Id;
                        break;
                    }
                    case 6:
                    {
                        int? days = await PromptIntAsync("Retention days (0 keeps every backup)", 0, 3650, policy.RetentionDays.ToString()).ConfigureAwait(false);
                        if (days != null)
                            policy.RetentionDays = days.Value;
                        break;
                    }
                    case 7:
                    {
                        int? workers = await PromptIntAsync(
                            "Parallel workers — files processed at once (1 is serial)",
                            Policy.MinParallelism,
                            Policy.MaxParallelismLimit,
                            policy.MaxParallelism.ToString()).ConfigureAwait(false);
                        if (workers != null)
                            policy.MaxParallelism = workers.Value;
                        break;
                    }
                    case 8:
                        policy.Enabled = !policy.Enabled;
                        break;
                    case 9:
                        policy.UseGlobalExcludes = !policy.UseGlobalExcludes;
                        break;
                    case 10:
                        await _Context.Database.Policies.UpdateAsync(policy).ConfigureAwait(false);
                        await LoadCurrentSectionAsync().ConfigureAwait(false);
                        SetStatus("Updated policy '" + policy.Name + "'.");
                        return;
                    default:
                        return; // Cancel or Esc.
                }
            }
        }

        private async Task EditTargetAsync(StorageTarget original)
        {
            StorageTargetService service = new StorageTargetService(_Context.Database, _Context.CredentialProtector);
            StorageTarget? target = await service.ReadDecryptedAsync(original.Id).ConfigureAwait(false);
            if (target == null)
            {
                await NotifyAsync("Backup target not found", "'" + original.Name + "' no longer exists.").ConfigureAwait(false);
                return;
            }

            while (true)
            {
                // The name is always editable; the remaining fields depend on the target type. The type
                // itself is not editable — changing it would strand the data already stored there.
                List<string> labels = new List<string> { "Name: " + target.Name };
                List<Func<Task>> actions = new List<Func<Task>> { async () =>
                {
                    string? name = await PromptAsync("Backup target name", target.Name).ConfigureAwait(false);
                    if (!String.IsNullOrWhiteSpace(name))
                        target.Name = name!.Trim();
                } };

                AddTargetTypeFields(target, labels, actions);

                labels.Add("Save changes");
                labels.Add("Cancel");

                int choice = await App().SelectAsync("Edit backup target '" + original.Name + "'  [" + target.Type + "]", labels.ToArray()).ConfigureAwait(false);
                if (choice < 0)
                    return;
                if (choice == labels.Count - 1)
                    return; // Cancel.
                if (choice == labels.Count - 2)
                {
                    try
                    {
                        await service.UpdateAsync(target).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await NotifyAsync("Could not save the backup target", ex.Message).ConfigureAwait(false);
                        return;
                    }
                    await LoadCurrentSectionAsync().ConfigureAwait(false);
                    SetStatus("Updated backup target '" + target.Name + "'.");
                    return;
                }

                await actions[choice]().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Append the editable, type-specific fields for a storage target to a parallel label/action
        /// list used by the edit menu. Secret fields keep their current value when left blank.
        /// </summary>
        private void AddTargetTypeFields(StorageTarget target, List<string> labels, List<Func<Task>> actions)
        {
            switch (target.Type)
            {
                case StorageTargetTypeEnum.Disk:
                    AddTextField(labels, actions, "Folder", target.DiskPath, v => target.DiskPath = v);
                    break;
                case StorageTargetTypeEnum.AmazonS3:
                    AddTextField(labels, actions, "Access key", target.AccessKey, v => target.AccessKey = v);
                    AddSecretField(labels, actions, "Secret key", () => target.SecretKey, v => target.SecretKey = v);
                    AddTextField(labels, actions, "Region", target.Region, v => target.Region = v);
                    AddTextField(labels, actions, "Bucket", target.Bucket, v => target.Bucket = v);
                    AddOptionalField(labels, actions, "Custom endpoint", target.Endpoint, v => target.Endpoint = v);
                    break;
                case StorageTargetTypeEnum.AzureBlob:
                    AddTextField(labels, actions, "Account name", target.AccountName, v => target.AccountName = v);
                    AddSecretField(labels, actions, "Account key", () => target.AccountKey, v => target.AccountKey = v);
                    AddTextField(labels, actions, "Endpoint", target.Endpoint, v => target.Endpoint = v);
                    AddTextField(labels, actions, "Container", target.Container, v => target.Container = v);
                    break;
                case StorageTargetTypeEnum.GoogleCloud:
                    AddTextField(labels, actions, "Project id", target.ProjectId, v => target.ProjectId = v);
                    AddTextField(labels, actions, "Bucket", target.Bucket, v => target.Bucket = v);
                    labels.Add("Service-account JSON: replace from file");
                    actions.Add(async () =>
                    {
                        string? path = await PromptAsync("Path to service-account JSON file (blank to keep current)").ConfigureAwait(false);
                        if (String.IsNullOrWhiteSpace(path))
                            return;
                        if (!File.Exists(path))
                        {
                            await NotifyAsync("File not found", "No file at " + path + ".", "The credential was left unchanged.").ConfigureAwait(false);
                            return;
                        }
                        target.CredentialJson = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                    });
                    break;
                case StorageTargetTypeEnum.Cifs:
                    AddTextField(labels, actions, "Host", target.Host, v => target.Host = v);
                    AddTextField(labels, actions, "Share name", target.ShareName, v => target.ShareName = v);
                    AddTextField(labels, actions, "Username", target.Username, v => target.Username = v);
                    AddSecretField(labels, actions, "Password", () => target.Password, v => target.Password = v);
                    break;
                case StorageTargetTypeEnum.Nfs:
                    AddTextField(labels, actions, "Host", target.Host, v => target.Host = v);
                    AddTextField(labels, actions, "Export path / share", target.ShareName, v => target.ShareName = v);
                    AddTextField(labels, actions, "NFS version", target.NfsVersion, v => target.NfsVersion = v);
                    break;
                default:
                    break;
            }
        }

        private void AddTextField(List<string> labels, List<Func<Task>> actions, string label, string? current, Action<string> set)
        {
            labels.Add(label + ": " + (String.IsNullOrEmpty(current) ? "—" : current));
            actions.Add(async () =>
            {
                string? value = await PromptAsync(label, current ?? String.Empty).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(value))
                    set(value!.Trim());
            });
        }

        private void AddOptionalField(List<string> labels, List<Func<Task>> actions, string label, string? current, Action<string?> set)
        {
            labels.Add(label + ": " + (String.IsNullOrEmpty(current) ? "—" : current));
            actions.Add(async () =>
            {
                // A single "-" clears the value; blank keeps it as-is.
                string? value = await PromptAsync(label + " (\"-\" to clear)", current ?? String.Empty).ConfigureAwait(false);
                if (value == null)
                    return;
                string trimmed = value.Trim();
                if (trimmed == "-")
                    set(null);
                else if (trimmed.Length > 0)
                    set(trimmed);
            });
        }

        private void AddSecretField(List<string> labels, List<Func<Task>> actions, string label, Func<string?> get, Action<string> set)
        {
            bool hasValue = !String.IsNullOrEmpty(get());
            labels.Add(label + ": " + (hasValue ? "•••••• (set)" : "not set"));
            actions.Add(async () =>
            {
                string? value = await PromptAsync(label + " (blank keeps the current value)").ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(value))
                    set(value!.Trim());
            });
        }

        private async Task EditScheduleAsync(Schedule original)
        {
            Schedule? schedule = await _Context.Database.Schedules.ReadAsync(original.Id).ConfigureAwait(false);
            if (schedule == null)
            {
                await NotifyAsync("Schedule not found", "This schedule no longer exists.").ConfigureAwait(false);
                return;
            }

            Dictionary<string, string> policyNames = await BuildPolicyNameMapAsync().ConfigureAwait(false);

            while (true)
            {
                string policyName = policyNames.TryGetValue(schedule.PolicyId, out string? n) ? n : schedule.PolicyId;
                int choice = await App().SelectAsync(
                    "Edit schedule for '" + policyName + "'",
                    "Policy: " + policyName,
                    "Frequency: " + DescribeCron(schedule.CronExpression),
                    "Enabled: " + (schedule.Enabled ? "yes" : "no"),
                    "Save changes",
                    "Cancel").ConfigureAwait(false);

                switch (choice)
                {
                    case 0:
                    {
                        Policy? policy = await PickPolicyAsync("Attach this schedule to which policy?").ConfigureAwait(false);
                        if (policy != null)
                            schedule.PolicyId = policy.Id;
                        break;
                    }
                    case 1:
                    {
                        (string Cron, string Description)? built = await BuildScheduleAsync().ConfigureAwait(false);
                        if (built != null)
                        {
                            schedule.CronExpression = built.Value.Cron;
                            // Recompute the next run from the new frequency on the following tick.
                            schedule.NextRunUtc = null;
                        }
                        break;
                    }
                    case 2:
                        schedule.Enabled = !schedule.Enabled;
                        break;
                    case 3:
                        await _Context.Database.Schedules.UpdateAsync(schedule).ConfigureAwait(false);
                        await LoadCurrentSectionAsync().ConfigureAwait(false);
                        SetStatus("Updated schedule for '" + policyName + "'.");
                        return;
                    default:
                        return; // Cancel or Esc.
                }
            }
        }

        private async Task EditKeyAsync(EncryptionKey original)
        {
            EncryptionKey? key = await _Context.Database.EncryptionKeys.ReadAsync(original.Id).ConfigureAwait(false);
            if (key == null)
            {
                await NotifyAsync("Password not found", "'" + original.Name + "' no longer exists.").ConfigureAwait(false);
                return;
            }

            // The password itself cannot be changed without re-wrapping the data key, so only the display
            // name is editable here.
            string? name = await PromptAsync("Rename encryption password", key.Name).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(name) || String.Equals(name!.Trim(), key.Name, StringComparison.Ordinal))
                return;

            key.Name = name.Trim();
            await _Context.Database.EncryptionKeys.UpdateAsync(key).ConfigureAwait(false);
            await LoadCurrentSectionAsync().ConfigureAwait(false);
            SetStatus("Renamed encryption password to '" + key.Name + "'.");
        }

        private static string FindTargetName(List<StorageTarget> targets, string? id)
        {
            if (String.IsNullOrEmpty(id))
                return "(none)";
            foreach (StorageTarget target in targets)
                if (String.Equals(target.Id, id, StringComparison.Ordinal))
                    return target.Name;
            return "(unknown)";
        }

        private static string FindKeyName(List<EncryptionKey> keys, string? id)
        {
            if (String.IsNullOrEmpty(id))
                return "(none)";
            foreach (EncryptionKey key in keys)
                if (String.Equals(key.Id, id, StringComparison.Ordinal))
                    return key.Name;
            return "(unknown)";
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
            if (String.IsNullOrWhiteSpace(policy.EncryptionKeyId))
            {
                await NotifyAsync("Cannot back up", "Policy '" + policy.Name + "' has no encryption password assigned.").ConfigureAwait(false);
                return;
            }

            // Backups run concurrently, but the same policy cannot run twice at once (the engine's run
            // lock enforces this across processes; here we give a friendlier answer before starting).
            foreach (JobEntry active in _Jobs)
            {
                if (active.Kind == JobKind.Backup && String.Equals(active.PolicyName, policy.Name, StringComparison.Ordinal))
                {
                    await NotifyAsync("Already running", "A backup of '" + policy.Name + "' is already in progress.").ConfigureAwait(false);
                    return;
                }
            }

            byte[]? dataKey = await UnlockAsync(policy.EncryptionKeyId!).ConfigureAwait(false);
            if (dataKey == null)
                return;

            string targetName = "the target";
            if (!String.IsNullOrWhiteSpace(policy.StorageTargetId))
            {
                StorageTarget? tgt = await _Context.Database.StorageTargets.ReadAsync(policy.StorageTargetId!).ConfigureAwait(false);
                if (tgt != null)
                    targetName = "'" + tgt.Name + "'";
            }
            string label = policy.BackupType + " backup to " + targetName;
            string policyId = policy.Id;
            string policyName = policy.Name;

            // Register the run so the status workspace can show its progress and offer to cancel it.
            string jobId = "ui_" + Guid.NewGuid().ToString("N");
            CancellationTokenSource cts = new CancellationTokenSource();
            JobEntry entry = new JobEntry(jobId, label, policyName, cts);
            _Jobs.Add(entry);
            RefreshJobView();
            SetStatus("Backing up '" + policy.Name + "'.");

            // Report progress into the job entry. We update whenever the percentage OR the file count
            // moves (and, during the pre-scan, whenever the running found-count moves) — updating on
            // percent alone would freeze the file count when a large total makes each file a tiny fraction
            // of a percent. Each file involves real I/O, so a post per completed file does not flood the UI.
            int lastPercent = -1;
            int lastScanFiles = -1;
            int lastFilesDone = -1;
            bool lastScanning = true;
            IProgress<BackupProgress> progress = new DelegateProgress<BackupProgress>(p =>
            {
                int pct = p.BytesTotal > 0
                    ? (int)(p.BytesDone * 100 / p.BytesTotal)
                    : (p.FilesTotal > 0 ? p.FilesDone * 100 / p.FilesTotal : 0);
                pct = Math.Clamp(pct, 0, 100);

                bool changed = pct != lastPercent
                    || p.Scanning != lastScanning
                    || (p.Scanning && p.FilesTotal != lastScanFiles)
                    || (!p.Scanning && p.FilesDone != lastFilesDone);
                if (!changed)
                    return;
                lastPercent = pct;
                lastScanning = p.Scanning;
                lastScanFiles = p.FilesTotal;
                lastFilesDone = p.FilesDone;

                bool scanning = p.Scanning;
                int filesDone = p.FilesDone;
                int filesTotal = p.FilesTotal;
                long bytesDone = p.BytesDone;
                long bytesTotal = p.BytesTotal;
                Post(() =>
                {
                    entry.Scanning = scanning;
                    entry.Percent = pct;
                    entry.FilesDone = filesDone;
                    entry.FilesTotal = filesTotal;
                    entry.BytesDone = bytesDone;
                    entry.BytesTotal = bytesTotal;
                    RefreshJobView();
                });
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    BackupService service = new BackupService(_Context);
                    DateTime startedAt = DateTime.UtcNow;
                    BackupJob job = await service.RunAsync(policyId, dataKey, null, true, cts.Token, progress).ConfigureAwait(false);
                    TimeSpan runtime = DateTime.UtcNow - startedAt;
                    Post(() =>
                    {
                        FinishJob(entry);
                        string summary = "Backup " + job.Status + ": " + job.FileCount + " files, " + job.ChunksWritten + " new / " + job.ChunksReused + " reused chunks.";
                        List<string> stats = BuildBackupStatistics(job, runtime);
                        SetStatus(summary);
                        LogBackupStatistics(stats);
                        ShowBackupResultModal("Backup complete", policyName, summary, stats);
                        if (_Current == Section.Jobs || _Current == Section.Runs)
                            Launch(LoadCurrentSectionAsync);
                    });
                }
                catch (OperationCanceledException)
                {
                    Post(() =>
                    {
                        FinishJob(entry);
                        SetStatus("Backup of '" + policyName + "' canceled.");
                        if (_Current == Section.Jobs || _Current == Section.Runs)
                            Launch(LoadCurrentSectionAsync);
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        FinishJob(entry);
                        SetStatus("Backup of '" + policyName + "' failed: " + ex.Message);
                        ShowBackupResultModal("Backup failed", policyName, "The backup did not finish.", new List<string> { ex.Message });
                        if (_Current == Section.Jobs || _Current == Section.Runs)
                            Launch(LoadCurrentSectionAsync);
                    });
                }
            });
        }

        // ---- In-progress job management --------------------------------------

        /// <summary>
        /// Rebuild the status workspace's snapshot list from the live job registry.
        /// </summary>
        private void RefreshJobView()
        {
            List<JobSnapshot> snapshots = new List<JobSnapshot>();
            foreach (JobEntry entry in _Jobs)
            {
                snapshots.Add(new JobSnapshot(
                    entry.Id,
                    entry.Label,
                    entry.Percent,
                    entry.FilesDone,
                    entry.FilesTotal,
                    entry.BytesDone,
                    entry.BytesTotal,
                    entry.Cancelling,
                    entry.Scanning));
            }
            _JobView?.SetJobs(snapshots);
        }

        /// <summary>
        /// Remove a finished (completed, failed, or canceled) job from the registry and dispose its
        /// cancellation source, then refresh the status workspace.
        /// </summary>
        /// <param name="entry">The job to remove.</param>
        private void FinishJob(JobEntry entry)
        {
            _Jobs.Remove(entry);
            entry.Cts.Dispose();
            RefreshJobView();
        }

        /// <summary>
        /// Offer to cancel the selected in-progress backup. Invoked when the user presses Enter on a job
        /// in the status workspace.
        /// </summary>
        /// <param name="jobId">The job's process-local handle.</param>
        private async Task ManageJobAsync(string jobId)
        {
            JobEntry? entry = null;
            foreach (JobEntry candidate in _Jobs)
            {
                if (String.Equals(candidate.Id, jobId, StringComparison.Ordinal))
                {
                    entry = candidate;
                    break;
                }
            }
            if (entry == null)
                return;

            bool isRestore = entry.Kind == JobKind.Restore;
            string noun = isRestore ? "restore" : "backup";

            if (entry.Cancelling)
            {
                await NotifyAsync("Canceling", "The " + noun + " of '" + entry.PolicyName + "' is already being canceled.").ConfigureAwait(false);
                return;
            }

            string question = isRestore
                ? "Cancel the restore of '" + entry.PolicyName + "'? Files already written to the destination are left in place."
                : "Cancel the backup of '" + entry.PolicyName + "'? Work done so far is discarded; the previous restore point is unaffected.";
            bool cancel = await ConfirmAsync(
                question,
                "Cancel " + noun,
                "Leave it running").ConfigureAwait(false);
            if (!cancel)
                return;

            // The job may have finished while the prompt was open.
            if (!_Jobs.Contains(entry))
                return;

            entry.Cancelling = true;
            RefreshJobView();
            SetStatus("Canceling " + noun + " of '" + entry.PolicyName + "'.");
            entry.Cts.Cancel();
        }

        private async Task ValidateTargetAsync(StorageTarget target)
        {
            if (_Busy)
            {
                await NotifyAsync("Busy", "Another operation is still running.").ConfigureAwait(false);
                return;
            }

            _Busy = true;
            _ActivityText = "Validating '" + target.Name + "'";
            SetStatus("Validating '" + target.Name + "'.");
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
                        _ActivityText = null;
                        SetStatus("Validation of '" + targetName + "': " + (ok ? "succeeded" : "failed") + ".");
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        _Busy = false;
                        _ActivityText = null;
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
                await NotifyAsync("Cannot restore", "The policy or encryption password for this backup could not be resolved.", "You can still restore it from 'Recover' using the target and password.").ConfigureAwait(false);
                return;
            }

            byte[]? dataKey = await UnlockAsync(policy.EncryptionKeyId!).ConfigureAwait(false);
            if (dataKey == null)
                return;

            (bool proceed, string? destinationRoot) = await AskRestoreDestinationAsync().ConfigureAwait(false);
            if (!proceed)
                return;

            RestoreJob restoreJob = new RestoreJob();
            restoreJob.BackupJobId = job.Id;
            restoreJob.Scope = RestoreScopeEnum.All;
            restoreJob.DestinationRoot = destinationRoot;

            // Register the restore in the shared job registry so the status workspace shows a live progress
            // bar and offers to cancel it, exactly as a backup does. A restore's totals are known up front
            // from the backup point-in-time, so the entry starts with its totals set and no scan phase.
            string jobId = "ui_" + Guid.NewGuid().ToString("N");
            CancellationTokenSource cts = new CancellationTokenSource();
            string label = "Restore of '" + policy.Name + "'";
            JobEntry entry = new JobEntry(jobId, label, policy.Name, cts, JobKind.Restore);
            entry.FilesTotal = job.FileCount > int.MaxValue ? int.MaxValue : (int)job.FileCount;
            entry.BytesTotal = job.BytesTotal;
            _Jobs.Add(entry);
            RefreshJobView();
            SetStatus("Restoring '" + policy.Name + "'.");

            // Report progress into the job entry, updating whenever the percentage or the file count moves
            // so the bar and counts advance without flooding the UI (each file is real I/O).
            int lastPercent = -1;
            int lastFilesDone = -1;
            string policyName = policy.Name;
            IProgress<RestoreProgress> progress = new DelegateProgress<RestoreProgress>(p =>
            {
                int pct = p.BytesTotal > 0
                    ? (int)(p.BytesDone * 100 / p.BytesTotal)
                    : (p.FilesTotal > 0 ? p.FilesDone * 100 / p.FilesTotal : 0);
                pct = Math.Clamp(pct, 0, 100);

                if (pct == lastPercent && p.FilesDone == lastFilesDone)
                    return;
                lastPercent = pct;
                lastFilesDone = p.FilesDone;

                int filesDone = p.FilesDone;
                int filesTotal = p.FilesTotal;
                long bytesDone = p.BytesDone;
                long bytesTotal = p.BytesTotal;
                Post(() =>
                {
                    entry.Scanning = false;
                    entry.Percent = pct;
                    entry.FilesDone = filesDone;
                    entry.FilesTotal = filesTotal;
                    entry.BytesDone = bytesDone;
                    entry.BytesTotal = bytesTotal;
                    RefreshJobView();
                });
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    RestoreService service = new RestoreService(_Context);
                    RestoreJob done = await service.RunAsync(restoreJob, dataKey, cts.Token, progress).ConfigureAwait(false);
                    Post(() =>
                    {
                        FinishJob(entry);
                        SetStatus("Restore " + done.Status + ": " + done.FilesRestored + " files, " + FormatBytes(done.BytesRestored) + ".");
                    });
                }
                catch (OperationCanceledException)
                {
                    Post(() =>
                    {
                        FinishJob(entry);
                        SetStatus("Restore of '" + policyName + "' canceled.");
                    });
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        FinishJob(entry);
                        SetStatus("Restore of '" + policyName + "' failed: " + ex.Message);
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
            return NotifyAsync(
                "Encryption password",
                "'" + key.Name + "', created " + FormatTimestamp(key.CreatedUtc) + ".",
                unattended
                    ? "The password is cached on this machine, so backups run unattended."
                    : "Not cached here — you will be asked for the password when it is needed.",
                "With the password you can restore on a fresh install of Armor.");
        }

        private async Task ExportSelfBackupAsync()
        {
            string defaultPath = Path.Combine(_Context.Paths.RootDirectory, "armor-selfbackup.zip");
            string? destination = await PromptAsync("Self-backup zip path", defaultPath).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(destination))
                return;

            string databaseFile = _Context.Settings.DatabaseFilename ?? _Context.Paths.DefaultDatabasePath;
            SetStatus("Exporting self-backup.");
            await Task.Run(() => Armor.Core.Backup.ConfigBackup.ExportAsync(_Context.Paths.ConfigFilePath, databaseFile, _Context.Paths.StateDirectory, destination!)).ConfigureAwait(false);
            SetStatus("Self-backup written to " + destination + ".");
            await NotifyAsync("Self-backup exported", "Written to " + destination + ".").ConfigureAwait(false);
        }

        private Task ShowHelpAsync()
        {
            string[] lines =
            {
                HelpRow("↑/↓", "Move selection"),
                HelpRow("TAB/ESC", "Move focus across the nav, workspace, status area, and activity log"),
                HelpRow("ENTER", "Run the section action (back up / validate / restore / toggle)"),
                HelpRow("c/INS", "Create a new item"),
                HelpRow("e/F2", "Edit the selected policy, target, schedule, or password"),
                HelpRow("d/DEL", "Delete the selected item"),
                HelpRow("r", "Restore points for the selected policy"),
                HelpRow("F5", "Refresh the current section"),
                HelpRow("s", "Show backup statistics"),
                HelpRow("g", "Manage the shared global exclude list"),
                HelpRow("x", "Export a self-backup"),
                HelpRow("", ""),
                HelpRow("Cancel a run", "In 'Runs' press ENTER on it, or TAB to the status area"),
                HelpRow("Status area", "TAB to it, ↑/↓ to pick a running backup, ENTER to cancel it"),
                HelpRow("Activity log", "TAB to it; ↑/↓ PgUp/PgDn scroll; c copy all; x clear"),
                HelpRow("", ""),
                HelpRow("F1", "Show this help"),
                HelpRow("CTRL+Q", "Quit"),
            };

            // Left-aligned so the two columns line up (the splash modal centers by default).
            return App().ShowAsync(new ArmorSplashModal("Keyboard shortcuts", lines, "Press any key to continue", centered: false));
        }

        private static string HelpRow(string keys, string description)
        {
            return keys.PadRight(11) + description;
        }

        // ---- Unlock ----------------------------------------------------------

        private async Task<byte[]?> UnlockAsync(string keyId)
        {
            EncryptionKey? key = await _Context.Database.EncryptionKeys.ReadAsync(keyId).ConfigureAwait(false);
            if (key == null)
            {
                await NotifyAsync("Cannot unlock", "The encryption password for this policy was not found.").ConfigureAwait(false);
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

            // The built-in select modal supports Up/Down, PageUp/PageDown, and Home/End since TUIKit 0.8.2.
            int index = await App().SelectAsync(title, options).ConfigureAwait(false);
            if (index < 0 || index >= items.Count)
                return null;
            return items[index];
        }

        private Task<string?> PromptAsync(string title, string initial = "")
        {
            return App().PromptAsync(title, initial);
        }

        /// <summary>
        /// Open TUIKit's hierarchical file selector and return the chosen includes and excluded holes, or
        /// null when the user cancels. Previously-selected includes and holes are pre-checked and revealed.
        /// </summary>
        private async Task<FileSelection?> BrowseAsync(IEnumerable<string>? included = null, IEnumerable<string>? excluded = null)
        {
            FileSelectOptions options = new FileSelectOptions();
            options.Title = "Select folders and files to back up";
            if (included != null)
                options.PreCheckedIncludes = new List<string>(included);
            if (excluded != null)
                options.PreCheckedExcludes = new List<string>(excluded);

            return await App().ShowAsync<FileSelection>(new FileSelectModal(options)).ConfigureAwait(false);
        }

        // Exclude holes that the file browser creates are stored as full-path regex rules, tagged with a
        // no-op regex comment so they can be told apart from patterns the user typed and round-tripped
        // back into the browser on a later edit.
        private const string UiExcludeMarker = "(?#armor)";

        private static bool IsUiPathExclude(ExcludePattern pattern)
        {
            return pattern != null && pattern.IsRegex && pattern.Pattern != null && pattern.Pattern.StartsWith(UiExcludeMarker, StringComparison.Ordinal);
        }

        /// <summary>
        /// Turn the browser's excluded holes into full-path regex exclude rules (directory- or
        /// file-targeted), each tagged so it can be recognized and round-tripped on a later edit.
        /// </summary>
        private static List<ExcludePattern> EncodeUiExcludes(IReadOnlyList<FileExclusion> exclusions)
        {
            List<ExcludePattern> patterns = new List<ExcludePattern>();
            if (exclusions == null)
                return patterns;

            foreach (FileExclusion exclusion in exclusions)
            {
                string normalized = exclusion.Path.Replace('\\', '/');
                string regex = UiExcludeMarker + "^" + Regex.Escape(normalized) + "$";
                ExcludeTargetEnum target = exclusion.IsDirectory ? ExcludeTargetEnum.Directory : ExcludeTargetEnum.File;
                patterns.Add(new ExcludePattern(regex, true, target));
            }
            return patterns;
        }

        /// <summary>
        /// Recover the absolute path from a browser-generated exclude rule so it can be re-unchecked in
        /// the browser. Returns null when the rule is not a recognizable browser exclusion.
        /// </summary>
        private static string? DecodeUiExcludePath(ExcludePattern pattern)
        {
            if (!IsUiPathExclude(pattern))
                return null;

            string body = pattern.Pattern.Substring(UiExcludeMarker.Length);
            if (body.StartsWith("^", StringComparison.Ordinal))
                body = body.Substring(1);
            if (body.EndsWith("$", StringComparison.Ordinal))
                body = body.Substring(0, body.Length - 1);

            string normalized;
            try
            {
                normalized = Regex.Unescape(body);
            }
            catch (Exception)
            {
                return null;
            }
            return normalized.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Split a policy's exclude rules into the ones the file browser owns (path holes) and the ones
        /// the user typed (globs and free-form regexes).
        /// </summary>
        /// <summary>
        /// Manage the shared global exclude list — the rules applied to every policy that has "use global
        /// excludes" turned on. The list can be edited in the same rule editor policies use, or reset to the
        /// built-in defaults. Bound to the 'g' key from anywhere in the dashboard.
        /// </summary>
        private async Task ManageGlobalExcludesAsync()
        {
            while (true)
            {
                List<ExcludePattern> current = await _Context.Database.GlobalExcludes.ReadAllAsync().ConfigureAwait(false);
                int choice = await App().SelectAsync(
                    "Global excludes — applied to every policy that opts in",
                    "Edit the rules: " + Count(current.Count, "rule", "rules"),
                    "Restore the built-in defaults",
                    "Close").ConfigureAwait(false);

                switch (choice)
                {
                    case 0:
                    {
                        List<ExcludePattern>? edited = await EditExcludePatternsAsync(current).ConfigureAwait(false);
                        if (edited != null)
                        {
                            await _Context.Database.GlobalExcludes.ReplaceAllAsync(edited).ConfigureAwait(false);
                            SetStatus("Saved " + Count(edited.Count, "global exclude rule", "global exclude rules") + ".");
                        }
                        break;
                    }
                    case 1:
                    {
                        if (await ConfirmAsync("Replace the global exclude list with the built-in defaults?", "Restore", "Cancel").ConfigureAwait(false))
                        {
                            List<ExcludePattern> defaults = await _Context.Database.GlobalExcludes.ResetToDefaultsAsync().ConfigureAwait(false);
                            SetStatus("Restored " + Count(defaults.Count, "default global exclude rule", "default global exclude rules") + ".");
                        }
                        break;
                    }
                    default:
                        return; // Close or Esc.
                }
            }
        }

        private static void SplitExcludes(List<ExcludePattern> all, out List<ExcludePattern> uiExcludes, out List<ExcludePattern> manualExcludes)
        {
            uiExcludes = new List<ExcludePattern>();
            manualExcludes = new List<ExcludePattern>();
            if (all == null)
                return;
            foreach (ExcludePattern pattern in all)
            {
                if (IsUiPathExclude(pattern))
                    uiExcludes.Add(pattern);
                else
                    manualExcludes.Add(pattern);
            }
        }

        /// <summary>
        /// Edit a policy's exclude rules in TUIKit's list editor: each rule is shown with a plain-English
        /// description, new rules are added inline with a live preview and validation, and the selected
        /// rule can be removed. Returns the edited rule list, or null when the user cancels (Esc) so the
        /// caller can leave the rules unchanged.
        /// </summary>
        /// <param name="initial">The rules to start from, or null for an empty list.</param>
        private async Task<List<ExcludePattern>?> EditExcludePatternsAsync(IEnumerable<ExcludePattern>? initial)
        {
            ListEditorOptions<ExcludePattern> options = new ListEditorOptions<ExcludePattern>();
            options.Parse = raw =>
            {
                string rule = (raw ?? String.Empty).Trim();
                if (rule.Length == 0)
                    return ParseResult<ExcludePattern>.Failure("Enter a rule.");
                string? error = ValidateExcludeToken(rule);
                if (error != null)
                    return ParseResult<ExcludePattern>.Failure(error);
                ExcludePattern? pattern = ToExcludePattern(rule);
                if (pattern == null)
                    return ParseResult<ExcludePattern>.Failure("Enter a rule.");
                return ParseResult<ExcludePattern>.Success(pattern);
            };
            options.Describe = pattern => DescribeExcludeToken(ToExcludeToken(pattern));
            options.Help = new List<string>
            {
                "Exclude anything you don't want backed up. Add rules one at a time:",
                "  *.docx          a wildcard — every file matching it (here, Word docs)",
                "  .git            a bare name — excludes any file OR folder named .git",
                "                  (a matching folder is skipped entirely, not descended into)",
                "  node_modules    every node_modules folder (and any such file), anywhere",
                "  cache/          a trailing / limits the rule to folders only",
                "  re:^.*/cache/   advanced: a regular expression on the full path",
            };
            options.AllowDuplicates = false;
            options.AllowEmpty = true;
            options.DedupeComparer = ExcludePatternComparer.Instance;
            options.AddPrompt = "New exclude rule (e.g. *.docx, .git/, report.pdf, re:<regex>)";
            options.EmptyText = "No exclude rules yet. Press A to add one.";

            // Re-normalize each stored rule through its token so a policy saved before bare names pruned
            // directories heals itself on open: an old ".git" (stored File-only, which never pruned the
            // folder) re-parses to Any, while an explicit ".git/" stays directory-only. Saving then
            // persists the corrected rule.
            List<ExcludePattern> seed = new List<ExcludePattern>();
            if (initial != null)
            {
                foreach (ExcludePattern pattern in initial)
                {
                    ExcludePattern? normalized = ToExcludePattern(ToExcludeToken(pattern));
                    seed.Add(normalized ?? pattern);
                }
            }

            ListEditorModal<ExcludePattern> modal = new ListEditorModal<ExcludePattern>(seed, ToExcludeToken, options);
            IReadOnlyList<ExcludePattern>? result = await App().ShowAsync<IReadOnlyList<ExcludePattern>>(modal).ConfigureAwait(false);
            return result == null ? null : new List<ExcludePattern>(result);
        }

        /// <summary>Describe an exclude rule token in plain English for the editor's rows and preview.</summary>
        private static string DescribeExcludeToken(string rule)
        {
            if (rule.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
                return "regular expression on the full path";

            bool isDirectory = rule.EndsWith("/", StringComparison.Ordinal) || rule.EndsWith("\\", StringComparison.Ordinal);
            string name = isDirectory ? rule.TrimEnd('/', '\\') : rule;
            if (name.Length == 0)
                return "(empty rule)";

            bool wildcard = name.IndexOf('*') >= 0 || name.IndexOf('?') >= 0;
            if (isDirectory)
                return wildcard ? "every folder matching " + name : "every folder named " + name;
            // A bare name (no trailing slash) excludes both files and folders of that name.
            return wildcard ? "every file or folder matching " + name : "every file or folder named " + name;
        }

        /// <summary>Validate a typed exclude rule; returns an error message, or null when the rule is fine.</summary>
        private static string? ValidateExcludeToken(string rule)
        {
            if (rule.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
            {
                string expr = rule.Substring(3);
                if (expr.EndsWith("/", StringComparison.Ordinal) || expr.EndsWith("\\", StringComparison.Ordinal))
                    expr = expr.TrimEnd('/', '\\');
                if (expr.Length == 0)
                    return "Enter a regular expression after 're:'.";
                try
                {
                    _ = new Regex(expr, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException)
                {
                    return "That is not a valid regular expression.";
                }
                return null;
            }

            string name = rule.EndsWith("/", StringComparison.Ordinal) || rule.EndsWith("\\", StringComparison.Ordinal)
                ? rule.TrimEnd('/', '\\')
                : rule;
            return name.Length == 0 ? "Enter a name before the '/'." : null;
        }

        /// <summary>Render one exclude pattern back into its editable token form (trailing / for folders, re: for regex).</summary>
        private static string ToExcludeToken(ExcludePattern pattern)
        {
            if (pattern == null || String.IsNullOrEmpty(pattern.Pattern))
                return String.Empty;
            if (pattern.IsRegex)
                return "re:" + pattern.Pattern + (pattern.Target == ExcludeTargetEnum.Directory ? "/" : String.Empty);
            return pattern.Target == ExcludeTargetEnum.Directory ? pattern.Pattern + "/" : pattern.Pattern;
        }

        /// <summary>Parse a single exclude rule token into a pattern, or null when it is empty.</summary>
        private static ExcludePattern? ToExcludePattern(string rule)
        {
            string entry = rule.Trim();
            if (entry.Length == 0)
                return null;

            bool isRegex = false;
            if (entry.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
            {
                isRegex = true;
                entry = entry.Substring(3);
            }

            // A bare name (for example ".git" or "node_modules") excludes anything of that name — both a
            // file and a directory — so the walk prunes the directory instead of descending into it. A
            // trailing slash makes the rule explicitly directory-only.
            ExcludeTargetEnum target = ExcludeTargetEnum.Any;
            if (entry.EndsWith("/", StringComparison.Ordinal) || entry.EndsWith("\\", StringComparison.Ordinal))
            {
                target = ExcludeTargetEnum.Directory;
                entry = entry.TrimEnd('/', '\\');
            }
            if (entry.Length == 0)
                return null;

            return new ExcludePattern(entry, isRegex, target);
        }

        /// <summary>
        /// Ask where a restore should write: back to each file's original path, or into a folder the
        /// user chooses. Returns whether to proceed and the destination root (null means in place).
        /// </summary>
        private async Task<(bool Proceed, string? DestinationRoot)> AskRestoreDestinationAsync()
        {
            int where = await App().SelectAsync(
                "Where should the files be restored?",
                "Back to their original locations",
                "Into a different folder").ConfigureAwait(false);
            if (where < 0)
                return (false, null);
            if (where == 0)
                return (true, null);

            string? folder = await PromptAsync("Folder to restore into").ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(folder))
                return (false, null);
            return (true, folder!);
        }

        private Task<bool> ConfirmAsync(string message)
        {
            return App().ConfirmAsync(message, "Delete", "Cancel");
        }

        private Task<bool> ConfirmAsync(string message, string confirmLabel, string cancelLabel)
        {
            return App().ConfirmAsync(message, confirmLabel, cancelLabel);
        }

        private Task NotifyAsync(string title, params string[] lines)
        {
            // Mirror the notification into the activity log so it persists after the modal is dismissed.
            SetStatus(lines.Length > 0 ? title + " — " + lines[0] : title);
            return App().ShowAsync(new ArmorSplashModal(title, lines, "Press any key to continue"));
        }

        /// <summary>
        /// Pop a modal summarizing a finished backup — the same headline and statistics already written to
        /// the activity log. Shown fire-and-forget (the caller is a UI-thread post that cannot await) and
        /// left-aligned so the aligned metric columns line up.
        /// </summary>
        /// <param name="title">The modal title, e.g. "Backup complete" or "Backup failed".</param>
        /// <param name="policyName">The policy the run belonged to; appended to the title.</param>
        /// <param name="headline">The one-line summary shown first inside the modal.</param>
        /// <param name="stats">The detail lines shown beneath the headline.</param>
        private void ShowBackupResultModal(string title, string policyName, string headline, IReadOnlyList<string> stats)
        {
            List<string> lines = new List<string> { headline };
            if (stats.Count > 0)
            {
                lines.Add(String.Empty);
                foreach (string line in stats)
                    lines.Add(line);
            }

            Launch(() => App().ShowAsync(new ArmorSplashModal(title + " — " + policyName, lines, "Press any key to close", centered: false)));
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
            // Every activity-log line carries a severity tag. Status messages the UI writes directly are
            // informational, so they are tagged [INFO] — matching the [WARN]/[ERROR]/[DEBUG] tags on the
            // mirrored engine log. No leading space: the log region's left padding supplies the gap.
            _Log?.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  [INFO] " + text);
        }

        /// <summary>
        /// Mirror a file-log message into the on-screen log pane. Called from the logging thread, so the
        /// write is marshaled onto the UI loop.
        /// </summary>
        private void OnLogMessage(string severity, string message)
        {
            TuiApplication? app = _App;
            if (app == null)
                return;
            // Always tag with the severity ([INFO]/[WARN]/[ERROR]/[DEBUG]) so every activity-log line is
            // consistently labeled, matching the [INFO] tag on the UI's own status messages.
            string prefix = "[" + severity.ToUpperInvariant() + "] ";
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + prefix + message;
            try
            {
                app.Post(() => _Log?.WriteLine(line));
            }
            catch (Exception)
            {
                // Log mirroring must never disturb the app.
            }
        }

        // ---- Statistics ------------------------------------------------------

        /// <summary>
        /// Show a modal summarizing every backup run: how many succeeded and failed, which failed, total
        /// data and runtime, average throughput, and deduplication.
        /// </summary>
        private async Task ShowStatisticsAsync()
        {
            List<BackupJob> jobs = await _Context.Database.BackupJobs.ReadAllAsync().ConfigureAwait(false);
            if (jobs.Count == 0)
            {
                await NotifyAsync("Backup statistics", "No backups have run yet.").ConfigureAwait(false);
                return;
            }

            Dictionary<string, string> names = await BuildPolicyNameMapAsync().ConfigureAwait(false);

            int completed = 0, failed = 0, canceled = 0, running = 0;
            long fileCount = 0, totalBytes = 0, written = 0, dedup = 0, chunksWritten = 0, chunksReused = 0;
            TimeSpan runtime = TimeSpan.Zero;
            double throughputSeconds = 0;
            long throughputBytes = 0;
            List<BackupJob> failures = new List<BackupJob>();
            BackupJob? latest = null;

            foreach (BackupJob job in jobs)
            {
                switch (job.Status)
                {
                    case JobStatusEnum.Completed: completed++; break;
                    case JobStatusEnum.Failed: failed++; failures.Add(job); break;
                    case JobStatusEnum.Canceled: canceled++; break;
                    case JobStatusEnum.Running: running++; break;
                    default: break;
                }

                if (job.Status == JobStatusEnum.Completed)
                {
                    fileCount += job.FileCount;
                    totalBytes += job.BytesTotal;
                    written += job.BytesWritten;
                    dedup += job.BytesDeduplicated;
                    chunksWritten += job.ChunksWritten;
                    chunksReused += job.ChunksReused;
                    if (job.StartedUtc.HasValue && job.CompletedUtc.HasValue)
                    {
                        TimeSpan span = job.CompletedUtc.Value - job.StartedUtc.Value;
                        if (span > TimeSpan.Zero)
                        {
                            runtime += span;
                            throughputSeconds += span.TotalSeconds;
                            throughputBytes += job.BytesTotal;
                        }
                    }
                }

                DateTime key = job.CompletedUtc ?? job.StartedUtc ?? job.CreatedUtc;
                if (latest == null || (latest.CompletedUtc ?? latest.StartedUtc ?? latest.CreatedUtc) < key)
                    latest = job;
            }

            // Runs left in "Running" that are not actually live this session were interrupted (crashed or
            // the process was killed) and count as failures. A genuinely in-progress run is excluded from
            // the rate because it has neither succeeded nor failed yet.
            // Count only live backups here: `running` is the number of backup runs the database still has
            // in "Running", so a concurrently live restore must not be subtracted from it.
            int liveRunning = 0;
            foreach (JobEntry active in _Jobs)
            {
                if (active.Kind == JobKind.Backup)
                    liveRunning++;
            }
            int incomplete = Math.Max(0, running - liveRunning);
            int decided = completed + failed + canceled + incomplete;
            double successRate = decided > 0 ? 100.0 * completed / decided : 0;
            double megabytesPerSecond = throughputSeconds > 0 ? throughputBytes / throughputSeconds / (1024.0 * 1024.0) : 0;
            double dedupPercent = totalBytes > 0 ? 100.0 * dedup / totalBytes : 0;

            List<string> lines = new List<string>();
            lines.Add(Stat("Runs", jobs.Count + "   (" + completed + " completed · " + failed + " failed · " + canceled + " canceled" + (incomplete > 0 ? " · " + incomplete + " interrupted" : "") + ")"));
            lines.Add(Stat("Success rate", successRate.ToString("0.0") + "%   (" + completed + " of " + decided + " finished runs)"));

            // Live in-progress runs (this session), shown separately from the completed-run totals.
            if (_Jobs.Count > 0)
            {
                lines.Add("");
                lines.Add("Currently running:");
                foreach (JobEntry active in _Jobs)
                {
                    string detail = active.Scanning
                        ? "scanning — " + active.FilesTotal + " files found (" + FormatBytes(active.BytesTotal) + ")"
                        : active.Percent + "%  ·  " + active.FilesDone + " / " + active.FilesTotal + " files  ·  " + FormatBytes(active.BytesDone) + " of " + FormatBytes(active.BytesTotal);
                    lines.Add("   " + active.Label + " — " + detail);
                }
            }

            lines.Add("");
            lines.Add("Completed runs:");
            lines.Add(Stat("Files", fileCount.ToString()));
            lines.Add(Stat("Data processed", FormatBytes(totalBytes)));
            lines.Add(Stat("Stored", FormatBytes(written) + "   (" + chunksWritten + " chunks written)"));
            lines.Add(Stat("Deduplicated", FormatBytes(dedup) + "   (" + dedupPercent.ToString("0.0") + "% of data, " + chunksReused + " chunks reused)"));
            lines.Add(Stat("Total runtime", FormatDuration(runtime)));
            lines.Add(Stat("Avg throughput", megabytesPerSecond.ToString("0.0") + " MB/s"));
            if (latest != null)
            {
                string lastWhen = latest.CompletedUtc.HasValue ? FormatTimestamp(latest.CompletedUtc.Value) : (latest.StartedUtc.HasValue ? FormatTimestamp(latest.StartedUtc.Value) : "—");
                lines.Add(Stat("Last backup", PolicyLabel(names, latest) + " — " + latest.Status + " " + lastWhen));
            }

            if (failures.Count > 0)
            {
                failures.Sort((a, b) => (b.CompletedUtc ?? b.CreatedUtc).CompareTo(a.CompletedUtc ?? a.CreatedUtc));
                lines.Add("");
                lines.Add("Recent failures:");
                int shown = 0;
                foreach (BackupJob failure in failures)
                {
                    if (shown >= 8)
                    {
                        lines.Add("   … and " + (failures.Count - 8) + " more (see the log)");
                        break;
                    }
                    shown++;
                    string when = failure.CompletedUtc.HasValue ? FormatTimestamp(failure.CompletedUtc.Value) : (failure.StartedUtc.HasValue ? FormatTimestamp(failure.StartedUtc.Value) : "—");
                    string error = String.IsNullOrWhiteSpace(failure.Error) ? "(no message)" : failure.Error!;
                    if (error.Length > 58)
                        error = error.Substring(0, 57) + "…";
                    lines.Add("   " + PolicyLabel(names, failure) + "  " + when + "  " + error);
                }
            }

            await App().ShowAsync(new ArmorSplashModal("Backup statistics", lines, "Press any key to close", centered: false)).ConfigureAwait(false);
        }

        private static string Stat(string label, string value)
        {
            return " " + (label + ":").PadRight(17) + value;
        }

        private static string PolicyLabel(Dictionary<string, string> names, BackupJob job)
        {
            return names.TryGetValue(job.PolicyId, out string? name) ? name : job.PolicyId;
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalSeconds < 1)
                return "0s";
            long total = (long)span.TotalSeconds;
            long days = total / 86400;
            long hours = (total % 86400) / 3600;
            long minutes = (total % 3600) / 60;
            long seconds = total % 60;
            string result = String.Empty;
            if (days > 0)
                result += days + "d ";
            if (hours > 0 || days > 0)
                result += hours + "h ";
            if (minutes > 0 || hours > 0 || days > 0)
                result += minutes + "m ";
            result += seconds + "s";
            return result;
        }

        /// <summary>
        /// A minimal <see cref="IProgress{T}"/> that invokes its handler synchronously on the reporting
        /// thread, so the caller controls marshaling and throttling rather than a captured context.
        /// </summary>
        private sealed class DelegateProgress<T> : IProgress<T>
        {
            private readonly Action<T> _Handler;

            public DelegateProgress(Action<T> handler)
            {
                _Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            }

            public void Report(T value)
            {
                _Handler(value);
            }
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

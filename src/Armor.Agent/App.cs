namespace Armor.Agent
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using Armor.Core.Models;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Themes.Fluent;
    using Avalonia.Threading;

    /// <summary>
    /// The Avalonia application for the Armor agent. It builds the tray icon and its menu (About, Open,
    /// Status, Exit) and starts the background agent host that runs scheduled backups. The status menu
    /// item reflects the host's current state.
    /// </summary>
    public sealed class App : Application
    {
        private AgentHost? _Host;
        private TrayIcon? _Tray;
        private NativeMenuItem? _StatusItem;
        private NativeMenu? _BackupMenu;
        private string _BackupSignature = String.Empty;

        /// <summary>
        /// Initialize application-level styles.
        /// </summary>
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        /// <summary>
        /// Complete framework initialization: build the tray and start the agent host.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            BuildTray();

            _Host = new AgentHost();
            _Host.StatusChanged += OnStatusChanged;
            _Host.Start();

            base.OnFrameworkInitializationCompleted();
        }

        private void BuildTray()
        {
            NativeMenu menu = new NativeMenu();

            NativeMenuItem about = new NativeMenuItem("About");
            about.Click += OnAbout;
            menu.Items.Add(about);

            NativeMenuItem open = new NativeMenuItem("Open");
            open.Click += OnOpen;
            menu.Items.Add(open);

            // "Back up now" holds a submenu of policies; picking one starts an interactive backup in the
            // agent. Populated once the runtime context is ready and refreshed as the policy list changes.
            NativeMenuItem backup = new NativeMenuItem("Back up now");
            _BackupMenu = new NativeMenu();
            _BackupMenu.Items.Add(new NativeMenuItem("Loading policies…") { IsEnabled = false });
            backup.Menu = _BackupMenu;
            menu.Items.Add(backup);

            _StatusItem = new NativeMenuItem("Status: Starting");
            _StatusItem.IsEnabled = false;
            menu.Items.Add(_StatusItem);

            menu.Items.Add(new NativeMenuItemSeparator());

            NativeMenuItem exit = new NativeMenuItem("Exit");
            exit.Click += OnExit;
            menu.Items.Add(exit);

            _Tray = new TrayIcon();
            _Tray.ToolTipText = "Armor — data protection for the paranoid";
            _Tray.Icon = LoadIcon();
            _Tray.Menu = menu;
            _Tray.IsVisible = true;
        }

        private static WindowIcon? LoadIcon()
        {
            try
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Armor.Agent.logo.ico");
                if (stream == null)
                    return null;
                using (stream)
                {
                    return new WindowIcon(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_StatusItem != null)
                    _StatusItem.Header = "Status: " + status;
            });

            // A status change means the context is up and a tick just ran — a cheap, natural moment to keep
            // the "Back up now" policy list current (the rebuild is skipped when the list is unchanged).
            _ = RefreshBackupMenuAsync();
        }

        private async Task RefreshBackupMenuAsync()
        {
            AgentHost? host = _Host;
            if (host == null)
                return;

            IReadOnlyList<Policy> policies = await host.ListPoliciesAsync().ConfigureAwait(true);

            // Skip the rebuild unless the set of policies actually changed, so an open menu is not disturbed
            // on every tick.
            StringBuilder signatureBuilder = new StringBuilder();
            foreach (Policy policy in policies)
                signatureBuilder.Append(policy.Id).Append('=').Append(policy.Name).Append('|');
            string signature = signatureBuilder.ToString();

            Dispatcher.UIThread.Post(() =>
            {
                if (_BackupMenu == null || signature == _BackupSignature)
                    return;
                _BackupSignature = signature;
                _BackupMenu.Items.Clear();

                if (policies.Count == 0)
                {
                    _BackupMenu.Items.Add(new NativeMenuItem("No policies configured") { IsEnabled = false });
                    return;
                }

                foreach (Policy policy in policies)
                {
                    string policyId = policy.Id;
                    NativeMenuItem item = new NativeMenuItem(policy.Name);
                    item.Click += (sender, e) => _Host?.RunPolicyBackup(policyId);
                    _BackupMenu.Items.Add(item);
                }
            });
        }

        private void OnAbout(object? sender, EventArgs e)
        {
            AboutWindow window = new AboutWindow();
            window.Show();
        }

        private void OnOpen(object? sender, EventArgs e)
        {
            _Host?.LaunchTui();
        }

        private void OnExit(object? sender, EventArgs e)
        {
            _Host?.Stop();
            if (_Tray != null)
                _Tray.IsVisible = false;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
    }
}

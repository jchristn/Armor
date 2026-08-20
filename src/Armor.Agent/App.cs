namespace Armor.Agent
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Platform;
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
                return new WindowIcon(AssetLoader.Open(new Uri("avares://Armor.Agent/Assets/logo.ico")));
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

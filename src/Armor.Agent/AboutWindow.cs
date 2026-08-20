namespace Armor.Agent
{
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// A small modeless window shown by the tray's About action, displaying the product name, tagline,
    /// version, and repository link.
    /// </summary>
    public sealed class AboutWindow : Window
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AboutWindow"/> class.
        /// </summary>
        public AboutWindow()
        {
            Title = "About Armor";
            Width = 380;
            Height = 220;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(24);
            panel.Spacing = 8;

            TextBlock name = new TextBlock();
            name.Text = "Armor";
            name.FontSize = 24;
            name.FontWeight = FontWeight.Bold;
            panel.Children.Add(name);

            TextBlock tagline = new TextBlock();
            tagline.Text = "Data protection for the paranoid.";
            panel.Children.Add(tagline);

            TextBlock version = new TextBlock();
            version.Text = "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0");
            panel.Children.Add(version);

            TextBlock repo = new TextBlock();
            repo.Text = "https://github.com/jchristn/Armor";
            panel.Children.Add(repo);

            Button close = new Button();
            close.Content = "Close";
            close.HorizontalAlignment = HorizontalAlignment.Center;
            close.Margin = new Thickness(0, 12, 0, 0);
            close.Click += (sender, args) => Close();
            panel.Children.Add(close);

            Content = panel;
        }
    }
}

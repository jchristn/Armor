namespace Armor.Agent
{
    using System;
    using System.IO;
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;

    /// <summary>
    /// A small modeless window shown by the tray's About action, displaying the product logo, name,
    /// tagline, version, and repository link.
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
            Height = 320;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(24);
            panel.Spacing = 8;
            panel.HorizontalAlignment = HorizontalAlignment.Center;

            Bitmap? logo = LoadLogo();
            if (logo != null)
            {
                Image image = new Image();
                image.Source = logo;
                image.Width = 96;
                image.Height = 96;
                image.HorizontalAlignment = HorizontalAlignment.Center;
                image.Margin = new Thickness(0, 0, 0, 8);
                panel.Children.Add(image);
            }

            TextBlock name = new TextBlock();
            name.Text = "Armor";
            name.FontSize = 24;
            name.FontWeight = FontWeight.Bold;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(name);

            TextBlock tagline = new TextBlock();
            tagline.Text = "Data protection for the paranoid.";
            tagline.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(tagline);

            TextBlock version = new TextBlock();
            version.Text = "Version " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0");
            version.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(version);

            TextBlock repo = new TextBlock();
            repo.Text = "https://github.com/jchristn/Armor";
            repo.HorizontalAlignment = HorizontalAlignment.Center;
            panel.Children.Add(repo);

            Button close = new Button();
            close.Content = "Close";
            close.HorizontalAlignment = HorizontalAlignment.Center;
            close.Margin = new Thickness(0, 12, 0, 0);
            close.Click += (sender, args) => Close();
            panel.Children.Add(close);

            Content = panel;
        }

        /// <summary>
        /// Load the embedded product logo, or null when it cannot be read.
        /// </summary>
        private static Bitmap? LoadLogo()
        {
            try
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Armor.Agent.logo.png");
                if (stream == null)
                    return null;
                using (stream)
                {
                    return new Bitmap(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

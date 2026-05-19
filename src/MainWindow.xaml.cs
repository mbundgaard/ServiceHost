using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ServiceHost.Models;
using ServiceHost.ViewModels;

namespace ServiceHost;

public partial class MainWindow : Window
{
    private const int MaxLogChars = 500_000;
    private const int TrimChunk = 50_000;

    private MainViewModel? _viewModel;
    private int _logLength;

    public MainWindow()
    {
        InitializeComponent();

        // Auto-scroll log to bottom when content changes
        LogTextBox.TextChanged += (s, e) =>
        {
            LogTextBox.ScrollToEnd();
        };

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.LogReset -= OnLogReset;
            _viewModel.LogAppended -= OnLogAppended;
        }

        _viewModel = e.NewValue as MainViewModel;

        if (_viewModel != null)
        {
            _viewModel.LogReset += OnLogReset;
            _viewModel.LogAppended += OnLogAppended;
        }
    }

    private void OnLogReset(string content)
    {
        LogTextBox.Text = content;
        _logLength = content.Length;
    }

    private void OnLogAppended(string newLines)
    {
        LogTextBox.AppendText(newLines);
        _logLength += newLines.Length;

        // Trim from the front when we exceed MaxLogChars by at least TrimChunk,
        // so we don't pay the full-text rebuild cost on every flush.
        if (_logLength > MaxLogChars + TrimChunk)
        {
            var fullText = LogTextBox.Text;
            var target = fullText.Length - MaxLogChars;
            var idx = fullText.IndexOf('\n', target);
            if (idx > 0)
            {
                var trimmed = fullText.Substring(idx + 1);
                LogTextBox.Text = trimmed;
                _logLength = trimmed.Length;
            }
        }
    }

    private void ConfigPath_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && !string.IsNullOrEmpty(vm.ConfigPath))
        {
            // Open Explorer and select the config file
            Process.Start("explorer.exe", $"/select,\"{vm.ConfigPath}\"");
        }
    }

    private void ServiceUrl_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.SelectedService?.Url is string url && !string.IsNullOrEmpty(url))
        {
            // Open URL in default browser
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/mbundgaard/ServiceHost") { UseShellExecute = true });
    }
}

/// <summary>
/// Converts ServiceStatus to a color for the status indicator
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(78, 196, 105));  // Running - #4ec469
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(110, 110, 110)); // Stopped - #6e6e6e
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(231, 72, 86));    // Failed - #e74856
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(252, 180, 38)); // Starting/Stopping - #fcb426

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ServiceStatus status)
        {
            return status switch
            {
                ServiceStatus.Running => GreenBrush,
                ServiceStatus.Starting => OrangeBrush,
                ServiceStatus.Stopping => OrangeBrush,
                ServiceStatus.Failed => RedBrush,
                _ => GrayBrush
            };
        }
        return GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}

/// <summary>
/// Converts bool to Visibility
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

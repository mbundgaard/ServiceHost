using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ServiceHost.Models;

namespace ServiceHost;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Auto-scroll log to bottom when content changes
        LogTextBox.TextChanged += (s, e) =>
        {
            LogTextBox.ScrollToEnd();
        };
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

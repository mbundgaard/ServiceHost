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
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(76, 175, 80));   // Running
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(158, 158, 158)); // Stopped
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(244, 67, 54));    // Failed
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(255, 152, 0)); // Starting/Stopping

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

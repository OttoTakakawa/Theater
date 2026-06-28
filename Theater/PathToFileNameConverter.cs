using System.Globalization;
using System.Windows.Data;

namespace Theater;

/// <summary>
/// Converts a full file path to just the file name.
/// Used in XAML bindings: {Binding Converter={StaticResource PathToFileName}}
/// </summary>
[ValueConversion(typeof(string), typeof(string))]
public sealed class PathToFileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrEmpty(path))
        {
            try { return Path.GetFileName(path); }
            catch { return path; }
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

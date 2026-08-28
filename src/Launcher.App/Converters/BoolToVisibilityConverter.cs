using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Launcher.App.Converters;

/// <summary>
/// Maps a bool to <see cref="Visibility"/>. Set <see cref="Invert"/> to collapse on true.
/// </summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;

        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("BoolToVisibilityConverter is one-way.");
}

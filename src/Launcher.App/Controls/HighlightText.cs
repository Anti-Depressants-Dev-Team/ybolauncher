using Launcher.Core.Search;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Launcher.App.Controls;

/// <summary>
/// Renders a list of <see cref="TextSegment"/> into a <see cref="TextBlock"/>, emphasising
/// the runs that matched the search query.
/// <para>
/// An attached property rather than a custom control: the only thing needed is to rebuild
/// <c>Inlines</c>, and this keeps the item template declarative.
/// </para>
/// </summary>
public static class HighlightText
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments",
        typeof(object),
        typeof(HighlightText),
        new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(DependencyObject element, object value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(SegmentsProperty, value);
    }

    public static object GetSegments(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(SegmentsProperty);
    }

    private static void OnSegmentsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBlock block)
        {
            return;
        }

        block.Inlines.Clear();

        if (args.NewValue is not IEnumerable<TextSegment> segments)
        {
            return;
        }

        Brush? accent = Application.Current.Resources["AccentTextFillColorPrimaryBrush"] as Brush;

        foreach (TextSegment segment in segments)
        {
            var run = new Run { Text = segment.Text };

            if (segment.IsMatch)
            {
                run.FontWeight = FontWeights.SemiBold;

                if (accent is not null)
                {
                    run.Foreground = accent;
                }
            }

            block.Inlines.Add(run);
        }
    }
}

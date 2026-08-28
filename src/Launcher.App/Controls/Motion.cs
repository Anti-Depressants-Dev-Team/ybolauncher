using Launcher.Core.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Launcher.App.Controls;

/// <summary>
/// Motion policy for the app. SPEC.md caps every transition at 250ms and requires the
/// reduced-motion setting to be honoured.
/// </summary>
internal static class Motion
{
    /// <summary>Hover lift and other micro-interactions.</summary>
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);

    /// <summary>Scale a tile grows to under the pointer.</summary>
    public const double HoverScale = 1.04;

    /// <summary>
    /// Read once at startup rather than per animation: this is queried on every pointer
    /// move over the grid, and the check crosses into WinRT and Win32. Changing the
    /// Windows animation setting therefore takes effect on the next launch.
    /// </summary>
    private static readonly Lazy<bool> Enabled = new(() =>
        SystemAccessibility.AreAnimationsEnabled() && !SystemAccessibility.IsHighContrast());

    /// <summary>
    /// False when the user has asked for reduced motion, or is in a high contrast theme
    /// where decorative movement is a distraction rather than a cue.
    /// </summary>
    public static bool AnimationsEnabled => Enabled.Value;

    /// <summary>
    /// Grows or shrinks an element under the pointer. Does nothing at all when animations
    /// are off - a reduced-motion user should get no movement, not faster movement.
    /// </summary>
    public static void AnimateScale(FrameworkElement element, double target)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (!AnimationsEnabled)
        {
            return;
        }

        if (element.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }

        var storyboard = new Storyboard();

        foreach (string property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                To = target,
                Duration = new Duration(Fast),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };

            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        storyboard.Begin();
    }
}

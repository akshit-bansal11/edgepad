using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Edgepad.Platform;

namespace Edgepad.Ui;

/// <summary>
/// The three motion durations, defined once (UX-27, DES-D07): a press answers in <see cref="Press"/>, a window or panel
/// appearing takes <see cref="Reveal"/>, a whole screen changing takes <see cref="Scene"/>. Linux has no screen-level
/// transition, so <see cref="Scene"/> has no consumer here; it stays as the third documented tier. A window appearing
/// decelerates (cubic ease out, DES-D30). Hover and press steps
/// in <see cref="Look"/> stay instant, and the tray menu is the desktop's.
/// </summary>
internal static class Motion
{
    public const int Press = 160;
    public const int Reveal = 300;
    public const int Scene = 400;

    private const double From = 0.96;

    /// <summary>Whether the desktop animates: GNOME's <c>enable-animations</c>. Animating when it cannot be read.</summary>
    public static bool Enabled => EnabledFrom(ReadSetting);

    /// <summary>The setting reader's answer ("true" or "false") as a flag; anything else, or no answer, animates.</summary>
    internal static bool EnabledFrom(Func<string?> read) => read()?.Trim() != "false";

    private static string? ReadSetting() =>
        Proc.Run("gsettings", ["get", "org.gnome.desktop.interface", "enable-animations"], TimeSpan.FromSeconds(1)) is { ExitCode: 0 } result
            ? result.Output
            : null;

    /// <summary>
    /// Makes <paramref name="window"/> fade and scale in from 0.96 about its centre over <see cref="Reveal"/> when it
    /// opens. With the desktop's animations off it opens at once. A second click during the fade still lands: the
    /// content is hit-testable throughout.
    /// </summary>
    public static void RevealWindow(Window window)
    {
        if (window.Content is not Control content || !Enabled)
        {
            return;
        }

        var duration = TimeSpan.FromMilliseconds(Reveal);
        content.Opacity = 0;
        content.RenderTransform = TransformOperations.Parse($"scale({From.ToString(CultureInfo.InvariantCulture)})");
        content.Transitions =
        [
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = duration, Easing = new CubicEaseOut() },
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = new CubicEaseOut() },
        ];
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            content.Opacity = 1;
            content.RenderTransform = TransformOperations.Parse("scale(1)");
        });
    }
}

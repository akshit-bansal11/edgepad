using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Edgepad.Ui;

/// <summary>
/// Edgepad 2.0's colours and type, the same two sets the Windows app and the phone draw with, and the few widgets
/// the dialogs share. Avalonia's Fluent theme supplies the controls; these values are applied on top by hand,
/// because Fluent's own accent cannot be set from code without relying on an API that moved between releases.
/// </summary>
internal sealed record Look(IBrush Card, IBrush Line, IBrush Ink, IBrush Dim, IBrush Accent, IBrush OnAccent, IBrush Danger)
{
    /// <summary>Lato, embedded from the Windows app's font folder under its OFL text.</summary>
    public static readonly FontFamily Lato = new("avares://Edgepad.Linux/Fonts#Lato");

    private static readonly Look Dark = new(
        Hex(0x1B1C20), Hex(0x2B2D33), Hex(0xF2F3F5), Hex(0x9A9CA3), Hex(0x4DA3FF), Hex(0x0B1A2E), Hex(0xFF6961));

    private static readonly Look Light = new(
        Hex(0xFFFFFF), Hex(0xE3E5EA), Hex(0x16171A), Hex(0x6B6E76), Hex(0x0068D6), Hex(0xFFFFFF), Hex(0xD70015));

    /// <summary>The set for the desktop's light or dark mode right now. Call on the UI thread.</summary>
    public static Look Current => Application.Current?.ActualThemeVariant == ThemeVariant.Dark ? Dark : Light;

    public TextBlock Text(string text, double size, FontWeight weight = FontWeight.Normal, bool dim = false) => new()
    {
        Text = text,
        FontFamily = Lato,
        FontSize = size,
        FontWeight = weight,
        Foreground = dim ? Dim : Ink,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A button: the accent fill for the primary one, a hairline outline for the rest, red text if dangerous.</summary>
    public Button Button(string text, bool primary = false, bool danger = false) => new()
    {
        Content = text,
        FontFamily = Lato,
        FontWeight = FontWeight.Bold,
        Padding = new Thickness(16, 8),
        CornerRadius = new CornerRadius(8),
        Foreground = primary ? OnAccent : danger ? Danger : Ink,
        Background = primary ? Accent : Brushes.Transparent,
        BorderBrush = primary ? Accent : Line,
        BorderThickness = new Thickness(1),
    };

    /// <summary>A dialog window with this look: fixed size to its content, no resizing, centred on the screen.</summary>
    public Window Dialog(string title, double width, Control content) => new()
    {
        Title = title,
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        Background = Card,
        FontFamily = Lato,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = content,
    };

    /// <summary>A row of buttons along the bottom edge.</summary>
    public static StackPanel Footer(params Control[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        row.Children.AddRange(buttons);
        return row;
    }

    private static SolidColorBrush Hex(uint rgb) => new(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}

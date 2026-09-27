using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Edgepad.Ui;

/// <summary>The weights the design uses. Lato ships each as its own file, so this is a choice of file, not a flag.</summary>
internal enum Weight
{
    Regular,
    Bold,
    Black,
}

/// <summary>
/// Edgepad 2.0's colours, the same two sets the phone draws with. The laptop follows Windows' own light or dark
/// app mode rather than having a setting of its own: the tray menu sits next to the system's, and one that
/// ignored the mode would be the only white menu on a dark taskbar.
/// </summary>
internal sealed record Palette(
    Color Card,
    Color Line,
    Color Faint,
    Color Ink,
    Color Dim,
    Color Accent,
    Color OnAccent,
    Color AccentSoft,
    Color Off,
    Color Danger,
    bool IsDark)
{
    public static readonly Palette Dark = new(
        Hex(0x1B1C20), Hex(0x2B2D33), Hex(0x24262B), Hex(0xF2F3F5), Hex(0x9A9CA3), Hex(0x4DA3FF),
        Hex(0x0B1A2E), Hex(0x1B2A3D), Hex(0x3A3C43), Hex(0xFF6961), IsDark: true);

    public static readonly Palette Light = new(
        Hex(0xFFFFFF), Hex(0xE3E5EA), Hex(0xEBEDF2), Hex(0x16171A), Hex(0x6B6E76), Hex(0x0068D6),
        Hex(0xFFFFFF), Hex(0xE6F0FC), Hex(0xE3E5EA), Hex(0xD70015), IsDark: false);

    /// <summary>
    /// The set for Windows' app mode right now. Read fresh on every call, because the owner can flip the mode
    /// while the tray app runs and the next menu should follow without a restart.
    /// </summary>
    public static Palette Current
    {
        get
        {
            try
            {
                // Absent on a fresh install, which is light: that is Windows' own default.
                var value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return value is 0 ? Dark : Light;
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                return Light;
            }
        }
    }

    private static Color Hex(int rgb) => Color.FromArgb(unchecked((int)0xFF000000) | rgb);
}

/// <summary>
/// Fonts, measurements and the handful of window calls the design needs. Everything here runs on the UI thread,
/// which is the only thread that paints.
/// </summary>
internal static partial class Theme
{
    /// <summary>Every measurement in the UI is written for 96 dpi and scaled from it, as the design is drawn.</summary>
    private const double BaselineDpi = 96.0;

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColour = 34;
    private const int DwmCornerRound = 2;
    private const uint SpiGetClientAreaAnimation = 0x1042;

    /// <summary>
    /// Holds Lato for the life of the process. AddMemoryFont reads the bytes where they lie rather than copying
    /// them, so the memory behind each font is allocated outside the managed heap and deliberately never freed:
    /// the collection is static, and freeing it would leave GDI+ reading released memory on the next paint.
    /// </summary>
    private static readonly PrivateFontCollection Collection = new();

    /// <summary>
    /// Lato Black, kept apart and only ever drawn through GDI+. Its file carries two family names, and GDI+ reports
    /// "Lato Black" on one run and plain "Lato" on the next — seen in about half of repeated test runs. In a shared
    /// collection that sometimes left no Black family at all; apart, it is always the collection's one family,
    /// whatever it is called. GDI finds a face by name, so a Black font handed to TextRenderer or a native control
    /// could come out Regular: Black text is drawn with Graphics.DrawString instead.
    /// </summary>
    private static readonly PrivateFontCollection BlackCollection = new();

    private static readonly Dictionary<(float Size, Weight Weight), Font> Fonts = [];

    private static readonly FontFamily Family;

    /// <summary>Lato's 900, which is its own file rather than a style of "Lato"; null with the fallback.</summary>
    private static readonly FontFamily? BlackFamily;

    static Theme()
    {
        if (Load())
        {
            Family = Collection.Families.First(family => family.Name == "Lato");
            BlackFamily = BlackCollection.Families.FirstOrDefault();
            return;
        }

        // Segoe UI is on every Windows this runs on, and is what the menu would have been drawn in anyway.
        Family = new FontFamily("Segoe UI");
    }

    public static int Scale(int value, int dpi) => (int)Math.Round(value * dpi / BaselineDpi);

    public static float Scale(float value, int dpi) => (float)(value * dpi / BaselineDpi);

    /// <summary>
    /// A font <paramref name="pixels"/> high in the given weight, shared and never disposed by the caller. The
    /// set is small — a few sizes at a few dpis — and building one per paint is the cost this avoids.
    /// A <see cref="Weight.Black"/> font must be drawn with Graphics.DrawString; see <see cref="BlackCollection"/>.
    /// </summary>
    public static Font Font(float pixels, Weight weight)
    {
        if (!Fonts.TryGetValue((pixels, weight), out var font))
        {
            font = weight switch
            {
                Weight.Black when BlackFamily is not null => new Font(BlackFamily, pixels, FontStyle.Regular, GraphicsUnit.Pixel),
                Weight.Regular => new Font(Family, pixels, FontStyle.Regular, GraphicsUnit.Pixel),
                _ => new Font(Family, pixels, FontStyle.Bold, GraphicsUnit.Pixel),
            };
            Fonts[(pixels, weight)] = font;
        }

        return font;
    }

    /// <summary>True once all three weights of Lato loaded. Exposed for the test that the resources are really there.</summary>
    internal static bool LatoLoaded => Family.Name == "Lato" && BlackFamily is not null;

    /// <summary><paramref name="from"/> moved <paramref name="amount"/> of the way to <paramref name="to"/>, opaque.</summary>
    public static Color Mix(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + ((to.R - from.R) * amount)),
        (int)Math.Round(from.G + ((to.G - from.G) * amount)),
        (int)Math.Round(from.B + ((to.B - from.B) * amount)));

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Whether Windows' "Show animations" is on. The ripple is decoration, and someone who has turned motion off
    /// has said so here; UIEffectsEnabled alone misses that switch.
    /// </summary>
    public static bool AnimationsEnabled =>
        SystemInformation.UIEffectsEnabled && (!Native.SystemParametersInfoW(SpiGetClientAreaAnimation, 0, out var on, 0) || on != 0);

    /// <summary>
    /// Asks Windows 11 for rounded corners and a border in <paramref name="border"/>. False on Windows 10, which
    /// has neither attribute, and the window keeps its square corners there.
    /// </summary>
    public static bool RoundCorners(nint window, Color border)
    {
        var round = DwmCornerRound;
        if (Native.DwmSetWindowAttribute(window, DwmWindowCornerPreference, ref round, sizeof(int)) != 0)
        {
            return false;
        }

        var colour = ColorTranslator.ToWin32(border);
        _ = Native.DwmSetWindowAttribute(window, DwmBorderColour, ref colour, sizeof(int));
        return true;
    }

    /// <summary>A dark caption on a dark dialog. Harmless where unsupported: the caption simply stays light.</summary>
    public static void DarkTitleBar(nint window, bool dark)
    {
        var on = dark ? 1 : 0;
        _ = Native.DwmSetWindowAttribute(window, DwmUseImmersiveDarkMode, ref on, sizeof(int));
    }

    /// <summary>Dark scroll bars for a list in a dark dialog, from the theme Explorer uses for the same thing.</summary>
    public static void DarkScrollbars(nint window) => _ = Native.SetWindowTheme(window, "DarkMode_Explorer", null);

    /// <summary>
    /// Registers the three embedded Lato files with GDI+ and with GDI. Both, because they are separate font
    /// tables: GDI+ draws the overlay and the painted text, but a TextBox is a native control that asks GDI for
    /// its face by name, and a font only GDI+ knew about would quietly become the system default there.
    /// Any failure falls back to Segoe UI whole rather than mixing two faces.
    /// </summary>
    private static bool Load()
    {
        try
        {
            var assembly = typeof(Theme).Assembly;
            foreach (var name in new[] { "Lato-Regular.ttf", "Lato-Bold.ttf", "Lato-Black.ttf" })
            {
                using var stream = assembly.GetManifestResourceStream($"Edgepad.Fonts.{name}");
                if (stream is null)
                {
                    return false;
                }

                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                var memory = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                (name == "Lato-Black.ttf" ? BlackCollection : Collection).AddMemoryFont(memory, bytes.Length);

                uint installed = 0;
                if (Native.AddFontMemResourceEx(memory, (uint)bytes.Length, 0, ref installed) == 0)
                {
                    return false;
                }
            }

            return Collection.Families.Any(family => family.Name == "Lato");
        }
        catch (Exception e) when (e is IOException or ExternalException or ArgumentException or OutOfMemoryException)
        {
            // A static constructor must not throw: every paint in the app goes through this class.
            Log.Write($"Lato could not be loaded, using Segoe UI: {e.Message}");
            return false;
        }
    }

    private static partial class Native
    {
        [LibraryImport("gdi32.dll")]
        public static partial nint AddFontMemResourceEx(nint font, uint size, nint reserved, ref uint installed);

        [LibraryImport("dwmapi.dll")]
        public static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SystemParametersInfoW(uint action, uint parameter, out int value, uint update);

        [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
        public static partial int SetWindowTheme(nint window, string? application, string? idList);
    }
}

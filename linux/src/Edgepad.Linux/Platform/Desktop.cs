using Edgepad.Input;
using Edgepad.Protocol;

namespace Edgepad.Platform;

/// <summary>The desktop environments this app knows shortcuts for.</summary>
internal enum DesktopEnvironment
{
    Unknown,
    Gnome,
    Kde,
}

/// <summary>
/// Which key chord each desktop-level action presses on each desktop. Windows has one answer; Linux has none, since
/// every desktop binds its own, so the choice is made from <c>XDG_CURRENT_DESKTOP</c> and an action with no known
/// chord is dropped and counted rather than guessed at.
///
/// ALL VALUES HERE ARE UNVERIFIED. They are written from memory of GNOME's and KDE Plasma's default shortcuts, not
/// seen working: the owner has no Linux machine, so the post-release tester settles them (docs/LINUX.md lists
/// each one to try). Defaults also differ between versions (Plasma 5 and 6, GNOME 3 and 4x) and a user may have
/// rebound them. This is the only file to change when a tester reports one wrong.
/// </summary>
internal static class Desktop
{
    // Windows virtual-key codes, mapped to evdev by position like every other key from the phone.
    private static readonly ushort Meta = KeyMap.ToEvdev(0x5B);
    private static readonly ushort Ctrl = KeyMap.ToEvdev(0x11);
    private static readonly ushort Alt = KeyMap.ToEvdev(0x12);
    private static readonly ushort Shift = KeyMap.ToEvdev(0x10);
    private static readonly ushort Space = KeyMap.ToEvdev(0x20);
    private static readonly ushort PageUp = KeyMap.ToEvdev(0x21);
    private static readonly ushort PageDown = KeyMap.ToEvdev(0x22);
    private static readonly ushort Left = KeyMap.ToEvdev(0x25);
    private static readonly ushort Right = KeyMap.ToEvdev(0x27);
    private static readonly ushort PrintScreen = KeyMap.ToEvdev(0x2C);
    private static readonly ushort A = KeyMap.ToEvdev(0x41);
    private static readonly ushort D = KeyMap.ToEvdev(0x44);
    private static readonly ushort N = KeyMap.ToEvdev(0x4E);
    private static readonly ushort R = KeyMap.ToEvdev(0x52);
    private static readonly ushort V = KeyMap.ToEvdev(0x56);
    private static readonly ushort W = KeyMap.ToEvdev(0x57);

    /// <summary>
    /// The desktop named by <c>XDG_CURRENT_DESKTOP</c>, a colon-separated list such as <c>ubuntu:GNOME</c> or
    /// <c>KDE</c>. The first entry that is a known desktop wins; anything else is Unknown.
    /// </summary>
    public static DesktopEnvironment Detect(string? currentDesktop)
    {
        foreach (var name in (currentDesktop ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (name.Equals("GNOME", StringComparison.OrdinalIgnoreCase))
            {
                return DesktopEnvironment.Gnome;
            }

            if (name.Equals("KDE", StringComparison.OrdinalIgnoreCase))
            {
                return DesktopEnvironment.Kde;
            }
        }

        return DesktopEnvironment.Unknown;
    }

    /// <summary>
    /// The evdev keys to press together, modifiers first, for a desktop-level or capture action; null when this
    /// desktop has none (the action is then dropped). Actions that are the same everywhere (the app switcher,
    /// zoom reset, the media and volume keys) are not here.
    /// </summary>
    public static ushort[]? Chord(DesktopEnvironment desktop, ActionId id) => (desktop, id) switch
    {
        // GNOME. Tapping Super opens the Activities overview; the applications grid is where typing searches;
        // Super+V shows the notification list; Super+PageUp/PageDown move between workspaces. Super+D is not bound in
        // stock GNOME ("Show desktop" is unset) but is in Ubuntu's, which is the common one.
        (DesktopEnvironment.Gnome, ActionId.TaskView) => [Meta],
        (DesktopEnvironment.Gnome, ActionId.ShowDesktop) => [Meta, D],
        (DesktopEnvironment.Gnome, ActionId.Search) => [Meta, A],
        (DesktopEnvironment.Gnome, ActionId.Notifications) => [Meta, V],
        (DesktopEnvironment.Gnome, ActionId.DesktopLeft) => [Meta, PageUp],
        (DesktopEnvironment.Gnome, ActionId.DesktopRight) => [Meta, PageDown],

        // KDE Plasma. Meta+W is Overview (Plasma 6), Meta+D shows the desktop, Alt+Space opens KRunner, Meta+N shows
        // notification history, and Ctrl+Meta+arrows switch desktop.
        (DesktopEnvironment.Kde, ActionId.TaskView) => [Meta, W],
        (DesktopEnvironment.Kde, ActionId.ShowDesktop) => [Meta, D],
        (DesktopEnvironment.Kde, ActionId.Search) => [Alt, Space],
        (DesktopEnvironment.Kde, ActionId.Notifications) => [Meta, N],
        (DesktopEnvironment.Kde, ActionId.DesktopLeft) => [Ctrl, Meta, Left],
        (DesktopEnvironment.Kde, ActionId.DesktopRight) => [Ctrl, Meta, Right],

        // Capture. Print opens the desktop's own screenshot tool (GNOME's screenshot UI, KDE's Spectacle), which
        // offers region and whole-screen itself; both actions press it, and it is the one key every desktop agrees
        // on, so it is not tied to a known desktop. Recording has no shared key: GNOME's screencast toggle is
        // Ctrl+Shift+Alt+R, Plasma 6's Spectacle records on Meta+Alt+R.
        (_, ActionId.CaptureRegion or ActionId.CaptureScreen) => [PrintScreen],
        (DesktopEnvironment.Gnome, ActionId.CaptureRecord) => [Ctrl, Shift, Alt, R],
        (DesktopEnvironment.Kde, ActionId.CaptureRecord) => [Meta, Alt, R],

        _ => null,
    };
}

using Edgepad.Input;

namespace Edgepad.Platform;

/// <summary>
/// Whether this user may create input devices. Without it every pointer and key frame does nothing while the phone
/// shows connected, so the app checks at start and on each connection, and says so where the owner is looking.
/// </summary>
internal static class UinputAccess
{
    /// <summary>What the status line says in place of "Connected to".</summary>
    public const string Blocked = "Cannot create input devices: log out and back in once";

    /// <summary>The notification's body, which can carry what the status line cannot.</summary>
    public const string BlockedDetail =
        "Edgepad cannot open /dev/uinput, so the phone cannot move the pointer or type. Log out and back in once "
            + "after installing, so your session gets access. More: https://edgepad.akshitbansal.dev/docs";

    /// <summary>Null when <paramref name="path"/> (default /dev/uinput) opens for writing; else why not.</summary>
    public static string? Problem(string? path = null)
    {
        string error;
        using var device = path is null ? Uinput.Open(out error) : Uinput.Open(path, out error);
        return device is null ? error : null;
    }
}

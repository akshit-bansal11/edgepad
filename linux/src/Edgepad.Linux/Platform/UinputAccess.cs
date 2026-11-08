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

    private static volatile bool blocked;

    /// <summary>Raised, on the thread that probed, when <see cref="Check"/> finds the answer changed. Sessions send TEXT 18 from it.</summary>
    public static event Action<bool>? Changed;

    /// <summary>The TEXT 18 payload: <c>blocked</c> or <c>ok</c>.</summary>
    public static string StateText(bool isBlocked) => isBlocked ? "blocked" : "ok";

    /// <summary>Probes, remembers the answer and raises <see cref="Changed"/> when it flipped. Returns the problem, or null.</summary>
    public static string? Check()
    {
        var problem = Problem();
        var now = problem is not null;
        if (blocked != now)
        {
            blocked = now;
            Changed?.Invoke(now);
        }

        return problem;
    }

    /// <summary>Null when <paramref name="path"/> (default /dev/uinput) opens for writing; else why not.</summary>
    public static string? Problem(string? path = null)
    {
        string error;
        using var device = path is null ? Uinput.Open(out error) : Uinput.Open(path, out error);
        return device is null ? error : null;
    }
}

using System.Globalization;
using Edgepad.Macros;
using Edgepad.Native;

namespace Edgepad.Platform;

/// <summary>
/// The windows of macro apps, on X11 only. Wayland gives an ordinary client no way to list or control another app's
/// windows, so there <see cref="Available"/> is false, the phone is never told <c>running</c>, and a press launches.
/// A sandboxed app (Flatpak, Snap) whose pid means nothing here can only match by class; failing that it reads Closed,
/// and a press launches it again.
/// </summary>
internal sealed class LinuxMacroWindows(Func<string, string?> environment, Func<bool> opens) : IMacroWindows
{
    private readonly Lazy<bool> available = new(() => Check(environment, opens));

    public LinuxMacroWindows()
        : this(Environment.GetEnvironmentVariable, X11Windows.Opens)
    {
    }

    public bool Available => available.Value;

    public OpenState[] Survey(IReadOnlyList<Macro> macros)
    {
        var programs = macros.Select(Program).ToList();
        var desktop = programs.Exists(program => program is not null) ? X11Windows.Read(ImageOf) : null;
        return [.. programs.Select(program => State(program, desktop))];
    }

    public bool Minimize(Macro macro) =>
        X11Windows.Read(ImageOf) is { } desktop
        && Mine(Program(macro), desktop).Exists(window => window.Id == desktop.Active)
        && X11Windows.Minimize(desktop.Active);

    /// <summary>The newest window: _NET_CLIENT_LIST is in mapping order.</summary>
    public bool Raise(Macro macro) =>
        X11Windows.Read(ImageOf) is { } desktop
        && Mine(Program(macro), desktop) is [.., var newest]
        && X11Windows.Raise(newest.Id);

    public bool Close(Macro macro) =>
        X11Windows.Read(ImageOf) is { } desktop
        && X11Windows.Close([.. Mine(Program(macro), desktop).Select(window => window.Id)]);

    /// <summary>
    /// Never throws, because a caller reads it outside any guard. Under XWayland $DISPLAY opens too, and shows only
    /// the X clients, so the session type comes first.
    /// </summary>
    private static bool Check(Func<string, string?> environment, Func<bool> opens)
    {
        if (environment("XDG_SESSION_TYPE") != "x11" || string.IsNullOrEmpty(environment("DISPLAY")))
        {
            return false;
        }

        try
        {
            return opens();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Write($"Macro apps are not followed, because libX11 could not be used: {e.Message}");
            return false;
        }
    }

    private static OpenState State(WindowProgram? program, X11Desktop? desktop)
    {
        if (program is null || desktop is null)
        {
            return OpenState.Untracked;
        }

        var mine = Mine(program, desktop);
        if (mine.Count == 0)
        {
            return OpenState.Closed;
        }

        // A window manager may leave a minimized window active, and a press must bring that one back.
        return mine.Exists(window => window.Id == desktop.Active && !window.Hidden) ? OpenState.Front : OpenState.Open;
    }

    private static List<ClientWindow> Mine(WindowProgram? program, X11Desktop desktop) => program is null
        ? []
        : [.. desktop.Windows.Where(window => WindowMatch.Belongs(program, window.Image, window.Classes))];

    private static WindowProgram? Program(Macro macro) => WindowMatch.Program(
        macro.Target,
        LinuxLauncher.Classify,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("PATH"),
        File.Exists,
        LinkTarget);

    private static string? ImageOf(uint pid) => LinkTarget(string.Create(CultureInfo.InvariantCulture, $"/proc/{pid}/exe"));

    /// <summary>
    /// Where a symlink ends, or null for a plain file or a link this user may not read, as another user's process.
    /// ponytail: follows the file itself, not its folders, so a target under a symlinked folder (/bin on a merged-usr
    /// system) misses the image and falls to the class match; realpath(3) if that proves too weak.
    /// </summary>
    private static string? LinkTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

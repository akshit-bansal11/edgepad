using Edgepad.Foreground;
using Edgepad.Macros;
using Edgepad.Native;

namespace Edgepad.Platform;

/// <summary>
/// The apps for TEXT OPEN_APPS. On X11, the WM_CLASS class of every client window, newest first, as the foreground reporter
/// names it; on Wayland no other window can be seen, so only the macro apps are sent. Then the macros' programs, named by the
/// file name WM_CLASS is matched against.
/// </summary>
internal static class LinuxOpenApps
{
    public static string Body(IReadOnlyList<Macro> macros)
    {
        var desktop = LinuxForeground.Offered ? X11Windows.Read(_ => null) : null;
        return Body(
            desktop?.Windows.Select(window => window.Classes) ?? [],
            macros.Select(LinuxMacroWindows.Program).OfType<WindowProgram>().Select(program => program.Name));
    }

    /// <summary>Pure half: the classes of each window in mapping order, and the macro programs' file names.</summary>
    internal static string Body(IEnumerable<IReadOnlyList<string>> windowClasses, IEnumerable<string> macroPrograms) =>
        OpenApps.Build(
            windowClasses.Reverse().Select(classes => LinuxForeground.ParseWmClass(string.Join('\0', classes))),
            macroPrograms.Select(name => ForegroundApp.Clean(name.ToLowerInvariant(), name)));
}

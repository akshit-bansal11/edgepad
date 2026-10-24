using System.Diagnostics;

namespace Edgepad.Macros;

/// <summary>Opens a macro's target through the Windows shell, exactly as a double-click would.</summary>
internal sealed class ShellLauncher : IMacroLauncher
{
    public void Launch(string target, string? arguments) =>
        // UseShellExecute is what makes a document, a folder and an https:// URL work as targets and not
        // only an .exe: Windows applies whatever is registered for them, exactly as a double-click would.
        // Nothing here waits on or watches what it started, so the handle is let go immediately; a null
        // back means the shell handed the target to something already running, such as a browser opening
        // a URL in a window it already had, which is a success with no new process to hold.
        Process.Start(new ProcessStartInfo(target, arguments ?? string.Empty)
        {
            UseShellExecute = true,
        })?.Dispose();
}

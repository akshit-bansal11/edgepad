using Edgepad.Controls;
using Edgepad.Foreground;
using Edgepad.Native;

namespace Edgepad.Platform;

/// <summary>
/// Which app is in front, on an X11 session only: Wayland, and XWayland, cannot see other clients' focus. The X11 watch
/// (a thread and a connection) exists only while a phone watches. The token is the lower-cased WM_CLASS class; the name
/// is that class as given.
/// </summary>
internal sealed class LinuxForeground : IForegroundApp
{
    private static readonly Lazy<bool> OfferedOnce = new(() => new LinuxMacroWindows().Available);

    private readonly Lock gate = new();
    private readonly List<Action<ForegroundApp>> watchers = [];
    private X11ActiveWatch? watch;

    /// <summary>True on an X11 session with a display that opens: the <c>foreground</c> feature token.</summary>
    internal static bool Offered => OfferedOnce.Value;

    public bool Available => Offered;

    public IDisposable Watch(Action<ForegroundApp> onChange)
    {
        lock (gate)
        {
            watchers.Add(onChange);
            watch ??= X11ActiveWatch.Start(Changed);
        }

        return new Subscription(() => Remove(onChange));
    }

    /// <summary>The class part of a WM_CLASS value, "instance NUL Class NUL". None when there is no class.</summary>
    internal static ForegroundApp ParseWmClass(string text)
    {
        var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? ForegroundApp.None : ForegroundApp.Clean(parts[1].ToLowerInvariant(), parts[1]);
    }

    private void Remove(Action<ForegroundApp> onChange)
    {
        lock (gate)
        {
            _ = watchers.Remove(onChange);
            if (watchers.Count == 0)
            {
                watch?.Dispose();
                watch = null;
            }
        }
    }

    private void Changed(string wmClass)
    {
        Action<ForegroundApp>[] now;
        lock (gate)
        {
            now = [.. watchers];
        }

        var app = ParseWmClass(wmClass);
        foreach (var watcher in now)
        {
            watcher(app);
        }
    }
}

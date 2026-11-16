using System.Diagnostics;
using System.Runtime.InteropServices;
using Edgepad.Foreground;
using Edgepad.Macros;

namespace Edgepad.Controls;

/// <summary>
/// Which app is in front, from one WinEvent hook that exists only while a phone watches: the first watcher installs it on
/// the UI thread (the hook needs that thread's message loop) and the last one to go removes it. No timer and no polling; an
/// event costs a process lookup and, after the first time, a cache hit. An elevated process cannot be read and reports
/// nothing known. A Store app reports the host process it runs in, "applicationframehost.exe", which is a known limit.
/// </summary>
internal sealed partial class WindowsForeground(SynchronizationContext ui) : IForegroundApp
{
    private const uint ForegroundChanged = 3; // EVENT_SYSTEM_FOREGROUND
    private const uint OutOfContext = 0; // WINEVENT_OUTOFCONTEXT
    private const int CacheSize = 32;

    // The hook's callback is static, so it finds its owner here. One owner per process.
    private static volatile WindowsForeground? active;

    private readonly Lock gate = new();
    private readonly List<Action<ForegroundApp>> watchers = [];
    private readonly Dictionary<string, ForegroundApp> names = [];
    private nint hook; // UI thread only

    public bool Available => true;

    public IDisposable Watch(Action<ForegroundApp> onChange)
    {
        lock (gate)
        {
            watchers.Add(onChange);
            if (watchers.Count == 1)
            {
                ui.Post(_ => Install(), null);
            }
        }

        onChange(Current());
        return new Subscription(() => Remove(onChange));
    }

    private void Remove(Action<ForegroundApp> onChange)
    {
        lock (gate)
        {
            _ = watchers.Remove(onChange);
            if (watchers.Count == 0)
            {
                ui.Post(_ => Uninstall(), null);
            }
        }
    }

    // Both re-read the watcher list, so a post that arrives late finds the state it should act on.
    private unsafe void Install()
    {
        lock (gate)
        {
            if (hook != 0 || watchers.Count == 0)
            {
                return;
            }
        }

        active = this;
        hook = SetWinEventHook(ForegroundChanged, ForegroundChanged, 0, &OnForeground, 0, 0, OutOfContext);
    }

    private void Uninstall()
    {
        lock (gate)
        {
            if (hook == 0 || watchers.Count > 0)
            {
                return;
            }
        }

        _ = UnhookWinEvent(hook);
        hook = 0;
        active = null;
    }

    [UnmanagedCallersOnly]
    private static void OnForeground(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time)
    {
        try
        {
            if (window != 0 && active is { } owner)
            {
                owner.Changed(Of(window));
            }
        }
        catch (Exception)
        {
            // An exception out of an unmanaged callback ends the process; a missed change is corrected by the next.
        }
    }

    private void Changed(ForegroundApp app)
    {
        Action<ForegroundApp>[] now;
        lock (gate)
        {
            now = [.. watchers];
        }

        foreach (var watcher in now)
        {
            watcher(app);
        }
    }

    private ForegroundApp Current()
    {
        var window = GetForegroundWindow();
        return window == 0 ? ForegroundApp.None : Of(window);
    }

    private ForegroundApp Of(nint window)
    {
        _ = GetWindowThreadProcessId(window, out var pid);
        if (WindowsMacroWindows.ImageOf(pid) is not { } path)
        {
            return ForegroundApp.None;
        }

        lock (gate)
        {
            if (names.TryGetValue(path, out var known))
            {
                return known;
            }

            if (names.Count >= CacheSize)
            {
                names.Clear();
            }

            return names[path] = ForegroundApp.Clean(ForegroundNaming.Token(path), ForegroundNaming.Name(path, Description(path)));
        }
    }

    private static string? Description(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileDescription;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [LibraryImport("user32.dll")]
    private static unsafe partial nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module, delegate* unmanaged<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}

/// <summary>What the phone is told about a program, from its image path alone. Pure, so a test needs no desktop.</summary>
internal static class ForegroundNaming
{
    /// <summary>The lower-cased file name, or empty when it would not be a legal token.</summary>
    public static string Token(string path) =>
        ForegroundApp.Clean(Path.GetFileName(path).ToLowerInvariant(), null).Token;

    /// <summary>The file's own description, else its name without the extension.</summary>
    public static string Name(string path, string? description) =>
        string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(path) : description.Trim();
}

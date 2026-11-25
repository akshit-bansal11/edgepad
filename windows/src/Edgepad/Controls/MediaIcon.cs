using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Edgepad.Blob;
using Edgepad.Macros;
using Edgepad.Protocol;
using Windows.Storage.Streams;

namespace Edgepad.Controls;

/// <summary>
/// The picture the phone shows beside the media title: a browser's media artwork, else the playing app's own icon.
/// Work happens only when a phone watches and the app or track changed, once per change; nothing is fetched from
/// the web, the artwork is only what Windows already holds in the system media controls.
/// </summary>
internal static class MediaIcon
{
    /// <summary>Which picture goes beside the media title: none, the system media controls' artwork, or the playing app's icon.</summary>
    internal enum Pick
    {
        None,
        Thumbnail,
        AppIcon,
    }

    internal const string ArtworkLabel = "artwork";
    internal const int Size = 64;
    internal const int MaxBytes = 24 * 1024;
    private const int CacheSize = 8;

    private static readonly int[] Sides = [Size, 48, 32];
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, (Pick Pick, byte[] Png)> Cache = [];
    private static readonly Queue<string> Order = new();

    /// <summary>A browser with artwork shows the artwork; anything else with a running exe shows the exe's icon.</summary>
    internal static Pick Choose(bool isBrowser, bool hasThumbnail, bool hasExe) =>
        isBrowser && hasThumbnail ? Pick.Thumbnail : hasExe ? Pick.AppIcon : Pick.None;

    internal static string Label(Pick pick, string appUserModelId) =>
        pick == Pick.Thumbnail ? ArtworkLabel : MediaSessions.AppName(appUserModelId);

    /// <summary>Changes when the picture should: the track for a browser (its artwork is per track), the app otherwise. Empty with nothing playing.</summary>
    internal static string Key(MediaState state)
    {
        if (state.Aumid.Length == 0)
        {
            return "";
        }

        return BrowserTitle.IsBrowser(MediaSessions.Executable(state.Aumid)) ? state.Aumid + "|" + state.NowPlayingFull : state.Aumid;
    }

    /// <summary>Finds the picture and sends it as BLOB 4. Nothing is sent when none is found or <paramref name="abandoned"/> says so.</summary>
    internal static async Task SendAsync(MediaState state, BlobSender blobs, Func<bool> abandoned, CancellationToken ended)
    {
        var key = Key(state);
        (Pick Pick, byte[] Png) found;
        bool cached;
        lock (Gate)
        {
            cached = Cache.TryGetValue(key, out found);
        }

        if (!cached)
        {
            var (pick, png) = await FindAsync(state, ended);
            if (png is null)
            {
                return;
            }

            found = (pick, png);
            Remember(key, found);
        }

        if (!abandoned())
        {
            blobs.Send(BlobKind.MediaIcon, Label(found.Pick, state.Aumid), found.Png, abandoned);
        }
    }

    private static async Task<(Pick, byte[]?)> FindAsync(MediaState state, CancellationToken ended)
    {
        var name = MediaSessions.Executable(state.Aumid);
        var browser = BrowserTitle.IsBrowser(name);
        var thumbnail = browser && state.Thumbnail is not null;
        var exe = thumbnail ? null : ExePath(name);
        var pick = Choose(browser, thumbnail, exe is not null);
        try
        {
            return pick switch
            {
                Pick.Thumbnail => (pick, await ArtworkAsync(state.Thumbnail!, ended)),
                Pick.AppIcon => (pick, MacroIcons.PngForPath(exe!, Size)),
                _ => (pick, null),
            };
        }
        catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException or ExternalException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Write($"No media icon: {e.Message}");
            return (pick, null);
        }
    }

    private static async Task<byte[]?> ArtworkAsync(IRandomAccessStreamReference thumbnail, CancellationToken ended)
    {
        using var stream = await thumbnail.OpenReadAsync();
        using var net = stream.AsStreamForRead();
        using var source = new Bitmap(net);
        ended.ThrowIfCancellationRequested();
        var side = Math.Min(source.Width, source.Height);
        if (side <= 0)
        {
            return null;
        }

        using var square = source.Clone(new Rectangle((source.Width - side) / 2, (source.Height - side) / 2, side, side), source.PixelFormat);
        foreach (var edge in Sides)
        {
            var png = MacroIcons.Encode(square, edge);
            if (png.Length <= MaxBytes)
            {
                return png;
            }
        }

        return null;
    }

    private static string? ExePath(string processName)
    {
        if (processName.Length == 0)
        {
            return null;
        }

        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainModule?.FileName is { Length: > 0 } path)
                    {
                        return path;
                    }
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    // Another user's or an elevated process: its icon cannot be read from here.
                }
            }

            return null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void Remember(string key, (Pick Pick, byte[] Png) value)
    {
        lock (Gate)
        {
            if (Cache.TryAdd(key, value))
            {
                Order.Enqueue(key);
                while (Order.Count > CacheSize)
                {
                    Cache.Remove(Order.Dequeue());
                }
            }
        }
    }
}

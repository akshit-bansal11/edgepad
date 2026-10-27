using Edgepad.Macros;
using SkiaSharp;
using Svg.Skia;

namespace Edgepad.Platform;

/// <summary>
/// The picture on a macro button, the Linux twin of the Windows <c>MacroIcons</c>. Same direction of flow: this reads
/// files on the laptop and sends pixels to the phone, and nothing the phone sends ever reaches a path here. The phone
/// asks for "the icons" and gets whatever the owner's own list points at, found by <see cref="DesktopIcons"/>. A macro
/// whose icon cannot be read is a button with a label, not an error.
///
/// SkiaSharp is used directly rather than Avalonia's <c>Bitmap</c>: this runs on the session's read thread, and Skia
/// renders off the UI thread with no dispatcher in the way. Avalonia already ships the native Skia, so it costs no
/// second copy; Svg.Skia adds only the SVG parser, because the hicolor theme's <c>scalable</c> icons are SVG.
/// </summary>
internal static class LinuxMacroIcons
{
    /// <summary>
    /// The largest file this will open. An owner's icon path can point at anything, and a photograph or a disk image
    /// read whole on the session's thread would stall the phone for a picture that would be refused anyway.
    /// </summary>
    private const long MaxSourceBytes = 8 * 1024 * 1024;

    /// <summary>Mitchell is a cubic that stays sharp downscaling a 256 or 512 pixel icon to the phone's square.</summary>
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    private static readonly Lock Gate = new();

    /// <summary>
    /// Keyed by path and last-write time together, as on Windows: a replaced icon is picked up rather than served stale
    /// for the life of the process, and a phone reconnecting costs no work at all. A null result is cached too, so a
    /// bad file is logged once and not on every connect.
    /// </summary>
    // ponytail: never evicted; one entry per path and write time. Bound it if a long-running session ever shows it.
    private static readonly Dictionary<string, byte[]?> Cache = [];

    /// <summary>
    /// The PNG for <paramref name="macro"/>, or null when it has no readable picture. Safe from any thread; two callers
    /// may render the same icon at once, which costs a duplicate read and never a wrong answer.
    /// </summary>
    public static byte[]? Png(Macro macro)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dirs = DesktopIcons.DataDirs(
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"), Environment.GetEnvironmentVariable("XDG_DATA_DIRS"), home);

        // Not logged: a URL or a folder has no picture, and that is normal.
        var path = DesktopIcons.Find(macro, dirs, home, LinuxLauncher.Classify);
        if (path is null)
        {
            return null;
        }

        string key;
        try
        {
            key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var png = Render(path);
        lock (Gate)
        {
            Cache[key] = png;
        }

        return png;
    }

    /// <summary>
    /// <paramref name="path"/> as a PNG of <see cref="MacroIconChunks.Size"/>, or of <see cref="MacroIconChunks.Fallback"/>
    /// when that is too heavy for the wire, or null when it cannot be read or is too heavy even then.
    /// </summary>
    internal static byte[]? Render(string path)
    {
        // Unfiltered on purpose, as LinuxSession.Run's last catch is: a third-party SVG parser can throw anything, and
        // an exception escaping onto the session's read thread would drop the phone's connection over one bad icon.
        try
        {
            if (new FileInfo(path).Length > MaxSourceBytes)
            {
                Log.Write($"No icon for {path}: the file is too large to be an icon");
                return null;
            }

            if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                using var svg = new SKSvg();
                svg.Load(path);
                var picture = svg.Picture;
                if (picture is null)
                {
                    return Undecodable(path);
                }

                var cull = picture.CullRect;
                return Fit(path, cull.Width, cull.Height, (canvas, dest) =>
                {
                    canvas.Translate(dest.Left, dest.Top);
                    canvas.Scale(dest.Width / cull.Width);
                    canvas.Translate(-cull.Left, -cull.Top);
                    canvas.DrawPicture(picture);
                });
            }

            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                return Undecodable(path);
            }

            using var image = SKImage.FromBitmap(bitmap);
            if (image is null)
            {
                return Undecodable(path);
            }

            return Fit(path, image.Width, image.Height, (canvas, dest) => canvas.DrawImage(image, dest, Sampling));
        }
        catch (Exception e)
        {
            Log.Write($"No icon for {path}: {e.Message}");
            return null;
        }
    }

    private static byte[]? Undecodable(string path)
    {
        Log.Write($"No icon for {path}: not a picture this can read");
        return null;
    }

    /// <summary>The square at <see cref="MacroIconChunks.Size"/>, else at the fallback, else null and logged.</summary>
    private static byte[]? Fit(string path, float width, float height, Action<SKCanvas, SKRect> draw)
    {
        if (!(width > 0 && height > 0))
        {
            return null;
        }

        var png = Encode(MacroIconChunks.Size, width, height, draw);
        if (png.Length <= MacroIconChunks.MaxBytes)
        {
            return png;
        }

        var small = Encode(MacroIconChunks.Fallback, width, height, draw);
        if (small.Length <= MacroIconChunks.MaxBytes)
        {
            return small;
        }

        Log.Write($"Macro icon from {path} is {small.Length} bytes even at {MacroIconChunks.Fallback}px and was not sent");
        return null;
    }

    /// <summary>
    /// The source drawn into a <paramref name="size"/> square with alpha, cleared to transparent first: the phone draws
    /// the button on black or white by theme, so transparent corners must survive, as on Windows. The source is scaled
    /// to fit with its aspect kept and centred, so a non-square picture is letterboxed rather than stretched; that
    /// differs from Windows only because Windows icons are always square.
    /// </summary>
    private static byte[] Encode(int size, float width, float height, Action<SKCanvas, SKRect> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Skia could not create a surface");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var scale = Math.Min(size / width, size / height);
        var w = width * scale;
        var h = height * scale;
        draw(canvas, SKRect.Create((size - w) / 2, (size - h) / 2, w, h));

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Skia could not encode a PNG");
        return data.ToArray();
    }
}

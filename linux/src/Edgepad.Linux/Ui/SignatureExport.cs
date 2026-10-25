using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Edgepad.Annotation;
using Edgepad.Input;
using Edgepad.Protocol;
using MediaPen = Avalonia.Media.Pen; // Edgepad.Protocol.Pen, the PEN frame, is in scope here too

namespace Edgepad.Ui;

/// <summary>
/// Turns finished strokes into the PNG a signature is pasted from: transparent, at screen resolution, cropped to the
/// ink so it drops into a document at a sensible size. Also where it is saved and how it reaches the clipboard. The
/// rendering needs Avalonia's renderer, so it runs on the UI thread; the rest is plain and tested.
/// </summary>
internal static class SignatureExport
{
    /// <summary>Breathing room around the ink, in pixels, so the stroke's anti-aliased edge is never clipped.</summary>
    internal const int Margin = 8;

    private const string PngType = "image/png";

    /// <summary>
    /// The part of a <paramref name="width"/> by <paramref name="height"/> screen that the ink and its margin cover;
    /// null when there is no ink. The Windows app's crop, to the pixel.
    /// </summary>
    public static PixelRect? CropRect(IReadOnlyList<Stroke> strokes, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        var any = false;
        foreach (var stroke in strokes)
        {
            if (stroke.Ink(width, height) is not { } ink)
            {
                continue;
            }

            min = Vector2.Min(min, ink.Min);
            max = Vector2.Max(max, ink.Max);
            any = true;
        }

        if (!any)
        {
            return null;
        }

        var left = Math.Max(0, (int)MathF.Floor(min.X) - Margin);
        var top = Math.Max(0, (int)MathF.Floor(min.Y) - Margin);
        var right = Math.Min(width, (int)MathF.Ceiling(max.X) + Margin);
        var bottom = Math.Min(height, (int)MathF.Ceiling(max.Y) + Margin);
        return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : null;
    }

    /// <summary>The PNG, or null when there is nothing to export. On the UI thread: it draws with Avalonia's renderer.</summary>
    public static byte[]? Render(IReadOnlyList<Stroke> strokes, int width, int height)
    {
        if (CropRect(strokes, width, height) is not { } crop)
        {
            return null;
        }

        // Only the crop is allocated, at 96 dpi so one unit is one pixel; it starts cleared to transparent.
        using var bitmap = new RenderTargetBitmap(crop.Size, new Avalonia.Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext(clear: true))
        using (context.PushTransform(Matrix.CreateTranslation(-crop.X, -crop.Y)))
        {
            foreach (var stroke in strokes)
            {
                Draw(context, stroke, width, height);
            }
        }

        using var png = new MemoryStream();
        bitmap.Save(png, PngBitmapEncoderOptions.Default);
        return png.ToArray();
    }

    /// <summary>
    /// One stroke as it looks on a <paramref name="width"/> by <paramref name="height"/> screen, in that screen's
    /// pixels; the overlay draws with it too.
    /// </summary>
    internal static void Draw(DrawingContext context, Stroke stroke, int width, int height)
    {
        var points = stroke.Path(width, height);
        if (points.Length == 0)
        {
            return;
        }

        var rgb = Palette.Colour(stroke.Colour) ?? 0;
        var alpha = stroke.Tool == StrokeTool.Highlighter ? (byte)Math.Round(255 * Stroke.HighlighterAlpha) : (byte)255;
        var brush = new ImmutableSolidColorBrush(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        double size = stroke.WidthPx(height);

        // A tap is a dot, not nothing: a line of no length draws no pixels.
        if (points.All(p => p == points[0]))
        {
            context.DrawEllipse(brush, null, At(points[0]), size / 2, size / 2);
            return;
        }

        var pen = new MediaPen(brush, size, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (stroke.Tool is StrokeTool.Rectangle or StrokeTool.Ellipse)
        {
            var box = new Rect(
                Math.Min(points[0].X, points[1].X),
                Math.Min(points[0].Y, points[1].Y),
                Math.Abs(points[1].X - points[0].X),
                Math.Abs(points[1].Y - points[0].Y));
            if (stroke.Tool == StrokeTool.Rectangle)
            {
                context.DrawRectangle(null, pen, box);
            }
            else
            {
                context.DrawEllipse(null, pen, box);
            }

            return;
        }

        // One geometry, one stroke: overlapping stretches of a translucent highlighter must not darken twice.
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(At(points[0]), isFilled: false);
            foreach (var point in points.Skip(1))
            {
                path.LineTo(At(point));
            }

            path.EndFigure(isClosed: false);
            var wings = stroke.ArrowWings(width, height);
            if (wings.Length == 2)
            {
                path.BeginFigure(At(wings[0]), isFilled: false);
                path.LineTo(At(points[1]));
                path.LineTo(At(wings[1]));
                path.EndFigure(isClosed: false);
            }
        }

        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// Where signatures go: the XDG Pictures folder's Edgepad folder. <c>$XDG_PICTURES_DIR</c> when it is set, else
    /// the Pictures line of <c>user-dirs.dirs</c> (the file <c>xdg-user-dir</c> itself reads, so no process and no
    /// package is needed for it), else <c>~/Pictures</c>.
    /// </summary>
    public static string Folder() => Path.Combine(UserFolder("PICTURES", "Pictures"), "Edgepad");

    /// <summary>
    /// One of the XDG user folders, by its <c>user-dirs.dirs</c> key without the <c>XDG_</c> and <c>_DIR</c> (such as
    /// <c>PICTURES</c> or <c>VIDEOS</c>), found as <see cref="Folder"/> finds Pictures, else <c>~/<paramref name="fallback"/></c>.
    /// </summary>
    public static string UserFolder(string key, string fallback)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Xdg.Resolve(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"), home, ".config");
        string? dirs = null;
        try
        {
            dirs = File.ReadAllText(Path.Combine(config, "user-dirs.dirs"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No file (a desktop without xdg-user-dirs): the default below.
        }

        return UserDir($"XDG_{key}_DIR", fallback, Environment.GetEnvironmentVariable($"XDG_{key}_DIR"), dirs, home);
    }

    /// <summary>
    /// The Pictures folder from the variable, else from <paramref name="userDirs"/> (lines like
    /// <c>XDG_PICTURES_DIR="$HOME/Pictures"</c>; only an absolute path or one under <c>$HOME/</c> is valid there),
    /// else <c>~/Pictures</c>. The home folder itself means the folder is turned off, so it is not used.
    /// </summary>
    internal static string Pictures(string? variable, string? userDirs, string home) =>
        UserDir("XDG_PICTURES_DIR", "Pictures", variable, userDirs, home);

    /// <summary>As <see cref="Pictures"/>, for the user folder whose line in <c>user-dirs.dirs</c> is <paramref name="name"/>.</summary>
    internal static string UserDir(string name, string fallbackName, string? variable, string? userDirs, string home)
    {
        var fallback = Path.Combine(home, fallbackName);
        var chosen = variable;
        if (string.IsNullOrEmpty(chosen) && userDirs is not null)
        {
            var key = name + "=";
            var line = userDirs.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(key, StringComparison.Ordinal));
            var value = line?[key.Length..].Trim('"');
            chosen = value is null ? null
                : value.StartsWith("$HOME/", StringComparison.Ordinal) ? Path.Combine(home, value["$HOME/".Length..])
                : value;
        }

        return !string.IsNullOrEmpty(chosen) && Path.IsPathRooted(chosen)
            && Path.TrimEndingDirectorySeparator(chosen) != Path.TrimEndingDirectorySeparator(home)
            ? chosen
            : fallback;
    }

    /// <summary>The file name stem for a signature saved at <paramref name="now"/>.</summary>
    internal static string Stem(DateTime now) =>
        string.Create(CultureInfo.InvariantCulture, $"signature-{now:yyyyMMdd-HHmmss}");

    /// <summary>
    /// Writes <paramref name="png"/> as <c>stem.png</c> in <paramref name="folder"/>, or <c>stem-2.png</c>,
    /// <c>stem-3.png</c> and on when that is taken, and returns the path. Never over an existing file: two saves in
    /// one second share a stem, and overwriting would lose the first signature. CreateNew rather than a check first,
    /// so a file that appears between the check and the write cannot be overwritten either.
    /// </summary>
    internal static string WriteNew(string folder, string stem, byte[] png)
    {
        Directory.CreateDirectory(folder);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? $"{stem}.png" : string.Create(CultureInfo.InvariantCulture, $"{stem}-{n}.png");
            var path = Path.Combine(folder, name);
            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Taken: try the next number. Only the open is caught, so a failed write (a full disk) still fails
                // the save rather than spinning through names.
                continue;
            }

            using (file)
            {
                file.Write(png);
            }

            return path;
        }
    }

    /// <summary>
    /// The command-line tools that can put a PNG on the clipboard in this session, in the order tried: wl-copy on
    /// Wayland (WAYLAND_DISPLAY set), then xclip on X11 or XWayland (DISPLAY set). Both are optional packages.
    /// </summary>
    internal static IReadOnlyList<ClipboardTool> ImageTools(string? waylandDisplay, string? display)
    {
        List<ClipboardTool> tools = [];
        if (!string.IsNullOrEmpty(waylandDisplay))
        {
            tools.Add(new ClipboardTool("wl-copy", ["--type", PngType]));
        }

        if (!string.IsNullOrEmpty(display))
        {
            tools.Add(new ClipboardTool("xclip", ["-selection", "clipboard", "-t", PngType]));
        }

        return tools;
    }

    /// <summary>
    /// Puts <paramref name="png"/> on the clipboard as image/png: through Avalonia's X11 clipboard first, which this
    /// app then serves for as long as it runs, and through wl-copy or xclip when that is missing or throws. False only
    /// when every way failed. On the UI thread; the tools run off it.
    /// </summary>
    public static async Task<bool> CopyAsync(TopLevel? top, byte[] png)
    {
        if (top?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetValueAsync(DataFormat.CreateBytesPlatformFormat(PngType), png);
                return true;
            }
            catch (Exception e)
            {
                Log.Write($"Avalonia could not put the signature on the clipboard, trying wl-copy and xclip: {e.Message}");
            }
        }

        foreach (var tool in ImageTools(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("DISPLAY")))
        {
            // Waits for the tool, up to its timeout, so not on the UI thread.
            if (await Task.Run(() => Typing.Copy(tool, png)) is not { } failure)
            {
                return true;
            }

            Log.Write($"The signature could not be put on the clipboard with {tool.Program}: {failure}");
        }

        return false;
    }

    private static Point At(Vector2 point) => new(point.X, point.Y);
}

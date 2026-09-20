using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Edgepad.Macros;

/// <summary>
/// The picture on a macro button, taken from the thing the button opens.
///
/// This reads files on the laptop and sends pixels to the phone, which is the opposite direction to
/// everything in <see cref="MacroStore"/>, and worth saying plainly: nothing the phone sends ever reaches
/// a path here. The phone asks for "the icons" and gets whatever the owner's own list points at. A macro
/// whose icon cannot be read is a button with a label, not an error.
///
/// Where the picture comes from is decided by <see cref="SourceFor"/> and nothing else, because the three
/// cases behave differently in a way that is easy to get backwards: a program keeps its icon inside itself
/// as a resource, an ordinary document has no icon of its own and borrows its file type's, and a picture
/// IS the icon. Running a .png through the document path would put the image viewer's icon on the button,
/// which looks like a bug in the extractor rather than the wrong branch.
/// </summary>
internal static class MacroIcons
{
    /// <summary>
    /// The square the phone is sent. Big enough for a macro button at the densities a phone of this era
    /// has, small enough that a full grid is a second of link time rather than a stall — and it is a
    /// fixed size rather than the source's own so one oversized icon cannot cost the whole grid.
    /// </summary>
    public const int Size = 48;

    /// <summary>Tried when <see cref="Size"/> encodes past <see cref="MaxBytes"/>; a photograph will.</summary>
    private const int Fallback = 32;

    /// <summary>
    /// The most an icon may weigh on the wire. A TEXT payload is 255 bytes, so this is about thirty frames
    /// for one button — the point past which a picture is costing the link more than a label is worth.
    /// </summary>
    private const int MaxBytes = 6 * 1024;

    private static readonly Lock Gate = new();

    /// <summary>
    /// Keyed by path and last-write time together, so rebuilding an exe replaces its icon rather than
    /// serving the old one for the life of the process, and a phone reconnecting costs no work at all.
    /// </summary>
    private static readonly Dictionary<string, byte[]?> Cache = [];

    /// <summary>Extensions whose icon lives inside the file as a resource.</summary>
    private static readonly string[] Programs = [".exe", ".dll", ".ico", ".scr", ".cpl"];

    /// <summary>Extensions the platform decodes as an image, which is then the icon itself.</summary>
    private static readonly string[] Pictures = [".png", ".bmp", ".jpg", ".jpeg", ".gif"];

    /// <summary>Where a target's picture comes from, or that there is none.</summary>
    internal enum Source
    {
        /// <summary>A URL, a folder, or a path that is not there: no icon, and the button keeps its name.</summary>
        None,

        /// <summary>A program or an icon file, which carries its own icon as a resource.</summary>
        Resource,

        /// <summary>An ordinary document, which has no icon of its own and shows its file type's.</summary>
        Associated,

        /// <summary>An image file, which is the picture rather than merely having one.</summary>
        Picture,
    }

    /// <summary>
    /// The PNG for <paramref name="macro"/>, or null when it has no readable picture. Safe from any thread;
    /// two callers may extract the same icon at once, which costs a duplicate read and never a wrong answer.
    /// </summary>
    public static byte[]? Png(Macro macro)
    {
        // The override wins when it is there, and falls through to the target when it is not: a macro whose
        // icon file has been deleted should go back to the program's own icon, not to no icon at all.
        var path = Readable(macro.Icon) ?? Readable(macro.Target);
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
    /// Which of the three ways <paramref name="path"/> yields a picture. Split out and given its own test
    /// because it is the whole decision: the Win32 calls it chooses between cannot be exercised on a build
    /// machine that has no real program icons on it, but which one gets called can.
    /// </summary>
    internal static Source SourceFor(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // Folders and URLs land here too. Both could be given an icon — the shell has one for a folder,
            // and a URL's site has a favicon — and neither is worth a web request or a shell interop layer
            // for a button that reads fine with a name on it.
            return Source.None;
        }

        var extension = Path.GetExtension(path);
        if (Programs.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Source.Resource;
        }

        return Pictures.Contains(extension, StringComparer.OrdinalIgnoreCase) ? Source.Picture : Source.Associated;
    }

    /// <summary>The path if it is a file this can read something from, else null.</summary>
    private static string? Readable(string? path) => SourceFor(path) == Source.None ? null : path;

    private static byte[]? Render(string path)
    {
        try
        {
            using var source = Load(path);
            if (source is null)
            {
                return null;
            }

            var png = Encode(source, Size);
            if (png.Length <= MaxBytes)
            {
                return png;
            }

            var small = Encode(source, Fallback);
            if (small.Length <= MaxBytes)
            {
                return small;
            }

            Log.Write($"Macro icon from {path} is {png.Length} bytes even at {Fallback}px and was not sent");
            return null;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException
            or OutOfMemoryException or ExternalException)
        {
            // GDI+ reports a file it cannot decode as OutOfMemoryException, which is its own long-standing
            // oddity rather than a machine out of memory; ExternalException covers the rest of it. This runs
            // on the session thread, so an escape here would drop the phone's connection over a bad .ico.
            Log.Write($"No icon for {path}: {e.Message}");
            return null;
        }
    }

    private static Bitmap? Load(string path) => SourceFor(path) switch
    {
        Source.Resource => FromIcon(Icon.ExtractIcon(path, 0, Size)),
        Source.Associated => FromIcon(Icon.ExtractAssociatedIcon(path)),
        Source.Picture => new Bitmap(path),
        _ => null,
    };

    private static Bitmap? FromIcon(Icon? icon)
    {
        if (icon is null)
        {
            return null;
        }

        using (icon)
        {
            return icon.ToBitmap();
        }
    }

    /// <summary>
    /// <paramref name="source"/> as a PNG square of <paramref name="size"/>. Always 32-bit: an icon's
    /// transparent corners have to survive, because the phone draws it on black or on white depending on
    /// its theme, and a picture flattened against the wrong one is a white box on a black screen.
    /// </summary>
    private static byte[] Encode(Bitmap source, int size)
    {
        using var square = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(square))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, size, size));
        }

        using var stream = new MemoryStream();
        square.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}

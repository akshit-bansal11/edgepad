using System.Globalization;

namespace Edgepad.Annotation;

/// <summary>Where a saved signature's PNG goes on disk: its name, and a write that never replaces an earlier one.</summary>
internal static class SignatureFile
{
    /// <summary>The file name stem for a signature saved at <paramref name="now"/>.</summary>
    public static string Stem(DateTime now) =>
        string.Create(CultureInfo.InvariantCulture, $"signature-{now:yyyyMMdd-HHmmss}");

    /// <summary>
    /// Writes <paramref name="png"/> as <c>stem.png</c> in <paramref name="folder"/>, or <c>stem-2.png</c>,
    /// <c>stem-3.png</c> and on when that is taken, and returns the path. Never over an existing file: two saves in
    /// one second share a stem, and overwriting would lose the first signature. CreateNew rather than a check first,
    /// so a file that appears between the check and the write cannot be overwritten either.
    /// </summary>
    public static string WriteNew(string folder, string stem, byte[] png)
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
}

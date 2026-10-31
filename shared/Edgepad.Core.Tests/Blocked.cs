namespace Edgepad.Tests;

/// <summary>
/// Makes a file unreadable, undeletable and unwritable by putting a folder where it was, which fails the same way
/// on Windows and on Linux. (A FileStream opened with FileShare.None only locks on Windows: Unix has no mandatory
/// locks.) Disposing puts the file back as it was.
/// </summary>
internal sealed class Blocked : IDisposable
{
    private readonly string path;
    private readonly string aside;

    public Blocked(string path)
    {
        this.path = path;
        aside = path + ".aside";
        File.Move(path, aside);
        Directory.CreateDirectory(path);
    }

    public void Dispose()
    {
        Directory.Delete(path);
        File.Move(aside, path);
    }
}

namespace Edgepad.Platform;

/// <summary>
/// What a window is matched against: <paramref name="Image"/>, the program with its symlinks followed, as
/// /proc/pid/exe reads, and <paramref name="Name"/>, the file name the macro reached it by, for WM_CLASS.
/// </summary>
internal sealed record WindowProgram(string Image, string Name);

/// <summary>Which windows belong to a macro's program. Pure: everything it asks of the machine is passed in.</summary>
internal static class WindowMatch
{
    /// <summary>
    /// The program a macro runs, or null when it is not followed: what goes to xdg-open is a URL, a folder or a
    /// document, and a program that cannot be found has no windows to find.
    /// </summary>
    public static WindowProgram? Program(
        string target,
        Func<string, PathKind> classify,
        string home,
        string? searchPath,
        Func<string, bool> exists,
        Func<string, string?> linkTarget)
    {
        var plan = LinuxLauncher.Plan(target, null, classify, home);
        if (plan.File == "xdg-open" || Find(plan.File, searchPath, exists) is not { } path)
        {
            return null;
        }

        return new WindowProgram(linkTarget(path) ?? path, Path.GetFileName(path));
    }

    /// <summary>
    /// A window is the program's when its process runs that file, or, for wrapper scripts and Electron apps, whose
    /// process is some other file, when either WM_CLASS string is the program's name.
    /// </summary>
    public static bool Belongs(WindowProgram program, string? image, IEnumerable<string> classes) =>
        image == program.Image
        || classes.Any(name => name.Equals(program.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A file with a slash in it is that path; a bare name is the first hit on PATH, as starting it would find.</summary>
    private static string? Find(string file, string? searchPath, Func<string, bool> exists)
    {
        if (file.Contains('/', StringComparison.Ordinal))
        {
            return exists(file) ? file : null;
        }

        return (searchPath ?? "")
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, file))
            .FirstOrDefault(exists);
    }
}

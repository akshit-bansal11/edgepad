namespace Edgepad.Macros;

/// <summary>
/// Which program a macro's target names, if any: the whole decision about what the toggle and the close cross may
/// follow. Only a full path to an .exe, or a shortcut to one, counts. A URL, a folder, a document and a Store app
/// (whose windows belong to ApplicationFrameHost.exe) are untracked and always just launch.
/// </summary>
internal static class MacroTarget
{
    /// <summary>
    /// The normalized path of the program, or null when the target is not one. Environment variables are not
    /// expanded, so a target that needs them is untracked rather than matched against the wrong program.
    /// </summary>
    public static string? Program(
        string target, Func<string, bool> exists, Func<string, string?> resolve, string? arguments = null)
    {
        // Arguments make the launch something else (a program opened on a folder), so its windows are not the macro's.
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        var path = target.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path))
        {
            return null;
        }

        var program = Has(path, ".lnk") && exists(path) ? resolve(path) : path;
        return program is not null && Path.IsPathFullyQualified(program) && Has(program, ".exe") && exists(program)
            ? Path.GetFullPath(program)
            : null;
    }

    /// <summary>Whether a process image is the program: full paths, and Windows paths ignore case.</summary>
    public static bool Same(string? image, string program) =>
        string.Equals(image, program, StringComparison.OrdinalIgnoreCase);

    private static bool Has(string path, string extension) => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}

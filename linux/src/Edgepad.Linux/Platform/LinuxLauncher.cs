using System.Diagnostics;
using System.Text;
using Edgepad.Macros;

namespace Edgepad.Platform;

/// <summary>What a macro's target is on disk, as far as launching it is concerned.</summary>
internal enum PathKind
{
    /// <summary>Nothing at that path, so the target is a program name looked up on PATH (or a path that is missing).</summary>
    None,
    Folder,
    File,
    Executable,
}

/// <summary>A launch worked out but not yet started: the program to run and its arguments, one per element.</summary>
internal sealed record LaunchPlan(string File, IReadOnlyList<string> Arguments);

/// <summary>
/// Opens a macro's target the way a double-click would, and never through a shell. A web address, a folder or a file
/// that is not a program goes to <c>xdg-open</c>, which asks the desktop what opens it; anything else is run directly
/// as a program, with the macro's arguments split on spaces (quotes group, nothing is expanded). Because no shell is
/// involved, a <c>;</c>, <c>$(...)</c> or <c>*</c> in a target or its arguments is just a character. The target comes
/// from the owner's own macros file; the phone only names a slot (see MacroStore).
/// </summary>
internal sealed class LinuxLauncher : IMacroLauncher
{
    public void Launch(string target, string? arguments)
    {
        var plan = Plan(target, arguments, Classify, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var info = new ProcessStartInfo(plan.File) { UseShellExecute = false };
        foreach (var argument in plan.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        // Nothing waits on the child or reads from it. Its output is not redirected, so it can never block on a
        // full pipe, and disposing the handle leaves the runtime to reap it when it exits, so a launched program
        // that ends is not left a zombie. A program that cannot start throws, which the store logs and reports.
        Process.Start(info)?.Dispose();
    }

    /// <summary>
    /// How <paramref name="target"/> is started. A URL (<c>scheme://...</c>, or <c>mailto:</c>) and a folder or a
    /// file that is not executable go to xdg-open with the target as its one argument; an executable file or any
    /// other text is run as the program, with <paramref name="arguments"/> split by <see cref="SplitArguments"/>.
    /// A leading <c>~/</c> means the home folder, since no shell is there to say so.
    /// </summary>
    public static LaunchPlan Plan(string target, string? arguments, Func<string, PathKind> classify, string home)
    {
        if (IsUrl(target))
        {
            return new LaunchPlan("xdg-open", [target]);
        }

        var path = target.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, target[2..]) : target;
        return classify(path) switch
        {
            PathKind.Folder or PathKind.File => new LaunchPlan("xdg-open", [path]),
            _ => new LaunchPlan(path, SplitArguments(arguments)),
        };
    }

    public static bool IsUrl(string target) =>
        target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Splits on spaces; single or double quotes group words (and are dropped), and inside double quotes a backslash
    /// escapes a quote or a backslash. Nothing else is interpreted: no variables, globs or substitution. An
    /// unterminated quote runs to the end.
    /// </summary>
    public static List<string> SplitArguments(string? arguments)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return result;
        }

        var word = new StringBuilder();
        var inWord = false;
        char quote = '\0';
        for (var i = 0; i < arguments.Length; i++)
        {
            var c = arguments[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else if (quote == '"' && c == '\\' && i + 1 < arguments.Length && arguments[i + 1] is '"' or '\\')
                {
                    word.Append(arguments[++i]);
                }
                else
                {
                    word.Append(c);
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord)
                {
                    result.Add(word.ToString());
                    word.Clear();
                    inWord = false;
                }
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }

        if (inWord)
        {
            result.Add(word.ToString());
        }

        return result;
    }

    internal static PathKind Classify(string path)
    {
        if (Directory.Exists(path))
        {
            return PathKind.Folder;
        }

        if (!File.Exists(path))
        {
            return PathKind.None;
        }

        try
        {
            // The guard is for the platform analyzer, which the class-level attribute in Program.cs does not reach.
            return !OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0
                ? PathKind.Executable
                : PathKind.File;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PathKind.File;
        }
    }
}

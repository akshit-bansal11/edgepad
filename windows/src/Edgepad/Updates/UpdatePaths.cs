using System.Text.RegularExpressions;

namespace Edgepad.Updates;

/// <summary>Where an in-place update is put, and which old copy the new one may delete. Pure apart from the writability probe.</summary>
internal static partial class UpdatePaths
{
    public const string ReplacesOption = "--replaces";

    [GeneratedRegex(@"^Edgepad-(\d+\.\d+\.\d+)\.exe$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReleaseName();

    /// <summary>The value after <c>--replaces</c>, or null when absent or empty or itself an option.</summary>
    public static string? ReplacesArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == ReplacesOption)
            {
                var value = args[i + 1];
                return value.Length > 0 && !value.StartsWith("--", StringComparison.Ordinal) ? value : null;
            }
        }

        return null;
    }

    /// <summary>The folder that receives the download: beside the running exe when it can be written, else the fallback.</summary>
    public static string ChooseDirectory(string runningDirectory, string fallbackDirectory, Func<string, bool> isWritable) =>
        isWritable(runningDirectory) ? runningDirectory : fallbackDirectory;

    public static string FallbackDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Edgepad", "Updates");

    /// <summary>Whether a file can be created in <paramref name="directory"/>; a temp file is made and removed, nothing is assumed from attributes.</summary>
    public static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".edgepad-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the new copy at <paramref name="selfPath"/> (version <paramref name="self"/>) may delete <paramref name="candidate"/>:
    /// a full path with no traversal, in its own directory, named Edgepad-x.y.z.exe, of a lower version, and not itself.
    /// </summary>
    public static bool IsReplaceable(string candidate, string selfPath, Version self)
    {
        try
        {
            if (!Path.IsPathFullyQualified(candidate) || !Path.IsPathFullyQualified(selfPath)
                || Path.GetFullPath(candidate) != candidate)
            {
                return false;
            }

            var directory = Path.GetDirectoryName(candidate);
            if (directory is null
                || !string.Equals(directory, Path.GetDirectoryName(selfPath), StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, selfPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var match = ReleaseName().Match(Path.GetFileName(candidate));
            return match.Success && Version.TryParse(match.Groups[1].Value, out var old) && old < self;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

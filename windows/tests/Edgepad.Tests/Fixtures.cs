namespace Edgepad.Tests;

/// <summary>Reads the shared files under protocol/, which the Android suite reads too.</summary>
internal static class Fixtures
{
    public static string[] Lines(string name) =>
        [.. File.ReadAllLines(Find(name)).Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))];

    private static string Find(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "protocol", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"protocol/{name} was not found above the test output directory");
    }
}

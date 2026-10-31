using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LauncherTests
{
    private const string Home = "/home/me";

    [Theory]
    [InlineData("https://example.com/a?b=c&d=e")]
    [InlineData("http://localhost:8080")]
    [InlineData("steam://rungameid/440")]
    [InlineData("mailto:me@example.com")]
    public void AUrlGoesToXdgOpenWholeAndWithoutItsArguments(string url)
    {
        var plan = LinuxLauncher.Plan(url, "ignored", _ => PathKind.None, Home);
        Assert.Equal("xdg-open", plan.File);
        Assert.Equal([url], plan.Arguments);
    }

    [Theory]
    [InlineData("Folder")]
    [InlineData("File")]
    public void AFolderOrADocumentGoesToXdgOpen(string kind)
    {
        var plan = LinuxLauncher.Plan("/home/me/notes.txt", null, _ => Enum.Parse<PathKind>(kind), Home);
        Assert.Equal("xdg-open", plan.File);
        Assert.Equal(["/home/me/notes.txt"], plan.Arguments);
    }

    [Fact]
    public void AnExecutableIsRunNotOpened()
    {
        var plan = LinuxLauncher.Plan("/usr/bin/firefox", "--private-window https://example.com", _ => PathKind.Executable, Home);
        Assert.Equal("/usr/bin/firefox", plan.File);
        Assert.Equal(["--private-window", "https://example.com"], plan.Arguments);
    }

    [Fact]
    public void AProgramNameIsRunAsItIsForPathToFind()
    {
        var plan = LinuxLauncher.Plan("firefox", null, _ => PathKind.None, Home);
        Assert.Equal("firefox", plan.File);
        Assert.Empty(plan.Arguments);
    }

    [Fact]
    public void AHomeShortcutIsExpandedBeforeTheCheck()
    {
        string? asked = null;
        var plan = LinuxLauncher.Plan(
            "~/Documents",
            null,
            path =>
            {
                asked = path;
                return PathKind.Folder;
            },
            Home);
        Assert.Equal("/home/me/Documents", asked);
        Assert.Equal(["/home/me/Documents"], plan.Arguments);
    }

    [Fact]
    public void ShellSyntaxIsJustCharacters()
    {
        var plan = LinuxLauncher.Plan("echo", "a; rm -rf ~ $(whoami) *", _ => PathKind.None, Home);
        Assert.Equal("echo", plan.File);
        Assert.Equal(["a;", "rm", "-rf", "~", "$(whoami)", "*"], plan.Arguments);
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("  a   b  ", new[] { "a", "b" })]
    [InlineData("--name \"My App\" x", new[] { "--name", "My App", "x" })]
    [InlineData("'single quoted' two", new[] { "single quoted", "two" })]
    [InlineData("a\"b c\"d", new[] { "ab cd" })]
    [InlineData("\"say \\\"hi\\\" \\\\ now\"", new[] { "say \"hi\" \\ now" })]
    // Backslash means nothing outside double quotes, or before anything but a quote or a backslash.
    [InlineData("C:\\path 'it\\s'", new[] { "C:\\path", "it\\s" })]
    [InlineData("\"unterminated rest", new[] { "unterminated rest" })]
    [InlineData("\"\" x", new[] { "", "x" })]
    public void ArgumentsSplitOnSpacesWithQuotesGrouping(string? arguments, string[] expected) =>
        Assert.Equal(expected, LinuxLauncher.SplitArguments(arguments));
}

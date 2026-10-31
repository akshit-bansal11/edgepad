using Edgepad.Macros;
using Edgepad.Platform;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class WindowMatchTests
{
    private const string Home = "/home/me";
    private const string SearchPath = "/usr/local/bin:/usr/bin";

    private static WindowProgram? Program(
        string target,
        Func<string, bool>? exists = null,
        Func<string, string?>? linkTarget = null,
        PathKind kind = PathKind.None) =>
        WindowMatch.Program(target, _ => kind, Home, SearchPath, exists ?? (_ => false), linkTarget ?? (_ => null));

    [Fact]
    public void AProgramWithArgumentsIsNotFollowed()
    {
        var program = WindowMatch.Program(
            "firefox", _ => PathKind.None, Home, SearchPath, path => path == "/usr/bin/firefox", _ => null, "/home/me/proj");

        Assert.Null(program);
    }

    [Fact]
    public void ABareNameIsLookedUpOnPath()
    {
        var program = Program("firefox", path => path == "/usr/bin/firefox");

        Assert.Equal(new WindowProgram("/usr/bin/firefox", "firefox"), program);
    }

    [Fact]
    public void TheFirstHitOnPathWins()
    {
        var program = Program("tool", path => path is "/usr/local/bin/tool" or "/usr/bin/tool");

        Assert.Equal("/usr/local/bin/tool", program?.Image);
    }

    [Fact]
    public void AnExecutablePathIsItself()
    {
        var program = Program("/opt/app/app", path => path == "/opt/app/app", kind: PathKind.Executable);

        Assert.Equal(new WindowProgram("/opt/app/app", "app"), program);
    }

    [Fact]
    public void AHomePathMeansTheHomeFolder()
    {
        var program = Program("~/bin/tool", path => path == "/home/me/bin/tool", kind: PathKind.Executable);

        Assert.Equal("/home/me/bin/tool", program?.Image);
    }

    [Fact]
    public void ASymlinkedProgramMatchesTheFileItsProcessRuns()
    {
        var program = Program(
            "firefox",
            path => path == "/usr/bin/firefox",
            path => path == "/usr/bin/firefox" ? "/usr/lib/firefox/firefox" : null);

        Assert.NotNull(program);
        Assert.True(WindowMatch.Belongs(program, "/usr/lib/firefox/firefox", []));
    }

    [Fact]
    public void ASymlinkedProgramKeepsTheNameItWasReachedBy()
    {
        var program = Program(
            "google-chrome",
            path => path == "/usr/bin/google-chrome",
            _ => "/opt/google/chrome/google-chrome-stable");

        Assert.NotNull(program);
        Assert.True(WindowMatch.Belongs(program, "/opt/google/chrome/chrome", ["google-chrome", "Google-chrome"]));
    }

    [Fact]
    public void AProgramThatCannotBeFoundIsNotFollowed() => Assert.Null(Program("nothing"));

    [Fact]
    public void AnEmptyPathFindsNothing() =>
        Assert.Null(WindowMatch.Program("firefox", _ => PathKind.None, Home, null, _ => true, _ => null));

    [Theory]
    [InlineData("https://example.com", "None")]
    [InlineData("mailto:me@example.com", "None")]
    [InlineData("/home/me/Documents", "Folder")]
    [InlineData("/home/me/notes.txt", "File")]
    public void WhatGoesToXdgOpenIsNotFollowed(string target, string kind) =>
        Assert.Null(Program(target, _ => true, kind: Enum.Parse<PathKind>(kind)));

    [Theory]
    // Electron: the process is the app's own binary, but a wrapper script is what the macro names.
    [InlineData("code", "code", "Code")]
    // Firefox's instance is Navigator; either string will do.
    [InlineData("firefox", "Navigator", "firefox")]
    public void TheClassIsTheFallback(string name, string instance, string className)
    {
        var program = new WindowProgram($"/usr/bin/{name}", name);

        Assert.True(WindowMatch.Belongs(program, "/usr/share/other/electron", [instance, className]));
    }

    [Fact]
    public void AnUnrelatedProgramDoesNotMatch()
    {
        var program = new WindowProgram("/usr/bin/gedit", "gedit");

        Assert.False(WindowMatch.Belongs(program, "/usr/bin/firefox", ["Navigator", "firefox"]));
    }

    [Fact]
    public void AWindowWhoseProcessCannotBeReadCanStillMatchByClass()
    {
        var program = new WindowProgram("/usr/bin/gedit", "gedit");

        Assert.True(WindowMatch.Belongs(program, null, ["gedit", "Gedit"]));
        Assert.False(WindowMatch.Belongs(program, null, []));
    }

    [Fact]
    public void WaylandIsNeverAvailableAndNeverTouchesX()
    {
        var windows = new LinuxMacroWindows(
            name => name == "XDG_SESSION_TYPE" ? "wayland" : ":0",
            () => throw new InvalidOperationException("opened a display on Wayland"));

        Assert.False(windows.Available);
    }

    [Fact]
    public void X11WithNoDisplayIsNotAvailable()
    {
        var windows = new LinuxMacroWindows(
            name => name == "XDG_SESSION_TYPE" ? "x11" : null,
            () => throw new InvalidOperationException("opened a display that is not set"));

        Assert.False(windows.Available);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void X11IsAvailableOnlyWhenTheDisplayOpens(bool opens)
    {
        var windows = new LinuxMacroWindows(name => name == "XDG_SESSION_TYPE" ? "x11" : ":0", () => opens);

        Assert.Equal(opens, windows.Available);
    }

    [Fact]
    public void AMissingLibX11IsNotAvailableRatherThanAnException()
    {
        var windows = new LinuxMacroWindows(
            name => name == "XDG_SESSION_TYPE" ? "x11" : ":0",
            () => throw new DllNotFoundException("libX11.so.6"));

        Assert.False(windows.Available);
    }

    [Fact]
    public void WithoutAWatcherRunningIsNeverNamed()
    {
        var windows = new LinuxMacroWindows(_ => "wayland", () => false);
        var store = new MacroStore(
            Path.Combine(Path.GetTempPath(), $"edgepad-{Guid.NewGuid():N}.txt"), new LinuxLauncher(), work => work(), windows);

        Assert.Null(store.CreateWatcher(_ => { }));
        Assert.Equal("annotate/capture", Annotator.Features(annotate: true));
    }

    [Fact]
    public void RunningIsAppendedToTheTokensInForce()
    {
        Assert.Equal("annotate/capture/running", Annotator.Features(annotate: true, running: true));
        Assert.Equal("capture/running", Annotator.Features(annotate: false, running: true));
    }
}

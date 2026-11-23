using System.Diagnostics;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class ProcessUrlOpenerTests
{
    private static readonly Uri Repo = new("https://github.com/me/repo/pull/3?a=1&b=2");

    private readonly List<ProcessStartInfo> launched = [];
    private readonly List<string> logged = [];

    private bool Launch(ProcessStartInfo info)
    {
        launched.Add(info);
        return true;
    }

    [Fact]
    public void WindowsShellExecutesTheExactAddressWithNoArgumentsAndNoCmd()
    {
        Assert.True(new ProcessUrlOpener(ProcessUrlOpener.Windows, Launch, logged.Add).Open(Repo));

        var info = Assert.Single(launched);
        Assert.True(info.UseShellExecute);
        Assert.Equal(Repo.AbsoluteUri, info.FileName);
        Assert.Equal(string.Empty, info.Arguments);
        Assert.Empty(info.ArgumentList);
        Assert.DoesNotContain("cmd", info.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinuxRunsXdgOpenWithTheAddressAsItsOneArgumentAndNoShell()
    {
        Assert.True(new ProcessUrlOpener(ProcessUrlOpener.Linux, Launch, logged.Add).Open(Repo));

        var info = Assert.Single(launched);
        Assert.False(info.UseShellExecute);
        Assert.Equal("xdg-open", info.FileName);
        Assert.Equal(string.Empty, info.Arguments);
        Assert.Equal(Repo.AbsoluteUri, Assert.Single(info.ArgumentList));
    }

    [Theory]
    [InlineData("http://github.com/me/repo")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://github.com/x")]
    [InlineData("mailto:a@b.example")]
    public void AnythingButHttpsIsNeverLaunched(string address)
    {
        Assert.False(new ProcessUrlOpener(ProcessUrlOpener.Windows, Launch, logged.Add).Open(new Uri(address)));
        Assert.False(new ProcessUrlOpener(ProcessUrlOpener.Linux, Launch, logged.Add).Open(new Uri(address)));
        Assert.Empty(launched);
    }

    [Fact]
    public void ALaunchThatFailsToStartIsReportedNotThrown()
    {
        var opener = new ProcessUrlOpener(ProcessUrlOpener.Linux, _ => throw new InvalidOperationException("no xdg-open"), logged.Add);
        Assert.False(opener.Open(Repo));
        Assert.Equal(1, opener.Failed);

        // Host and path only: the query never reaches the log.
        var line = Assert.Single(logged);
        Assert.Contains("github.com/me/repo/pull/3", line, StringComparison.Ordinal);
        Assert.DoesNotContain("a=1", line, StringComparison.Ordinal);
    }
}

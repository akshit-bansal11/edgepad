using Edgepad.Network;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The netsh arguments. Running them needs an administrator's yes, which no CI runner gives.</summary>
public sealed class FirewallTests
{
    [Fact]
    public void TheRuleNamesThisExeQuotedForEveryProfileButOnlyTheLocalSubnet()
    {
        Assert.Equal(
            "advfirewall firewall add rule name=\"Edgepad\" dir=in action=allow program=\"C:\\Program Files\\Edgepad\\Edgepad.exe\" enable=yes profile=any remoteip=localsubnet",
            Firewall.Arguments(@"C:\Program Files\Edgepad\Edgepad.exe"));
    }

    [Fact]
    public void TheDeleteNamesThisExeQuotedAndInboundOnly()
    {
        Assert.Equal(
            "advfirewall firewall delete rule name=all dir=in program=\"C:\\Program Files\\Edgepad\\Edgepad.exe\"",
            Firewall.DeleteArguments(@"C:\Program Files\Edgepad\Edgepad.exe"));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Edgepad\Edgepad.exe")]
    [InlineData(@"C:\Tools & Apps\Edgepad.exe")]
    public void CmdRunsTheDeleteThenTheAddWithNetshByItsFullPath(string exe)
    {
        var netsh = Path.Combine(Environment.SystemDirectory, "netsh.exe");

        Assert.Equal(
            $"\"\"{netsh}\" advfirewall firewall delete rule name=all dir=in program=\"{exe}\" & "
                + $"\"{netsh}\" advfirewall firewall add rule name=\"Edgepad\" dir=in action=allow program=\"{exe}\" enable=yes profile=any remoteip=localsubnet\"",
            Firewall.CommandLine(exe));
    }

    [Theory]
    [InlineData("C:\\x\" action=block program=\"C:\\y.exe")]
    [InlineData(@"C:\%TEMP%\Edgepad.exe")]
    public void APathCmdCouldMisreadIsRefused(string hostile)
    {
        Assert.Throws<ArgumentException>(() => Firewall.Arguments(hostile));
        Assert.Throws<ArgumentException>(() => Firewall.DeleteArguments(hostile));
        Assert.Throws<ArgumentException>(() => Firewall.CommandLine(hostile));
    }
}

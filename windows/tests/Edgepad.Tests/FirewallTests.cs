using Edgepad.Network;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The netsh arguments. Running them needs an administrator's yes, which no CI runner gives.</summary>
public sealed class FirewallTests
{
    [Fact]
    public void TheRuleNamesThisExeQuotedForEveryProfile()
    {
        Assert.Equal(
            "advfirewall firewall add rule name=\"Edgepad\" dir=in action=allow program=\"C:\\Program Files\\Edgepad\\Edgepad.exe\" enable=yes profile=any",
            Firewall.Arguments(@"C:\Program Files\Edgepad\Edgepad.exe"));
    }

    [Fact]
    public void TheDeleteNamesThisExeQuotedAndInboundOnly()
    {
        Assert.Equal(
            "advfirewall firewall delete rule name=all dir=in program=\"C:\\Program Files\\Edgepad\\Edgepad.exe\"",
            Firewall.DeleteArguments(@"C:\Program Files\Edgepad\Edgepad.exe"));
    }

    [Fact]
    public void APathThatCouldEndTheQuotingIsRefused()
    {
        var hostile = "C:\\x\" action=block program=\"C:\\y.exe";

        Assert.Throws<ArgumentException>(() => Firewall.Arguments(hostile));
        Assert.Throws<ArgumentException>(() => Firewall.DeleteArguments(hostile));
    }
}

using Edgepad.Network;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The netsh arguments. Running them needs an administrator's yes, which no CI runner gives.</summary>
public sealed class FirewallTests
{
    [Fact]
    public void TheRuleNamesThisExeQuotedForPrivateAndPublicNetworks()
    {
        Assert.Equal(
            "advfirewall firewall add rule name=\"Edgepad\" dir=in action=allow program=\"C:\\Program Files\\Edgepad\\Edgepad.exe\" enable=yes profile=private,public",
            Firewall.Arguments(@"C:\Program Files\Edgepad\Edgepad.exe"));
    }

    [Fact]
    public void APathThatCouldEndTheQuotingIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Firewall.Arguments("C:\\x\" action=block program=\"C:\\y.exe"));
    }
}

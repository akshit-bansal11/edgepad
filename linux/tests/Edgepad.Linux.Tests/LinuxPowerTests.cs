using Edgepad.Media;
using Edgepad.Platform;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxPowerTests
{
    [Theory]
    [InlineData(0, "Suspend")]
    [InlineData(1, "PowerOff")]
    public void EachKindMapsToItsLogindMethod(int kind, string method) =>
        Assert.Equal(method, LinuxPower.MethodFor((EndKind)kind));

    [Theory]
    [InlineData("yes", true)]
    [InlineData("challenge", true)]
    [InlineData("na", false)]
    [InlineData("no", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAYesOrAChallengeMeansLogindCan(string? answer, bool allowed) =>
        Assert.Equal(allowed, LinuxPower.Allowed(answer));

    [Fact]
    public void TheEndActionTokenIsOfferedOnlyWhenAsked()
    {
        Assert.DoesNotContain("endaction", Annotator.Features(annotate: true));
        Assert.EndsWith("/endaction", Annotator.Features(annotate: true, endAction: true), StringComparison.Ordinal);
    }
}

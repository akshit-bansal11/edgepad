using System.Runtime.InteropServices;
using Edgepad.Updates;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdateCheckTests
{
    private const string Tag = "https://github.com/akshit-bansal11/edgepad/releases/tag/v";

    [Fact]
    public void TheVersionIsReadFromTheTagRedirect() =>
        Assert.Equal(new Version(3, 2, 1), UpdateCheck.VersionIn(Tag + "3.2.1"));

    [Theory]
    [InlineData(null)]
    [InlineData("https://github.com/someone-else/edgepad/releases/tag/v9.9.9")]
    [InlineData("https://github.com/akshit-bansal11/edgepad/releases")]
    public void ARedirectThatIsNotThisRepositorysTagPageIsRefused(string? location) =>
        Assert.Null(UpdateCheck.VersionIn(location));

    [Theory]
    [InlineData("3.2")]
    [InlineData("3.2.1.4")]
    [InlineData("3.2.1-draft.4")]
    [InlineData("3.2.+1")]
    [InlineData("3.2.1/../../evil")]
    public void ATagThatIsNotAPlainVersionIsRefused(string tag) =>
        Assert.Null(UpdateCheck.VersionIn(Tag + tag));

    [Theory]
    [InlineData("3.2.1", "3.2.0", true)]
    [InlineData("3.10.0", "3.9.5", true)]
    [InlineData("3.2.0", "3.2.0", false)]
    [InlineData("3.1.9", "3.2.0", false)]
    [InlineData("3.3.0", "3.3.0-draft.38", false)]
    [InlineData("3.3.0", "not a version", false)]
    public void OnlyALaterReleaseIsNewer(string candidate, string running, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(candidate), running));

    [Fact]
    public void ReleaseFilesAreAddressedUnderTheirOwnTag()
    {
        var version = new Version(5, 0, 0);
        const string Base = "https://github.com/akshit-bansal11/edgepad/releases/download/v5.0.0/";
        Assert.Equal(Base + "SHA256SUMS.txt", UpdateCheck.SumsUrl(version).AbsoluteUri);
        Assert.Equal(Base + "SHA256SUMS.txt.sig", UpdateCheck.SigUrl(version).AbsoluteUri);
        Assert.Equal(Base + "Edgepad-5.0.0.exe", UpdateCheck.AssetUrl(version, UpdateCheck.ExeName(version)).AbsoluteUri);
        Assert.Equal("Edgepad-5.0.0-linux-x64.tar.gz", UpdateCheck.LinuxName(version, Architecture.X64));
        Assert.Equal("Edgepad-5.0.0-mac.zip", UpdateCheck.MacName(version));
    }

    [Fact]
    public void TheLinuxArchiveMatchesTheCpuAndAnUnknownCpuHasNone()
    {
        var version = new Version(5, 0, 0);
        Assert.Equal("Edgepad-5.0.0-linux-arm64.tar.gz", UpdateCheck.LinuxName(version, Architecture.Arm64));
        Assert.Null(UpdateCheck.LinuxName(version, Architecture.Arm));
        Assert.Null(UpdateCheck.LinuxName(version, Architecture.X86));
    }

    [Theory]
    [InlineData("5.0.1", "5.0.0", null, true, UpdateDecision.Install)]
    [InlineData("5.0.0", "5.0.0", null, true, UpdateDecision.NotNewer)]
    [InlineData("4.9.9", "5.0.0", null, true, UpdateDecision.NotNewer)]
    [InlineData("5.0.1", "5.0.1-draft.3", null, true, UpdateDecision.NotNewer)]
    [InlineData("5.0.1", "not a version", null, true, UpdateDecision.NotNewer)]
    [InlineData("5.0.1", "5.0.0", "5.0.1", true, UpdateDecision.Skipped)]
    [InlineData("5.0.2", "5.0.0", "5.0.1", true, UpdateDecision.Install)]
    [InlineData("5.0.1", "5.0.0", null, false, UpdateDecision.NoKey)]
    [InlineData("6.0.0", "5.2.0", null, true, UpdateDecision.InstallNeedsMajorConfirm)]
    [InlineData("6.0.0", "5.2.0", null, false, UpdateDecision.NoKey)]
    [InlineData("5.0", "4.0.0", null, true, UpdateDecision.NotNewer)]
    [InlineData("5.0.1.2", "4.0.0", null, true, UpdateDecision.NotNewer)]
    public void ThePolicyDecidesFromTheOfferTheRunningVersionTheSkipAndTheKey(
        string offered, string running, string? skipped, bool keySet, UpdateDecision expected) =>
        Assert.Equal(
            expected,
            UpdatePolicy.Decide(Version.Parse(offered), running, skipped is null ? null : Version.Parse(skipped), keySet));
}

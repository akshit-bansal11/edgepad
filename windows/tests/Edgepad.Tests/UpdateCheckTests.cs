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
}

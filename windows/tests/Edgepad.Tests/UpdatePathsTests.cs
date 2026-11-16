using Edgepad.Updates;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdatePathsTests
{
    private const string Self = @"C:\Apps\Edgepad-5.0.1.exe";
    private static readonly Version SelfVersion = new(5, 0, 1);

    [Theory]
    [InlineData(@"C:\Apps\Edgepad-5.0.0.exe", true)]
    [InlineData(@"C:\Apps\Edgepad-4.2.1.exe", true)]
    [InlineData(@"C:\apps\Edgepad-5.0.0.EXE", false)]
    [InlineData(@"C:\Apps\Edgepad-5.0.1.exe", false)]
    [InlineData(@"C:\Apps\Edgepad-5.0.2.exe", false)]
    [InlineData(@"C:\Apps\..\Apps\Edgepad-5.0.0.exe", false)]
    [InlineData(@"C:\Other\Edgepad-5.0.0.exe", false)]
    [InlineData(@"C:\Apps\Sub\Edgepad-5.0.0.exe", false)]
    [InlineData(@"C:\Apps\Edgepad.exe", false)]
    [InlineData(@"C:\Apps\Edgepad-5.0.exe", false)]
    [InlineData(@"C:\Apps\evil-Edgepad-5.0.0.exe", false)]
    [InlineData(@"Edgepad-5.0.0.exe", false)]
    [InlineData("", false)]
    public void ReplaceablePredicate(string candidate, bool expected) =>
        Assert.Equal(expected, UpdatePaths.IsReplaceable(candidate, Self, SelfVersion));

    [Fact]
    public void BesideTheExeWhenWritable() =>
        Assert.Equal(@"C:\Apps", UpdatePaths.ChooseDirectory(@"C:\Apps", @"C:\Fallback", _ => true));

    [Fact]
    public void FallbackWhenNotWritable() =>
        Assert.Equal(@"C:\Fallback", UpdatePaths.ChooseDirectory(@"C:\Apps", @"C:\Fallback", _ => false));

    [Fact]
    public void ReplacesArgumentIsRead() =>
        Assert.Equal(@"C:\Apps\Edgepad-5.0.0.exe", UpdatePaths.ReplacesArgument(["--replaces", @"C:\Apps\Edgepad-5.0.0.exe"]));

    [Theory]
    [InlineData]
    [InlineData("--replaces")]
    [InlineData("--replaces", "")]
    [InlineData("--replaces", "--restart")]
    [InlineData("--restart", "S-1")]
    public void ReplacesArgumentIsRefusedWhenMalformed(params string[] args) =>
        Assert.Null(UpdatePaths.ReplacesArgument(args));

    [Fact]
    public async Task NoKeyInstallsNothing()
    {
        // ponytail: valid only while UpdateKey is unset; once the key is embedded the network is reached and this test is replaced.
        if (UpdateKey.IsSet)
        {
            return;
        }

        var outcome = await WindowsUpdater.InstallAsync(new Version(9, 9, 9), Self, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateOutcome.Failed, outcome);
    }
}

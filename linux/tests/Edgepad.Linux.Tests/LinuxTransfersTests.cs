using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxTransfersTests
{
    private static readonly string[] AbsolutePaths = ["/tmp/a b.txt", "/tmp/c.txt"];

    [Fact]
    public void FilesLandInAnEdgepadFolder() =>
        Assert.Equal("Edgepad", Path.GetFileName(LinuxTransfers.Destination()));

    [Fact]
    public void ARestrictedFileIsOwnerReadWriteOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.GetTempFileName();
        try
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.GroupRead);
            LinuxTransfers.Restrict(file);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(3, 1, true)]
    [InlineData(4, 0, false)]
    [InlineData(2, 2, false)]
    public void SocketsWaitingToIdentifyThemselvesShareTheUnauthenticatedCaps(int total, int fromAddress, bool allowed) =>
        Assert.Equal(allowed, SessionHost.CanAwait(total, fromAddress));

    [Fact]
    public void OnlyAbsolutePathsAreTakenFromTheSendSocket() =>
        Assert.Equal(AbsolutePaths, SendSocket.ParsePaths("/tmp/a b.txt\n\nrelative.txt\n /tmp/c.txt \n"));
}

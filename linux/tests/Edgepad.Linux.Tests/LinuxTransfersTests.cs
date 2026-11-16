using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxTransfersTests
{
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
}

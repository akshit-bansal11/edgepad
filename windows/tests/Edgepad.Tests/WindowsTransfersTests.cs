using Edgepad.Dispatch;
using Edgepad.Net;
using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Where received files go, the Mark of the Web on them, and the cap on sockets still to say what they are.</summary>
public sealed class WindowsTransfersTests
{
    [Fact]
    public void FilesLandInEdgepadUnderDownloads()
    {
        Assert.Equal("Edgepad", Path.GetFileName(WindowsTransfers.Destination()));
        Assert.Equal(WindowsTransfers.DownloadsFolder(), Path.GetDirectoryName(WindowsTransfers.Destination()));
    }

    [Fact]
    public void AReceivedFileIsMarkedAsFromTheInternet()
    {
        var folder = Directory.CreateTempSubdirectory("edgepad-zone");
        try
        {
            var file = Path.Combine(folder.FullName, "a.txt");
            File.WriteAllText(file, "x");

            WindowsTransfers.MarkFromWeb(file);

            Assert.Equal(WindowsTransfers.ZoneIdentifier, File.ReadAllText(file + ":Zone.Identifier"));
            Assert.Equal("x", File.ReadAllText(file));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFileThatCannotBeMarkedIsNotAnError()
    {
        WindowsTransfers.MarkFromWeb(Path.Combine(Path.GetTempPath(), "edgepad-missing-" + Guid.NewGuid(), "a.txt"));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 1, true)]
    [InlineData(1, TcpLinkListener.MaxUnauthenticatedPerHost, false)]
    [InlineData(TcpLinkListener.MaxUnauthenticated, 0, false)]
    public void SocketsWaitingToIdentifyThemselvesAreBounded(int total, int fromAddress, bool allowed)
    {
        Assert.Equal(allowed, SessionHost.CanAwait(total, fromAddress));
    }
}

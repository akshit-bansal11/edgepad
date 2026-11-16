using System.Text;
using Edgepad.Blob;
using Edgepad.Input;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PasteGuardTests
{
    [Fact]
    public void PreviousTextIsRestoredAfterThePaste()
    {
        var surface = new FakeSurface(Text("before"));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => surface.Calls.Add("settle"));
        Assert.Equal(["read", "paste", "settle", "write before"], surface.Calls);
    }

    [Fact]
    public void NothingIsWrittenForAnEmptyClipboard()
    {
        var surface = new FakeSurface(null);
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { });
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void ASensitiveClipboardIsNotWritten()
    {
        var surface = new FakeSurface(new ClipboardContent(BlobKind.ClipText, [], Sensitive: true));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { });
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void ACutOffReadIsNotWrittenBack()
    {
        var surface = new FakeSurface(new ClipboardContent(BlobKind.ClipText, new byte[ClipboardSync.MaxTextBytes + 1]));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { });
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void AThrowingReadStillPastes()
    {
        var surface = new FakeSurface(null) { ReadThrows = true };
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { });
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void AThrowingWriteIsNotAnError()
    {
        var surface = new FakeSurface(Text("before")) { WriteThrows = true };
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { });
        Assert.Equal(["read", "paste", "write before"], surface.Calls);
    }

    [Fact]
    public void AThrowingPasteStillRestoresAndPropagates()
    {
        var surface = new FakeSurface(Text("before"));
        Assert.Throws<InvalidOperationException>(() =>
            PasteGuard.Around(surface, () => throw new InvalidOperationException("paste"), () => surface.Calls.Add("settle")));
        Assert.Equal(["read", "write before"], surface.Calls);
    }

    private static ClipboardContent Text(string text) => new(BlobKind.ClipText, Encoding.UTF8.GetBytes(text));

    private sealed class FakeSurface(ClipboardContent? held) : IClipboardSurface
    {
        public List<string> Calls { get; } = [];

        public bool ReadThrows { get; init; }

        public bool WriteThrows { get; init; }

        public ClipboardContent? Read()
        {
            Calls.Add("read");
            return ReadThrows ? throw new InvalidOperationException("read") : held;
        }

        public void Write(ClipboardContent content)
        {
            Calls.Add($"write {Encoding.UTF8.GetString(content.Data)}");
            if (WriteThrows)
            {
                throw new InvalidOperationException("write");
            }
        }
    }
}

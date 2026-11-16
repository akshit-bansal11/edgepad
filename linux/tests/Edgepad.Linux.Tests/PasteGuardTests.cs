using System.Text;
using Edgepad.Blob;
using Edgepad.Input;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PasteGuardTests
{
    private const string Pasted = "pasted";

    [Fact]
    public void PreviousTextIsRestoredAfterThePaste()
    {
        var surface = new FakeSurface(Text("before"), Text(Pasted));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => surface.Calls.Add("settle"), Pasted);
        Assert.Equal(["read", "paste", "settle", "read", "write before"], surface.Calls);
    }

    [Fact]
    public void ACopyMadeDuringTheTypingIsKept()
    {
        var surface = new FakeSurface(Text("before"), Text("copied meanwhile"));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { }, Pasted);
        Assert.Equal(["read", "paste", "read"], surface.Calls);
    }

    [Fact]
    public void AFailedCheckLeavesTheClipboardAlone()
    {
        var surface = new FakeSurface(Text("before")) { ThrowFromRead = 2 };
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { }, Pasted);
        Assert.Equal(["read", "paste", "read"], surface.Calls);
    }

    [Fact]
    public void NothingIsWrittenForAnEmptyClipboard()
    {
        var surface = new FakeSurface(null, Text(Pasted));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { }, Pasted);
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void ASensitiveClipboardIsNotWritten()
    {
        var surface = new FakeSurface(new ClipboardContent(BlobKind.ClipText, [], Sensitive: true), Text(Pasted));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { }, Pasted);
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void ACutOffReadIsNotWrittenBack()
    {
        var surface = new FakeSurface(new ClipboardContent(BlobKind.ClipText, new byte[ClipboardSync.MaxTextBytes + 1]), Text(Pasted));
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { }, Pasted);
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void AThrowingReadStillPastes()
    {
        var surface = new FakeSurface { ThrowFromRead = 1 };
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { }, Pasted);
        Assert.Equal(["read", "paste"], surface.Calls);
    }

    [Fact]
    public void AThrowingWriteIsNotAnError()
    {
        var surface = new FakeSurface(Text("before"), Text(Pasted)) { WriteThrows = true };
        PasteGuard.Around(surface, () => surface.Calls.Add("paste"), () => { }, Pasted);
        Assert.Equal(["read", "paste", "read", "write before"], surface.Calls);
    }

    [Fact]
    public void AThrowingPasteStillRestoresAndPropagates()
    {
        var surface = new FakeSurface(Text("before"), Text(Pasted));
        Assert.Throws<InvalidOperationException>(() =>
            PasteGuard.Around(surface, () => throw new InvalidOperationException("paste"), () => surface.Calls.Add("settle"), Pasted));
        Assert.Equal(["read", "read", "write before"], surface.Calls);
    }

    private static ClipboardContent Text(string text) => new(BlobKind.ClipText, Encoding.UTF8.GetBytes(text));

    /// <summary>Answers each read with the next of <paramref name="reads"/>, the last one again once they run out.</summary>
    private sealed class FakeSurface(params ClipboardContent?[] reads) : IClipboardSurface
    {
        private int count;

        public List<string> Calls { get; } = [];

        /// <summary>The read, counted from 1, that throws; 0 for none.</summary>
        public int ThrowFromRead { get; init; }

        public bool WriteThrows { get; init; }

        public ClipboardContent? Read()
        {
            Calls.Add("read");
            count++;
            return count == ThrowFromRead
                ? throw new InvalidOperationException("read")
                : reads[Math.Min(count, reads.Length) - 1];
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

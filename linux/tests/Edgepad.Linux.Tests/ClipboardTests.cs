using Edgepad.Blob;
using Edgepad.Net;
using Edgepad.Platform;
using Edgepad.Protocol;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>In one collection with <see cref="PasteGuardTests"/>, since both raise the process-wide <see cref="ClipboardQuiet"/>.</summary>
[Collection(nameof(ClipboardQuiet))]
public sealed class ClipboardTests
{
    [Fact]
    public void APasswordManagerHintMeansNothingIsRead()
    {
        var pick = LinuxClipboard.Pick(["text/plain;charset=utf-8", "x-kde-passwordManagerHint", "image/png"]);

        Assert.True(pick.Sensitive);
        Assert.Null(pick.Kind);
        Assert.Null(pick.Target);
    }

    [Fact]
    public void AnImageWinsOverText()
    {
        var pick = LinuxClipboard.Pick(["UTF8_STRING", "image/png"]);

        Assert.False(pick.Sensitive);
        Assert.Equal(BlobKind.ClipImage, pick.Kind);
        Assert.Equal("image/png", pick.Target);
    }

    [Theory]
    [InlineData("text/plain;charset=utf-8")]
    [InlineData("UTF8_STRING")]
    public void Utf8TextIsTaken(string type)
    {
        var pick = LinuxClipboard.Pick(["TARGETS", type]);

        Assert.Equal(BlobKind.ClipText, pick.Kind);
        Assert.Equal(type, pick.Target);
    }

    [Theory]
    [InlineData("STRING")]
    [InlineData("text/html")]
    [InlineData("image/jpeg")]
    public void WhatCannotBeSharedIsNothing(string type)
    {
        var pick = LinuxClipboard.Pick([type]);

        Assert.False(pick.Sensitive);
        Assert.Null(pick.Kind);
    }

    [Fact]
    public void TheClipboardTokenFollowsTheSwitch()
    {
        Assert.Equal("capture/clipboard", Annotator.Features(annotate: false, clipboard: true));
        Assert.Equal("annotate/capture/running/clipboard", Annotator.Features(annotate: true, running: true, clipboard: true));
        Assert.Equal("capture", Annotator.Features(annotate: false));
    }

    [Fact]
    public void TypingThroughTheClipboardSendsNothing()
    {
        var (sync, changes) = Sync(shared: true);
        var state = new LinuxClipboard.PollState();
        byte[] current = [1];
        LinuxClipboard.Tick(() => current, sync, () => true, state);

        using (ClipboardQuiet.Begin())
        {
            current = [2];
            LinuxClipboard.Tick(() => current, sync, () => true, state);
            current = [1];
            LinuxClipboard.Tick(() => current, sync, () => true, state);
        }

        Assert.Equal(0, changes());

        // The first tick after the paste only learns; a copy after that is sent.
        LinuxClipboard.Tick(() => current, sync, () => true, state);
        current = [3];
        LinuxClipboard.Tick(() => current, sync, () => true, state);
        Assert.Equal(1, changes());
    }

    [Fact]
    public void APasteStartingDuringTheReadIsNotSent()
    {
        var (sync, changes) = Sync(shared: true);
        var state = new LinuxClipboard.PollState();
        LinuxClipboard.Tick(() => [1], sync, () => true, state);

        IDisposable? quiet = null;
        LinuxClipboard.Tick(
            () =>
            {
                quiet = ClipboardQuiet.Begin();
                return [2];
            },
            sync,
            () => true,
            state);
        quiet?.Dispose();

        Assert.Equal(0, changes());
        Assert.False(state.Primed);
    }

    [Fact]
    public void NothingIsReadUntilThePhoneAsks()
    {
        var (sync, _) = Sync(shared: false);
        var reads = 0;
        var state = new LinuxClipboard.PollState();
        LinuxClipboard.Tick(() => [(byte)++reads], sync, () => true, state);
        LinuxClipboard.Tick(() => [(byte)++reads], sync, () => true, state);

        Assert.Equal(0, reads);
    }

    [Fact]
    public void TheSyncReadsNothingWhileTypingBorrowsTheClipboard()
    {
        var typed = new ClipboardContent(BlobKind.ClipText, [1]);
        IDisposable? quiet = null;
        var startsTyping = new QuietClipboard(new ReadingSurface(() =>
        {
            quiet = ClipboardQuiet.Begin();
            return typed;
        }));
        var plain = new QuietClipboard(new ReadingSurface(() => typed));

        Assert.Same(typed, plain.Read());
        using (ClipboardQuiet.Begin())
        {
            Assert.Null(plain.Read());
        }

        Assert.Null(startsTyping.Read());
        quiet?.Dispose();
        Assert.Same(typed, plain.Read());
    }

    private static (ClipboardSync Sync, Func<int> Changes) Sync(bool shared)
    {
        var token = TestContext.Current.CancellationToken;
        var changes = 0;
        var sync = new ClipboardSync(
            new NoSurface(),
            new BlobSender(_ => { }, LinkKind.Bluetooth, () => 0, token),
            () => true,
            token,
            later: (_, _) => changes++);
        if (shared)
        {
            sync.SetShared(1);
        }

        return (sync, () => changes);
    }

    private sealed class ReadingSurface(Func<ClipboardContent?> read) : IClipboardSurface
    {
        public ClipboardContent? Read() => read();

        public void Write(ClipboardContent content)
        {
        }
    }

    private sealed class NoSurface : IClipboardSurface
    {
        public ClipboardContent? Read() => null;

        public void Write(ClipboardContent content)
        {
        }
    }
}

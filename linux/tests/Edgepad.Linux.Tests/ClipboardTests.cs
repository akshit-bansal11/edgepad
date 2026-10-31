using Edgepad.Platform;
using Edgepad.Protocol;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

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
}

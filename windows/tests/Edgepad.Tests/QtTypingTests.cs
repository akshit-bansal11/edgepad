using Edgepad.Injection;
using Xunit;

namespace Edgepad.Tests;

/// <summary>#65: only a Qt window is typed into by WM_CHAR; everything else, Windows Terminal above all, keeps SendInput.</summary>
public sealed class QtTypingTests
{
    [Theory]
    [InlineData("Qt5QWindowIcon")]
    [InlineData("Qt51514QWindowIcon")]
    [InlineData("Qt51519QWindowIcon")]
    [InlineData("Qt662QWindowToolSaveBits")]
    public void QtWindowClassesAreRecognised(string name) => Assert.True(QtTyping.IsQtClass(name));

    [Theory]
    [InlineData("")]
    [InlineData("Notepad")]
    [InlineData("RichEditD2DPT")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("QtQWindowIcon")]
    [InlineData("Qt5QWin")]
    [InlineData("QWidget")]
    public void OtherWindowClassesAreNot(string name) => Assert.False(QtTyping.IsQtClass(name));
}

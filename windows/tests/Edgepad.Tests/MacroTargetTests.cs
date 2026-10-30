using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// What a macro's target counts as. A wrong yes here would minimize or close a window the macro never opened, and a
/// wrong no only means the press launches. Nothing here touches a real window.
/// </summary>
public sealed class MacroTargetTests
{
    private const string App = @"C:\Program Files\App\app.exe";

    [Fact]
    public void AnExistingExeIsTracked() => Assert.Equal(App, Program(App));

    [Fact]
    public void AMissingExeIsNot() => Assert.Null(MacroTarget.Program(App, _ => false, NoShortcut));

    [Fact]
    public void QuotesAndSpacesAroundThePathAreIgnored() => Assert.Equal(App, Program($" \"{App}\" "));

    [Fact]
    public void ThePathIsNormalized() => Assert.Equal(App, Program(@"C:\Program Files\Other\..\App\app.exe"));

    [Fact]
    public void TheExtensionMatchesWhateverItsCase() => Assert.NotNull(Program(@"C:\Apps\APP.EXE"));

    [Fact]
    public void EnvironmentVariablesAreNotExpanded() => Assert.Null(Program(@"%ProgramFiles%\App\app.exe"));

    [Theory]
    [InlineData("https://example.com/app.exe")]
    [InlineData("app.exe")]
    [InlineData(@"C:app.exe")]
    [InlineData(@"C:\Users\me\Documents")]
    [InlineData(@"C:\Users\me\notes.txt")]
    [InlineData("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    public void AnythingElseIsUntracked(string target) => Assert.Null(Program(target));

    [Fact]
    public void AShortcutIsTheProgramItPointsAt() =>
        Assert.Equal(App, MacroTarget.Program(@"C:\Links\app.LNK", _ => true, _ => App));

    [Fact]
    public void AShortcutThatCannotBeResolvedIsUntracked() =>
        Assert.Null(MacroTarget.Program(@"C:\Links\app.lnk", _ => true, _ => null));

    [Fact]
    public void AShortcutToADocumentIsUntracked() =>
        Assert.Null(MacroTarget.Program(@"C:\Links\notes.lnk", _ => true, _ => @"C:\notes.txt"));

    [Fact]
    public void AShortcutToAMissingProgramIsUntracked() =>
        Assert.Null(MacroTarget.Program(@"C:\Links\app.lnk", path => path.EndsWith(".lnk", StringComparison.Ordinal), _ => App));

    [Fact]
    public void AMissingShortcutIsNeverResolved() =>
        Assert.Null(MacroTarget.Program(@"C:\Links\app.lnk", _ => false, NoShortcut));

    [Fact]
    public void AnImageMatchesWhateverItsCase() => Assert.True(MacroTarget.Same(App.ToUpperInvariant(), App));

    [Fact]
    public void AnImageWithTheSameNameElsewhereDoesNot() =>
        Assert.False(MacroTarget.Same(@"D:\Other\app.exe", App));

    [Fact]
    public void AnUnreadableImageMatchesNothing() => Assert.False(MacroTarget.Same(null, App));

    private static string? Program(string target) => MacroTarget.Program(target, _ => true, NoShortcut);

    private static string? NoShortcut(string path) => throw new InvalidOperationException($"{path} is not a shortcut");
}

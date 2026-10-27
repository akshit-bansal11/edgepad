using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Which extraction path a macro target takes. Nothing here extracts a real icon: the calls behind the
/// branch are Win32 and a runner has no program icons worth reading, so the branch is what gets pinned — a
/// .png routed down the document path would put the image viewer's icon on the button, which reads as a
/// broken extractor rather than a wrong case.
/// </summary>
public sealed class MacroIconsTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-icons-" + Guid.NewGuid().ToString("N"));

    public MacroIconsTests() => Directory.CreateDirectory(dir);

    [Fact]
    public void AProgramCarriesItsOwnIcon() =>
        Assert.Equal(MacroIcons.Source.Resource, MacroIcons.SourceFor(Make("app.exe")));

    [Fact]
    public void AnIconFileIsReadTheSameWayAProgramIs() =>
        Assert.Equal(MacroIcons.Source.Resource, MacroIcons.SourceFor(Make("mark.ico")));

    [Fact]
    public void ThatExtensionIsMatchedWhateverItsCase() =>
        Assert.Equal(MacroIcons.Source.Resource, MacroIcons.SourceFor(Make("APP.EXE")));

    [Fact]
    public void APictureIsTheIconRatherThanHavingOne() =>
        Assert.Equal(MacroIcons.Source.Picture, MacroIcons.SourceFor(Make("logo.png")));

    [Fact]
    public void ADocumentBorrowsItsFileTypesIcon() =>
        Assert.Equal(MacroIcons.Source.Associated, MacroIcons.SourceFor(Make("notes.txt")));

    [Fact]
    public void AnExtensionlessFileIsStillADocument() =>
        Assert.Equal(MacroIcons.Source.Associated, MacroIcons.SourceFor(Make("README")));

    [Fact]
    public void AUrlHasNoIconToRead() =>
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor("https://mail.google.com"));

    [Fact]
    public void AFolderHasNoIconToRead() =>
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(dir));

    [Fact]
    public void APathThatIsNotThereHasNoIconToRead() =>
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(Path.Combine(dir, "gone.exe")));

    [Fact]
    public void NothingAtAllHasNoIconToRead()
    {
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(null));
        Assert.Equal(MacroIcons.Source.None, MacroIcons.SourceFor(string.Empty));
    }

    [Fact]
    public void AMacroWithNothingReadableHasNoPicture()
    {
        // The whole fallback chain in one: an override that is not there falls through to the target, and a
        // target that is a URL falls through to no icon — a button with a label, never an error.
        Assert.Null(MacroIcons.Png(new Macro("Mail", "https://mail.google.com", null)));
        Assert.Null(MacroIcons.Png(new Macro("Mail", "https://mail.google.com", null, Path.Combine(dir, "gone.png"))));
    }

    [Theory]
    [InlineData(@"""C:\Program Files\App\app.exe"",-101", @"C:\Program Files\App\app.exe", -101)]
    [InlineData(@"C:\Windows\System32\imageres.dll,  -102", @"C:\Windows\System32\imageres.dll", -102)]
    [InlineData(@"C:\Icons\doc.ico", @"C:\Icons\doc.ico", 0)]
    [InlineData(@"C:\Tools\a,b.exe,3", @"C:\Tools\a,b.exe", 3)]
    public void AFileTypesIconIsReadAsTheFileAndIndexItNames(string value, string file, int index) =>
        Assert.Equal((file, index), MacroIcons.ParseDefaultIcon(value));

    [Theory]
    [InlineData("%1")]
    [InlineData("\"%1\"")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ATypeWhoseFilesAreTheirOwnIconsOrThatNamesNothingHasNone(string? value) =>
        Assert.Null(MacroIcons.ParseDefaultIcon(value));

    /// <summary>An empty file with the given name. Only its path and its extension are ever looked at.</summary>
    private string Make(string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

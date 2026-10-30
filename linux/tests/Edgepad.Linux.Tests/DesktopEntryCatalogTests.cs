using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class DesktopEntryCatalogTests
{
    private static readonly string[] None = [];

    [Fact]
    public void AnApplicationIsListedByItsName() =>
        Assert.Equal("Files", DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application", "Name=Files"], None));

    [Theory]
    [InlineData("NoDisplay=true")]
    [InlineData("Hidden=true")]
    public void AHiddenEntryIsNotListed(string line) =>
        Assert.Null(DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application", "Name=X", line], None));

    [Fact]
    public void ALinkOrANamelessEntryIsNotListed()
    {
        Assert.Null(DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Link", "Name=X"], None));
        Assert.Null(DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application"], None));
    }

    [Fact]
    public void AnActionGroupIsIgnored() =>
        Assert.Equal("Browser", DesktopEntryCatalog.Parse(
            ["[Desktop Entry]", "Type=Application", "Name=Browser", "[Desktop Action new]", "Name=New window", "NoDisplay=true"], None));

    [Fact]
    public void ALocalisedNameWinsWhenAskedFor() =>
        Assert.Equal("Dateien", DesktopEntryCatalog.Parse(
            ["[Desktop Entry]", "Type=Application", "Name=Files", "Name[de]=Dateien"], ["de_AT", "de"]));

    [Fact]
    public void ControlCharactersInANameCannotCorruptTheList()
    {
        var name = DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application", @"Name=A\tB\nC"], None);
        Assert.NotNull(name);
        Assert.DoesNotContain('\t', name);
        Assert.DoesNotContain('\n', name);
    }

    [Fact]
    public void LanguagesComeFromLang()
    {
        Assert.Equal(["de_AT", "de"], DesktopEntryCatalog.Languages("de_AT.UTF-8"));
        Assert.Empty(DesktopEntryCatalog.Languages("C"));
    }

    [Fact]
    public void TheOwnersFolderWinsAndASpaceInThePathIsSkipped()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var mine = Directory.CreateDirectory(Path.Combine(root, "mine")).FullName;
            var system = Directory.CreateDirectory(Path.Combine(root, "system")).FullName;
            var spaced = Directory.CreateDirectory(Path.Combine(root, "with space")).FullName;
            File.WriteAllText(Path.Combine(mine, "a.desktop"), "[Desktop Entry]\nType=Application\nName=Mine\n");
            File.WriteAllText(Path.Combine(system, "a.desktop"), "[Desktop Entry]\nType=Application\nName=System\n");
            File.WriteAllText(Path.Combine(spaced, "b.desktop"), "[Desktop Entry]\nType=Application\nName=Spaced\n");

            var apps = new DesktopEntryCatalog([mine, system, spaced], []).Scan();

            var app = Assert.Single(apps);
            Assert.Equal("Mine", app.Name);
            Assert.Equal("gio", app.Target);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

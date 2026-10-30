using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class DesktopEntryCatalogTests
{
    private static readonly string[] None = [];

    [Fact]
    public void An_application_is_listed_by_its_name() =>
        Assert.Equal("Files", DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application", "Name=Files"], None));

    [Theory]
    [InlineData("NoDisplay=true")]
    [InlineData("Hidden=true")]
    public void A_hidden_entry_is_not_listed(string line) =>
        Assert.Null(DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application", "Name=X", line], None));

    [Fact]
    public void A_link_or_a_nameless_entry_is_not_listed()
    {
        Assert.Null(DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Link", "Name=X"], None));
        Assert.Null(DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application"], None));
    }

    [Fact]
    public void An_action_group_is_ignored() =>
        Assert.Equal("Browser", DesktopEntryCatalog.Parse(
            ["[Desktop Entry]", "Type=Application", "Name=Browser", "[Desktop Action new]", "Name=New window", "NoDisplay=true"], None));

    [Fact]
    public void A_localised_name_wins_when_asked_for() =>
        Assert.Equal("Dateien", DesktopEntryCatalog.Parse(
            ["[Desktop Entry]", "Type=Application", "Name=Files", "Name[de]=Dateien"], ["de_AT", "de"]));

    [Fact]
    public void Control_characters_in_a_name_cannot_corrupt_the_list()
    {
        var name = DesktopEntryCatalog.Parse(["[Desktop Entry]", "Type=Application", @"Name=A\tB\nC"], None);
        Assert.NotNull(name);
        Assert.DoesNotContain('\t', name);
        Assert.DoesNotContain('\n', name);
    }

    [Fact]
    public void Languages_come_from_LANG()
    {
        Assert.Equal(["de_AT", "de"], DesktopEntryCatalog.Languages("de_AT.UTF-8"));
        Assert.Empty(DesktopEntryCatalog.Languages("C"));
    }

    [Fact]
    public void The_owners_folder_wins_and_a_space_in_the_path_is_skipped()
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

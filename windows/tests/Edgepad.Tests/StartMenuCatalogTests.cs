using Edgepad.Macros;
using Xunit;

namespace Edgepad.Tests;

/// <summary>What the Start menu walk offers the phone: nothing but apps, once each, with names that fit a tab-separated line.</summary>
public sealed class StartMenuCatalogTests : IDisposable
{
    private readonly string user = Path.Combine(Path.GetTempPath(), "edgepad-sm-" + Guid.NewGuid().ToString("N"), "user");
    private readonly string shared;

    public StartMenuCatalogTests()
    {
        shared = Path.Combine(Path.GetDirectoryName(user)!, "shared");
        Directory.CreateDirectory(user);
        Directory.CreateDirectory(shared);
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(user)!, true);

    [Fact]
    public void ShortcutsAreFoundInSubfolders()
    {
        Touch(user, "Tool.lnk");
        Touch(user, @"Suite\Editor.lnk");

        Assert.Equal(["Editor", "Tool"], Names());
    }

    [Theory]
    [InlineData("Uninstall Tool")]
    [InlineData("Tool Readme")]
    [InlineData("Help")]
    [InlineData("Tool Documentation")]
    [InlineData("Release Notes")]
    [InlineData("Tool Website")]
    public void NonAppsAreSkipped(string name)
    {
        Touch(user, name + ".lnk");
        Touch(user, "Keep.lnk");

        Assert.Equal(["Keep"], Names());
    }

    [Fact]
    public void AWordThatOnlyContainsAnExcludedOneStays()
    {
        Touch(user, "HelpNDoc.lnk");

        Assert.Equal(["HelpNDoc"], Names());
    }

    [Fact]
    public void ANameInBothTreesComesFromTheOwners()
    {
        Touch(user, "Tool.lnk");
        Touch(shared, "tool.lnk");

        var app = Assert.Single(Catalog().Scan());
        Assert.StartsWith(user, app.Target, StringComparison.Ordinal);
    }

    [Fact]
    public void DepthIsCapped()
    {
        Touch(user, @"a\b\c\d\Deep.lnk");
        Touch(user, @"a\b\c\d\e\TooDeep.lnk");

        Assert.Equal(["Deep"], Names());
    }

    [Fact]
    public void ControlCharactersInANameAreReplaced()
    {
        // A file system will not hold them on Windows, so the name is read from a path as a string.
        Assert.Equal("a b c", StartMenuCatalog.NameOf("a\tb\nc.lnk"));
    }

    [Fact]
    public void AMissingRootCostsNothing() =>
        Assert.Empty(new StartMenuCatalog([Path.Combine(user, "absent")], _ => null).Scan());

    [Fact]
    public void TheIconIsTheTargetsNotTheShortcuts()
    {
        Touch(user, "Tool.lnk");
        var app = Assert.Single(new StartMenuCatalog([user], _ => @"C:\Apps\tool.exe").Scan());

        Assert.Equal(@"C:\Apps\tool.exe", app.IconPath);
    }

    [Fact]
    public void AShortcutWithoutATargetHasNoIcon()
    {
        Touch(user, "Tool.lnk");
        var catalog = new StartMenuCatalog([user], _ => null);

        Assert.Null(catalog.Icon(Assert.Single(catalog.Scan())));
    }

    private StartMenuCatalog Catalog() => new([user, shared], _ => null);

    private string[] Names() => [.. Catalog().Scan().Select(app => app.Name).Order(StringComparer.Ordinal)];

    private static void Touch(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }
}

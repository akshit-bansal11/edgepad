using Edgepad.Macros;
using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Finding a macro's icon file from <c>.desktop</c> entries and the hicolor theme, against a real temp folder laid
/// out like <c>~/.local/share</c> and <c>/usr/share</c>. Nothing here needs a desktop, and nothing is decoded: the
/// answer is a path.
/// </summary>
public sealed class DesktopIconsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "edgepad-desktopicons-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> executables = [];

    private string Home => Path.Combine(root, "home");

    private string UserShare => Path.Combine(Home, ".local", "share");

    private string SystemShare => Path.Combine(root, "usr", "share");

    private IReadOnlyList<string> Dirs => [UserShare, SystemShare];

    public DesktopIconsTests() => Directory.CreateDirectory(root);

    [Fact]
    public void ReadEntryTakesTheDesktopEntryGroupsIconAndExec()
    {
        var (icon, exec) = DesktopIcons.ReadEntry(["[Desktop Entry]", "Name=Fox", "Icon=firefox", "Exec=firefox %u"]);

        Assert.Equal("firefox", icon);
        Assert.Equal("firefox %u", exec);
    }

    [Fact]
    public void ReadEntryIgnoresALaterGroupsIcon()
    {
        var (icon, _) = DesktopIcons.ReadEntry(["[Desktop Entry]", "Name=Fox", "[Desktop Action new]", "Icon=other"]);

        Assert.Null(icon);
    }

    [Fact]
    public void ReadEntryIgnoresATranslatedIconKey()
    {
        var (icon, _) = DesktopIcons.ReadEntry(["[Desktop Entry]", "Icon[de]=falsch", "Icon=richtig"]);

        Assert.Equal("richtig", icon);
    }

    [Fact]
    public void ReadEntryIgnoresCommentsAndBlankLines()
    {
        var (icon, exec) = DesktopIcons.ReadEntry(["# Icon=no", "", "[Desktop Entry]", "# Exec=no", "", "Icon=yes"]);

        Assert.Equal("yes", icon);
        Assert.Null(exec);
    }

    [Fact]
    public void ReadEntryToleratesSpacesAroundTheEquals()
    {
        var (icon, exec) = DesktopIcons.ReadEntry(["[Desktop Entry]", "  Icon = spaced  ", "Exec\t=\tprog"]);

        Assert.Equal("spaced", icon);
        Assert.Equal("prog", exec);
    }

    [Theory]
    [InlineData("/usr/bin/firefox %u", "firefox")]
    [InlineData("\"/opt/My App/run\" --x", "run")]
    [InlineData("env FOO=1 prog", "env")] // The first word is the whole rule, so a wrapper hides the program behind it.
    public void ExecProgramIsTheFirstWordsFileName(string exec, string expected) =>
        Assert.Equal(expected, DesktopIcons.ExecProgram(exec));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DataDirsFallBackToTheSpecDefaults(string? dataDirs) =>
        Assert.Equal(
            ["/home/me/.local/share", "/usr/local/share", "/usr/share"],
            DesktopIcons.DataDirs(null, dataDirs, "/home/me").Select(dir => dir.Replace('\\', '/')));

    [Fact]
    public void DataDirsDropsRelativeEntriesAndARelativeDataHome()
    {
        var dirs = DesktopIcons.DataDirs("relative/share", "rel:/abs/one::/abs/two", "/home/me");

        Assert.Equal(
            ["/home/me/.local/share", "/abs/one", "/abs/two"],
            dirs.Select(dir => dir.Replace('\\', '/')));
    }

    [Theory]
    [InlineData("https://x")]
    [InlineData("mailto:a@b")]
    public void AUrlHasNoIconEvenWhenAFileOfThatNameExists(string url)
    {
        Write(Path.Combine("usr", "share", "pixmaps", "x.png"));

        Assert.Null(Find(new Macro("n", url, null)));
    }

    [Fact]
    public void TheOverrideWinsOverTheTarget()
    {
        var mine = Write("mine.png");
        var theirs = Write("theirs.png");

        Assert.Equal(mine, Find(new Macro("n", theirs, null, mine)));
    }

    [Fact]
    public void AnOverrideThatDoesNotResolveFallsThroughToTheTarget()
    {
        var theirs = Write("theirs.png");

        Assert.Equal(theirs, Find(new Macro("n", theirs, null, Path.Combine(root, "deleted.png"))));
    }

    [Fact]
    public void APictureTargetIsItsOwnIconWhateverItsCase()
    {
        var picture = Write("Logo.PNG");

        Assert.Equal(picture, Find(new Macro("n", picture, null)));
    }

    [Fact]
    public void ADesktopFileTargetGivesItsIconByAbsolutePath()
    {
        var icon = Write("art.svg");
        var entry = Write("app.desktop", $"[Desktop Entry]\nIcon={icon}\n");

        Assert.Equal(icon, Find(new Macro("n", entry, null)));
    }

    [Fact]
    public void ADesktopFileTargetGivesItsIconByName()
    {
        var icon = Write(Path.Combine("usr", "share", "icons", "hicolor", "48x48", "apps", "art.png"));
        var entry = Write("app.desktop", "[Desktop Entry]\nIcon=art\n");

        Assert.Equal(icon, Find(new Macro("n", entry, null)));
    }

    [Theory]
    [InlineData("art.png")]
    [InlineData("art.SVG")]
    [InlineData("art.xpm")]
    public void AnOldIconNameWithItsExtensionIsFoundWithoutIt(string name)
    {
        var icon = Write(Path.Combine("usr", "share", "icons", "hicolor", "48x48", "apps", "art.png"));
        var entry = Write("app.desktop", $"[Desktop Entry]\nIcon={name}\n");

        Assert.Equal(icon, Find(new Macro("n", entry, null)));
    }

    [Fact]
    public void AnExecutablesDesktopEntryIsFoundByItsFileNameInDataHomeBeforeSystem()
    {
        var program = Executable(Path.Combine("bin", "tool"));
        Write(Path.Combine("usr", "share", "pixmaps", "sys.png"));
        var user = Write(Path.Combine("usr", "share", "pixmaps", "usr.png"));
        Write(Path.Combine("usr", "share", "applications", "tool.desktop"), "[Desktop Entry]\nIcon=sys\n");
        Write(Path.Combine("home", ".local", "share", "applications", "tool.desktop"), "[Desktop Entry]\nIcon=usr\n");

        Assert.Equal(user, Find(new Macro("n", program, null)));
    }

    [Fact]
    public void AnExecutablesDesktopEntryNamedDifferentlyIsFoundByItsExec()
    {
        var program = Executable(Path.Combine("bin", "tool"));
        var icon = Write(Path.Combine("usr", "share", "pixmaps", "pretty.png"));
        Write(Path.Combine("usr", "share", "applications", "unrelated.desktop"), "[Desktop Entry]\nIcon=nope\nExec=/usr/bin/other\n");
        Write(Path.Combine("usr", "share", "applications", "org.vendor.Tool.desktop"), "[Desktop Entry]\nIcon=pretty\nExec=/opt/tool/tool %U\n");

        Assert.Equal(icon, Find(new Macro("n", program, null)));
    }

    [Fact]
    public void ABareNameWithAHicolorIconButNoDesktopEntryUsesThatIcon()
    {
        var icon = Write(Path.Combine("usr", "share", "icons", "hicolor", "scalable", "apps", "gimp.svg"));

        Assert.Equal(icon, Find(new Macro("n", "gimp", null)));
    }

    [Fact]
    public void AnAbsoluteIconThatIsMissingIsNothingRatherThanANameSearch()
    {
        Write(Path.Combine("usr", "share", "pixmaps", "gone.png"));
        Write(Path.Combine("usr", "share", "applications", "app.desktop"), $"[Desktop Entry]\nIcon={Path.Combine(root, "gone.png")}\n");

        // The entry is the one that matches by name, yet its missing absolute icon is not searched for by file name;
        // the lookup falls to the program's own name, which has no icon either.
        Assert.Null(Find(new Macro("n", "app", null)));
    }

    [Fact]
    public void AnIconNameWithASlashIsNeverAPath()
    {
        Write(Path.Combine("usr", "share", "pixmaps", "x.png"));
        var entry = Write("app.desktop", "[Desktop Entry]\nIcon=../pixmaps/x\n");

        Assert.Null(Find(new Macro("n", entry, null)));
    }

    [Fact]
    public void ALargerHicolorSizeBeatsASmallerOne()
    {
        var big = Write(Path.Combine("usr", "share", "icons", "hicolor", "256x256", "apps", "app.png"));
        Write(Path.Combine("usr", "share", "icons", "hicolor", "128x128", "apps", "app.png"));
        Write(Path.Combine("usr", "share", "icons", "hicolor", "48x48", "apps", "app.png"));

        Assert.Equal(big, Find(new Macro("n", "app", null)));
    }

    [Fact]
    public void APngBeatsAnSvgAtTheSameSize()
    {
        var png = Write(Path.Combine("usr", "share", "icons", "hicolor", "128x128", "apps", "app.png"));
        Write(Path.Combine("usr", "share", "icons", "hicolor", "128x128", "apps", "app.svg"));

        Assert.Equal(png, Find(new Macro("n", "app", null)));
    }

    [Fact]
    public void HicolorBeatsPixmaps()
    {
        var themed = Write(Path.Combine("usr", "share", "icons", "hicolor", "32x32", "apps", "app.png"));
        Write(Path.Combine("usr", "share", "pixmaps", "app.png"));

        Assert.Equal(themed, Find(new Macro("n", "app", null)));
    }

    [Fact]
    public void PixmapsAreUsedWhenHicolorHasNothing()
    {
        var loose = Write(Path.Combine("usr", "share", "pixmaps", "app.png"));

        Assert.Equal(loose, Find(new Macro("n", "app", null)));
    }

    [Fact]
    public void AFolderHasNoIcon()
    {
        var folder = Path.Combine(root, "docs");
        Directory.CreateDirectory(folder);

        Assert.Null(Find(new Macro("n", folder, null)));
    }

    [Fact]
    public void ADocumentThatIsNotExecutableHasNoIcon()
    {
        var document = Write("notes.txt");

        Assert.Null(Find(new Macro("n", document, null)));
    }

    [Fact]
    public void ALeadingTildeMeansTheHomeFolder()
    {
        var picture = Write(Path.Combine("home", "x.png"));

        Assert.Equal(picture, Find(new Macro("n", "~/x.png", null)));
    }

    private string? Find(Macro macro) => DesktopIcons.Find(macro, Dirs, Home, Classify);

    private PathKind Classify(string path) =>
        Directory.Exists(path) ? PathKind.Folder
        : !File.Exists(path) ? PathKind.None
        : executables.Contains(path) ? PathKind.Executable
        : PathKind.File;

    /// <summary>A file under the temp root, with the given text, and its full path back.</summary>
    private string Write(string relative, string text = "")
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string Executable(string relative)
    {
        var path = Write(relative);
        executables.Add(path);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

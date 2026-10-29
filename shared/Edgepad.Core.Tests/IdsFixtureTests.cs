using System.Globalization;
using System.Text.RegularExpressions;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

/// <summary>Holds <see cref="ActionId"/> and <see cref="ControlId"/> to protocol/actions.txt, which Android reads too.</summary>
public sealed partial class IdsFixtureTests
{
    [Fact]
    public void ActionIdsMatchTheSharedTable() =>
        Assert.Equal(Expected("ACTION"), Actual<ActionId>());

    [Fact]
    public void ControlIdsMatchTheSharedTable() =>
        Assert.Equal(Expected("CONTROL"), Actual<ControlId>());

    [Fact]
    public void TextKindsMatchTheSharedTable() =>
        Assert.Equal(Expected("TEXT"), Actual<TextKind>());

    [Fact]
    public void PadButtonMasksMatchTheSharedTable() =>
        Assert.Equal(Expected("PAD_BUTTON"), Actual<PadButton>());

    [Fact]
    public void PadStatusTokensMatchTheSharedTable()
    {
        // The token is what crosses the wire, so the file holds it and this holds the constants to it. A
        // row's third column is the name both apps use, its second the bytes, which is the part that
        // matters. The names are written out here because a const, unlike an enum, has none to derive.
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parts in Rows("PAD_STATUS"))
        {
            expected.Add(parts[2], parts[1]);
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["READY"] = PadStatus.Ready,
            ["NO_DRIVER"] = PadStatus.NoDriver,
            ["ATTACH_FAILED"] = PadStatus.AttachFailed,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FeatureTokensMatchTheSharedTable()
    {
        // Written out like the PAD_STATUS names, for the same reason: a const has no name to derive.
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parts in Rows("FEATURE"))
        {
            expected.Add(parts[2], parts[1]);
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["ANNOTATE"] = Feature.Annotate,
            ["CAPTURE"] = Feature.Capture,
            ["CLIPBOARD"] = Feature.Clipboard,
            ["APPS"] = Feature.Apps,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SignStatusTokensMatchTheSharedTable()
    {
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parts in Rows("SIGN_STATUS"))
        {
            expected.Add(parts[2], parts[1]);
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["SAVED"] = SignStatus.Saved,
            ["EMPTY"] = SignStatus.Empty,
            ["FAILED"] = SignStatus.Failed,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CaptureStatusTokensMatchTheSharedTable()
    {
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parts in Rows("CAPTURE_STATUS"))
        {
            expected.Add(parts[2], parts[1]);
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["TOO_LARGE"] = CaptureStatus.TooLarge,
            ["NOT_FOUND"] = CaptureStatus.NotFound,
            ["FAILED"] = CaptureStatus.Failed,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DeviceOsTokensMatchTheSharedTable()
    {
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parts in Rows("DEVICE_OS"))
        {
            expected.Add(parts[2], parts[1]);
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["WINDOWS"] = DeviceOs.Windows,
            ["LINUX"] = DeviceOs.Linux,
            ["MACOS"] = DeviceOs.Macos,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AppAddStatusTokensMatchTheSharedTable()
    {
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parts in Rows("APP_ADD_STATUS"))
        {
            expected.Add(parts[2], parts[1]);
        }

        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["ADDED"] = AppAddStatus.Added,
            ["FULL"] = AppAddStatus.Full,
            ["DUPLICATE"] = AppAddStatus.Duplicate,
            ["STALE"] = AppAddStatus.Stale,
            ["FAILED"] = AppAddStatus.Failed,
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BlobKindsMatchTheSharedTable() =>
        Assert.Equal(Expected("BLOB_KIND"), Actual<BlobKind>());

    [Fact]
    public void FileTypesMatchTheSharedTable()
    {
        Assert.Equal(Expected("FILE_TYPE"), Actual<FileType>());
        foreach (var parts in Rows("FILE_TYPE"))
        {
            var type = (FileType)int.Parse(parts[1], CultureInfo.InvariantCulture);
            Assert.Equal(parts[3], FileTypes.Mime(type));
            Assert.Equal(parts[4], FileTypes.Extension(type));
            Assert.Equal(type, FileTypes.FromPath("Screenshot 1." + parts[4].ToUpperInvariant()));
        }

        Assert.Null(FileTypes.FromPath("notes.txt"));
    }

    [Fact]
    public void FileLimitsMatchTheSharedTable()
    {
        var limits = Rows("FILE_LIMIT").ToDictionary(parts => parts[2], parts => long.Parse(parts[1], CultureInfo.InvariantCulture));
        Assert.Equal(FileTypes.ImageLimit, limits["IMAGE"]);
        Assert.Equal(FileTypes.VideoLimit, limits["VIDEO"]);
    }

    [Fact]
    public void FileStatusesMatchTheSharedTable() =>
        Assert.Equal(Expected("FILE_STATUS"), Actual<FileStatus>());

    [Fact]
    public void ThePaletteMatchesTheSharedTable()
    {
        // Index and RRGGBB; the names are the phone's and the fixture's business, an index is all that is sent.
        var rows = Rows("PALETTE");
        Assert.Equal(Palette.Count, rows.Count);
        foreach (var parts in rows)
        {
            var index = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var rgb = int.Parse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.Equal(rgb, Palette.Colour(index));
        }

        Assert.Null(Palette.Colour(Palette.Count));
        Assert.Null(Palette.Colour(-1));
    }

    [Fact]
    public void TheHandshakeMatchesTheSharedTable()
    {
        var handshake = Rows("HANDSHAKE").ToDictionary(parts => parts[2], parts => parts[1]);
        Assert.Equal(ProtocolConstants.Version.ToString(CultureInfo.InvariantCulture), handshake["VERSION"]);
        Assert.Equal(ProtocolConstants.ServiceId.ToString(), handshake["SERVICE_ID"]);
    }

    private static SortedDictionary<int, string> Expected(string kind)
    {
        var table = new SortedDictionary<int, string>();
        foreach (var parts in Rows(kind))
        {
            table.Add(int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]);
        }

        return table;
    }

    private static List<string[]> Rows(string kind)
    {
        var rows = Fixtures.Lines("actions.txt")
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts[0] == kind)
            .ToList();
        Assert.NotEmpty(rows);
        return rows;
    }

    // MuteToggle -> MUTE_TOGGLE, so the C# names are compared in the file's own spelling.
    private static SortedDictionary<int, string> Actual<T>()
        where T : struct, Enum => new(Enum.GetValues<T>().ToDictionary(
            value => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            value => WordBoundary().Replace(value.ToString(), "_$1").ToUpperInvariant()));

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex WordBoundary();
}

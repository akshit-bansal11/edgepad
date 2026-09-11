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

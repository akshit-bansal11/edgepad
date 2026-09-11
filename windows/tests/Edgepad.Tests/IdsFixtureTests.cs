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

    private static SortedDictionary<int, string> Expected(string kind)
    {
        var table = new SortedDictionary<int, string>();
        foreach (var line in Fixtures.Lines("actions.txt"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == kind)
            {
                table.Add(int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]);
            }
        }

        Assert.NotEmpty(table);
        return table;
    }

    // MuteToggle -> MUTE_TOGGLE, so the C# names are compared in the file's own spelling.
    private static SortedDictionary<int, string> Actual<T>()
        where T : struct, Enum => new(Enum.GetValues<T>().ToDictionary(
            value => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            value => WordBoundary().Replace(value.ToString(), "_$1").ToUpperInvariant()));

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex WordBoundary();
}

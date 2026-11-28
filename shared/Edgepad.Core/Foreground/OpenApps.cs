using System.Text;

namespace Edgepad.Foreground;

/// <summary>
/// The body of <see cref="Edgepad.Protocol.TextKind.OpenApps"/>: one "token TAB name" line per app, the apps open now first and
/// then the apps already added as macros, deduplicated by token and capped. A token is exactly what
/// <see cref="Edgepad.Protocol.TextKind.ForegroundApp"/> reports, so the phone can key a profile by it. Pure, so a test needs no desktop.
/// </summary>
internal static class OpenApps
{
    public const int MaxLines = 40;

    /// <summary>Apps with no token (<see cref="ForegroundApp.None"/>) are skipped; a name has no tab or line break, as <see cref="ForegroundApp.Clean"/> sees to.</summary>
    public static string Build(IEnumerable<ForegroundApp> open, IEnumerable<ForegroundApp> macros)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        StringBuilder body = new();
        foreach (var app in open.Concat(macros))
        {
            if (app.Token.Length == 0 || seen.Count == MaxLines || !seen.Add(app.Token))
            {
                continue;
            }

            _ = body.Append(body.Length == 0 ? "" : "\n").Append(app.Token).Append('\t').Append(app.Name);
        }

        return body.ToString();
    }
}

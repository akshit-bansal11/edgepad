using System.Text;

namespace Edgepad.Foreground;

/// <summary>
/// The app in front, as <see cref="Edgepad.Protocol.TextKind.ForegroundApp"/> carries it. Personal data, so nothing here
/// logs it: the log says only that a watch started or stopped.
/// </summary>
internal readonly record struct ForegroundApp(string Token, string Name)
{
    public const int MaxToken = 64;

    public const int MaxNameCodePoints = 40;

    /// <summary>Nothing is known: an empty token, which <see cref="Encode"/> writes as empty text.</summary>
    public static ForegroundApp None { get; } = new("", "");

    /// <summary>"token/name", split by the phone on the first "/"; empty for <see cref="None"/>.</summary>
    public static string Encode(ForegroundApp app) => app.Token.Length == 0 ? "" : $"{app.Token}/{app.Name}";

    /// <summary>
    /// A token of 1 to 64 of A-Z a-z 0-9 . _ - or <see cref="None"/>. A name has control characters and "|" turned to a space, is
    /// trimmed and is cut to 40 code points, never inside a surrogate pair.
    /// </summary>
    public static ForegroundApp Clean(string token, string? name)
    {
        if (token is not { Length: > 0 and <= MaxToken } || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            return None;
        }

        StringBuilder cleaned = new();
        var count = 0;
        foreach (var rune in (name ?? "").Trim().EnumerateRunes())
        {
            if (count == MaxNameCodePoints)
            {
                break;
            }

            cleaned.Append(Rune.IsControl(rune) || rune.Value == '|' ? new Rune(' ') : rune);
            count++;
        }

        return new ForegroundApp(token, cleaned.ToString().Trim());
    }
}

/// <summary>Says which app is in front. A platform implements it; nothing runs until <see cref="Watch"/>.</summary>
internal interface IForegroundApp
{
    bool Available { get; }

    /// <summary>
    /// Calls <paramref name="onChange"/> with the app in front now and then on every change, on any thread, with
    /// <see cref="ForegroundApp.None"/> when nothing is known. Disposing stops it and releases whatever it held.
    /// </summary>
    IDisposable Watch(Action<ForegroundApp> onChange);
}

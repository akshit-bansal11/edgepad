using Edgepad.Protocol;

namespace Edgepad.Ui;

/// <summary>What the PIN boxes accept and when they are done: the Windows dialog's rules, without its controls.</summary>
internal static class PinEntry
{
    /// <summary>The ASCII digits of <paramref name="text"/>, no more than a PIN's length. A paste goes through this too.</summary>
    public static string Digits(string? text) =>
        new([.. (text ?? "").Where(char.IsAsciiDigit).Take(Pin.Length)]);

    /// <summary>Why the two boxes cannot be used as a new PIN, or null if they can. Never repeats the digits.</summary>
    public static string? Problem(string? first, string? again)
    {
        if (first is null || first.Length != Pin.Length || !first.All(char.IsAsciiDigit))
        {
            return "A PIN is exactly six digits.";
        }

        return first == again ? null : "The two PINs are not the same.";
    }
}

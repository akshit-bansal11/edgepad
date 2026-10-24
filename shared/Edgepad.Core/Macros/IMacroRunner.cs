namespace Edgepad.Macros;

/// <summary>
/// Launches the macro in a slot the phone named. The phone sends the index and nothing else: what the slot opens
/// is this laptop's, which is why a slot past the list is dropped rather than guessed at.
/// </summary>
internal interface IMacroRunner
{
    /// <summary>
    /// A 5x3 grid on the phone, which is a page of buttons rather than a start menu. Fifteen is also exactly
    /// what one TEXT frame can name: 15 x <c>MacroStore.MaxNameBytes</c> plus 14 separators is 254 of the 255 a
    /// payload holds, so a full list always reaches the phone labelled. The number is not a coincidence and
    /// must not be raised without cutting the name budget to match.
    /// </summary>
    const int MaxMacros = 15;

    /// <summary>Launches the macro in slot <paramref name="index"/>. False when it is empty or the launch failed.</summary>
    bool Run(int index);
}

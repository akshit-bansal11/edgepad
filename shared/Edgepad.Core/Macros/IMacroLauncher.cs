namespace Edgepad.Macros;

/// <summary>
/// Opens a macro's target the way this operating system opens things. The store owns the list and which slot
/// runs; this is the one OS-specific step, so a laptop app supplies it and the store stays portable. It takes
/// the target and arguments from a slot the owner filled in on this laptop, never a string from the phone.
/// </summary>
internal interface IMacroLauncher
{
    /// <summary>Starts <paramref name="target"/>. Throws when it cannot; the store logs that and reports the failure.</summary>
    void Launch(string target, string? arguments);
}

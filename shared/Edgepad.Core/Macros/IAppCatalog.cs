namespace Edgepad.Macros;

/// <summary>
/// The apps this laptop would offer the phone as macros. Each OS supplies its own. Everything an entry says comes from
/// this machine, never from the phone, which is what lets the phone add a macro at all: see <see cref="AppPicker"/>.
/// </summary>
internal interface IAppCatalog
{
    /// <summary>What is installed now. May be slow and may throw: <see cref="AppPicker"/> never calls it on the read thread.</summary>
    IReadOnlyList<AppEntry> Scan();

    /// <summary>A PNG of this app, or null for none. Asked per entry, after the list has gone, because it is the slow part.</summary>
    byte[]? Icon(AppEntry app);
}

/// <summary>One app on offer: what its macro would be. <see cref="IconPath"/> becomes <see cref="Macro.Icon"/>; null takes it from the target.</summary>
internal sealed record AppEntry(string Name, string Target, string? Arguments, string? IconPath = null);

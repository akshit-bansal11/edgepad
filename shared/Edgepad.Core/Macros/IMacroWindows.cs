namespace Edgepad.Macros;

/// <summary>What a macro's app is doing on this laptop right now. The order is the wire's: '-', '0', '1', '2'.</summary>
internal enum OpenState
{
    /// <summary>Nothing this laptop can follow: a URL, a folder, a document, or an app it cannot see.</summary>
    Untracked,
    Closed,
    Open,
    Front,
}

/// <summary>
/// The windows of the apps macros open. The one OS-specific part of the toggle and the close cross, so a laptop app
/// supplies it and the store stays portable. Every call takes a <see cref="Macro"/> out of this laptop's own list:
/// the phone names a slot, and no string of its ever reaches a window match, a raise or a close.
/// </summary>
internal interface IMacroWindows
{
    /// <summary>False where this desktop cannot be surveyed at all: no crosses on the phone, and a press just launches.</summary>
    bool Available { get; }

    /// <summary>One state per macro, in order, from one pass over the desktop.</summary>
    OpenState[] Survey(IReadOnlyList<Macro> macros);

    /// <summary>Minimizes the macro's app. False when it did not act.</summary>
    bool Minimize(Macro macro);

    /// <summary>Brings the macro's app to the front, restoring it if need be. False when it did not act.</summary>
    bool Raise(Macro macro);

    /// <summary>Asks the macro's app to close, as its own close button would, so it may ask to save. Never a kill.</summary>
    bool Close(Macro macro);
}

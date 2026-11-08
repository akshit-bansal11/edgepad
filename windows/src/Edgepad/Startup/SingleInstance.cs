namespace Edgepad.Startup;

/// <summary>What a starting copy does about one that is already running.</summary>
internal enum LaunchChoice
{
    /// <summary>Nothing else holds the lock: run.</summary>
    Run,

    /// <summary>The same version is running: say so and exit, so a second double-click costs the owner nothing.</summary>
    Notify,

    /// <summary>A different version is running: ask it to quit and take over, which is how running an update works.</summary>
    TakeOver,
}

internal static class SingleInstance
{
    /// <summary>
    /// The lock is not enough to decide on: a second launch of the version already running used to quit it, which
    /// hung up a connected phone and showed nothing. Only a different version takes over.
    /// </summary>
    public static LaunchChoice Decide(bool lockFree, bool sameVersionRunning) =>
        lockFree ? LaunchChoice.Run : sameVersionRunning ? LaunchChoice.Notify : LaunchChoice.TakeOver;

    /// <summary>The name a running copy of <paramref name="version"/> holds for its whole life, so a later copy can see which one it is.</summary>
    public static string VersionMutexName(string version) => $@"Local\Edgepad.Version.{version}";
}

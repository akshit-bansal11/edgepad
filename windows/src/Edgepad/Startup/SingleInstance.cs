using System.Security.Principal;

namespace Edgepad.Startup;

/// <summary>What a starting copy does about one that is already running.</summary>
internal enum LaunchChoice
{
    /// <summary>Nothing else holds the lock: run.</summary>
    Run,

    /// <summary>The same version is running: say so and exit, so a second double-click costs the owner nothing.</summary>
    Notify,

    /// <summary>A different version, or this one restarting itself as administrator, is running: ask it to quit and take over.</summary>
    TakeOver,
}

/// <summary>The command line, read strictly: only the options below count, and anything else is ignored.</summary>
/// <param name="RestartSid">The user SID after <c>--restart</c>, or null when the option is absent or has no value.</param>
/// <param name="ProbeNotifications">True with <c>--probe-notifications</c>.</param>
/// <param name="Send">The arguments after <c>--send</c>, or null when there are none (so a launch without it still compares equal).</param>
internal readonly record struct LaunchArgs(string? RestartSid, bool ProbeNotifications, IReadOnlyList<string>? Send = null)
{
    /// <summary>Every argument after <c>--send</c> up to the next option (Explorer's Send to appends all selected files), empty when none.</summary>
    public IReadOnlyList<string> SendPaths => Send ?? [];
}

/// <summary>
/// One Edgepad per user: the lock a running copy holds, and how a starting copy reads its command line and decides
/// what to do about it.
/// </summary>
internal static class SingleInstance
{
    /// <summary>The Win32 error for a UAC prompt the owner declined.</summary>
    public const int UacDeclined = 1223;

    /// <summary>The option the Send to shortcut passes; Explorer appends the chosen file after it.</summary>
    public const string SendOption = "--send";

    /// <summary>
    /// The lock is not enough to decide on: a second launch of the version already running used to quit it, which
    /// hung up a connected phone and showed nothing. Only a different version takes over, and so does the same
    /// version when it started itself again as administrator (<paramref name="restart"/>).
    /// </summary>
    public static LaunchChoice Decide(bool lockFree, bool sameVersionRunning, bool restart = false) =>
        lockFree ? LaunchChoice.Run : sameVersionRunning && !restart ? LaunchChoice.Notify : LaunchChoice.TakeOver;

    /// <summary>
    /// A restart request is honoured only for the user who is running this copy: an elevation with another admin
    /// account would change what HKCU means. No request is always fine.
    /// </summary>
    public static bool RestartAllowed(string? requestedSid, string? currentSid) =>
        requestedSid is null || (currentSid is not null && string.Equals(requestedSid, currentSid, StringComparison.OrdinalIgnoreCase));

    public static LaunchArgs Parse(string[] args)
    {
        string? sid = null;
        var probe = false;
        var send = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--restart" when i + 1 < args.Length && args[i + 1].Length > 0 && !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    sid = args[++i];
                    break;
                case "--probe-notifications":
                    probe = true;
                    break;
                case SendOption:
                    while (i + 1 < args.Length && args[i + 1].Length > 0 && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        send.Add(args[++i]);
                    }

                    break;
            }
        }

        return new LaunchArgs(sid, probe, send.Count > 0 ? send : null);
    }

    public static string RestartArguments(string sid) => $"--restart {sid}";

    public static string? CurrentUserSid() => WindowsIdentity.GetCurrent().User?.Value;

    /// <summary>The name a running copy of <paramref name="version"/> holds for its whole life, so a later copy can see which one it is.</summary>
    public static string VersionMutexName(string version) => $@"Local\Edgepad.Version.{version}";
}

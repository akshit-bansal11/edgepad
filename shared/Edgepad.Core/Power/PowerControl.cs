using Edgepad.Protocol;

namespace Edgepad.Power;

/// <summary>What a countdown ends in.</summary>
internal enum PowerKind
{
    Restart,
    ShutDown,
}

/// <summary>What this laptop can do on the phone's request. Each flag is the feature token of the same name.</summary>
[Flags]
internal enum PowerCaps
{
    None = 0,
    Sleep = 1,
    Hibernate = 2,
    Restart = 4,
    ShutDown = 8,
}

/// <summary>The operating system's side of <see cref="PowerControl"/>. Every call may be made from any thread.</summary>
internal interface IPowerPlatform
{
    PowerCaps Caps { get; }

    bool Sleep();

    bool Hibernate();

    /// <summary>Starts a cancellable countdown. <c>ended</c> is called exactly once: true at zero, false when cancelled.</summary>
    bool StartCountdown(PowerKind kind, int seconds, Action<bool> ended);

    void CancelCountdown();

    /// <summary>Restarts or shuts down now.</summary>
    bool Execute(PowerKind kind);

    /// <summary>A countdown already up, for a phone that reconnects.</summary>
    (PowerKind Kind, int SecondsLeft)? Pending { get; }
}

/// <summary>
/// Sleep, hibernate, restart and shut down on the phone's request. Restart and shut down run a
/// <see cref="CountdownSeconds"/> second countdown the phone can cancel; <c>stateChanged</c> gets one
/// <see cref="TextKind.PowerState"/> text on every change. A countdown outlives the session that started it.
/// </summary>
internal sealed class PowerControl(IPowerPlatform platform, Action<string> stateChanged)
{
    public const int CountdownSeconds = 60;

    private readonly Lock gate = new();
    private bool counting;

    /// <summary>False (dropped) when the laptop cannot do it. Restart or shut down while one is pending is true and changes nothing.</summary>
    public bool Run(ActionId action) =>
        action switch
        {
            ActionId.Sleep => platform.Caps.HasFlag(PowerCaps.Sleep) && !IsCounting() && platform.Sleep(),
            ActionId.Hibernate => platform.Caps.HasFlag(PowerCaps.Hibernate) && !IsCounting() && platform.Hibernate(),
            ActionId.Restart => Start(PowerKind.Restart, PowerCaps.Restart),
            ActionId.ShutDown => Start(PowerKind.ShutDown, PowerCaps.ShutDown),
            ActionId.PowerCancel => Cancel(),
            _ => false,
        };

    /// <summary>"restart/52", "shutdown/7" or empty, for the send after a reconnect.</summary>
    public string CurrentState() => platform.Pending is { } pending ? State(pending.Kind, pending.SecondsLeft) : "";

    /// <summary>The FEATURES tokens for these caps, in a fixed order.</summary>
    public static string Features(PowerCaps caps)
    {
        List<string> tokens = [];
        AddIf(tokens, caps, PowerCaps.Sleep, Feature.Sleep);
        AddIf(tokens, caps, PowerCaps.Hibernate, Feature.Hibernate);
        AddIf(tokens, caps, PowerCaps.Restart, Feature.Restart);
        AddIf(tokens, caps, PowerCaps.ShutDown, Feature.ShutDown);
        return string.Join('/', tokens);
    }

    private static void AddIf(List<string> tokens, PowerCaps caps, PowerCaps flag, string token)
    {
        if (caps.HasFlag(flag))
        {
            tokens.Add(token);
        }
    }

    private static string State(PowerKind kind, int seconds) =>
        $"{(kind == PowerKind.Restart ? PowerStateToken.Restart : PowerStateToken.ShutDown)}/{seconds}";

    /// <summary>Sleep during a countdown would let it run out in the sleep, so both are refused while one is pending.</summary>
    private bool IsCounting()
    {
        lock (gate)
        {
            return counting;
        }
    }

    private bool Start(PowerKind kind, PowerCaps cap)
    {
        if (!platform.Caps.HasFlag(cap))
        {
            return false;
        }

        lock (gate)
        {
            if (counting)
            {
                return true;
            }

            counting = true;
        }

        if (!platform.StartCountdown(kind, CountdownSeconds, fired => Ended(kind, fired)))
        {
            lock (gate)
            {
                counting = false;
            }

            return false;
        }

        stateChanged(State(kind, CountdownSeconds));
        return true;
    }

    private void Ended(PowerKind kind, bool fired)
    {
        lock (gate)
        {
            counting = false;
        }

        stateChanged("");
        if (fired)
        {
            _ = platform.Execute(kind);
        }
    }

    private bool Cancel()
    {
        if (!platform.Caps.HasFlag(PowerCaps.Restart) && !platform.Caps.HasFlag(PowerCaps.ShutDown))
        {
            return false;
        }

        platform.CancelCountdown();
        return true;
    }
}

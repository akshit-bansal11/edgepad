using Edgepad.Protocol;

namespace Edgepad.Dashboard;

/// <summary>What happened to an agent session, in the board's own words.</summary>
public enum SessionEvent
{
    Start,
    Prompt,
    NeedsYou,
    Stop,
    End,
}

/// <summary>One lifecycle event of one session. The id and the folder stay on the laptop; only the folder's last name is shown.</summary>
internal sealed record HookEvent(string Agent, string SessionId, SessionEvent Event, string Cwd)
{
    /// <summary>Claude Code hook name (and Notification type) to an event; null for the ones the board does not track.</summary>
    public static HookEvent? FromClaude(string? hookEventName, string? notificationType, string sessionId, string cwd) =>
        Map(hookEventName, notificationType) is { } e ? new HookEvent(AgentToken.Claude, sessionId, e, cwd) : null;

    /// <summary>Codex hooks carry approvals and SessionStart only; the rollout watcher supplies the rest.</summary>
    public static HookEvent? FromCodex(string? hookEventName, string sessionId, string cwd) =>
        hookEventName == "SessionStart" ? new HookEvent(AgentToken.Codex, sessionId, SessionEvent.Start, cwd) : null;

    private static SessionEvent? Map(string? name, string? notificationType) => name switch
    {
        "SessionStart" => SessionEvent.Start,
        "UserPromptSubmit" => SessionEvent.Prompt,
        "Notification" when notificationType is "permission_prompt" or "idle_prompt" or "agent_needs_input" => SessionEvent.NeedsYou,
        "Stop" => SessionEvent.Stop,
        "SessionEnd" => SessionEvent.End,
        _ => null,
    };
}

/// <summary>A session found on disk before any hook spoke of it: its id, state token, folder and when its file was last written.</summary>
internal sealed record SeedSession(string Agent, string SessionId, string State, string Cwd, DateTimeOffset Touched);

/// <summary>
/// Which agent sessions exist and what each is doing, newest first, for TEXT SESSIONS. Start and Stop mean idle, Prompt busy,
/// NeedsYou waiting, End gone. A session with a permission ask pending reads as waiting until the ask is answered. At most 8
/// are kept (the oldest idle one makes room) and one nobody touched for 6 hours is forgotten. <see cref="Changed"/> fires
/// outside the lock and only when the list a phone would see differs.
/// </summary>
internal sealed class SessionBoard(Func<DateTimeOffset> clock)
{
    public const int Capacity = 8;
    public const int MaxIdLength = 200;
    public static readonly TimeSpan Expiry = TimeSpan.FromHours(6);

    private static readonly char[] Separators = ['/', '\\'];

    private readonly Lock gate = new();
    private readonly Dictionary<(string Agent, string Id), Session> sessions = [];
    private long order;

    public event Action? Changed;

    public void Apply(HookEvent hook)
    {
        if (!DashWire.IsAgent(hook.Agent) || hook.SessionId.Length is 0 or > MaxIdLength)
        {
            return;
        }

        Run(() =>
        {
            var key = (hook.Agent, hook.SessionId);
            if (hook.Event == SessionEvent.End)
            {
                sessions.Remove(key);
                return;
            }

            if (!sessions.TryGetValue(key, out var session))
            {
                MakeRoom();
                session = new Session();
                sessions[key] = session;
            }

            session.State = hook.Event switch
            {
                SessionEvent.Prompt => SessionStateToken.Busy,
                SessionEvent.NeedsYou => SessionStateToken.Wait,
                _ => SessionStateToken.Idle,
            };
            if (hook.Cwd.Length > 0)
            {
                session.Title = TitleOf(hook.Cwd);
            }

            Touch(session);
        });
    }

    /// <summary>
    /// Adds sessions found on disk (a transcript written before Edgepad ran or the phone linked). A session the board already knows
    /// is left alone (hook-fed state wins), one past <see cref="Expiry"/> is ignored, and a full board takes no more: seeds never
    /// push out a live session. The next hook event for the same id replaces the seeded state through <see cref="Apply"/>.
    /// </summary>
    internal void Seed(IEnumerable<SeedSession> found) =>
        Run(() =>
        {
            var cutoff = clock() - Expiry;
            var fresh = found
                .Where(s => s.Touched > cutoff && DashWire.IsAgent(s.Agent) && s.SessionId.Length is > 0 and <= MaxIdLength)
                .Where(s => !sessions.ContainsKey((s.Agent, s.SessionId)))
                .GroupBy(s => (s.Agent, s.SessionId))
                .Select(g => g.OrderByDescending(s => s.Touched).First())
                .OrderByDescending(s => s.Touched)
                .Take(Capacity - sessions.Count)
                .Reverse();
            foreach (var seed in fresh)
            {
                sessions[(seed.Agent, seed.SessionId)] = new Session
                {
                    State = seed.State,
                    Title = seed.Cwd.Length > 0 ? TitleOf(seed.Cwd) : string.Empty,
                    Touched = seed.Touched,
                    Order = ++order,
                };
            }
        });

    /// <summary>The permission broker's callback: a session with an ask pending reads as waiting. An unknown session is ignored.</summary>
    public void SetWaiting(string sessionId, bool waiting) =>
        Run(() =>
        {
            foreach (var (key, session) in sessions)
            {
                if (key.Id == sessionId)
                {
                    session.Waiting = waiting;
                    Touch(session);
                }
            }
        });

    /// <summary>Forgets what expired. Called by <see cref="Apply"/> and <see cref="Snapshot"/> already; a timer may call it too.</summary>
    public void Sweep() => Run(static () => { });

    /// <summary>Newest first.</summary>
    public IReadOnlyList<SessionEntry> Snapshot()
    {
        Sweep();
        lock (gate)
        {
            return Project();
        }
    }

    private static string TitleOf(string cwd)
    {
        var trimmed = cwd.TrimEnd(Separators);
        return DashWire.Clean(trimmed[(trimmed.LastIndexOfAny(Separators) + 1)..], 24);
    }

    private void Run(Action change)
    {
        bool changed;
        lock (gate)
        {
            var before = Project();
            var cutoff = clock() - Expiry;
            foreach (var key in sessions.Where(s => s.Value.Touched <= cutoff).Select(s => s.Key).ToList())
            {
                sessions.Remove(key);
            }

            change();
            changed = !before.SequenceEqual(Project());
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void Touch(Session session)
    {
        session.Touched = clock();
        session.Order = ++order;
    }

    // Under the gate. Makes room for one more: the oldest idle session, else the oldest of all.
    private void MakeRoom()
    {
        if (sessions.Count < Capacity)
        {
            return;
        }

        var victim = sessions
            .OrderBy(s => s.Value.State == SessionStateToken.Idle && !s.Value.Waiting ? 0 : 1)
            .ThenBy(s => s.Value.Order)
            .First();
        sessions.Remove(victim.Key);
    }

    private List<SessionEntry> Project() =>
        [.. sessions
            .OrderByDescending(s => s.Value.Order)
            .Select(s => new SessionEntry(s.Key.Agent, s.Value.Waiting ? SessionStateToken.Wait : s.Value.State, s.Value.Title))];

    private sealed class Session
    {
        public string State { get; set; } = SessionStateToken.Idle;

        public string Title { get; set; } = string.Empty;

        public bool Waiting { get; set; }

        public DateTimeOffset Touched { get; set; }

        public long Order { get; set; }
    }
}

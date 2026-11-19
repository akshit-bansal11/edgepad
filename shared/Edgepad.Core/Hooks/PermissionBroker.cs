using System.Security.Cryptography;
using Edgepad.Dashboard;
using Edgepad.Protocol;

namespace Edgepad.Hooks;

/// <summary>What became of a permission request. Defer means the agent shows its own prompt, as if Edgepad were not there.</summary>
internal enum PermDecision
{
    Defer,
    Allow,
    Deny,
}

/// <summary>
/// An approval on its way to the phones: BLOB PERM_ASK with label <c>Id/Complete</c> (<see cref="DashWire.PermLabel"/>) and
/// <paramref name="Body"/> as UTF-8. When <paramref name="Complete"/> is false the body has no text and the phone offers Deny only.
/// </summary>
internal sealed record PermPrompt(string Id, bool Complete, string Body);

/// <summary>
/// Holds the approvals that wait for a phone. Each gets a fresh random 12-hex id; the first valid answer wins and every later one,
/// a replay or a guessed id is refused. No phone, 8 asks already waiting, or 20 asks in the last minute: deferred at once, so
/// the agent's own prompt shows and nothing piles up. An ask nobody answers in 30 s, or whose hook went away first, is deferred
/// and announced <see cref="Gone"/>, so every phone drops its sheet. The text shown is built here from a whitelist of tool fields,
/// never from anything else the hook sent. While an ask waits its session reads as waiting on the board.
/// </summary>
internal sealed class PermissionBroker(SessionBoard board, Func<bool> phoneLinked, Func<DateTimeOffset>? clock = null, TimeSpan? wait = null)
{
    public const int MaxPending = 8;
    public const int MaxPerMinute = 20;
    public static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(30);

    private readonly Lock gate = new();
    private readonly Dictionary<string, Pending> pending = [];
    private readonly Queue<DateTimeOffset> recent = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly TimeSpan answerWait = wait ?? AnswerWait;

    /// <summary>A new ask for every linked phone. Raised outside the lock; a handler that throws is logged and the others still run.</summary>
    public event Action<PermPrompt>? Asked;

    /// <summary>The ask with this id is over (answered, timed out, or its hook left): every phone dismisses it.</summary>
    public event Action<string>? Gone;

    /// <summary>How many asks wait right now.</summary>
    public int PendingCount
    {
        get
        {
            lock (gate)
            {
                return pending.Count;
            }
        }
    }

    /// <summary>The text an approval shows for this tool: Bash's command, the file of an edit or read, WebFetch's URL; for Codex, whose tool names are not documented, the first of command, file and URL it sent; nothing for any other tool.</summary>
    public static string TextFor(PermRequest request)
    {
        var input = request.Input;
        var text = request.Tool switch
        {
            "Bash" => input.Command,
            "Edit" or "Write" or "MultiEdit" or "Read" or "NotebookEdit" => input.FilePath,
            "WebFetch" => input.Url,
            _ when request.Agent == AgentToken.Codex => input.Command ?? input.FilePath ?? input.Url,
            _ => null,
        };
        return text ?? string.Empty;
    }

    /// <summary>
    /// Asks the phones and waits for the first answer, 30 s at most. <paramref name="cancellationToken"/> is the hook going away
    /// (answered in the terminal, hook cancelled): that ends the ask as <see cref="PermDecision.Defer"/> and raises <see cref="Gone"/>.
    /// </summary>
    public async Task<PermDecision> RequestAsync(PermRequest request, CancellationToken cancellationToken)
    {
        if (!phoneLinked())
        {
            return PermDecision.Defer;
        }

        var text = TextFor(request);
        var body = PermAskBody.Encode(request.Agent, request.Tool, text, out var complete);
        if (complete && !request.Input.CommandSafe && request.Input.Command is { } command && text == command)
        {
            body = PermAskBody.Encode(request.Agent, request.Tool, string.Empty, out _);
            complete = false;
        }

        Pending ask;
        lock (gate)
        {
            var cutoff = now() - TimeSpan.FromMinutes(1);
            while (recent.Count > 0 && recent.Peek() <= cutoff)
            {
                recent.Dequeue();
            }

            if (pending.Count >= MaxPending || recent.Count >= MaxPerMinute)
            {
                return PermDecision.Defer;
            }

            recent.Enqueue(now());
            string id;
            do
            {
                id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
            }
            while (pending.ContainsKey(id));

            ask = new Pending(id, request.Session);
            pending[id] = ask;
        }

        board.SetWaiting(request.Session, true);
        Raise(Asked, new PermPrompt(ask.Id, complete, body));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var winner = await Task.WhenAny(ask.Answer.Task, Task.Delay(answerWait, stop.Token)).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        if (winner == ask.Answer.Task)
        {
            return await ask.Answer.Task.ConfigureAwait(false);
        }

        // Timed out, or the hook left: end it unless an answer slipped in at the same moment.
        if (Finish(ask.Id) is not null)
        {
            ask.Answer.TrySetResult(PermDecision.Defer);
        }

        return await ask.Answer.Task.ConfigureAwait(false);
    }

    /// <summary>A phone's PERM_ANSWER. True when the id waits and this is its first answer; false for an unknown, used or guessed id.</summary>
    public bool Answer(string id, bool allow)
    {
        if (Finish(id) is not { } ask)
        {
            return false;
        }

        ask.Answer.TrySetResult(allow ? PermDecision.Allow : PermDecision.Deny);
        return true;
    }

    /// <summary>The <see cref="TextKind.PermAnswer"/> wire, for the Dispatcher: <c>id/a</c> or <c>id/d</c>. False when it is not one or the id does not wait.</summary>
    public bool Answer(string wire) => PermAnswer.TryParse(wire, out var answer) && Answer(answer.Id, answer.Allow);

    /// <summary>A phone went away. When none is left, every waiting ask is deferred so the agent shows its own prompt.</summary>
    public void PhonesChanged()
    {
        if (phoneLinked())
        {
            return;
        }

        List<string> ids;
        lock (gate)
        {
            ids = [.. pending.Keys];
        }

        foreach (var id in ids)
        {
            Finish(id)?.Answer.TrySetResult(PermDecision.Defer);
        }
    }

    private static void Raise<T>(Action<T>? handlers, T value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                handler(value);
            }
            catch (Exception e)
            {
                // One phone's broken link must not keep the ask from the others, or end the hook's thread.
                Log.Write($"An approval could not reach a phone: {e.GetType().Name}");
            }
        }
    }

    // Removes the ask once, clears its session's waiting mark when no other ask of that session waits, and announces it gone.
    private Pending? Finish(string id)
    {
        Pending? ask;
        bool sessionStillWaits;
        lock (gate)
        {
            if (!pending.Remove(id, out ask))
            {
                return null;
            }

            sessionStillWaits = pending.Values.Any(p => p.Session == ask.Session);
        }

        if (!sessionStillWaits)
        {
            board.SetWaiting(ask.Session, false);
        }

        Raise(Gone, id);
        return ask;
    }

    private sealed class Pending(string id, string session)
    {
        public string Id { get; } = id;

        public string Session { get; } = session;

        public TaskCompletionSource<PermDecision> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

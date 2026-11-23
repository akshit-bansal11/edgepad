using System;
using System.Collections.Generic;

namespace Edgepad.Notify;

/// <summary>
/// Lets at most <see cref="MaxPerMinute"/> notifications through in any minute, and drops one that is identical (same
/// app, title and body) to one let through in the last 10 seconds. A dropped one does not count toward the minute.
/// Holds at most <see cref="MaxPerMinute"/> entries, so memory stays bounded. Clock is milliseconds, as in
/// <see cref="Trust.PairingLimiter"/>; the caller supplies it, so tests move it by hand.
/// </summary>
public sealed class NotifyLimiter(Func<long> clockMs)
{
    public const int MaxPerMinute = 10;

    private const long MinuteMs = 60_000;
    private const long DuplicateMs = 10_000;

    private readonly Queue<(long At, string App, string Title, string Body)> sent = new();

    /// <summary>True when the notification may be sent now, and records it as sent.</summary>
    public bool TryAccept(string app, string title, string body)
    {
        var now = clockMs();
        while (sent.Count > 0 && now - sent.Peek().At >= MinuteMs)
        {
            sent.Dequeue();
        }

        foreach (var entry in sent)
        {
            if (entry.App == app && entry.Title == title && entry.Body == body && now - entry.At < DuplicateMs)
            {
                return false;
            }
        }

        if (sent.Count >= MaxPerMinute)
        {
            return false;
        }

        sent.Enqueue((now, app, title, body));
        return true;
    }
}

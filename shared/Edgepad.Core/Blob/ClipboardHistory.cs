using System.Text;

namespace Edgepad.Blob;

/// <summary>Which end a copied text came from.</summary>
internal enum ClipSide { Laptop, Phone }

/// <summary>One copied text and which end it came from.</summary>
internal sealed record ClipEntry(string Text, ClipSide Side, DateTimeOffset At);

/// <summary>
/// The most recent copied texts, newest first, for the history panel. Memory only: never logged, never saved.
/// Safe from any thread.
/// </summary>
internal sealed class ClipboardHistory(Func<DateTimeOffset>? clock = null)
{
    public const int MaxItems = 25;

    /// <summary>UTF-8 bytes. Longer text still syncs; it is just not listed.</summary>
    public const int MaxBytes = 32 * 1024;

    private readonly Lock gate = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.Now);
    private readonly List<ClipEntry> items = [];

    /// <summary>Raised outside the lock after an effective change.</summary>
    public event Action? Changed;

    /// <summary>A copy of the list, newest first.</summary>
    public IReadOnlyList<ClipEntry> Items
    {
        get
        {
            lock (gate)
            {
                return [.. items];
            }
        }
    }

    /// <summary>False for blank or over-long text. A repeat moves to the front and takes the new side and time.</summary>
    public bool Record(string text, ClipSide side)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaxBytes)
        {
            return false;
        }

        lock (gate)
        {
            items.RemoveAll(e => e.Text == text);
            items.Insert(0, new ClipEntry(text, side, now()));
            if (items.Count > MaxItems)
            {
                items.RemoveRange(MaxItems, items.Count - MaxItems);
            }
        }

        Changed?.Invoke();
        return true;
    }

    public bool Remove(string text)
    {
        int removed;
        lock (gate)
        {
            removed = items.RemoveAll(e => e.Text == text);
        }

        if (removed == 0)
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        lock (gate)
        {
            if (items.Count == 0)
            {
                return;
            }

            items.Clear();
        }

        Changed?.Invoke();
    }
}

/// <summary>The unit a history entry's age is shown in.</summary>
internal enum AgeUnit { JustNow, Minutes, Hours, Days }

/// <summary>An age as the panel words it: <paramref name="Count"/> of <paramref name="Unit"/>, 0 for just now.</summary>
internal readonly record struct Age(AgeUnit Unit, int Count);

/// <summary>Turns how long ago something was copied into the coarse age the history panel shows. Pure, so it is tested.</summary>
internal static class RelativeAge
{
    public static Age Of(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(60))
        {
            return new Age(AgeUnit.JustNow, 0);
        }

        if (elapsed < TimeSpan.FromMinutes(60))
        {
            return new Age(AgeUnit.Minutes, (int)elapsed.TotalMinutes);
        }

        if (elapsed < TimeSpan.FromHours(24))
        {
            return new Age(AgeUnit.Hours, (int)elapsed.TotalHours);
        }

        return new Age(AgeUnit.Days, (int)elapsed.TotalDays);
    }
}

namespace Edgepad.Platform;

/// <summary>
/// Pending work where only the newest request for each key matters: a dial drag is dozens of SET frames a second, and
/// running a process or a D-Bus call for each would lag behind the finger. Posting is cheap and never blocks; a worker
/// takes whatever is pending in one go, so a burst of 40 volume values becomes one.
/// </summary>
internal sealed class Coalescer<TKey, TValue>
    where TKey : notnull
{
    private readonly Lock gate = new();
    private readonly Dictionary<TKey, TValue> pending = [];
    private readonly List<TKey> order = [];

    /// <summary>Records <paramref name="value"/> for <paramref name="key"/>, replacing one not yet taken.</summary>
    public void Post(TKey key, TValue value)
    {
        lock (gate)
        {
            if (!pending.ContainsKey(key))
            {
                order.Add(key);
            }

            pending[key] = value;
        }
    }

    /// <summary>Everything pending, each key once with its newest value, in the order the keys first arrived; empties the queue.</summary>
    public List<KeyValuePair<TKey, TValue>> Take()
    {
        lock (gate)
        {
            var taken = new List<KeyValuePair<TKey, TValue>>(order.Count);
            foreach (var key in order)
            {
                taken.Add(new KeyValuePair<TKey, TValue>(key, pending[key]));
            }

            order.Clear();
            pending.Clear();
            return taken;
        }
    }
}

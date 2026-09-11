namespace Edgepad.Controls;

/// <summary>A watch that runs its release once, on the first Dispose.</summary>
internal sealed class Subscription(Action release) : IDisposable
{
    private Action? release = release;

    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}

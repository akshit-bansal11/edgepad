using System.Runtime.InteropServices;
using Edgepad.Dispatch;
using Edgepad.Protocol;
using Microsoft.Win32;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Radios;
using Windows.Networking.Sockets;

namespace Edgepad.Bluetooth;

/// <summary>
/// Advertises the Edgepad RFCOMM service and hands each connection to the <see cref="SessionHost"/>, which serves one
/// phone at a time. The service is started again by itself when a start fails, when the laptop wakes from sleep
/// and when the Bluetooth radio comes back on, any of which used to leave it deaf while the tray still said it was
/// waiting.
/// </summary>
internal sealed class RfcommServer(Action<string> onStatus, SessionHost host) : IDisposable
{
    /// <summary>How long a failed start waits before the next try, for as long as it takes.</summary>
    private static readonly TimeSpan RetryStartEvery = TimeSpan.FromSeconds(30);

    private const string Waiting = "Waiting for your phone";
    private const string BluetoothOff = "Bluetooth is off";
    private const string StartFailing = "Bluetooth is off or unavailable; trying again";

    private readonly Lock gate = new();

    private RfcommServiceProvider? provider;
    private StreamSocketListener? listener;

    // Set while a start, or the wait before retrying one, is under way. There is only ever one; a restart asked
    // for meanwhile is folded into it through startAgain.
    private bool starting;
    private string? startAgain;

    // Completed to cut the wait before the next try short, when a restart is asked for during it.
    private TaskCompletionSource? wake;

    // A restart asked for while a phone was connected. It runs when that phone's session ends: tearing the
    // service down under a working link would cost the owner the link for nothing.
    private string? deferredRestart;

    // The radio whose state is watched, and whether it last said it was off.
    private Radio? radio;
    private bool radioOff;

    private bool disposed;

    // The last failure logged, so a radio that stays off is not reported every 30 s. Only the start under way
    // touches these, and there is only ever one.
    private string? lastStartFailure;
    private string? lastRadioProblem;

    /// <summary>
    /// Starts the service, trying again every 30 s until it is up, and from then on restarts it after sleep and
    /// whenever the radio comes back on. Called once, on the UI thread, which is where the power events are
    /// raised from then on.
    /// </summary>
    public Task StartAsync()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        lock (gate)
        {
            starting = true;
        }

        return StartUntilServingAsync(null);
    }

    /// <summary>
    /// Starts the service, and keeps trying until it is up or the server is disposed. A non-null
    /// <paramref name="restart"/> says why the running service is being replaced. Only one runs at a time:
    /// whoever sets the starting flag under the lock runs it.
    /// </summary>
    private async Task StartUntilServingAsync(string? restart)
    {
        try
        {
            while (true)
            {
                var woken = Task.CompletedTask;
                string? deferredFor = null;
                lock (gate)
                {
                    if (disposed)
                    {
                        starting = false;
                        return;
                    }

                    // A phone admitted since the restart was asked for keeps its link; the restart waits for it.
                    if (restart is not null && host.CurrentAddress is { } live)
                    {
                        deferredRestart = restart;
                        deferredFor = live;
                        starting = false;
                    }
                    else
                    {
                        startAgain = null;
                        wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        woken = wake.Task;
                    }
                }

                if (deferredFor is not null)
                {
                    Log.Write($"Restarting the Bluetooth service once {deferredFor} disconnects: {restart}");
                    return;
                }

                if (restart is not null)
                {
                    Log.Write($"Restarting the Bluetooth service: {restart}");
                    StopServing();
                }

                try
                {
                    await StartOnceAsync();
                }
                catch (Exception e)
                {
                    // Bluetooth not ready at sign-in, a radio switched off, an adapter unplugged: all of them a
                    // state to wait out, not an end. Logged once per distinct reason.
                    if (e.Message != lastStartFailure)
                    {
                        lastStartFailure = e.Message;
                        Log.Write($"Bluetooth service failed to start; trying again every {(int)RetryStartEvery.TotalSeconds} s: {e}");
                    }

                    lock (gate)
                    {
                        if (!disposed && host.CurrentAddress is null)
                        {
                            onStatus(StartFailing);
                        }
                    }

                    await Task.WhenAny(woken, Task.Delay(RetryStartEvery));

                    // Nothing is running to be replaced: the next round is a plain start.
                    restart = null;
                    continue;
                }

                lastStartFailure = null;
                Log.Write("RFCOMM service advertising");
                await WatchRadioAsync();
                lock (gate)
                {
                    if (disposed)
                    {
                        starting = false;
                        return;
                    }

                    onStatus(StatusNow());
                    if (startAgain is null)
                    {
                        starting = false;
                        return;
                    }

                    // Asked for while this start was under way, so it may have started on a radio that was
                    // still coming up.
                    restart = startAgain;
                }
            }
        }
        catch (Exception e)
        {
            // Top-level boundary of a fire-and-forget task: an escaped exception here would be lost silently.
            Log.Write($"The Bluetooth service stopped trying to start: {e}");
            lock (gate)
            {
                starting = false;
            }
        }
    }

    /// <summary>
    /// One start. A half-finished one is undone before the exception leaves, so the next try starts clean, and
    /// so is one that <see cref="Dispose"/> overtook.
    /// </summary>
    private async Task StartOnceAsync()
    {
        var socketListener = new StreamSocketListener();
        RfcommServiceProvider? serviceProvider = null;
        var advertising = false;
        var published = false;
        try
        {
            serviceProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(ProtocolConstants.ServiceId));
            socketListener.ConnectionReceived += OnConnectionReceived;

            // Encryption with authentication: only a device already paired with this laptop can connect.
            await socketListener.BindServiceNameAsync(
                serviceProvider.ServiceId.AsString(),
                SocketProtectionLevel.BluetoothEncryptionWithAuthentication);

            serviceProvider.StartAdvertising(socketListener);
            advertising = true;
            lock (gate)
            {
                published = !disposed;
                if (published)
                {
                    provider = serviceProvider;
                    listener = socketListener;
                }
            }
        }
        finally
        {
            if (!published)
            {
                if (advertising)
                {
                    StopAdvertising(serviceProvider);
                }

                socketListener.Dispose();
            }
        }
    }

    /// <summary>Stops advertising and closes the listener. Connections already taken are left to run.</summary>
    private void StopServing()
    {
        RfcommServiceProvider? stopping;
        StreamSocketListener? closing;
        lock (gate)
        {
            stopping = provider;
            closing = listener;
            provider = null;
            listener = null;
        }

        StopAdvertising(stopping);
        closing?.Dispose();
    }

    private static void StopAdvertising(RfcommServiceProvider? stopping)
    {
        try
        {
            stopping?.StopAdvertising();
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            // Already stopped, or the radio went away: nothing left to stop. Throwing here left a stuck
            // process holding the single-instance lock, so no update could take over.
            Log.Write($"StopAdvertising: {e.Message}");
        }
    }

    /// <summary>
    /// Restarts the service, now or once the connected phone's session ends, folded into a start already under
    /// way if there is one.
    /// </summary>
    private void RequestRestart(string reason)
    {
        string? deferredFor = null;
        var begin = false;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (host.CurrentAddress is { } live)
            {
                deferredRestart = reason;
                deferredFor = live;
            }
            else if (starting)
            {
                startAgain = reason;
                wake?.TrySetResult();
            }
            else
            {
                starting = true;
                begin = true;
            }
        }

        if (deferredFor is not null)
        {
            Log.Write($"Restarting the Bluetooth service once {deferredFor} disconnects: {reason}");
        }
        else if (begin)
        {
            _ = StartUntilServingAsync(reason);
        }
    }

    /// <summary>
    /// Watches the Bluetooth radio found now, in place of any watched before: it may not have existed at sign-in,
    /// and the one watched before may be gone. Without one, a failed start is still retried; this only makes a
    /// radio switched back on take effect at once.
    /// </summary>
    private async Task WatchRadioAsync()
    {
        try
        {
            var found = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (found is null)
            {
                NoteRadioProblem("No Bluetooth radio was found to watch; a failed start is still retried every 30 s");
                return;
            }

            var off = found.State != RadioState.On;
            found.StateChanged += OnRadioStateChanged;
            Radio? previous = null;
            bool kept;
            lock (gate)
            {
                kept = !disposed;
                if (kept)
                {
                    previous = radio;
                    radio = found;
                    radioOff = off;
                }
            }

            // Subscribed before the old one is let go, so no change falls between the two; one subscription
            // either way once this is done.
            if (!kept)
            {
                found.StateChanged -= OnRadioStateChanged;
            }

            if (previous is not null)
            {
                previous.StateChanged -= OnRadioStateChanged;
            }

            lastRadioProblem = null;
        }
        catch (Exception e)
        {
            NoteRadioProblem($"The Bluetooth radio could not be watched; a failed start is still retried every 30 s: {e.Message}");
        }
    }

    private void NoteRadioProblem(string problem)
    {
        if (problem != lastRadioProblem)
        {
            lastRadioProblem = problem;
            Log.Write(problem);
        }
    }

    private void OnRadioStateChanged(Radio sender, object? args)
    {
        // Top-level boundary of a WinRT event: an exception out of it would be lost, and the radio is the one
        // device here most likely to vanish mid-call.
        try
        {
            var off = sender.State != RadioState.On;
            bool changed;
            lock (gate)
            {
                changed = !disposed && off != radioOff;
                if (changed)
                {
                    radioOff = off;
                    if (off)
                    {
                        onStatus(BluetoothOff);
                    }
                }
            }

            if (!changed)
            {
                return;
            }

            if (off)
            {
                Log.Write("The Bluetooth radio turned off");
            }
            else
            {
                RequestRestart("the Bluetooth radio turned back on");
            }
        }
        catch (Exception e)
        {
            Log.Write($"The Bluetooth radio's state could not be read: {e}");
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            RequestRestart("this laptop resumed from sleep");
        }
    }

    /// <summary>
    /// What the tray says when nothing more particular is going on: the connected phone, a radio that is off, or
    /// waiting. The "Connected to " prefix is what the tray's badge reads. Called under the lock.
    /// </summary>
    private string StatusNow()
    {
        if (host.CurrentAddress is { } live)
        {
            return $"Connected to {host.Shown(live)}";
        }

        return radioOff ? BluetoothOff : Waiting;
    }

    private void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        // Declared out here so the catches can dispose the socket.
        StreamSocket? socket = null;
        BluetoothLink link;

        // A peer that drops in the instant after connecting makes these calls throw, and an exception out of
        // a WinRT event handler would leak the socket.
        try
        {
            socket = args.Socket;
            link = new BluetoothLink(socket);
        }
        catch (Exception e) when (e is COMException or ObjectDisposedException or InvalidOperationException)
        {
            Log.Write($"A connection was dropped before it could be served: {e.Message}");
            socket?.Dispose();
            return;
        }
        catch (Exception e)
        {
            // Top-level boundary of a WinRT event: nothing may escape it, and the socket must not leak.
            Log.Write($"A connection could not be served: {e}");
            socket?.Dispose();
            return;
        }

        // Catches everything itself, and closes the link on every path that does not hand it to a session.
        host.Accept(link);
    }

    /// <summary>
    /// Called by the host as each session ends, with the step that ends it, which says whether it was the
    /// connected phone. Run under the lock, so a restart deferred for that phone is taken together with it.
    /// </summary>
    public void OnSessionEnded(Func<bool> end)
    {
        string? restart = null;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (end())
            {
                restart = deferredRestart;
                deferredRestart = null;
            }

            // Every ending says what is true now. A candidate's question or refusal must not stay over a live
            // phone's status, and a phone that was replaced may have posted its own status after the one that
            // replaced it. Posted under the lock, so it cannot overtake a newer one.
            onStatus(StatusNow());
        }

        if (restart is not null)
        {
            RequestRestart(restart);
        }
    }

    /// <summary>
    /// Stops advertising and watching the radio. The sessions are the host's, which its owner disposes after this,
    /// once no new connection can arrive.
    /// </summary>
    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        RfcommServiceProvider? stopping;
        StreamSocketListener? closing;
        Radio? watched;
        lock (gate)
        {
            disposed = true;
            wake?.TrySetResult();
            stopping = provider;
            closing = listener;
            provider = null;
            listener = null;
            watched = radio;
            radio = null;
        }

        StopAdvertising(stopping);
        closing?.Dispose();

        try
        {
            if (watched is not null)
            {
                watched.StateChanged -= OnRadioStateChanged;
            }
        }
        catch (Exception e) when (e is InvalidOperationException or COMException)
        {
            // The radio went away with its subscription; there is nothing left to unsubscribe from.
            Log.Write($"Unwatching the Bluetooth radio: {e.Message}");
        }
    }
}

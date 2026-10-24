using Edgepad.Dispatch;
using Edgepad.Platform;
using Edgepad.Trust;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Edgepad.Bluetooth;

/// <summary>
/// Serves the Edgepad RFCOMM service through BlueZ, one phone at a time, with the Windows hand-off rules
/// (<see cref="Admission{T}"/>). It registers the profile with whichever bluetoothd owns <c>org.bluez</c> and
/// registers it again each time that name changes hands, since a restarted bluetoothd has forgotten every
/// profile. A start that fails (no system bus, bluetoothd not running, the call refused) is tried again every
/// 30 s for as long as it takes, as on Windows: all of them are states to wait out, not an end.
/// </summary>
internal sealed class LinuxServer(TrustStore trust, PinStore pin, Func<string, bool> askTrust, Action<string> onStatus) : IDisposable
{
    // Joined at exit so each session's own cleanup, which releases held keys and buttons, gets to run.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan RetryStartEvery = TimeSpan.FromSeconds(30);

    private const string Waiting = "Waiting for your phone";
    private const string StartFailing = "Bluetooth is off or unavailable; trying again";

    private readonly Admission<LinuxSession> admission = new();
    private readonly CancellationTokenSource stopping = new();

    // The unique bus name (":1.42") of the bluetoothd the profile is registered with; null while there is none.
    // Read on the D-Bus dispatch thread to check who is calling the profile.
    private volatile string? bluetoothd;
    private Task? serving;

    public void Start() => serving = Task.Run(() => ServeAsync(stopping.Token));

    private async Task ServeAsync(CancellationToken stop)
    {
        string? lastFailure = null;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await ServeOnceAsync(stop);
            }
            catch (Exception e) when (!stop.IsCancellationRequested)
            {
                bluetoothd = null;

                // Logged once per distinct reason, so a laptop with Bluetooth off is not reported every 30 s.
                if (e.Message != lastFailure)
                {
                    lastFailure = e.Message;
                    Log.Write($"The Bluetooth service failed to start; trying again every {(int)RetryStartEvery.TotalSeconds} s: {e}");
                }

                onStatus(StartFailing);
                try
                {
                    await Task.Delay(RetryStartEvery, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception)
            {
                // Quitting: the bus was disposed under a call. Nothing to report.
                return;
            }
        }
    }

    /// <summary>
    /// One connection to the system bus, for as long as it lasts: export the profile, then register it with
    /// each bluetoothd that comes along. Returns when quitting; throws when anything fails, which the caller
    /// waits out. Disposing the connection is also what unregisters the profile: bluetoothd drops the profiles
    /// of a client that leaves the bus.
    /// </summary>
    private async Task ServeOnceAsync(CancellationToken stop)
    {
        using var bus = new DBusConnection(DBusAddress.System ?? throw new IOException("The system bus address is unknown"));
        await bus.ConnectAsync();
        bus.AddMethodHandler(new BluezProfile(() => bluetoothd, OnNewConnection, OnRequestDisconnection));
        var disconnected = bus.DisconnectedAsync();
        using var owner = await bus.WatchNameOwnerAsync(BluezProfile.ServiceName);
        while (true)
        {
            if (owner.GetCurrentOwner() is null)
            {
                bluetoothd = null;
                Log.Write("Waiting for bluetoothd to start");
                onStatus(StartFailing);
            }

            // An owner identifier, not the plain name: a call addressed to it fails rather than reaching a
            // bluetoothd that replaced this one meanwhile.
            var id = await owner.WaitForOwnerAsync(stop);
            var changed = owner.GetOwnerChangedCancellationToken(id);
            bluetoothd = NameOwnerWatcher.GetOwnerBusName(id);
            await bus.CallMethodAsync(BluezProfile.RegisterMessage(bus, id));
            Log.Write($"RFCOMM profile registered with bluetoothd ({bluetoothd})");
            onStatus(admission.CurrentAddress is { } live ? $"Connected to {live}" : Waiting);

            using var until = CancellationTokenSource.CreateLinkedTokenSource(stop, changed);
            await Task.WhenAny(Task.Delay(Timeout.Infinite, until.Token), disconnected);
            if (stop.IsCancellationRequested)
            {
                return;
            }

            if (disconnected.IsCompleted)
            {
                throw new IOException("The system bus connection was lost", await disconnected);
            }

            // Connections already handed over are the app's own sockets and live on; only new ones need this.
            Log.Write("bluetoothd restarted; registering the profile again");
        }
    }

    /// <summary>
    /// Called on the D-Bus thread, which must not block: the session runs on a thread of its own. True when the
    /// socket was taken; false when it was refused, and the profile then closes it.
    /// </summary>
    private bool OnNewConnection(string address, SafeFileHandle socket)
    {
        // A device this laptop already knows it will refuse is turned away before it costs a thread. Nothing that
        // gets past this touches the connected phone yet: that waits for the handshake's onAdmitted.
        var refusal = admission.Refusal(address);
        if (refusal is null && trust.Refuses(address))
        {
            refusal = $"Refused {address} without disturbing the connected phone: the owner is being asked about a phone "
                + "or just declined this one, or the trust file cannot be read";
        }

        if (refusal is not null)
        {
            Log.Write(refusal);
            return false;
        }

        // Input state belongs to one connection: its own virtual keyboard, destroyed when the session ends.
        var platform = new LinuxPlatform();
        var session = new LinuxSession(
            socket,
            address,
            trust,
            pin,
            askTrust,
            new Dispatcher(platform, platform),
            platform.Dispose,
            onStatus,
            OnAdmitted,
            OnSessionEnded);
        if (!admission.AddCandidate(session, address))
        {
            Log.Write($"Refused {address}: Edgepad is quitting");
            session.Dispose();
            return false;
        }

        session.Start();
        return true;
    }

    private void OnRequestDisconnection(string address)
    {
        foreach (var session in admission.Sessions(address))
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// Called by a session once trust and the PIN have said yes, just before it answers HELLO_ACK: the moment a
    /// new phone may replace the connected one.
    /// </summary>
    private bool OnAdmitted(LinuxSession session)
    {
        if (!admission.Admit(session, out var replaced, out var refusal))
        {
            Log.Write($"Refused {session.Address}: {refusal}");
            return false;
        }

        // Outside the admission lock: the replaced session's exit calls OnSessionEnded.
        if (replaced is not null)
        {
            Log.Write($"{session.Address} replaced the connection with {replaced.Address}");
            replaced.Dispose();
        }

        return true;
    }

    private void OnSessionEnded(LinuxSession session)
    {
        admission.Ended(session);
        if (!stopping.IsCancellationRequested)
        {
            onStatus(admission.CurrentAddress is { } live ? $"Connected to {live}" : bluetoothd is null ? StartFailing : Waiting);
        }
    }

    public void Dispose()
    {
        stopping.Cancel();

        // Outside any lock, because each session's exit calls OnSessionEnded. Held keys and buttons are released
        // in the session's own finally on its thread, and the process can exit before that runs.
        var ending = admission.Close();
        foreach (var session in ending)
        {
            session.Dispose();
        }

        foreach (var session in ending)
        {
            session.Join(ExitGrace);
        }

        try
        {
            serving?.Wait(ExitGrace);
        }
        catch (AggregateException e)
        {
            Log.Write($"The Bluetooth service stopped with an error: {e.InnerException?.Message}");
        }

        stopping.Dispose();
    }
}

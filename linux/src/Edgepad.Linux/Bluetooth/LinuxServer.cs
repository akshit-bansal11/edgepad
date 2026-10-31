using Edgepad.Net;
using Edgepad.Platform;
using Edgepad.Trust;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Edgepad.Bluetooth;

/// <summary>
/// Serves the Edgepad RFCOMM service through BlueZ, handing each connection to a <see cref="SessionHost"/>, which
/// serves one phone at a time. It registers the profile with whichever bluetoothd owns <c>org.bluez</c> and
/// registers it again each time that name changes hands, since a restarted bluetoothd has forgotten every
/// profile. A start that fails (no system bus, bluetoothd not running, the call refused) is tried again every
/// 30 s for as long as it takes, as on Windows: all of them are states to wait out, not an end.
/// </summary>
internal sealed class LinuxServer : IDisposable
{
    // How long Dispose waits for the service's own loop to stop.
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan RetryStartEvery = TimeSpan.FromSeconds(30);

    private const string Waiting = "Waiting for your phone";
    private const string StartFailing = "Bluetooth is off or unavailable; trying again";

    private readonly Action<string> onStatus;
    private readonly SessionHost host;
    private readonly CancellationTokenSource stopping = new();

    // The unique bus name (":1.42") of the bluetoothd the profile is registered with; null while there is none.
    // Read on the D-Bus dispatch thread to check who is calling the profile.
    private volatile string? bluetoothd;
    private Task? serving;

    public LinuxServer(TrustStore trust, PinStore pin, Func<string, CancellationToken, bool> askTrust, LinkIdentity identity, Action<string> onStatus)
    {
        this.onStatus = onStatus;
        host = new SessionHost(trust, pin, askTrust, identity, onStatus, OnSessionEnded);
    }

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
            onStatus(host.CurrentAddress is { } live ? $"Connected to {host.Shown(live)}" : Waiting);

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
    private bool OnNewConnection(string address, SafeFileHandle socket) => host.Accept(new BluezLink(socket, address));

    /// <summary>Takes a Wi-Fi or USB phone's link, as <see cref="SessionHost.Accept"/> does; <c>NetworkServer</c> calls it.</summary>
    public bool Accept(IPhoneLink link) => host.Accept(link);

    /// <summary>What the owner reads for <paramref name="address"/>: a network phone's name, else the address itself.</summary>
    public string Shown(string address) => host.Shown(address);

    private void OnRequestDisconnection(string address) => host.Disconnect(address);

    /// <summary>
    /// Hangs up on every phone and refuses new connections until <see cref="Resume"/>. For an owner dialog that
    /// changes security: a connected phone is a keyboard, and a trusted one reconnects within seconds.
    /// </summary>
    public void HoldOff() => host.HoldOff();

    /// <summary>Whether a hold or a quiet period refuses new connections right now; no pairing may start then.</summary>
    public bool IsRefusing => host.IsRefusing;

    /// <summary>Ends one <see cref="HoldOff"/>.</summary>
    public void Resume() => host.Resume();

    /// <summary>Hangs up on every phone and refuses new connections for <paramref name="quiet"/>.</summary>
    public void HangUp(TimeSpan quiet) => host.HangUp(quiet);

    private void OnSessionEnded()
    {
        if (!stopping.IsCancellationRequested)
        {
            onStatus(host.CurrentAddress is { } live ? $"Connected to {host.Shown(live)}" : bluetoothd is null ? StartFailing : Waiting);
        }
    }

    public void Dispose()
    {
        stopping.Cancel();

        // Ends every session and waits for each one's cleanup, which releases held keys and buttons.
        host.Dispose();

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

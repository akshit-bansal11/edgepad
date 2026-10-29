using System.Net.Sockets;
using Edgepad.Net;
using Edgepad.Trust;

namespace Edgepad.Network;

/// <summary>
/// Takes Wi-Fi and USB phones: a <see cref="TcpLinkListener"/> for their connections and an <see cref="MdnsAdvertiser"/>
/// so they can find this laptop. Every connection runs <see cref="LinkResponder"/> on the listener's thread for it, and
/// only a link it lets through, a phone paired here or pairing now with the owner typing its code, is handed to
/// <paramref name="accept"/>. Everything else is closed. The tray decides when this runs, from <c>network.txt</c>.
/// </summary>
/// <param name="accept">Takes a link the phone has proved itself on; <c>SessionHost.Accept</c> in the app.</param>
/// <param name="onPaired">Told the phone's name when a pairing has just succeeded, before that link is handed on.</param>
internal sealed class NetworkServer(
    LinkIdentity identity,
    PairedLinks paired,
    TrustStore trust,
    IPairingPrompt prompt,
    Action<IPhoneLink> accept,
    Action<string> onPaired) : IDisposable
{
    // Outlives a stop and a start, so turning the listener off and on again does not lift a pairing lock.
    private readonly PairingLimiter limiter = new();

    // Taken around starting, stopping and handing a link on, so a link let through as the listener stops is either
    // closed by the stop or refused here, never handed on afterwards.
    private readonly Lock gate = new();

    // Links handed on, so stopping closes them too: the owner turning Wi-Fi and USB off means those phones as well.
    private readonly List<NetworkLink> live = [];

    private TcpLinkListener? listener;
    private MdnsAdvertiser? advertiser;

    // Cancelled by a stop, which withdraws a pairing question still open and refuses a link let through after it.
    // ponytail: never disposed, it has no timer or wait handle; one per start.
    private CancellationTokenSource? running;

    /// <summary>The port listened on, or null when not listening.</summary>
    public int? Port
    {
        get
        {
            lock (gate)
            {
                return listener?.Port;
            }
        }
    }

    /// <summary>
    /// Listens on <paramref name="port"/>, or on a free one when that is taken, and announces the laptop; whatever ran
    /// before is stopped first. False, logged, when no port would open.
    /// </summary>
    public bool Start(int port)
    {
        lock (gate)
        {
            StopLocked();
            var stopping = new CancellationTokenSource();
            var opened = new TcpLinkListener(port, (socket, done) => Serve(socket, done, stopping.Token));
            if (!opened.Start())
            {
                opened.Dispose();
                stopping.Dispose();
                return false;
            }

            if (opened.Port != port)
            {
                // mDNS carries the real port, so Wi-Fi still finds it; the USB probe only tries the usual one.
                Log.Write($"Listening for Wi-Fi and USB phones on port {opened.Port}, not {port}: a phone looking over USB tethering will not find this laptop");
            }
            else
            {
                Log.Write($"Listening for Wi-Fi and USB phones on port {port}");
            }

            // False is logged by the advertiser: the laptop is then not announced, and a phone can still reach it by address.
            var announcing = new MdnsAdvertiser(identity, (ushort)opened.Port);
            _ = announcing.Start();
            listener = opened;
            advertiser = announcing;
            running = stopping;
            return true;
        }
    }

    /// <summary>Stops listening and announcing, and closes the links it handed on.</summary>
    public void Stop()
    {
        lock (gate)
        {
            StopLocked();
        }
    }

    public void Dispose() => Stop();

    private void StopLocked()
    {
        if (listener is null)
        {
            return;
        }

        running?.Cancel();
        listener.Dispose();
        advertiser?.Dispose();
        foreach (var link in live)
        {
            link.Close();
        }

        live.Clear();
        listener = null;
        advertiser = null;
        running = null;
        Log.Write("Stopped listening for Wi-Fi and USB phones");
    }

    /// <summary>
    /// One connection, on the listener's thread for it. An exception goes back to the listener, which logs it and
    /// closes the socket; LinkResponder never puts a code or a key in one.
    /// </summary>
    private void Serve(Socket socket, Action done, CancellationToken stopping)
    {
        var raw = new NetworkStream(socket, ownsSocket: true);
        LinkResult? result = null;
        try
        {
            result = LinkResponder.Accept(raw, identity, paired, limiter, prompt, trust, Log.Write, socket.Dispose, stopping);
        }
        finally
        {
            if (result is null)
            {
                raw.Dispose();
            }
        }

        if (result is null)
        {
            return;
        }

        // Proved: the listener's read and write deadlines no longer apply; the session keeps its own.
        done();
        var link = new NetworkLink(socket, result);
        lock (gate)
        {
            if (stopping.IsCancellationRequested)
            {
                Log.Write($"Refused {result.Address}: Wi-Fi and USB connections were turned off");
                link.Release();
                return;
            }

            _ = live.RemoveAll(ended => ended.Closed);
            live.Add(link);
        }

        if (result.JustPaired)
        {
            onPaired(result.PhoneName);
        }

        // Catches everything itself, and closes the link on every path that does not hand it to a session.
        accept(link);
    }
}

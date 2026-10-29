using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Edgepad.Protocol;

namespace Edgepad.Net;

/// <summary>
/// Makes the laptop findable as <c>_edgepad._tcp</c> on the local network: a small mDNS responder on UDP 5353 for
/// this one service, shared by Windows and Linux (the Mac uses Bonjour). It announces twice a second apart, answers
/// queries for the service, its instance and its host, re-announces when the machine's addresses change, and says
/// goodbye on <see cref="Dispose"/>. Nothing here throws out of a thread: a failed packet is logged and the loop goes on.
/// </summary>
internal sealed class MdnsAdvertiser(LinkIdentity identity, ushort port) : IDisposable
{
    private const int MdnsPort = 5353;
    private const int MaxAddresses = 8;

    // Multicast answers closer together than this are dropped, as are unicast answers to one asker: a flood of
    // queries must not become a flood of ours.
    private const long MinReplyGapMs = 250;

    // How many askers' last unicast answer is remembered; past it the table starts again, which only lets an asker
    // through early, never a flood (each one is still on the LAN and still gapped from then on).
    private const int MaxAskers = 64;

    private static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    private static readonly IPEndPoint GroupEndPoint = new(Group, MdnsPort);

    private readonly CancellationTokenSource stop = new();
    private readonly Lock gate = new();
    private readonly HashSet<int> joined = [];
    private readonly MdnsService baseService = new(
        MdnsPacket.Label(identity.Name),
        "edgepad-" + identity.IdHex[..8],
        port,
        [
            $"id={identity.IdHex}",
            $"n={MdnsPacket.Truncate(identity.Name, 200)}",
            $"v={ProtocolConstants.Version}",
            $"lv={LinkWire.LinkVersion}",
        ],
        []);

    private Socket? socket;
    private Thread? receiver;
    private Thread? announcer;
    private (int Index, IPAddress Address, IPAddress Mask)[] interfaces = [];

    // Both are touched only by the receive thread, the one caller of Handle.
    private readonly Dictionary<IPAddress, long> lastUnicast = [];
    private long lastMulticast = long.MinValue / 2;
    private bool disposed;

    /// <summary>
    /// Opens the socket and starts announcing. False, logged, when UDP 5353 cannot be opened (another program holds it
    /// without sharing); the laptop is then simply not announced and a phone can still reach it by address.
    /// </summary>
    public bool Start()
    {
        lock (gate)
        {
            if (disposed || socket is not null)
            {
                return false;
            }

            var opened = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                opened.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                opened.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
                opened.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, false);
                opened.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            }
            catch (SocketException e)
            {
                Log.Write($"The laptop could not be announced on the network, UDP {MdnsPort} would not open: {e.Message}");
                opened.Dispose();
                return false;
            }

            socket = opened;
            Refresh();
            receiver = new Thread(() => Receive(opened)) { IsBackground = true, Name = "edgepad-mdns-receive" };
            announcer = new Thread(AnnounceTwice) { IsBackground = true, Name = "edgepad-mdns-announce" };
            receiver.Start();
            announcer.Start();
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            return true;
        }
    }

    public void Dispose()
    {
        Thread?[] threads;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            stop.Cancel();
            if (socket is not null)
            {
                Send(MdnsPacket.Build(Current(), goodbye: true, MdnsKinds.Ptr | MdnsKinds.Srv | MdnsKinds.Txt | MdnsKinds.A, MdnsKinds.None), null);
                socket.Dispose();
            }

            threads = [receiver, announcer];
        }

        foreach (var thread in threads)
        {
            if (thread is not null && thread != Thread.CurrentThread && !thread.Join(TimeSpan.FromSeconds(2)))
            {
                Log.Write($"The mDNS thread {thread.Name} did not stop within 2 s");
            }
        }

        stop.Dispose();
    }

    /// <summary>Every up, multicast-capable, non-loopback interface's first IPv4 address that is not link-local.</summary>
    internal static (int Index, IPAddress Address, IPAddress Mask)[] LocalAddresses()
    {
        var found = new List<(int, IPAddress, IPAddress)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || !nic.SupportsMulticast || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            try
            {
                var properties = nic.GetIPProperties();
                var index = properties.GetIPv4Properties().Index;
                foreach (var unicast in properties.UnicastAddresses)
                {
                    if (Advertisable(unicast.Address))
                    {
                        found.Add((index, unicast.Address, unicast.IPv4Mask));
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // An interface with no IPv4 stack, or one that went away while it was read: not one to announce on.
            }
        }

        return [.. found.Take(MaxAddresses)];
    }

    internal static bool Advertisable(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return !(bytes[0] == 169 && bytes[1] == 254) && !address.Equals(IPAddress.Any);
    }

    /// <summary>
    /// True when <paramref name="sender"/> is on the same subnet as one of <paramref name="local"/>'s addresses. A
    /// unicast answer goes only there: mDNS is link-local, and answering a forged off-link source would make this
    /// laptop reflect packets at a third machine.
    /// </summary>
    internal static bool OnLink(IPAddress sender, (int Index, IPAddress Address, IPAddress Mask)[] local)
    {
        if (sender.IsIPv4MappedToIPv6)
        {
            sender = sender.MapToIPv4();
        }

        if (sender.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var from = sender.GetAddressBytes();
        foreach (var (_, address, mask) in local)
        {
            var own = address.GetAddressBytes();
            var bits = mask.GetAddressBytes();
            var same = true;
            for (var i = 0; i < 4 && same; i++)
            {
                same = (from[i] & bits[i]) == (own[i] & bits[i]);
            }

            if (same)
            {
                return true;
            }
        }

        return false;
    }

    private MdnsService Current()
    {
        var current = interfaces;
        return baseService with { Addresses = [.. current.Select(i => i.Address)] };
    }

    /// <summary>Joins the multicast group on any interface not joined yet and takes the current address list.</summary>
    private void Refresh()
    {
        var local = LocalAddresses();
        lock (gate)
        {
            interfaces = local;
            foreach (var (index, _, _) in local)
            {
                if (socket is null || !joined.Add(index))
                {
                    continue;
                }

                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, index));
                }
                catch (SocketException e)
                {
                    joined.Remove(index);
                    Log.Write($"The mDNS group could not be joined on interface {index}: {e.Message}");
                }
            }
        }
    }

    private void Announce()
    {
        Send(MdnsPacket.Build(Current(), goodbye: false, MdnsKinds.Ptr, MdnsKinds.Srv | MdnsKinds.Txt | MdnsKinds.A), null);
    }

    private void AnnounceTwice()
    {
        try
        {
            Announce();
            if (!stop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1)))
            {
                Announce();
            }
        }
        catch (Exception e)
        {
            Log.Write($"The mDNS announcement failed: {e.Message}");
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        try
        {
            if (!stop.IsCancellationRequested)
            {
                Refresh();
                Announce();
            }
        }
        catch (Exception ex)
        {
            // A pool thread: an escape ends the process.
            Log.Write($"The mDNS announcement after a network change failed: {ex.Message}");
        }
    }

    private void Receive(Socket from)
    {
        var buffer = new byte[MdnsPacket.MaxPacket];
        while (!stop.IsCancellationRequested)
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                var length = from.ReceiveFrom(buffer, SocketFlags.None, ref remote);
                Handle(buffer.AsSpan(0, length), remote as IPEndPoint);
            }
            catch (Exception e)
            {
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                // Anything on the LAN can put anything in a datagram; none of it may end this thread or the process,
                // and none of it may pause the thread either, or a stream of bad packets would hold answers back.
                Log.Write($"An mDNS packet was dropped: {e.Message}");
            }
        }
    }

    private void Handle(ReadOnlySpan<byte> packet, IPEndPoint? sender)
    {
        if (!MdnsPacket.TryParse(packet, out var query) || query is null)
        {
            return;
        }

        var reply = MdnsPacket.Answer(Current(), query, oneShot: sender is not null && sender.Port != MdnsPort);
        if (reply is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (reply.Unicast && sender is not null)
        {
            if (!OnLink(sender.Address, interfaces))
            {
                return;
            }

            if (lastUnicast.TryGetValue(sender.Address, out var last) && now - last < MinReplyGapMs)
            {
                return;
            }

            if (lastUnicast.Count >= MaxAskers)
            {
                lastUnicast.Clear();
            }

            lastUnicast[sender.Address] = now;
            Send(reply.Packet, sender);
            return;
        }

        // The clock moves only when an answer goes out: moving it on every query would let a steady stream of
        // queries hold every answer back, and the phone would never find the laptop.
        if (now - lastMulticast >= MinReplyGapMs)
        {
            lastMulticast = now;
            Send(reply.Packet, null);
        }
    }

    /// <summary>To <paramref name="unicast"/>, or multicast out of each interface in turn.</summary>
    private void Send(byte[] packet, IPEndPoint? unicast)
    {
        lock (gate)
        {
            if (socket is null)
            {
                return;
            }

            try
            {
                if (unicast is not null)
                {
                    socket.SendTo(packet, unicast);
                    return;
                }

                foreach (var (index, address, _) in interfaces.DistinctBy(i => i.Index))
                {
                    try
                    {
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                        socket.SendTo(packet, GroupEndPoint);
                    }
                    catch (SocketException e)
                    {
                        Log.Write($"An mDNS packet could not be sent on interface {index}: {e.Message}");
                    }
                }
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                Log.Write($"An mDNS packet could not be sent: {e.Message}");
            }
        }
    }
}

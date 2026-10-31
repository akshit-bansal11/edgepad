using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Edgepad.Net;
using Edgepad.Network;
using Edgepad.Protocol;
using Edgepad.Trust;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The Wi-Fi and USB server on a loopback port: <see cref="LinkInitiator"/> pairs as the phone does, the owner types the
/// code it shows, and the link the server hands on carries the phone's HELLO through the handshake to a HELLO_ACK.
/// Not covered, and not coverable on a CI runner: the firewall prompt, a phone finding the laptop over mDNS, and
/// USB tethering's routing.
/// </summary>
public sealed class NetworkServerTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly byte[] PhoneId = [.. Enumerable.Range(0xA0, 16).Select(i => (byte)i)];
    private static readonly byte[] LaptopId = [.. Enumerable.Range(0x80, 16).Select(i => (byte)i)];

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TrustStore trust;
    private readonly PinStore pin;
    private readonly PairedLinks paired;
    private readonly Owner owner = new();
    private readonly ConcurrentQueue<string> pairedNames = new();
    private readonly TaskCompletionSource<(IPhoneLink Link, bool Admitted)> handedOn = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource ended = new();
    private readonly NetworkServer server;

    public NetworkServerTests()
    {
        trust = new TrustStore(Path.Combine(dir, "trusted-phone.txt"));
        pin = new PinStore(dir);
        paired = new PairedLinks(Path.Combine(dir, "paired-links.txt"));
        server = new NetworkServer(new LinkIdentity(LaptopId, "Laptop"), paired, trust, owner, Serve, pairedNames.Enqueue, () => false);
    }

    public void Dispose()
    {
        server.Dispose();
        ended.Cancel();
        ended.Dispose();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }

    /// <summary>What SessionHost's session does first with a link it is given: the handshake, at level 1.</summary>
    private void Serve(IPhoneLink link)
    {
        var frames = new FrameStream(link.Input, link.Output);
        var admitted = Handshake.Run(frames, link.Address, trust, pin, (_, _) => false, () => true, _ => { }, _ => { }, link.Close, ended.Token);
        handedOn.TrySetResult((link, admitted));
    }

    private async Task<TcpClient> ConnectAsync()
    {
        var port = server.Port;
        Assert.NotNull(port);
        var client = new TcpClient { ReceiveTimeout = (int)Patience.TotalMilliseconds, SendTimeout = (int)Patience.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port.Value, TestContext.Current.CancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    [Fact]
    public async Task APhonePairedByTheOwnerIsHandedOnAndAnsweredWithHelloAck()
    {
        Assert.True(server.Start(0));
        using var client = await ConnectAsync();

        var phone = await Task.Run(() => LinkInitiator.Pair(client.GetStream(), PhoneId, "Pixel", owner.PhoneShows), TestContext.Current.CancellationToken)
            .WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(LinkWire.StatusOk, phone.Status);
        Assert.NotNull(phone.Secure);
        using var secure = phone.Secure;

        var frames = new FrameStream(secure, secure);
        frames.Send(new Hello(ProtocolConstants.Version));
        Assert.Equal(ProtocolConstants.Version, Assert.IsType<HelloAck>(frames.ReadFrame()).Version);

        var (link, admitted) = await handedOn.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.True(admitted);
        Assert.Equal(LinkKind.Network, link.Kind);
        Assert.Equal("Pixel", link.Label);
        Assert.Equal(LinkResponder.AddressOf(PhoneId), link.Address);
        Assert.Equal("Pixel", Assert.Single(pairedNames));
        link.Release();
    }

    [Fact]
    public async Task APhoneTheOwnerNeverTypedForIsNotHandedOn()
    {
        owner.Cancels = true;
        Assert.True(server.Start(0));
        using var client = await ConnectAsync();

        // The laptop closes the link; the phone may read that as an end or, on a socket, as a reset.
        Stream? secure;
        try
        {
            var phone = await Task.Run(() => LinkInitiator.Pair(client.GetStream(), PhoneId, "Rogue", _ => { }), TestContext.Current.CancellationToken)
                .WaitAsync(Patience, TestContext.Current.CancellationToken);
            secure = phone.Secure;
        }
        catch (IOException)
        {
            secure = null;
        }

        Assert.Null(secure);
        Assert.False(handedOn.Task.IsCompleted);
        Assert.Empty(pairedNames);
    }

    [Fact]
    public async Task StoppingClosesThePortAndStartingAgainOpensOne()
    {
        Assert.True(server.Start(0));
        var first = server.Port;
        Assert.NotNull(first);

        server.Stop();
        Assert.Null(server.Port);
        using (var refused = new TcpClient())
        {
            await Assert.ThrowsAnyAsync<SocketException>(
                () => refused.ConnectAsync(IPAddress.Loopback, first.Value, TestContext.Current.CancellationToken).AsTask());
        }

        Assert.True(server.Start(0));
        using var client = await ConnectAsync();
        Assert.True(client.Connected);
    }

    /// <summary>The owner at the laptop: types whatever code the phone showed, or cancels the question.</summary>
    private sealed class Owner : IPairingPrompt
    {
        private readonly TaskCompletionSource<string> code = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancels { get; set; }

        public void PhoneShows(string digits) => code.TrySetResult(digits);

        public IPairingQuestion Show(string phoneName, Action<string> onTyped, Action onOwnerCancelled)
        {
            if (Cancels)
            {
                onOwnerCancelled();
            }
            else
            {
                _ = TypeWhenShownAsync(onTyped);
            }

            return new Withdrawn();
        }

        private async Task TypeWhenShownAsync(Action<string> onTyped) => onTyped(await code.Task.ConfigureAwait(false));
    }

    // Nothing on screen to tell or take down.
    private sealed class Withdrawn : IPairingQuestion
    {
        public void Matched()
        {
        }

        public void Mismatched()
        {
        }

        public void Finished(bool paired)
        {
        }

        public void Dispose()
        {
            // Nothing on screen to take down.
        }
    }
}

using System.Net.Sockets;
using System.Text;
using Edgepad.Dashboard;
using Edgepad.Hooks;
using Xunit;

namespace Edgepad.Tests;

public sealed class HookListenerTests : IDisposable
{
    private const string Token = "tok_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abcdefg";
    private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-hook");

    public void Dispose() => dir.Delete(recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(dir.FullName, "edgepad");

    [Fact]
    public void NoRuntimeFolderMeansNoListener()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Linux app's listener");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Assert.Null(LinuxHookListener.Open(null));
    }

    [Fact]
    public void AFolderWithTheWrongModeIsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix modes");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Directory.CreateDirectory(Folder, Private);
        File.SetUnixFileMode(Folder, Private | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Assert.Null(LinuxHookListener.Open(Folder));
        Assert.False(File.Exists(Path.Combine(Folder, UnixSocketListener.SocketName)));
    }

    [Fact]
    public async Task APeerOfAnotherUserIsClosedUnread()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "SO_PEERCRED is Linux's");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var listener = LinuxHookListener.Open(Folder, _ => 4242, myUid: 1000);
        Assert.NotNull(listener);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var accept = listener.AcceptAsync(stop.Token);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(listener.Endpoint), Ct);
        client.ReceiveTimeout = 5_000;
        Assert.Equal(0, client.Receive(new byte[1]));
        Assert.False(accept.IsCompleted);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => accept);
    }

    [Fact]
    public async Task AWrongTokenGetsNoReplyAndTheRightOneDoes()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix sockets with SO_PEERCRED");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var server = Serve(Folder, out var endpoint);
        Assert.Null(await Exchange(endpoint, Request("tok_wrong_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")));
        Assert.Equal("""{"v":1,"ok":true}""", await Exchange(endpoint, Request(Token)));
    }

    [Fact]
    public async Task AFolderWithSpacesWorks()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Unix sockets with SO_PEERCRED");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var spaced = Path.Combine(dir.FullName, "run user", "edge pad");
        Directory.CreateDirectory(Path.GetDirectoryName(spaced)!);
        using var server = Serve(spaced, out var endpoint);
        Assert.Equal(Path.Combine(spaced, UnixSocketListener.SocketName), endpoint);
        Assert.Equal("""{"v":1,"ok":true}""", await Exchange(endpoint, Request(Token)));
    }

    private static string Request(string token) =>
        $$$"""{"v":1,"token":"{{{token}}}","agent":"claude","kind":"session","d":{"hook_event_name":"SessionStart","session_id":"s1","cwd":"/work/app"}}""";

    // A hook server over the Linux listener, Claude switched on.
    private HookServer Serve(string folder, out string endpoint)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        var settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        settings.SetFlag(IntegrationSettings.Claude, true);
        var usage = new UsageStore(Path.Combine(dir.FullName, UsageStore.FileName), DateTimeOffset.UtcNow);
        var sessions = new SessionBoard(() => DateTimeOffset.UtcNow);
        var broker = new PermissionBroker(sessions, () => false);
        var listener = LinuxHookListener.Open(folder) ?? throw new IOException("no listener");
        endpoint = listener.Endpoint;
        var server = new HookServer(listener, () => Token, broker, sessions, new LimitsBoard(usage), new SpendBoard(usage), usage, settings);
        server.Start();
        return server;
    }

    // One request line; the reply line, or null when the server closed without one.
    private static async Task<string?> Exchange(string endpoint, string line)
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), Ct);
        client.ReceiveTimeout = 10_000;
        try
        {
            client.Send(Encoding.UTF8.GetBytes(line + "\n"));
        }
        catch (SocketException)
        {
            return null;
        }

        var reply = new List<byte>();
        var one = new byte[1];
        try
        {
            while (client.Receive(one) == 1)
            {
                if (one[0] == '\n')
                {
                    return Encoding.UTF8.GetString([.. reply]);
                }

                reply.Add(one[0]);
            }
        }
        catch (SocketException)
        {
            // Reset by the server: the same as closed.
        }

        return null;
    }
}

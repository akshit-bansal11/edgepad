using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Edgepad.Dashboard;
using Edgepad.Hooks;
using Xunit;

namespace Edgepad.Tests;

public sealed class HookListenerTests : IDisposable
{
    private const string Token = "right-token";
    private static readonly SecurityIdentifier Network = new(WellKnownSidType.NetworkSid, null);

    private readonly string pipe = "edgepad-hook-test-" + Guid.NewGuid().ToString("N");
    private readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("edgepad-hook-");

    public void Dispose() => dir.Delete(recursive: true);

    private static List<PipeAccessRule> Rules(PipeSecurity acl) =>
        [.. acl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>()];

    [Fact]
    public void TheDaclAllowsOnlyTheCurrentUserAndDeniesNetwork()
    {
        var me = WindowsHookListener.CurrentUser();
        var acl = WindowsHookListener.Security(me);
        var rules = Rules(acl);

        var allow = Assert.Single(rules, r => r.AccessControlType == AccessControlType.Allow);
        Assert.Equal(me, allow.IdentityReference);
        var deny = Assert.Single(rules, r => r.AccessControlType == AccessControlType.Deny);
        Assert.Equal(Network, deny.IdentityReference);
        Assert.Equal(2, rules.Count);
        Assert.True(acl.AreAccessRulesProtected);
        Assert.Equal(me, acl.GetOwner(typeof(SecurityIdentifier)));
    }

    [Theory]
    [InlineData("S-1-5-21-1111111111-2222222222-3333333333-1001")]
    [InlineData("S-1-1-0")]
    [InlineData("S-1-5-11")]
    [InlineData("S-1-5-32-545")]
    [InlineData("S-1-5-32-544")]
    public void NoOtherUserOrGroupIsAllowed(string sid)
    {
        var rules = Rules(WindowsHookListener.Security(WindowsHookListener.CurrentUser()));
        Assert.DoesNotContain(rules, r => r.AccessControlType == AccessControlType.Allow && r.IdentityReference.Equals(new SecurityIdentifier(sid)));
    }

    [Fact]
    public async Task TheLivePipeCarriesThatDacl()
    {
        using var listener = new WindowsHookListener(pipe);
        Assert.Equal(@"\\.\pipe\" + pipe, listener.Endpoint);

        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = listener.AcceptAsync(TestContext.Current.CancellationToken);
        await client.ConnectAsync(5000, TestContext.Current.CancellationToken);
        await using var served = Assert.IsAssignableFrom<NamedPipeServerStream>(await accept);

        var rules = Rules(served.GetAccessControl());
        var allow = Assert.Single(rules, r => r.AccessControlType == AccessControlType.Allow);
        Assert.Equal(WindowsHookListener.CurrentUser(), allow.IdentityReference);
        Assert.Contains(rules, r => r.AccessControlType == AccessControlType.Deny && r.IdentityReference.Equals(Network));
    }

    [Fact]
    public void ASecondListenerOnTheSameNameIsRefused()
    {
        using var first = new WindowsHookListener(pipe);
        var refused = Record.Exception(() => new WindowsHookListener(pipe).Dispose());
        Assert.True(refused is IOException or UnauthorizedAccessException, refused?.GetType().Name ?? "no exception");
    }

    [Fact]
    public async Task AcceptReturnsNullOnceDisposed()
    {
        var listener = new WindowsHookListener(pipe);
        listener.Dispose();
        Assert.Null(await listener.AcceptAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheRightTokenIsAnsweredAndAWrongOneIsClosedUnanswered()
    {
        var settings = new IntegrationSettings(Path.Combine(dir.FullName, IntegrationSettings.FileName));
        var usage = new UsageStore(Path.Combine(dir.FullName, UsageStore.FileName), DateTimeOffset.UtcNow);
        var sessions = new SessionBoard(() => DateTimeOffset.UtcNow);
        var broker = new PermissionBroker(sessions, () => false);
        using var server = new HookServer(
            new WindowsHookListener(pipe), () => Token, broker, sessions, new LimitsBoard(usage), new SpendBoard(usage), usage, settings);
        server.Start();

        // Claude is off, so a good request is answered "defer" at once.
        Assert.Contains("\"defer\"", await Ask(Token), StringComparison.Ordinal);
        Assert.Equal(string.Empty, await Ask("wrong-token"));
        Assert.Equal(string.Empty, await Ask(Token[..^1] + "N"));
    }

    private async Task<string> Ask(string token)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000, ct);
        var line = "{\"v\":1,\"token\":\"" + token + "\",\"agent\":\"claude\",\"kind\":\"perm\",\"d\":{\"session_id\":\"s1\",\"cwd\":\"C:/w\",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"ls\"}}}\n";
        await client.WriteAsync(Encoding.UTF8.GetBytes(line), ct);
        await client.FlushAsync(ct);

        using var reply = new MemoryStream();
        var buffer = new byte[256];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        int read;
        while ((read = await client.ReadAsync(buffer, deadline.Token)) > 0)
        {
            reply.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(reply.ToArray());
    }
}

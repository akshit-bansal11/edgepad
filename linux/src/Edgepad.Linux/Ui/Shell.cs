using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Edgepad.Bluetooth;
using Edgepad.Net;
using Edgepad.Network;
using Edgepad.Trust;

namespace Edgepad.Ui;

/// <summary>
/// What the tray does: shows the status, asks the trust question, opens the Security window, forgets phones and
/// quits. Owner dialogs that change security run under the server's HoldOff/Resume with try/finally, because a
/// connected phone is a keyboard: it could press Enter on the confirmation, or change the level it is held by,
/// and a trusted one reconnects within seconds. Everything that touches a window runs on the UI thread; the
/// Bluetooth threads reach it only through <see cref="AskTrust"/> and <see cref="SetStatus"/>.
/// </summary>
internal sealed class Shell : IDisposable
{
    /// <summary>
    /// How long after a Forget every connection is refused. It outlasts the phone's own automatic retries, so
    /// the trust question can only come back from someone deliberately choosing this laptop on the phone.
    /// </summary>
    private static readonly TimeSpan QuietAfterForget = TimeSpan.FromSeconds(30);

    private readonly TrustStore trust;
    private readonly PinStore pin;
    private readonly string version;
    private readonly TrustPrompt prompt;
    private readonly NetworkSettings networkSettings;
    private readonly PairedLinks paired;
    private readonly LinkIdentity identity;
    private readonly PairingPrompt pairing;

    private LinuxServer? server;
    private NetworkServer? network;
    private Tray? tray;
    private IClassicDesktopStyleApplicationLifetime? lifetime;

    // Set once the tray exists. Read on Bluetooth threads, so volatile; the trust question is refused until then.
    private volatile bool ready;
    private volatile bool quitRequested;
    private volatile string status = "Starting";

    // The tray menu stays clickable under a dialog, and a second one would lift the hold while the first was still
    // open. All of this runs on the UI thread, so plain bools are enough.
    private bool forgetting;
    private bool securing;

    // The Wi-Fi and USB row's hint: off, the port listened on, or why not. Only the UI thread touches it.
    private string networkHint = "Off";

    public Shell(TrustStore trust, PinStore pin, NetworkSettings networkSettings, PairedLinks paired, LinkIdentity identity, string version)
    {
        this.trust = trust;
        this.pin = pin;
        this.networkSettings = networkSettings;
        this.paired = paired;
        this.identity = identity;
        this.version = version;
        pairing = PairingPrompt.OnUiThread(phone => SetStatus($"Pair {phone}: type the code shown on the phone"));
        prompt = new TrustPrompt(
            (address, stillWaiting) => Dispatcher.UIThread.InvokeAsync<bool>(() => TrustWindow.Open(address, stillWaiting)),
            TrustPrompt.DefaultTimeout);
    }

    private LinuxServer Server => server ?? throw new InvalidOperationException("The Bluetooth server was never attached");

    /// <summary>Hands over the server the dialogs hold off. Before <see cref="Run"/>.</summary>
    public void Attach(LinuxServer bluetooth)
    {
        server = bluetooth;

        // Off until the owner turns it on (network.txt); only a phone that proves itself is handed to the server.
        network = new NetworkServer(identity, paired, trust, pairing, link => _ = bluetooth.Accept(link), name => SetStatus($"Paired with {name}"), () => bluetooth.IsRefusing);
    }

    /// <summary>Stops listening for Wi-Fi and USB phones and closes theirs. Before the server is disposed; harmless twice.</summary>
    public void StopNetwork() => network?.Dispose();

    /// <summary>Stops the network server if <see cref="StopNetwork"/> has not already; Program calls that earlier, before the server goes.</summary>
    public void Dispose() => network?.Dispose();

    /// <summary>Runs the UI on the calling thread until Quit; returns Avalonia's exit code. Throws if no display can be opened.</summary>
    public int Run() =>
        AppBuilder.Configure(() => new App(Start)).UsePlatformDetect().StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);

    /// <summary>Ends the UI loop from any thread (a signal, the Quit row). Harmless before it started or after it ended.</summary>
    public void RequestQuit()
    {
        quitRequested = true;
        if (ready)
        {
            Dispatcher.UIThread.Post(() => lifetime?.Shutdown());
        }
    }

    /// <summary>The status line, from any thread. Before the tray exists it is only remembered.</summary>
    public void SetStatus(string text)
    {
        status = text;
        Log.Write($"Status: {text}");
        if (ready)
        {
            Dispatcher.UIThread.Post(() => tray?.SetStatus(text));
        }
    }

    /// <summary>
    /// Asks the owner whether to trust <paramref name="address"/>, and waits for the answer. Called on a session
    /// thread, which holds the phone's HELLO_ACK until this returns. No answer, or no UI to ask on, is a no.
    /// A session that ends meanwhile (<paramref name="ended"/>: a hold, a hang-up) is a no too, and closes the
    /// question. The phone is named by its address, as the status line names it once it has connected.
    /// </summary>
    public bool AskTrust(string address, CancellationToken ended)
    {
        if (!ready)
        {
            Log.Write($"Not trusting {address}: there is no UI to ask on (is a display available?)");
            return false;
        }

        SetStatus($"Asking whether to trust {Server.Shown(address)}");
        var trusted = prompt.Ask(address, ended);
        Log.Write(trusted ? $"The owner trusted {address}" : $"The owner did not trust {address}");
        return trusted;
    }

    private void Start(App app)
    {
        lifetime = app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var menu = new Tray(
            version,
            () => $"Level {pin.Level}",
            () => trust.CanForget || paired.CanForget,
            () => networkSettings.Load().Enabled,
            () => networkHint,
            () => _ = Guard(ShowSecurityAsync),
            () => _ = Guard(ToggleNetworkAsync),
            () => _ = Guard(ConfirmForgetAsync),
            RequestQuit);
        tray = menu;
        menu.SetStatus(status);
        menu.Attach(app);
        if (lifetime is not null)
        {
            lifetime.Exit += (_, _) => menu.Dispose();
        }

        ready = true;
        ApplyNetwork();

        // The hidden overlay, the screen size and whether annotate is offered: only knowable once the UI is up.
        Annotator.StartUi();

        // A signal that arrived while Avalonia was still starting.
        if (quitRequested)
        {
            RequestQuit();
        }

        Log.Write("Tray ready. Windows are not assumed to stay on top (Wayland ignores it), so nothing relies on it.");
    }

    /// <summary>The Security window, with connections refused while it is open.</summary>
    private async Task ShowSecurityAsync()
    {
        if (securing)
        {
            return;
        }

        securing = true;
        var bluetooth = Server;
        bluetooth.HoldOff();
        network?.DropPending();
        try
        {
            var window = new SecurityWindow(pin, trust, ForgetPairedLinksAsync, () => bluetooth.HangUp(QuietAfterForget));
            window.Show();
            await window.Closed;
        }
        finally
        {
            bluetooth.Resume();
            securing = false;
        }
    }

    /// <summary>
    /// Forgets the Wi-Fi and USB pairings, and tells the owner when they could not be. Every way of forgetting phones
    /// (Forget trusted phones, Reset PIN in the Security window) goes through here.
    /// </summary>
    private async Task ForgetPairedLinksAsync()
    {
        if (paired.Forget())
        {
            return;
        }

        await Dialogs.Inform(
            null,
            "The Wi-Fi and USB pairings could not be forgotten: their file could not be removed or emptied. "
                + "Paired phones cannot reconnect over Wi-Fi or USB while Edgepad stays running, but they will "
                + "be able to again the next time it starts. Choose Forget trusted phones again, or delete "
                + $"paired-links.txt yourself from:\n\n{pin.Folder}");
    }

    /// <summary>
    /// Forgets every trusted phone once the owner says so. Asked first because the menu cannot undo it: each
    /// phone has to be trusted again, the one connected included. Connections are refused while the question is
    /// open; a yes forgets and keeps refusing for the quiet period, and anything else takes connections again.
    /// </summary>
    private async Task ConfirmForgetAsync()
    {
        if (forgetting)
        {
            return;
        }

        forgetting = true;
        var bluetooth = Server;
        bluetooth.HoldOff();
        network?.DropPending();
        try
        {
            var phones = string.Join(", ", trust.TrustedPhones);
            var yes = await Dialogs.Confirm(
                null,
                (phones.Length > 0 ? $"Forget every trusted phone ({phones})?" : "Forget the trusted phones?")
                    + " Each phone, this one included, will have to be trusted here again before it can use this laptop.",
                "Forget",
                "Don't forget",
                dangerous: true);
            if (!yes)
            {
                return;
            }

            var forgotten = trust.Forget();

            // A forgotten phone that had entered the PIN would otherwise skip it at level 2 after one mistaken yes.
            var verifiedForgotten = pin.ForgetVerified();

            // Started before the hold ends, so there is no instant in which a connection would be taken.
            bluetooth.HangUp(QuietAfterForget);
            if (!forgotten)
            {
                await Dialogs.Inform(
                    null,
                    "The trusted phones could not be forgotten: their file could not be removed or emptied. No phone can "
                        + "connect while Edgepad stays running, but those phones will be trusted again the next time "
                        + "Edgepad starts. Close whatever is holding the file and choose Forget trusted phones again, "
                        + $"or delete it yourself:\n\n{trust.FilePath}");
            }

            // The Wi-Fi and USB pairings too, or a forgotten phone would resume its link and only meet the trust
            // question, not a new pairing.
            await ForgetPairedLinksAsync();

            if (!verifiedForgotten)
            {
                await Dialogs.Inform(
                    null,
                    "The phones that entered the PIN could not be forgotten: their list could not be removed. While "
                        + "Edgepad stays running every phone is asked for the PIN on every connection, but those phones "
                        + "will skip it at level 2 again the next time Edgepad starts. Choose Forget trusted phones "
                        + $"again, or delete pin-verified.txt yourself from:\n\n{pin.Folder}");
            }
        }
        finally
        {
            bluetooth.Resume();
            forgetting = false;
        }
    }

    /// <summary>
    /// The Wi-Fi and USB switch. Turning it on says first what will happen, since a firewall may block it; a no there
    /// leaves it off. A setting that cannot be saved leaves things as they were.
    /// </summary>
    private async Task ToggleNetworkAsync()
    {
        var on = !networkSettings.Load().Enabled;
        if (on && !await Dialogs.Confirm(
            null,
            "Phones on the same Wi-Fi, or connected by USB tethering, will be able to find this laptop and ask to pair. "
                + "Pairing needs you to type here the code the phone shows. A firewall may block them: allow "
                + $"{NetworkSettings.DefaultPort}/tcp and 5353/udp (docs/LINUX.md).",
            "Turn on",
            "Cancel"))
        {
            return;
        }

        if (!networkSettings.SetEnabled(on))
        {
            await Dialogs.Inform(null, "The setting could not be saved, so nothing was changed. The log has the reason.");
            return;
        }

        ApplyNetwork();
    }

    /// <summary>Listens or stops as network.txt says, and words the row's hint for it. On the UI thread only.</summary>
    private void ApplyNetwork()
    {
        var net = network ?? throw new InvalidOperationException("The network server was never created");
        var config = networkSettings.Load();
        if (!config.Enabled)
        {
            net.Stop();
            networkHint = "Off";
        }
        else
        {
            networkHint = !net.Start(config.Port) ? "Could not listen; see the log"
                : net.Port == config.Port ? $"Listening on {config.Port}"
                : $"Port in use, using {net.Port}";
        }

        tray?.Refresh();
    }

    /// <summary>Nothing may throw out of a menu handler: an exception on the UI thread ends the whole app.</summary>
    private static async Task Guard(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            Log.Write($"A tray action failed: {e}");
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Edgepad.Bluetooth;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Network;
using Edgepad.Platform;
using Edgepad.Trust;
using Edgepad.Updates;

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

    private static readonly string NetworkNotice = $"{FirstRunWindow.NetworkBody} {FirstRunWindow.Firewall}";

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
    private FallbackWindow? fallback;
    private Timer? updateTimer;
    private IClassicDesktopStyleApplicationLifetime? lifetime;
    private readonly Autostart autostart = Autostart.ForCurrentUser();
    private readonly UpdateSetting updateSetting = UpdateSetting.ForCurrentUser();

    // Set once the tray exists. Read on Bluetooth threads, so volatile; the trust question is refused until then.
    private volatile bool ready;
    private volatile bool quitRequested;
    private volatile string status = LaptopStatus.Starting.Text;

    // Statuses arrive on session threads; the lock in SetStatus guards it.
    private readonly StatusMemory memory = new();

    // /dev/uinput cannot be opened: input would do nothing while the phone shows connected. Set from any thread.
    private volatile bool inputBlocked;
    private bool updateNotified;

    // The tray menu stays clickable under a dialog, and a second one would lift the hold while the first was still
    // open. All of this runs on the UI thread, so plain bools are enough.
    private bool forgetting;
    private bool securing;
    private bool editingMacros;

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
        pairing = PairingPrompt.OnUiThread(phone =>
        {
            SetStatus(LaptopStatus.Pairing(phone));
            Notifier.Send($"{phone} wants to pair", "Type the pairing code it shows.");
        });
        prompt = new TrustPrompt(
            (address, stillWaiting) => Dispatcher.UIThread.InvokeAsync<bool>(() => TrustWindow.Open(address, stillWaiting)),
            TrustPrompt.DefaultTimeout);
    }

    private LinuxServer Server => server ?? throw new InvalidOperationException("The Bluetooth server was never attached");

    /// <summary>Hands over the server the dialogs hold off. Before <see cref="Run"/>.</summary>
    public void Attach(LinuxServer bluetooth)
    {
        server = bluetooth;

        // Off until the owner says yes (network.txt); only a phone that proves itself is handed to the server.
        network = new NetworkServer(identity, paired, trust, pairing, link => _ = bluetooth.Accept(link), name => SetStatus(LaptopStatus.Paired(name)), () => bluetooth.IsRefusing);
    }

    /// <summary>Stops listening for Wi-Fi and USB phones and closes theirs. Before the server is disposed; harmless twice.</summary>
    public void StopNetwork() => network?.Dispose();

    /// <summary>Stops the network server if <see cref="StopNetwork"/> has not already; Program calls that earlier, before the server goes.</summary>
    public void Dispose()
    {
        updateTimer?.Dispose();
        network?.Dispose();
    }

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
    public void SetStatus(LaptopStatus update)
    {
        // A refusal is followed within microseconds by "waiting" when its session ends; the memory keeps the refusal on screen.
        LaptopStatus shown;
        bool announce;
        lock (memory)
        {
            var now = Environment.TickCount64;
            announce = memory.ShouldAnnounce(update, now);
            shown = memory.Resolve(update, now);
        }

        if (announce)
        {
            Notifier.Send("Edgepad", update.Text);
        }

        var text = shown.Text;

        // "Connected to" is only true if the phone's input can do anything: check each time, as the log said only at the end.
        if (shown.IsConnected)
        {
            CheckInput();
            if (inputBlocked)
            {
                text = UinputAccess.Blocked;
            }
        }

        status = text;
        Log.Write($"Status: {update.Text}");
        if (ready)
        {
            Dispatcher.UIThread.Post(() =>
            {
                tray?.SetStatus(text);
                fallback?.SetStatus(text);
                ShowProblem();
            });
        }
    }

    /// <summary>Probes /dev/uinput and remembers the answer; says so once, in a notification, when it turns bad.</summary>
    private void CheckInput()
    {
        var problem = UinputAccess.Check();
        var was = inputBlocked;
        inputBlocked = problem is not null;
        if (problem is not null && !was)
        {
            Log.Write($"Input is blocked: {problem}");
            Notifier.Send("Edgepad cannot control this laptop", UinputAccess.BlockedDetail);
        }
    }

    /// <summary>The problem row, on the UI thread: input blocked first, then a Wi-Fi listener that would not start.</summary>
    private void ShowProblem()
    {
        var text = inputBlocked ? UinputAccess.Blocked : networkHint.StartsWith("Could not", StringComparison.Ordinal) ? "Wi-Fi and USB connections could not start; see the log" : null;
        tray?.SetProblem(text);
        fallback?.SetProblem(text);
    }

    private void RefreshViews()
    {
        tray?.Refresh();
        fallback?.Refresh();
    }

    /// <summary>
    /// Asks the owner whether to trust <paramref name="address"/>, and waits for the answer. Called on a session
    /// thread, which holds the phone's HELLO_ACK until this returns. No answer, or no UI to ask on, is a no.
    /// A session that ends meanwhile (<paramref name="ended"/>: a hold, a hang-up) is a no too, and closes the
    /// question. The phone is named by its Bluetooth name where BlueZ has one, which the window reads from the cache
    /// this fills; the address is the fallback and stays in the log.
    /// </summary>
    public bool AskTrust(string address, CancellationToken ended)
    {
        if (!ready)
        {
            Log.Write($"Not trusting {address}: there is no UI to ask on (is a display available?)");
            return false;
        }

        var name = PhoneName.Of(address) ?? Server.Shown(address);
        SetStatus(LaptopStatus.Asking(name));

        // The question is a window that may open behind the one in use (Wayland), and the tray menu may not exist.
        Notifier.Send($"{name} wants to connect", "A window is asking whether to trust it. Look for it on your screen.");
        var trusted = prompt.Ask(address, ended);
        Log.Write(trusted ? $"The owner trusted {address}" : $"The owner did not trust {address}");
        return trusted;
    }

    private void Start(App app)
    {
        lifetime = app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var hooks = new TrayHooks(
            () => SecurityWindow.LevelNames[Math.Clamp(pin.Level, 1, SecurityWindow.LevelNames.Length) - 1],
            () => $"{LinuxPlatform.Shared.Macros.Count} of {MacroStore.MaxMacros}",
            () => networkSettings.Load().Enabled,
            () => networkHint,
            () => ClipboardSetting.Enabled,
            () => autostart.Enabled,
            () => updateSetting.Enabled,
            () => _ = Guard(ShowSecurityAsync),
            () => _ = Guard(ShowMacrosAsync),
            () => _ = Guard(ToggleNetworkAsync),
            () => _ = Guard(ToggleClipboardAsync),
            () => _ = Guard(ToggleAutostartAsync),
            () => _ = Guard(ToggleUpdatesAsync),
            RequestQuit);
        var menu = new Tray(hooks);
        tray = menu;
        menu.SetStatus(status);
        menu.Attach(app);
        if (lifetime is not null)
        {
            lifetime.Exit += (_, _) => menu.Dispose();
        }

        ready = true;
        CheckInput();
        ApplyNetwork();
        ShowProblem();
        _ = Guard(() => ShowFallbackIfNoTrayAsync(hooks));

        // Once now and then daily, as on Windows and the Mac, unless the owner turned it off.
        ApplyUpdateTimer();
        if (!networkSettings.HasChoice || !ClipboardSetting.HasChoice)
        {
            // Posted, so the tray is up and running first.
            Dispatcher.UIThread.Post(() => _ = Guard(AskFirstRunAsync));
        }

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
            var window = new SecurityWindow(pin, trust, paired, ForgetPairedLinksAsync, ConfirmForgetAsync, () => bluetooth.HangUp(QuietAfterForget));
            window.Show();
            await window.Closed;
        }
        finally
        {
            bluetooth.Resume();
            securing = false;
            RefreshViews();
        }
    }

    /// <summary>
    /// Forgets the Wi-Fi and USB pairings, and tells the owner when they could not be. Every way of forgetting phones
    /// (Forget all… and Reset PIN, both in the Security window) goes through here.
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
                + "be able to again the next time it starts. Choose Forget all… in Security… again, or delete "
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
                "Cancel",
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
            lock (memory)
            {
                memory.Clear();
            }

            if (!forgotten)
            {
                await Dialogs.Inform(
                    null,
                    "The trusted phones could not be forgotten: their file could not be removed or emptied. No phone can "
                        + "connect while Edgepad stays running, but those phones will be trusted again the next time "
                        + "Edgepad starts. Close whatever is holding the file and choose Forget all… in Security… again, "
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
                        + "will skip it at level 2 again the next time Edgepad starts. Choose Forget all… in Security… "
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
    /// leaves it off.
    /// </summary>
    private async Task ToggleNetworkAsync()
    {
        var on = !networkSettings.Load().Enabled;
        if (on && !await Dialogs.Confirm(null, NetworkNotice, "Turn on", "Cancel"))
        {
            // The check box changed on the click; put it back.
            RefreshViews();
            return;
        }

        await SaveNetworkAsync(on);
    }

    /// <summary>
    /// The Share clipboard switch. Off stops every read, send and apply at once; the phones are told again, since the
    /// feature token follows the switch.
    /// </summary>
    private Task ToggleClipboardAsync() => ApplyClipboardAsync(!ClipboardSetting.Enabled);

    private async Task ApplyClipboardAsync(bool on)
    {
        var saved = ClipboardSetting.Set(on);
        Annotator.FeaturesChanged();
        if (!saved)
        {
            await Dialogs.Inform(null, "This applies now, but it could not be saved, so it will go back after a restart. The log has the reason.");
        }

        RefreshViews();
    }

    /// <summary>Start at login: writes or removes the autostart entry, and says so when it could not.</summary>
    private Task ToggleAutostartAsync() => ApplyAutostartAsync(!autostart.Enabled);

    private async Task ApplyAutostartAsync(bool on)
    {
        if (!autostart.Set(on))
        {
            await Dialogs.Inform(null, "Start at login could not be changed. The log has the reason.");
        }

        RefreshViews();
    }

    /// <summary>
    /// The Check for updates daily switch. Off stops the timer, so no check is made until it is on again; on checks once
    /// now and then daily. An update already found stays in the menu.
    /// </summary>
    private async Task ToggleUpdatesAsync()
    {
        if (!updateSetting.Set(!updateSetting.Enabled))
        {
            await Dialogs.Inform(null, "This applies now, but it could not be saved, so it will go back after a restart. The log has the reason.");
        }

        ApplyUpdateTimer();
        RefreshViews();
    }

    /// <summary>Runs or stops the daily check as the setting says. A manual check does not exist on Linux, so off means none.</summary>
    private void ApplyUpdateTimer()
    {
        var old = Interlocked.Exchange(ref updateTimer, updateSetting.Enabled ? new Timer(_ => _ = Guard(CheckForUpdateAsync), null, TimeSpan.FromSeconds(5), UpdateCheck.Every) : null);
        old?.Dispose();
    }

    /// <summary>The Macros… editor, one at a time; a save reaches the connected phone through the shared store.</summary>
    private async Task ShowMacrosAsync()
    {
        if (editingMacros)
        {
            return;
        }

        editingMacros = true;
        try
        {
            var editor = new MacroEditorWindow(LinuxPlatform.Shared);
            editor.Show();
            await editor.Closed;
        }
        finally
        {
            editingMacros = false;
            RefreshViews();
        }
    }

    /// <summary>
    /// The first launch's questions: Wi-Fi and USB, and the clipboard as its own choice, both off until answered, with
    /// Not now the default so a stray Enter opens nothing. Only what was never answered is asked; closing the window is no
    /// answer, so the next launch asks again.
    /// </summary>
    private async Task AskFirstRunAsync()
    {
        var (wifi, clipboard, login) = await FirstRunWindow.Ask(!networkSettings.HasChoice, !ClipboardSetting.HasChoice);
        if (clipboard is bool share)
        {
            await ApplyClipboardAsync(share);
        }

        if (wifi is bool on)
        {
            await SaveNetworkAsync(on);
        }

        // Only a tick does anything: unticked leaves whatever entry there is.
        if (login is true)
        {
            await ApplyAutostartAsync(true);
        }
    }

    /// <summary>Shows the fallback window when the desktop has no tray host. Asked off the UI thread, which a bus call may hold up.</summary>
    private async Task ShowFallbackIfNoTrayAsync(TrayHooks hooks)
    {
        if (!await Task.Run(FallbackWindow.NoTrayHost))
        {
            return;
        }

        Log.Write("The desktop has no tray host, so Edgepad shows its window instead");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            fallback = new FallbackWindow(hooks, status);
            fallback.Show();
        });
    }

    /// <summary>Asks GitHub for the newest release; a newer one shows "Update available" and says so once.</summary>
    private async Task CheckForUpdateAsync()
    {
        var latest = await UpdateCheck.LatestAsync();
        if (latest is null || !UpdateCheck.IsNewer(latest, version))
        {
            return;
        }

        var number = latest.ToString(3);
        Dispatcher.UIThread.Post(() =>
        {
            tray?.SetUpdate(number);
            fallback?.SetUpdate(number);
        });
        if (!updateNotified)
        {
            updateNotified = true;
            Notifier.SendClickable("Edgepad update available", $"Edgepad {number} is available. Click to open the release page.", () => Tray.OpenWithDesktop(UpdateCheck.LatestUrl, "the release page"));
        }
    }

    /// <summary>Saves the owner's answer and acts on it. A setting that cannot be saved leaves things as they were.</summary>
    private async Task SaveNetworkAsync(bool on)
    {
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

        RefreshViews();
        ShowProblem();
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

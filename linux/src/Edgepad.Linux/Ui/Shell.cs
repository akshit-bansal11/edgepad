using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Edgepad.Bluetooth;
using Edgepad.Dashboard;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Network;
using Edgepad.Platform;
using Edgepad.Transfer;
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

    /// <summary>The most files one "Send a file" pick sends, as on Windows and Android.</summary>
    private const int MaxSendFiles = 100;

    /// <summary>How long a desktop with no tray host at start gets for one to appear before the window is shown.</summary>
    private static readonly TimeSpan TrayHostGrace = TimeSpan.FromSeconds(10);

    private static string NetworkNotice => $"{FirstRunWindow.NetworkBody} {FirstRunWindow.Firewall}";

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
    private TransferCard? card;
    private FallbackWindow? fallback;
    private Timer? updateTimer;
    private volatile IClassicDesktopStyleApplicationLifetime? lifetime;
    private readonly Autostart autostart = Autostart.ForCurrentUser();
    private readonly UpdateSetting updateSetting = UpdateSetting.ForCurrentUser();

    // Set once the tray exists. Read on Bluetooth threads, so volatile; the trust question is refused until then.
    private volatile bool ready;
    private volatile bool quitRequested;
    private volatile string status = LaptopStatus.Starting.Text;

    // Statuses arrive on session threads; the lock in SetStatus guards it.
    private readonly StatusMemory memory = new();

    // 1 while /dev/uinput cannot be opened: input would do nothing while the phone shows connected. Set from any
    // thread, by Interlocked.
    private int inputBlocked;
    private readonly UpdateChoice updateChoice = new(Path.Combine(Xdg.ConfigFolder, "update-choice.txt"));

    // The release on offer, and whether it can be installed (a key is embedded); null when none. Written on the timer
    // thread, read on the UI thread.
    private volatile Version? offered;
    private volatile bool offeredInstallable;
    private volatile bool offeredNeedsConfirm;
    private bool installing;

    // The tray menu stays clickable under a dialog, and a second one would lift the hold while the first was still
    // open. All of this runs on the UI thread, so plain bools are enough.
    private bool forgetting;
    private bool securing;
    private bool editingMacros;

    // The Wi-Fi and USB row's hint: off, the port listened on, or why not. Only the UI thread touches it.
    private string networkHint = LinuxStrings.Hint_Off;

    // Whether the listener could not start; the hint is words, so the problem row reads this instead of the text.
    private bool networkFailed;

    public Shell(TrustStore trust, PinStore pin, NetworkSettings networkSettings, PairedLinks paired, LinkIdentity identity, string version)
    {
        this.trust = trust;
        this.pin = pin;
        this.networkSettings = networkSettings;
        this.paired = paired;
        this.identity = identity;
        this.version = version;
        LinuxPower.CountdownRequested += ShutdownCountdown.Show;
        // However the question ends, the status line stops asking for the code.
        pairing = PairingPrompt.OnUiThread(
            phone =>
            {
                SetStatus(LaptopStatus.Pairing(phone));
                Notifier.Send(LinuxStrings.App_Name, Say.Format(LinuxStrings.Balloon_WantsToPair, phone));
            },
            () => server?.PostStatus());
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
        // A refused link leaves no session to end, so "Paired with" is replaced here.
        network = new NetworkServer(
            identity,
            paired,
            trust,
            pairing,
            link =>
            {
                if (!bluetooth.Accept(link))
                {
                    bluetooth.PostStatus();
                }
            },
            name => SetStatus(LaptopStatus.Paired(name)),
            () => bluetooth.IsRefusing);
    }

    /// <summary>Stops listening for Wi-Fi and USB phones and closes theirs. Before the server is disposed; harmless twice.</summary>
    public void StopNetwork() => network?.Dispose();

    /// <summary>Stops the network server if <see cref="StopNetwork"/> has not already; Program calls that earlier, before the server goes.</summary>
    public void Dispose()
    {
        LinuxPower.CountdownRequested -= ShutdownCountdown.Show;
        updateTimer?.Dispose();
        network?.Dispose();
    }

    /// <summary>Runs the UI on the calling thread until Quit; returns Avalonia's exit code. Throws if no display can be opened.</summary>
    public int Run()
    {
        try
        {
            return AppBuilder.Configure(() => new App(StartOrFail)).UsePlatformDetect().StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            // No UI loop is left to ask the trust question on or to post the status to.
            ready = false;
        }
    }

    /// <summary>
    /// A tray that could not be set up leaves an app with no way to quit it, so it ends instead: logged, told to the
    /// owner in a notification (the one place that needs no tray), and the loop exits with code 1.
    /// </summary>
    private void StartOrFail(App app)
    {
        try
        {
            Start(app);
        }
        catch (Exception e)
        {
            Log.Write($"The tray could not be set up, so Edgepad is quitting: {e}");
            var failing = app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            Dispatcher.UIThread.Post(() => failing?.Shutdown(1));
            Notifier.SendNow(LinuxStrings.Notice_TrayFailedTitle, LinuxStrings.Notice_TrayFailed);
        }
    }

    /// <summary>Ends the UI loop from any thread (a signal, the Quit row). Harmless before it started or after it ended.</summary>
    public void RequestQuit()
    {
        quitRequested = true;

        // Posted even before the tray is ready: Start also checks the flag at its end, but a Start that threw never gets there.
        if (lifetime is { } running)
        {
            Dispatcher.UIThread.Post(() => running.Shutdown());
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
            Notifier.Send(LinuxStrings.App_Name, update.Text);
        }

        var text = shown.Text;

        // "Connected to" is only true if the phone's input can do anything: check each time, as the log said only at the end.
        if (shown.IsConnected)
        {
            CheckInput();
            if (InputBlocked)
            {
                text = UinputAccess.Blocked;
            }
        }

        status = text;
        Log.Write($"Status: {update.Text}");
        if (ready)
        {
            // The status as it is when the post runs, not as this thread saw it: two threads' posts can run in
            // either order, and the last one must not put an older line back.
            Dispatcher.UIThread.Post(() =>
            {
                var now = status;
                tray?.SetStatus(now);
                fallback?.SetStatus(now);
                ShowProblem();
            });
        }
    }

    private bool InputBlocked => Volatile.Read(ref inputBlocked) != 0;

    /// <summary>Probes /dev/uinput and remembers the answer; says so once, in a notification, when it turns bad.</summary>
    private void CheckInput()
    {
        var problem = UinputAccess.Check();

        // One exchange, so two threads that both see it turn bad notify once.
        var was = Interlocked.Exchange(ref inputBlocked, problem is not null ? 1 : 0) != 0;
        if (problem is not null && !was)
        {
            Log.Write($"Input is blocked: {problem}");
            Notifier.Send(LinuxStrings.Notice_CannotControl, UinputAccess.BlockedDetail);
        }
    }

    /// <summary>The problem row, on the UI thread: input blocked first, then a Wi-Fi listener that would not start.</summary>
    private void ShowProblem()
    {
        var text = InputBlocked ? UinputAccess.Blocked : networkFailed ? LinuxStrings.Network_CouldNotListen : null;
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
        Notifier.Send(LinuxStrings.App_Name, Say.Format(LinuxStrings.Notice_WantsToConnect, name));
        var trusted = prompt.Ask(address, ended);
        Log.Write(trusted ? $"The owner trusted {address}" : $"The owner did not trust {address}");
        return trusted;
    }

    private void Start(App app)
    {
        // An exception in a posted body or a timer's tick would otherwise end the UI loop, and with it the app and
        // the Bluetooth service.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Write($"The UI thread hit an exception, which is ignored: {e.Exception}");
            e.Handled = true;
        };
        lifetime = app.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var hooks = new TrayHooks(
            () => SecurityWindow.LevelName(pin.Level),
            () => Say.Format(LinuxStrings.Menu_MacrosCount, LinuxPlatform.Shared.Macros.Count, MacroStore.MaxMacros),
            () => networkSettings.Load().Enabled,
            () => networkHint,
            () => ClipboardSetting.Enabled,
            () => NotifySetting.Enabled,
            () => LinuxDashboard.Shared?.Settings.Flag(IntegrationSettings.Developer) == true,
            () => autostart.Enabled,
            () => updateSetting.Enabled,
            () => _ = Guard(ShowSecurityAsync),
            () => _ = Guard(ShowMacrosAsync),
            () => _ = Guard(PickAndSendAsync),
            () => _ = Guard(ToggleNetworkAsync),
            () => _ = Guard(ToggleClipboardAsync),
            () => _ = Guard(ToggleNotifyAsync),
            () => _ = Guard(ToggleDeveloperAsync),
            () => LinuxDashboard.Shared?.ShowWindow(developer: true),
            () => _ = Guard(ToggleAutostartAsync),
            () => _ = Guard(ToggleUpdatesAsync),
            () => _ = Guard(InstallUpdateAsync),
            LaterUpdate,
            SkipUpdate,
            () => card?.ShowAgain(),
            RequestQuit);
        if (LinuxDashboard.Shared is { } shared)
        {
            shared.DeveloperChanged += () => Dispatcher.UIThread.Post(OnDeveloperChanged);
        }

        var menu = new Tray(hooks);
        tray = menu;
        card = new TransferCard(
            LinuxTransfers.Feed,
            () => server?.ConnectedPhone ?? "",
            sending => server?.PauseTransfer(sending) == true,
            sending => server?.ResumeTransfer(sending) == true,
            sending => server?.StopTransfer(sending));
        LinuxTransfers.Feed.Changed += view => Dispatcher.UIThread.Post(() => tray?.SetTransfers(view.Phase == TransferPhase.Running));
        menu.SetStatus(status);
        menu.Attach(app);
        if (lifetime is not null)
        {
            lifetime.Exit += (_, _) => menu.Dispose();
        }

        ready = true;

        // Each step on its own, so one that throws does not skip the rest (the update timer, first run, annotate).
        Step(CheckInput, "Input check");
        Step(ApplyNetwork, "Wi-Fi and USB");
        Step(ShowProblem, "Problem row");
        _ = Guard(() => ShowFallbackIfNoTrayAsync(hooks));

        // Once now and then daily, as on Windows and the Mac, unless the owner turned it off.
        Step(ApplyUpdateTimer, "Update timer");
        if (!networkSettings.HasChoice || !ClipboardSetting.HasChoice)
        {
            // Posted, so the tray is up and running first.
            Dispatcher.UIThread.Post(() => _ = Guard(AskFirstRunAsync));
        }

        // The hidden overlay, the screen size and whether annotate is offered: only knowable once the UI is up.
        Step(Annotator.StartUi, "Annotate");

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
    private async Task ForgetPairedLinksAsync(Window? owner)
    {
        if (paired.Forget())
        {
            return;
        }

        await Dialogs.Inform(owner, $"{LinuxStrings.Error_ForgetLinks}\n\n{pin.Folder}");
    }

    /// <summary>
    /// Forgets every trusted phone once the owner says so. Asked first because the menu cannot undo it: each
    /// phone has to be trusted again, the one connected included. Connections are refused while the question is
    /// open; a yes forgets and keeps refusing for the quiet period, and anything else takes connections again.
    /// </summary>
    private async Task ConfirmForgetAsync(Window? owner)
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
                owner,
                phones.Length > 0 ? Say.Format(LinuxStrings.Forget_AllNamed, phones) : LinuxStrings.Forget_All,
                LinuxStrings.Common_Forget,
                LinuxStrings.Common_Cancel,
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
                await Dialogs.Inform(owner, $"{LinuxStrings.Error_ForgetTrust}\n\n{trust.FilePath}");
            }

            // The Wi-Fi and USB pairings too, or a forgotten phone would resume its link and only meet the trust
            // question, not a new pairing.
            await ForgetPairedLinksAsync(owner);

            if (!verifiedForgotten)
            {
                await Dialogs.Inform(owner, $"{LinuxStrings.Error_ForgetPin}\n\n{pin.Folder}");
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
        if (on && !await Dialogs.Confirm(null, NetworkNotice, LinuxStrings.Dialog_TurnOn, LinuxStrings.Common_Cancel))
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
            await Dialogs.Inform(null, LinuxStrings.Error_NotSavedAfterRestart);
        }

        RefreshViews();
    }

    /// <summary>The Developer mode switch. The dashboard applies it off the UI thread and tells every phone.</summary>
    private async Task ToggleDeveloperAsync()
    {
        if (LinuxDashboard.Shared is not { } shared)
        {
            return;
        }

        var on = !shared.Settings.Flag(IntegrationSettings.Developer);
        await Task.Run(() => shared.SetDeveloper(on));
    }

    /// <summary>Developer mode moved, from the tray or the phone (UI thread): the rows follow and an open developer window reads its rows again.</summary>
    private void OnDeveloperChanged()
    {
        LinuxDashboard.Shared?.RefreshDeveloperWindow();
        RefreshViews();
    }

    /// <summary>The Forward notifications switch. Default off; phones are told again, since the feature token follows it.</summary>
    private async Task ToggleNotifyAsync()
    {
        var saved = NotifySetting.Set(!NotifySetting.Enabled);
        Annotator.FeaturesChanged();
        if (!saved)
        {
            await Dialogs.Inform(null, LinuxStrings.Error_NotSavedAfterRestart);
        }

        RefreshViews();
    }

    /// <summary>Start at login: writes or removes the autostart entry, and says so when it could not.</summary>
    private Task ToggleAutostartAsync() => ApplyAutostartAsync(!autostart.Enabled);

    private async Task ApplyAutostartAsync(bool on)
    {
        if (!autostart.Set(on))
        {
            await Dialogs.Inform(null, LinuxStrings.Error_AutostartFailed);
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
            await Dialogs.Inform(null, LinuxStrings.Error_NotSavedAfterRestart);
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

    /// <summary>
    /// Shows the fallback window when the desktop has no tray host. Asked off the UI thread, which a bus call may hold
    /// up. At login the tray host can start seconds after Edgepad, so a first "none" is asked once more 10 s later.
    /// </summary>
    private async Task ShowFallbackIfNoTrayAsync(TrayHooks hooks)
    {
        if (!await Task.Run(FallbackWindow.NoTrayHost))
        {
            return;
        }

        await Task.Delay(TrayHostGrace);
        if (!await Task.Run(FallbackWindow.NoTrayHost))
        {
            Log.Write("The tray host started after Edgepad did, so no window is needed");
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
        if (latest is null)
        {
            return;
        }

        // Skipped, or Later said within a day: no rows and no notice. With no embedded key the rows are only
        // Later and Skip and the notice opens the release page (fail closed).
        var decision = UpdatePolicy.Decide(latest, version, null, UpdateKey.IsSet && UpdateCheck.LinuxName(latest) is not null);
        if (decision is UpdateDecision.NotNewer || !updateChoice.ShouldShow(latest, DateTimeOffset.UtcNow))
        {
            return;
        }

        var number = latest.ToString(3);
        var installable = decision is UpdateDecision.Install or UpdateDecision.InstallNeedsMajorConfirm;
        offered = latest;
        offeredInstallable = installable;
        offeredNeedsConfirm = decision is UpdateDecision.InstallNeedsMajorConfirm;
        Dispatcher.UIThread.Post(() =>
        {
            tray?.SetUpdate(number, installable);
            fallback?.SetUpdate(number, installable);
        });
        if (!updateChoice.WasNotified(latest))
        {
            updateChoice.MarkNotified(latest);
            Notifier.SendClickable(LinuxStrings.Menu_Update, Say.Format(LinuxStrings.Notice_UpdateAvailable, number), () => Tray.OpenWithDesktop(UpdateCheck.LatestUrl, LinuxStrings.What_ReleasePage));
        }
    }

    private void HideUpdate()
    {
        offered = null;
        tray?.ClearUpdate();
        fallback?.ClearUpdate();
    }

    /// <summary>Later: the update stays out of sight until the next daily check after a day has passed.</summary>
    private void LaterUpdate()
    {
        if (!updateChoice.Later(DateTimeOffset.UtcNow))
        {
            Log.Write("Later could not be saved, so the update will show again after a restart.");
        }

        HideUpdate();
    }

    /// <summary>Skip: this version is never offered again; a newer one is.</summary>
    private void SkipUpdate()
    {
        if (offered is { } skipped && !updateChoice.Skip(skipped))
        {
            Log.Write("Skip could not be saved, so the update will show again after a restart.");
        }

        HideUpdate();
    }

    /// <summary>
    /// Install: verify, download and replace (see <see cref="LinuxUpdater"/>). A different first number asks first, because
    /// the phone must be updated too. Failure installs nothing and says so.
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        if (installing || offered is not { } target || !offeredInstallable)
        {
            return;
        }

        if (offeredNeedsConfirm
            && !await Dialogs.Confirm(null, Say.Format(LinuxStrings.Update_MajorConfirm, target.ToString(3)), LinuxStrings.Common_Install, LinuxStrings.Common_NotNow))
        {
            return;
        }

        installing = true;
        try
        {
            var outcome = await LinuxUpdater.InstallAsync(target, Path.Combine(Xdg.CacheFolder, "update"), Environment.ProcessPath, null, CancellationToken.None);
            switch (outcome)
            {
                case UpdateOutcome.Started:
                    HideUpdate();
                    break;
                case UpdateOutcome.NeedsRoot:
                    HideUpdate();
                    await Dialogs.Inform(null, $"{Say.Format(LinuxStrings.Update_NeedsRoot, target.ToString(3))}\n\n{LinuxUpdater.RootCommand(Path.Combine(Xdg.CacheFolder, "update", UpdateCheck.LinuxName(target) ?? string.Empty), target)}");
                    break;
                case UpdateOutcome.Failed:
                    await Dialogs.Inform(null, LinuxStrings.Update_InstallFailed);
                    break;
                default:
                    break;
            }
        }
        finally
        {
            installing = false;
        }
    }

    /// <summary>Saves the owner's answer and acts on it. A setting that cannot be saved leaves things as they were.</summary>
    private async Task SaveNetworkAsync(bool on)
    {
        if (!networkSettings.SetEnabled(on))
        {
            await Dialogs.Inform(null, LinuxStrings.Error_SettingNotSaved);
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
            networkHint = LinuxStrings.Hint_Off;
            networkFailed = false;
        }
        else
        {
            networkFailed = !net.Start(config.Port);
            networkHint = networkFailed ? LinuxStrings.Hint_CouldNotListen
                : Say.Format(net.Port == config.Port ? LinuxStrings.Hint_Listening : LinuxStrings.Hint_PortInUse, net.Port);
        }

        RefreshViews();
        ShowProblem();
    }

    /// <summary>One start-up step; its exception is logged and the next step still runs.</summary>
    private static void Step(Action work, string what)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            Log.Write($"{what} could not be set up: {e}");
        }
    }

    /// <summary>Nothing may throw out of a menu handler: an exception on the UI thread ends the whole app.</summary>
    /// <summary>The tray's "Send a file…": the desktop's file picker, on a window of no size, then the file goes to the phone.</summary>
    private async Task PickAndSendAsync()
    {
        List<string> paths = [];
        var owner = new Window { Width = 1, Height = 1, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None, Opacity = 0 };
        try
        {
            owner.Show();
            var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = LinuxStrings.Send_PickerTitle, AllowMultiple = true });
            paths.AddRange(picked.Select(item => item.TryGetLocalPath()).OfType<string>());
        }
        finally
        {
            owner.Close();
        }

        if (paths.Count > MaxSendFiles)
        {
            Log.Write($"{paths.Count - MaxSendFiles} picked files were left out: at most {MaxSendFiles} go in one send.");
        }

        Send(paths.Take(MaxSendFiles).ToList());
    }

    /// <summary>
    /// Sends <paramref name="path"/> to the connected phone; the transfer card shows how it goes. From the send socket; any
    /// thread. Never throws.
    /// </summary>
    public Task SendFileAsync(string path)
    {
        Send([path]);
        return Task.CompletedTask;
    }

    /// <summary>Queues files for the phone. With none connected a notification says so, since no card would.</summary>
    private void Send(List<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            if (!Server.Enqueue(paths))
            {
                Notifier.Send(LinuxStrings.App_Name, LinuxStrings.Send_NoPhone);
            }
        }
        catch (Exception e)
        {
            Log.Write($"Files could not be queued: {e.Message}");
        }
    }

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

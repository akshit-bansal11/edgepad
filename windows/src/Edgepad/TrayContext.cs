using System.ComponentModel;
using System.Diagnostics;
using Edgepad.Blob;
using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Network;
using Edgepad.Startup;
using Edgepad.Trust;
using Edgepad.Ui;
using Edgepad.Updates;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace Edgepad;

/// <summary>The whole UI: a tray icon whose menu shows connection status and the few settings there are.</summary>
internal sealed class TrayContext : ApplicationContext
{
    // NotifyIcon.Text is capped by the shell; stay under the oldest limit.
    private const int MaxTooltipLength = 63;

    // The documentation site. The phone's Settings › Help offers the same link, so the
    // two halves point at one page rather than each explaining itself.
    private const string DocumentationUrl = "https://edgepad.akshitbansal.dev";

    /// <summary>
    /// How long the question about a new phone waits for the owner. No answer is a no: a laptop left alone must
    /// not end up trusting whichever paired phone happened to try it. The phone waits longer than this for its
    /// HELLO_ACK, so it hears the refusal rather than giving up first.
    /// </summary>
    private static readonly TimeSpan TrustPromptTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long after a Forget every connection is refused. It outlasts the phone's own automatic retries, so
    /// the trust question can only come back from someone deliberately choosing this laptop on the phone.
    /// </summary>
    private static readonly TimeSpan QuietAfterForget = TimeSpan.FromSeconds(30);

    private const string NetworkNotice =
        "Phones on the same Wi-Fi, or connected by USB tethering, will be able to find this laptop and ask to pair. "
            + "Nothing connects without the code typed here. Windows will ask for permission once.";

    // Lucide's clipboard, on the same grid as the paths in Ui/Icons.
    private const string ClipboardGlyph = "M9 2h6a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1H9a1 1 0 0 1 -1 -1V3a1 1 0 0 1 1 -1z"
        + "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2H6a2 2 0 0 1 -2 -2V6a2 2 0 0 1 2 -2h2";

    private readonly MenuRow startWithWindows = new("Start with Windows", Icons.Power) { CheckOnClick = true, Switch = true };
    private readonly MenuRow macrosRow = new("Macros…", Icons.Macro);
    private readonly MenuRow securityRow = new("Security…", Icons.Smartphone);
    private readonly MenuRow networkRow = new("Wi-Fi and USB connections", Icons.Wifi) { CheckOnClick = true, Switch = true };
    private readonly MenuRow firewallRow = new("Allow through Windows Firewall…", Icons.Shield);
    private readonly MenuRow clipboardRow = new("Share clipboard", ClipboardGlyph) { CheckOnClick = true, Switch = true };
    private readonly MenuRow forget = new("Forget trusted phones", Icons.Unlink) { Danger = true };
    private readonly MenuRow update = new("Update available", Icons.Download) { Available = false };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = (int)UpdateCheck.Every.TotalMilliseconds };
    private readonly TrayMenu menu = new();
    private readonly NotifyIcon icon;
    private readonly SynchronizationContext ui;
    private Icon? themed;

    private readonly TrustStore trust = TrustStore.ForCurrentUser();
    private readonly PinStore pin = PinStore.ForCurrentUser();
    private readonly AudioEndpoint speakers = new(DataFlow.Render);
    private readonly AudioEndpoint microphone = new(DataFlow.Capture);
    private readonly BrightnessControl brightness = new();
    private readonly MediaSessions media = new();
    private readonly DisplayModes display = new();
    private readonly MacroStore macros = MacroStore.ForCurrentUser(new ShellLauncher(), new WindowsMacroWindows());
    private readonly LevelOverlay overlay;
    private readonly SessionHost host;
    private readonly RfcommServer server;
    private readonly PairedLinks pairedLinks = PairedLinks.ForCurrentUser();
    private readonly NetworkSettings networkSettings;
    private readonly FirewallMemory firewallMemory;
    private readonly NetworkServer network;
    private readonly ClipboardSetting clipboardSetting;
    private readonly WindowsClipboard clipboard;

    // The Wi-Fi and USB row's hint: off, the port listened on, or why not. Only the UI thread touches it.
    private string networkHint = "Off";

    // True from the question being put until the firewall step is over: nothing may listen before the rule exists, or
    // Windows' own alert appears as well. Only the UI thread touches it.
    private bool allowingNetwork;

    // The menu stays clickable under the confirmation, and a second one would lift the hold while the first was
    // still open. All of this runs on the UI thread, so a plain bool is enough.
    private bool forgetting;

    // The same for the Security window, which also holds connections off while it is open.
    private bool securing;

    public TrayContext(EventWaitHandle quit)
    {
        // First, because the menu's Opening handler below reads it.
        networkSettings = NetworkSettings.ForCurrentUser(pin.Folder);
        firewallMemory = FirewallMemory.ForCurrentUser(pin.Folder);
        clipboardSetting = ClipboardSetting.ForCurrentUser(pin.Folder);

        // Click, not CheckedChanged: the menu sets Checked from the registry each time it opens, and that must
        // read the key without writing it back — least of all deleting it after a failed read.
        startWithWindows.Click += (_, _) => RunAtLogin.Set(startWithWindows.Checked);
        forget.Click += (_, _) => ConfirmForget();
        macrosRow.Click += (_, _) => MacroEditor.Show(macros);
        securityRow.Click += (_, _) => ShowSecurity();
        networkRow.Click += (_, _) => ToggleNetwork(networkRow.Checked);
        firewallRow.Click += (_, _) => AllowFirewall(ReportFirewall);
        clipboardRow.Click += (_, _) => SaveClipboard(clipboardRow.Checked);
        update.Click += (_, _) => Open(UpdateCheck.LatestUrl, "the release page");

        // The header is the menu's own. The update row sits right under it and stays hidden until there is one.
        // The rest follow in the design's order, the destructive pair after a rule.
        menu.Items.Add(update);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(startWithWindows);
        menu.Items.Add(macrosRow);
        menu.Items.Add(securityRow);
        menu.Items.Add(networkRow);
        menu.Items.Add(firewallRow);
        menu.Items.Add(clipboardRow);
        menu.Items.Add(Row("Open log", Icons.FileText, OpenLog));
        menu.Items.Add(Row("Documentation", Icons.BookOpen, () => Open(DocumentationUrl, "the documentation"), new Uri(DocumentationUrl).Host));
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(forget);
        menu.Items.Add(Row("Quit Edgepad", Icons.Close, ExitThread, Program.Version));

        // Read the settings each time the menu opens, so it never shows a stale state.
        menu.Opening += (_, _) =>
        {
            startWithWindows.Checked = RunAtLogin.IsEnabled;
            forget.Enabled = trust.CanForget || pairedLinks.CanForget;
            securityRow.Hint = $"Level {pin.Level}";
            networkRow.Checked = networkSettings.Load().Enabled;
            networkRow.Enabled = !allowingNetwork;
            networkRow.Hint = networkHint;
            firewallRow.Available = networkRow.Checked && !allowingNetwork;
            clipboardRow.Checked = clipboardSetting.Enabled;
            macrosRow.Hint = $"{macros.Macros.Count} of {MacroStore.MaxMacros}";
            menu.Restyle();
        };

        // The exe's own icon (ApplicationIcon in the project), so the tray shows the Edgepad mark.
        var mark = Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
        icon = new NotifyIcon { Icon = mark ?? SystemIcons.Application, Text = "Edgepad", ContextMenuStrip = menu, Visible = true };

        // A right-click opens the menu through ContextMenuStrip. A left click opens the same one: the tray app
        // has no window for it to show instead, and one menu for both keeps a single keyboard-reachable path.
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMenu();
            }
        };

        // Creating the menu installed the WinForms context on this thread; status updates arrive from
        // Bluetooth threads and are marshalled back here.
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        // The taskbar's tile, light or dark, now and whenever Windows' theme changes (that arrives as General).
        ApplyTrayIcon();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

        // Built here and nowhere else: the overlay captures this thread's synchronisation context, and the
        // frames that ask it to show something arrive on a Bluetooth thread that has none of its own.
        overlay = new LevelOverlay();
        var identity = LinkIdentity.ForCurrentUser(pin.Folder);

        // Here for the same reason: its window, and every clipboard call after it, belong to this thread.
        clipboard = new WindowsClipboard(ui, () => clipboardSetting.Enabled);
        host = new SessionHost(
            SetStatus,
            trust,
            pin,
            AskTrust,
            speakers,
            microphone,
            brightness,
            media,
            display,
            macros,
            overlay,
            identity,
            clipboard,
            () => clipboardSetting.Enabled,
            ui,
            OnSessionEnded);
        server = new RfcommServer(SetStatus, host);
        _ = StartServerAsync();

        // Starts once the owner has said yes (network.txt); only a phone that proves itself is handed to the host.
        network = new NetworkServer(
            identity,
            pairedLinks,
            trust,
            new PairingPrompt(ui, AnnouncePairing),
            host.Accept,
            name => SetStatus($"Paired with {name}"),
            () => host.IsRefusing);
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        // An update is a new exe with no rule. Held before the first listen, or Windows' own alert would come first.
        var followExe = networkSettings.Load().Enabled && FirewallMemory.IsNew(firewallMemory.Load(), Environment.ProcessPath);
        allowingNetwork = followExe;
        ApplyNetwork();
        if (followExe)
        {
            // Posted, for the same reason as the question below: the firewall step needs the tray running.
            ui.Post(_ => FollowExe(), null);
        }

        if (!networkSettings.HasChoice)
        {
            // Posted, so it waits for the tray to be up and running rather than holding up this constructor.
            ui.Post(_ => AskAboutNetwork(), null);
        }

        _ = StartMediaAsync();

        // Once now and once a day after, for a tray app that is left running for weeks.
        updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        updateTimer.Start();
        _ = CheckForUpdateAsync();

        // A newer Edgepad asks this one to quit through the event, so running an update takes over cleanly.
        new Thread(() => WaitForQuit(quit)) { IsBackground = true, Name = "edgepad-quit" }.Start();
    }

    private void WaitForQuit(EventWaitHandle quit)
    {
        try
        {
            quit.WaitOne();
        }
        catch (ObjectDisposedException)
        {
            // This copy is already shutting down.
            return;
        }

        Log.Write("A newer Edgepad asked this one to quit");
        ui.Post(_ => ExitThread(), null);
    }

    private async Task StartMediaAsync()
    {
        try
        {
            await media.StartAsync();
        }
        catch (Exception e)
        {
            // Without it the media corner shows nothing; every other control still works.
            Log.Write($"Media sessions unavailable: {e}");
        }
    }

    /// <summary>
    /// Asks GitHub which release is newest and, when it is newer than this one, shows the menu row and says so
    /// once. The row opens the release page: this copy may have been installed by winget, which keeps its own
    /// record of the version, so the app does not replace itself.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        try
        {
            var latest = await UpdateCheck.LatestAsync();
            if (latest is null || !UpdateCheck.IsNewer(latest, Program.Version))
            {
                return;
            }

            ui.Post(
                _ =>
                {
                    var version = latest.ToString();
                    var announced = update.Available && update.Hint == version;
                    update.Hint = version;
                    update.Available = true;
                    if (!announced)
                    {
                        Log.Write($"Edgepad {version} is available");
                        icon.ShowBalloonTip(0, "Edgepad", $"Edgepad {version} is available. Open this menu to get it.", ToolTipIcon.Info);
                    }
                },
                null);
        }
        catch (Exception e)
        {
            // Top-level boundary of a fire-and-forget task: an escaped exception here would be lost silently.
            Log.Write($"The update check failed: {e}");
        }
    }

    private async Task StartServerAsync()
    {
        try
        {
            await server.StartAsync();
        }
        catch (Exception e)
        {
            // Top-level boundary of a fire-and-forget task: an escaped exception here would be lost silently.
            Log.Write($"Bluetooth service failed to start: {e}");
            SetStatus("Bluetooth is off or unavailable");
        }
    }

    // A method rather than a lambda in the constructor, where the server is not assigned yet. A session can only
    // end once the server has started, after the constructor.
    private void OnSessionEnded(Func<bool> end) => server.OnSessionEnded(end);

    /// <summary>
    /// The Wi-Fi and USB switch. Turning it on says first what will happen; a no there leaves it off.
    /// </summary>
    private void ToggleNetwork(bool on)
    {
        if (on && MessageBox.Show(
            NetworkNotice,
            "Edgepad",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information) != DialogResult.OK)
        {
            networkRow.Checked = false;
            return;
        }

        SaveNetwork(on);
    }

    /// <summary>
    /// The first launch's one question, with Not now the default so a stray Enter opens nothing. Nothing listens until
    /// it is answered, and closing it is not an answer. Not now is saved, so it is never asked again; the switch in the
    /// menu is the way back.
    /// </summary>
    private void AskAboutNetwork()
    {
        var allow = new TaskDialogButton("Allow");
        var notNow = new TaskDialogButton("Not now");
        var page = new TaskDialogPage
        {
            Caption = "Edgepad",
            Heading = "Let phones find this laptop?",
            Text = NetworkNotice,
            Icon = TaskDialogIcon.Shield,
            Buttons = { allow, notNow },
            DefaultButton = notNow,
        };

        allowingNetwork = true;
        bool on;
        try
        {
            on = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen) == allow;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Nothing listens, and the next launch asks again.
            Log.Write($"Could not ask about Wi-Fi and USB phones: {e.Message}");
            return;
        }
        finally
        {
            allowingNetwork = false;
        }

        SaveNetwork(on);
    }

    /// <summary>
    /// Saves the owner's answer and acts on it: turning on runs the firewall step and listens only after it, whatever
    /// it reported, so the rule is in place first. A setting that cannot be saved leaves things as they were.
    /// </summary>
    private void SaveNetwork(bool on)
    {
        if (!networkSettings.SetEnabled(on))
        {
            networkRow.Checked = !on;
            MessageBox.Show(
                "The setting could not be saved, so nothing was changed. The log has the reason.",
                "Edgepad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (!on)
        {
            ApplyNetwork();
            return;
        }

        AllowThenListen();
    }

    /// <summary>The first launch of a new exe with the network on: says why Windows is about to ask, then asks.</summary>
    private void FollowExe()
    {
        icon.ShowBalloonTip(
            0,
            "Edgepad",
            "This version of Edgepad is a new file, so Windows will ask once to let phones reach it.",
            ToolTipIcon.Info);
        AllowThenListen();
    }

    /// <summary>The firewall step, then listening whatever it reported, so nothing listens before the rule exists.</summary>
    private void AllowThenListen()
    {
        allowingNetwork = true;
        AllowFirewall(allowed =>
        {
            allowingNetwork = false;
            ReportFirewall(allowed);
            ApplyNetwork();
        });
    }

    /// <summary>
    /// Every way to the firewall step. The exe is remembered whatever the answer, so a no is not asked again at the next
    /// launch; the menu's Allow row is the way back.
    /// </summary>
    private void AllowFirewall(Action<bool> done)
    {
        Firewall.Allow(allowed =>
        {
            if (Environment.ProcessPath is { } exe)
            {
                firewallMemory.Remember(exe);
            }

            done(allowed);
        });
    }

    /// <summary>One notice when the rule did not go in, a declined prompt among the reasons. Asks nothing again.</summary>
    private void ReportFirewall(bool allowed)
    {
        if (!allowed)
        {
            icon.ShowBalloonTip(
                0,
                "Edgepad",
                "Windows Firewall was not changed, so phones may not reach this laptop. Allow through Windows Firewall in this menu tries again.",
                ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// The Share clipboard switch. It holds from this moment whether or not it could be saved, because a session reads it
    /// at every step; a phone already connected keeps showing the old state until it next connects.
    /// </summary>
    private void SaveClipboard(bool on)
    {
        if (!clipboardSetting.Set(on))
        {
            icon.ShowBalloonTip(
                0,
                "Edgepad",
                "Share clipboard changed for now, but could not be saved, so the next launch may not remember it.",
                ToolTipIcon.Warning);
        }
    }

    /// <summary>Listens or stops as network.txt says, and words the row's hint for it. On the UI thread only.</summary>
    private void ApplyNetwork()
    {
        // The question or the firewall step is open, and the end of that calls this. A wake now must not listen first.
        if (allowingNetwork)
        {
            return;
        }

        var config = networkSettings.Load();
        if (!config.Enabled)
        {
            network.Stop();
            networkHint = "Off";
            return;
        }

        if (!network.Start(config.Port))
        {
            networkHint = "Could not listen; see the log";
            return;
        }

        var listening = network.Port == config.Port ? $"Listening on {config.Port}" : $"Port in use, using {network.Port}";
        networkHint = network.IsAnnounced ? listening : $"{listening}, not announced";
    }

    // Sockets opened before sleep may not survive it. Raised on SystemEvents' thread; the listener is the UI thread's.
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            ui.Post(_ => ApplyNetwork(), null);
        }
    }

    /// <summary>The pairing window may open behind whatever has focus; the notification is what the owner notices.</summary>
    private void AnnouncePairing(string phoneName) => ui.Post(
        _ => icon.ShowBalloonTip(0, "Edgepad", $"{phoneName} wants to pair. Type the code it shows.", ToolTipIcon.Info),
        null);

    private void SetStatus(string text) => ui.Post(
        _ =>
        {
            menu.Header.SetStatus(text);
            var tooltip = $"Edgepad: {text}";
            icon.Text = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
        },
        null);

    private static MenuRow Row(string text, string glyph, Action run, string? hint = null)
    {
        var row = new MenuRow(text, glyph) { Hint = hint };
        row.Click += (_, _) => run();
        return row;
    }

    /// <summary>
    /// Opens the menu exactly as a right-click does. The shell only dismisses a tray menu cleanly when its
    /// owner took the foreground first, and NotifyIcon does that in a private method rather than a public one;
    /// showing the strip directly works, but can leave it open after a click elsewhere. So the private method is
    /// asked for by name, with the plain Show as the fallback should a WinForms release rename it.
    /// </summary>
    private void ShowMenu()
    {
        var show = typeof(NotifyIcon).GetMethod(
            "ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (show is not null)
        {
            show.Invoke(icon, null);
        }
        else
        {
            menu.Show(Cursor.Position);
        }
    }

    private static void OpenLog()
    {
        if (File.Exists(Log.FilePath))
        {
            Open(Log.FilePath, "the log");
        }
    }

    /// <summary>Hands a file or a link to whatever Windows opens it with, as a double-click would.</summary>
    private static void Open(string target, string what)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // No default browser or editor, or the shell refused the handler. A menu item must never
            // take the tray app down with it: an unhandled exception here ends the process.
            Log.Write($"Could not open {what}: {e.Message}");
        }
    }

    /// <summary>
    /// Forgets every trusted phone once the owner says so. Asked first because the menu cannot undo it: each
    /// phone has to be trusted again, the one connected included. Connections are refused while the
    /// question is open; a yes forgets and keeps refusing for the quiet period, and anything else takes
    /// connections again. If the trust file cannot be removed the owner is told, and the store refuses every
    /// phone until a later Forget succeeds.
    /// </summary>
    private void ConfirmForget()
    {
        if (forgetting)
        {
            return;
        }

        forgetting = true;

        // A connected phone is a keyboard: it could press Enter on this confirmation, where No is the default,
        // and so keep the owner from ever forgetting it. Hanging up is not enough, since the phone is still trusted
        // and reconnects within seconds, so nothing may connect until the question is answered.
        host.HoldOff();
        network.DropPending();
        try
        {
            var phones = string.Join(", ", trust.TrustedPhones);
            var answer = MessageBox.Show(
                (phones.Length > 0 ? $"Forget every trusted phone ({phones})?" : "Forget the trusted phones?")
                    + " Each phone, this one included, will have to be trusted here again before it can use this laptop.",
                "Edgepad",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            var forgotten = trust.Forget();

            // A forgotten phone that had entered the PIN would otherwise skip it at level 2 after one mistaken yes.
            var verifiedForgotten = pin.ForgetVerified();

            // Started before the hold ends, so there is no instant in which a connection would be taken.
            host.HangUp(QuietAfterForget);
            if (!forgotten)
            {
                MessageBox.Show(
                    "The trusted phones could not be forgotten: their file could not be removed or emptied. No phone can "
                        + "connect while Edgepad stays running, but those phones will be trusted again the next time "
                        + "Edgepad starts. Close whatever is holding the file and choose Forget trusted phones again, "
                        + $"or delete it yourself:\n\n{trust.FilePath}",
                    "Edgepad",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            // The Wi-Fi and USB pairings too, or a forgotten phone would resume its link and only meet the trust
            // question, not a new pairing.
            ForgetPairedLinks();

            if (!verifiedForgotten)
            {
                MessageBox.Show(
                    "The phones that entered the PIN could not be forgotten: their list could not be removed. While "
                        + "Edgepad stays running every phone is asked for the PIN on every connection, but those phones "
                        + "will skip it at level 2 again the next time Edgepad starts. Choose Forget trusted phones "
                        + $"again, or delete pin-verified.txt yourself from:\n\n{pin.Folder}",
                    "Edgepad",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            host.Resume();
            forgetting = false;
        }
    }

    /// <summary>
    /// Forgets the Wi-Fi and USB pairings, and tells the owner when they could not be. Every way of forgetting phones
    /// (Forget trusted phones, Reset PIN in the Security window) goes through here.
    /// </summary>
    private void ForgetPairedLinks()
    {
        if (pairedLinks.Forget())
        {
            return;
        }

        MessageBox.Show(
            "The Wi-Fi and USB pairings could not be forgotten: their file could not be removed or emptied. "
                + "Paired phones cannot reconnect over Wi-Fi or USB while Edgepad stays running, but they will "
                + "be able to again the next time it starts. Choose Forget trusted phones again, or delete "
                + $"paired-links.txt yourself from:\n\n{pin.Folder}",
            "Edgepad",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    /// <summary>
    /// The Security window, with connections refused while it is open for the reason <see cref="ConfirmForget"/>
    /// gives: a connected phone is a keyboard and could otherwise answer it, or change the level it is held by.
    /// </summary>
    private void ShowSecurity()
    {
        if (securing)
        {
            return;
        }

        securing = true;
        host.HoldOff();
        network.DropPending();
        try
        {
            using var window = new SecurityWindow(pin, trust, ForgetPairedLinks, () => host.HangUp(QuietAfterForget));
            window.ShowDialog();
        }
        finally
        {
            host.Resume();
            securing = false;
        }
    }

    /// <summary>
    /// Asks the owner whether to trust <paramref name="address"/>, and waits for the answer. Called on a session
    /// thread, which holds the phone's HELLO_ACK until this returns; the question itself is shown on this
    /// context's thread, which owns every window the app has. No answer within <see cref="TrustPromptTimeout"/>
    /// is a no, and so is a session that ends meanwhile (<paramref name="ended"/>: a hold, a hang-up), which also
    /// closes the question. The phone is named as the status line names it once it has connected (SessionHost.Shown).
    /// </summary>
    private bool AskTrust(string address, CancellationToken ended)
    {
        var phone = host.Shown(address);
        SetStatus($"Asking whether to trust {phone}");
        var deadline = Environment.TickCount64 + (long)TrustPromptTimeout.TotalMilliseconds;
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.Post(
            _ =>
            {
                // Only while the session is still waiting: a question whose answer nobody would read must not
                // be left on screen for the owner to answer.
                if (!answer.Task.IsCompleted && !ended.IsCancellationRequested)
                {
                    // The dialog may open behind whatever has focus, since a tray app cannot take the
                    // foreground; the notification is what the owner notices.
                    icon.ShowBalloonTip(0, "Edgepad", $"{phone} wants to connect. Choose Trust or Don't trust.", ToolTipIcon.Info);
                    answer.TrySetResult(ShowTrustPrompt(phone, deadline, ended));
                }
            },
            null);

        bool trusted;
        try
        {
            trusted = answer.Task.Wait(TrustPromptTimeout, ended) && answer.Task.Result;
        }
        catch (OperationCanceledException)
        {
            trusted = false;
        }

        answer.TrySetResult(false);
        Log.Write(trusted ? $"The owner trusted {address}" : $"The owner did not trust {address}");
        return trusted;
    }

    /// <summary>
    /// The question itself: Trust or Don't trust, with Don't trust the default so a stray Enter trusts nothing.
    /// It answers itself with Don't trust at <paramref name="deadline"/>, when the session stops waiting, or once
    /// <paramref name="ended"/> is cancelled.
    /// </summary>
    private static bool ShowTrustPrompt(string address, long deadline, CancellationToken ended)
    {
        var yes = new TaskDialogButton("Trust");
        var no = new TaskDialogButton("Don't trust");
        var page = new TaskDialogPage
        {
            Caption = "Edgepad",
            Heading = $"Trust {address}?",
            Text = "A paired phone is asking to control this laptop's pointer and keyboard. Trust it only if it is "
                + $"yours. With no answer in {(int)TrustPromptTimeout.TotalSeconds} seconds, it is not trusted.",
            Icon = TaskDialogIcon.Shield,
            AllowCancel = true,
            Buttons = { yes, no },
            DefaultButton = no,
        };

        // Polled on this thread rather than registered on the token, so the click can never land on a dialog that
        // has already closed.
        using var timeout = new System.Windows.Forms.Timer { Interval = 250 };
        timeout.Tick += (_, _) =>
        {
            if (ended.IsCancellationRequested || Environment.TickCount64 >= deadline)
            {
                timeout.Stop();
                no.PerformClick();
            }
        };
        timeout.Start();
        try
        {
            return TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen) == yes;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Refusing is the safe answer when the question cannot even be put.
            Log.Write($"Could not ask whether to trust {address}: {e.Message}");
            return false;
        }
    }

    // SystemEvents raises on its own thread; the icon is the UI thread's.
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            ui.Post(_ => ApplyTrayIcon(), null);
        }
    }

    /// <summary>Shows the tile that suits the taskbar. Anything missing or failing leaves the icon as it is.</summary>
    private void ApplyTrayIcon()
    {
        try
        {
            var name = Palette.TaskbarIsDark ? "Edgepad.Assets.tray-dark.ico" : "Edgepad.Assets.tray-light.ico";
            using var stream = typeof(TrayContext).Assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                return;
            }

            var next = new Icon(stream);
            icon.Icon = next;
            themed?.Dispose();
            themed = next;
        }
        catch (Exception e) when (e is ArgumentException or IOException or Win32Exception)
        {
            Log.Write($"The tray icon could not follow the taskbar theme: {e.Message}");
        }
    }

    protected override void ExitThreadCore()
    {
        icon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            icon.Dispose();
            themed?.Dispose();
            menu.Dispose();
            startWithWindows.Dispose();
            macrosRow.Dispose();
            securityRow.Dispose();
            forget.Dispose();
            networkRow.Dispose();
            firewallRow.Dispose();
            clipboardRow.Dispose();
            update.Dispose();
            updateTimer.Dispose();
            server.Dispose();
            network.Dispose();

            // After both servers, so no connection arrives once the sessions are ended.
            host.Dispose();
            clipboard.Dispose();
            media.Dispose();
            brightness.Dispose();
            speakers.Dispose();
            microphone.Dispose();
            display.Dispose();
            overlay.Dispose();
        }

        base.Dispose(disposing);
    }
}

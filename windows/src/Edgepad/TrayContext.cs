using System.ComponentModel;
using System.Diagnostics;
using Edgepad.Blob;
using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
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
    private const string DocumentationUrl = "https://edgepad.akshitbansal.dev/docs";

    // ViGEmBus is a separate install the owner makes; this is where it is downloaded from.
    private const string DriverUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";

    // A connection that drops within this long of launch is not worth a word: the app has only just started.
    private const long SettleMs = 8000;

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
            + "Nothing connects without the pairing code typed here. Windows will ask to run Command Prompt once; "
            + "that adds the Edgepad firewall rule.";

    private const string ClipboardNotice = "Share clipboard: copy on one, paste on the other, text and images. It stays off until you tick it.";

    private const string WhereItLives =
        "Edgepad runs in the taskbar corner. If you do not see its icon, click ^ and drag it out. To use Bluetooth, "
            + "pair the phone in Windows Bluetooth settings first, then open Edgepad on the phone.";

    // Lucide's clipboard, on the same grid as the paths in Ui/Icons.
    private const string ClipboardGlyph = "M9 2h6a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1H9a1 1 0 0 1 -1 -1V3a1 1 0 0 1 1 -1z"
        + "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2H6a2 2 0 0 1 -2 -2V6a2 2 0 0 1 2 -2h2";

    // An ampersand marks the row's access key (Windows menu convention); each letter is used once.
    private readonly MenuRow startWithWindows = new("S&tart with Windows", Icons.Power) { CheckOnClick = true, Switch = true };
    private readonly MenuRow macrosRow = new("&Macros…", Icons.Macro);
    private readonly MenuRow securityRow = new("S&ecurity…", Icons.Smartphone);
    private readonly MenuRow networkRow = new("&Wi-Fi and USB connections", Icons.Wifi) { CheckOnClick = true, Switch = true };
    private readonly MenuRow networkProblem = new("Wi-Fi and USB", Icons.Wifi) { Danger = true, Available = false };
    private readonly MenuRow firewallRow = new("Allow through Windows &Firewall…", Icons.Shield) { Available = false };
    private readonly MenuRow bluetoothRow = new("Open &Bluetooth settings", Icons.Smartphone) { Available = false };
    private readonly MenuRow clipboardRow = new("&Share clipboard", ClipboardGlyph)
    {
        CheckOnClick = true,
        Switch = true,
        ToolTipText = "Copy on one, paste on the other: text and images.",
    };

    private readonly MenuRow checkUpdates = new("&Check for updates daily", Icons.Download) { CheckOnClick = true, Switch = true };
    private readonly MenuRow openLog = new("Open &log", Icons.FileText);
    private readonly MenuRow update = new("&Update available", Icons.Download) { Available = false };
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = (int)UpdateCheck.Every.TotalMilliseconds };
    private readonly TrayMenu menu = new();
    private readonly NotifyIcon icon;
    private readonly SynchronizationContext ui;
    private Icon? themed;

    // The tray icon with its "connected" dot, and the handle GetHicon made for it, which nothing else frees.
    private Icon? dotted;
    private nint dottedHandle;

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
    private readonly UpdateSetting updateSetting;
    private readonly WindowsClipboard clipboard;

    // What is wrong with the Wi-Fi and USB listener, if anything (NetworkStatus). Only the UI thread touches it.
    private string? networkTrouble;

    // True once the firewall step did not go in during this run: its row is shown until a later step does. UI thread only.
    private bool firewallRefused;

    // True from the question being put until the firewall step is over: nothing may listen before the rule exists, or
    // Windows' own alert appears as well. Only the UI thread touches it.
    private bool allowingNetwork;

    private readonly StatusMemory statusMemory = new();
    private readonly long startedAt = Environment.TickCount64;
    private bool wasConnected;
    private bool announcedDriver;

    // What the header says now, so the quiet period's line can be taken down when it ends.
    private string? shownNow;

    // Until when a phone dropping is the owner's own doing (a Forget, the Security window), so it is not announced.
    private long ownHangUpUntil;

    // What clicking the balloon on screen does. UI thread only.
    private Action? balloonAction;

    // The menu stays clickable under the Security window, and a second one would lift the hold while the first was
    // still open. All of this runs on the UI thread, so a plain bool is enough.
    private bool securing;

    public TrayContext(EventWaitHandle quit)
    {
        // First, because the menu's Opening handler below reads it.
        networkSettings = NetworkSettings.ForCurrentUser(pin.Folder);
        firewallMemory = FirewallMemory.ForCurrentUser(pin.Folder);
        clipboardSetting = ClipboardSetting.ForCurrentUser(pin.Folder);
        updateSetting = UpdateSetting.ForCurrentUser(pin.Folder);

        // An install from before the first-run question asked about the clipboard had it on without a file. It keeps
        // what it had, written down so it is a stored value from now on; only an install that never answered asks.
        if (networkSettings.HasChoice && !clipboardSetting.HasChoice)
        {
            clipboardSetting.Set(true);
        }

        // Click, not CheckedChanged: the menu sets Checked from the registry each time it opens, and that must
        // read the key without writing it back — least of all deleting it after a failed read.
        startWithWindows.Click += (_, _) => SaveStartWithWindows(startWithWindows.Checked);
        macrosRow.Click += (_, _) => MacroEditor.Show(macros);
        securityRow.Click += (_, _) => ShowSecurity();
        networkRow.Click += (_, _) => ToggleNetwork(networkRow.Checked);
        firewallRow.Click += (_, _) => AllowFirewall(ReportFirewall);
        clipboardRow.Click += (_, _) => SaveClipboard(clipboardRow.Checked);
        checkUpdates.Click += (_, _) => SaveCheckUpdates(checkUpdates.Checked);
        update.Click += (_, _) => Open(UpdateCheck.LatestUrl, "the release page");
        bluetoothRow.Click += (_, _) => Open("ms-settings:bluetooth", "Bluetooth settings");
        networkProblem.Click += (_, _) => OpenLog();
        openLog.Click += (_, _) => OpenLog();
        VirtualPad.DriverMissing += OnDriverMissing;

        // The header is the menu's own. Under it, in the order the plan fixes: the update, then whatever is wrong or
        // needs doing (rows that stay hidden while all is well), then the switches, the windows, the help, and Quit.
        menu.Items.Add(update);
        menu.Items.Add(bluetoothRow);
        menu.Items.Add(networkProblem);
        menu.Items.Add(firewallRow);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(networkRow);
        menu.Items.Add(clipboardRow);
        menu.Items.Add(startWithWindows);
        menu.Items.Add(checkUpdates);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(macrosRow);
        menu.Items.Add(securityRow);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(Row("&Documentation", Icons.BookOpen, () => Open(DocumentationUrl, "the documentation"), new Uri(DocumentationUrl).Host));
        menu.Items.Add(openLog);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(Row("&Quit Edgepad", Icons.Close, ExitThread));

        // Read the settings each time the menu opens, so it never shows a stale state.
        menu.Opening += (_, _) =>
        {
            startWithWindows.Checked = RunAtLogin.IsEnabled;
            securityRow.Hint = SecurityWindow.LevelName(pin.Level);
            networkRow.Checked = networkSettings.Load().Enabled;
            networkRow.Enabled = !allowingNetwork;
            networkProblem.Text = Mnemonic.Escape(networkTrouble ?? string.Empty);
            networkProblem.Available = networkRow.Checked && networkTrouble is not null;
            firewallRow.Available = networkRow.Checked && firewallRefused && !allowingNetwork;
            bluetoothRow.Available = trust.TrustedPhones.Count == 0;
            clipboardRow.Checked = clipboardSetting.Enabled;
            checkUpdates.Checked = updateSetting.Enabled;
            openLog.Enabled = File.Exists(Log.FilePath);
            macrosRow.Hint = $"{macros.Macros.Count} of {MacroStore.MaxMacros}";
            menu.Restyle();
        };

        // The exe's own icon (ApplicationIcon in the project), so the tray shows the Edgepad mark.
        var mark = Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
        icon = new NotifyIcon { Icon = mark ?? SystemIcons.Application, Text = "Edgepad", ContextMenuStrip = menu, Visible = true };
        icon.BalloonTipClicked += (_, _) =>
        {
            var act = balloonAction;
            balloonAction = null;
            act?.Invoke();
        };

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
            AnnounceMacro,
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
            ui.Post(_ => AskFirstRun(), null);
        }

        _ = StartMediaAsync();

        // Once now and once a day after, for a tray app that is left running for weeks; neither when the owner has
        // turned "Check for updates daily" off.
        updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        if (updateSetting.Enabled)
        {
            updateTimer.Start();
            _ = CheckForUpdateAsync();
        }

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
    /// once. The row, and the balloon, open the release page: this copy may have been installed by winget, which keeps
    /// its own record of the version, so the app does not replace itself.
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
                    var label = $"&Update available: {version}";
                    var announced = update.Available && update.Text == label;
                    update.Text = label;
                    update.Available = true;
                    if (!announced)
                    {
                        Log.Write($"Edgepad {version} is available");
                        Balloon(
                            $"Edgepad {version} is available. Click to open the release page; download it and run it to replace this copy.",
                            ToolTipIcon.Info,
                            () => Open(UpdateCheck.LatestUrl, "the release page"));
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

    /// <summary>One notice, and what clicking it does. The Windows shell shows one balloon at a time, so the last wins.</summary>
    private void Balloon(string text, ToolTipIcon kind, Action? onClick = null)
    {
        balloonAction = onClick;
        icon.ShowBalloonTip(0, "Edgepad", text, kind);
    }

    private void SaveStartWithWindows(bool on)
    {
        if (RunAtLogin.Set(on))
        {
            return;
        }

        // The tick must not claim what the registry refused: policy can block the Run key, and the owner would find
        // out at the next sign-in, when Edgepad is not there.
        startWithWindows.Checked = !on;
        Balloon("Start with Windows could not be saved, so Edgepad will not start by itself. The log has the reason.", ToolTipIcon.Warning);
    }

    /// <summary>
    /// The Wi-Fi and USB switch. Turning it on says first what will happen; a no there leaves it off.
    /// </summary>
    private void ToggleNetwork(bool on)
    {
        if (on && AskNetwork(firstRun: false) is not { On: true })
        {
            networkRow.Checked = false;
            return;
        }

        SaveNetwork(on);
    }

    /// <summary>
    /// The first launch's one screen, with Not now the default so a stray Enter opens nothing. Nothing listens until it is
    /// answered, and closing it is not an answer. Not now is saved, so it is never asked again; the switch in the menu
    /// is the way back. It carries the clipboard question too, a separate choice: nothing is shared until it is ticked.
    /// </summary>
    private void AskFirstRun()
    {
        allowingNetwork = true;
        (bool On, bool Share)? answer;
        try
        {
            answer = AskNetwork(firstRun: true);
        }
        finally
        {
            allowingNetwork = false;
        }

        if (answer is not { } chosen)
        {
            // Nothing listens, and the next launch asks again.
            return;
        }

        if (!clipboardSetting.Set(chosen.Share))
        {
            Balloon("Share clipboard changed for now, but could not be saved, so the next launch may not remember it.", ToolTipIcon.Warning);
        }

        // A dialog holds one tick box, so this third choice is its own short question. Off unless chosen.
        if (AskStartWithWindows() && !RunAtLogin.Set(true))
        {
            Balloon("Start with Windows could not be saved, so Edgepad will not start by itself. The log has the reason.", ToolTipIcon.Warning);
        }

        SaveNetwork(chosen.On);
    }

    /// <summary>The first run's third question, with Not now the default. False when declined or when it cannot be put.</summary>
    private static bool AskStartWithWindows()
    {
        var start = new TaskDialogButton("Start with Windows");
        var notNow = new TaskDialogButton("Not now");
        var page = new TaskDialogPage
        {
            Caption = "Edgepad",
            Heading = "Start Edgepad when you sign in?",
            Text = "Edgepad then waits in the taskbar corner for your phone, with no window. You can change this later in its menu.",
            Icon = TaskDialogIcon.Information,
            Buttons = { start, notNow },
            DefaultButton = notNow,
        };

        try
        {
            return TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen) == start;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            Log.Write($"Could not ask about starting with Windows: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// The Wi-Fi and USB question, as one dialog wherever it is asked from. First run adds where Edgepad lives and the
    /// clipboard choice; null means the dialog could not be shown.
    /// </summary>
    private static (bool On, bool Share)? AskNetwork(bool firstRun)
    {
        var allow = new TaskDialogButton(firstRun ? "Allow" : "Turn on");
        var notNow = new TaskDialogButton(firstRun ? "Not now" : "Cancel");
        var share = firstRun ? new TaskDialogVerificationCheckBox("Share clipboard") : null;
        var page = new TaskDialogPage
        {
            Caption = firstRun ? "Edgepad" : "Wi-Fi and USB",
            Heading = firstRun ? "Let phones find this laptop?" : "Turn on Wi-Fi and USB connections?",
            Text = firstRun ? $"{NetworkNotice}\n\n{ClipboardNotice}\n\n{WhereItLives}" : NetworkNotice,
            Icon = TaskDialogIcon.Shield,
            Buttons = { allow, notNow },
            DefaultButton = notNow,
            Verification = share,
        };

        try
        {
            var on = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen) == allow;
            return (on, share?.Checked ?? false);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            Log.Write($"Could not ask about Wi-Fi and USB phones: {e.Message}");
            return null;
        }
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
                "Wi-Fi and USB",
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
        Balloon("This version of Edgepad is a new file, so Windows will ask once to let phones reach it.", ToolTipIcon.Info);
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
    /// launch; the menu's Allow row, shown after a no, is the way back.
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

    /// <summary>Says a macro was added from the phone. Called from a session's thread, so the balloon is posted to the UI thread.</summary>
    private void AnnounceMacro(string name) =>
        ui.Post(_ => Balloon($"Added {name} from your phone", ToolTipIcon.Info), null);

    /// <summary>One notice when the rule did not go in, a declined prompt among the reasons. Asks nothing again.</summary>
    private void ReportFirewall(bool allowed)
    {
        firewallRefused = !allowed;
        if (!allowed)
        {
            Balloon(
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
            Balloon("Share clipboard changed for now, but could not be saved, so the next launch may not remember it.", ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// The Check for updates daily switch. Off stops the daily timer; on starts it and checks once now. It holds from
    /// this moment whether or not it could be saved.
    /// </summary>
    private void SaveCheckUpdates(bool on)
    {
        if (!updateSetting.Set(on))
        {
            Balloon("Check for updates daily changed for now, but could not be saved, so the next launch may not remember it.", ToolTipIcon.Warning);
        }

        if (on)
        {
            updateTimer.Start();
            _ = CheckForUpdateAsync();
        }
        else
        {
            updateTimer.Stop();
        }
    }

    /// <summary>Listens or stops as network.txt says, and notes why not if it cannot. On the UI thread only.</summary>
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
            networkTrouble = null;
            return;
        }

        networkTrouble = NetworkStatus.Problem(network.Start(config.Port), network.IsAnnounced);
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
        _ => Balloon($"{phoneName} wants to pair. Type the pairing code it shows.", ToolTipIcon.Info),
        null);

    /// <summary>A phone asked for the controller and there is no driver. Once per run; clicking it opens the download.</summary>
    private void OnDriverMissing() => ui.Post(
        _ =>
        {
            if (announcedDriver)
            {
                return;
            }

            announcedDriver = true;
            Balloon("The controller needs the ViGEmBus driver. Click to open the download page.", ToolTipIcon.Info, () => Open(DriverUrl, "the driver page"));
        },
        null);

    private void SetStatus(string text) => ui.Post(_ => ApplyStatus(text), null);

    /// <summary>The header, the tooltip, the icon's dot and the connect/drop notice, for one status. On the UI thread only.</summary>
    private void ApplyStatus(string text)
    {
        var now = Environment.TickCount64;
        if (statusMemory.ShouldAnnounce(text, now))
        {
            Balloon(text, ToolTipIcon.Warning);
        }

        var shown = statusMemory.Resolve(text, now);
        shownNow = shown;
        menu.Header.SetStatus(shown);
        icon.Text = TrayStatus.Tooltip(shown, MaxTooltipLength);

        var connected = LaptopStatus.IsConnected(shown);
        if (connected == wasConnected)
        {
            return;
        }

        wasConnected = connected;
        ApplyTrayIcon();
        if (now - startedAt < SettleMs)
        {
            return;
        }

        // A drop the owner caused (Forget, the Security window) needs no pill; any other is the one they want to see.
        if (connected)
        {
            overlay.ShowNotice("Phone connected");
        }
        else if (!securing && now >= ownHangUpUntil)
        {
            overlay.ShowNotice("Phone disconnected");
        }
    }

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

    /// <summary>The trusted phones as the Security window lists them, newest knowledge first: a name Windows or a pairing gave.</summary>
    private List<PhoneEntry> TrustedPhones()
    {
        var pairings = pairedLinks.List();
        return [.. trust.TrustedPhones.Select(address => PhoneEntry.Describe(address, pairings, host.Shown))];
    }

    /// <summary>
    /// Forgets one trusted phone from the Security window, where connections are already held off. Its Wi-Fi and USB
    /// pairing goes with it, and the PIN-verified list is emptied, so a phone that is trusted again meets the PIN once
    /// more instead of skipping it on one mistaken yes. False, with the owner told why, when it could not be done.
    /// </summary>
    private bool ForgetPhone(IWin32Window owner, PhoneEntry phone)
    {
        var forgotten = trust.ForgetOne(phone.Address);
        var unpaired = phone.LinkId is not { } id || pairedLinks.ForgetOne(id);
        var verifiedForgotten = pin.ForgetVerified();

        // Started before the hold ends, so there is no instant in which this phone would be taken again.
        HangUpForgotten();
        if (forgotten && unpaired && verifiedForgotten)
        {
            Balloon($"Forgot {phone.Name}. Connect again from the phone in about {(int)QuietAfterForget.TotalSeconds} seconds.", ToolTipIcon.Info);
            return true;
        }

        MessageBox.Show(
            owner,
            $"Try Forget again once nothing else is using Edgepad's files in {pin.Folder}. {phone.Name} could not be fully forgotten: "
                + "a file could not be rewritten. It stays trusted on disk, but Edgepad refuses it until Edgepad is restarted, and it will "
                + "be trusted again from then on unless you forget it again.",
            "Security",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        return false;
    }

    /// <summary>
    /// Forgets every trusted phone, once the Security window's Forget all question has been answered Forget. Each phone has
    /// to be trusted again, the one connected included. Connections are refused for the quiet period after it. If a file
    /// cannot be removed the owner is told, and the store refuses every phone until a later Forget succeeds.
    /// </summary>
    private void ForgetAllPhones(IWin32Window owner)
    {
        var count = trust.TrustedPhones.Count;
        var forgotten = trust.Forget();

        // A forgotten phone that had entered the PIN would otherwise skip it at level 2 after one mistaken yes.
        var verifiedForgotten = pin.ForgetVerified();

        // Started before the hold ends, so there is no instant in which a connection would be taken.
        HangUpForgotten();
        if (!forgotten)
        {
            MessageBox.Show(
                owner,
                $"Close whatever is holding the trusted phones' file, then choose Forget all again, or delete it yourself:\n\n{trust.FilePath}\n\n"
                    + "The trusted phones could not be forgotten: their file could not be removed or emptied. No phone can "
                    + "connect while Edgepad stays running, but those phones will be trusted again the next time Edgepad starts.",
                "Security",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        // The Wi-Fi and USB pairings too, or a forgotten phone would resume its link and only meet the trust
        // question, not a new pairing.
        ForgetPairedLinks(owner);

        if (!verifiedForgotten)
        {
            MessageBox.Show(
                owner,
                $"Choose Forget all again, or delete pin-verified.txt yourself from:\n\n{pin.Folder}\n\n"
                    + "The phones that entered the PIN could not be forgotten: their list could not be removed. While "
                    + "Edgepad stays running every phone is asked for the PIN on every connection, but those phones "
                    + "will skip it at level 2 again the next time Edgepad starts.",
                "Security",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        if (forgotten)
        {
            var plural = count == 1 ? "phone" : "phones";
            Balloon($"Forgot {count} {plural}. Connect again from the phone in about {(int)QuietAfterForget.TotalSeconds} seconds.", ToolTipIcon.Info);
        }
    }

    /// <summary>Hangs up on the phones and says, in the header, when they may connect again.</summary>
    private void HangUpForgotten()
    {
        host.HangUp(QuietAfterForget);
        var until = Environment.TickCount64 + (long)QuietAfterForget.TotalMilliseconds;
        ownHangUpUntil = until + 2000;
        statusMemory.Clear();
        statusMemory.Quiet($"Phones can connect again at {DateTime.Now.Add(QuietAfterForget):t}", until);

        // Nothing posts a status when the quiet period ends, so a line that names a time would outlive it.
        _ = Task.Delay(QuietAfterForget + TimeSpan.FromSeconds(1)).ContinueWith(
            t => ui.Post(
                _ =>
                {
                    if (shownNow?.StartsWith("Phones can connect again", StringComparison.Ordinal) == true)
                    {
                        ApplyStatus(LaptopStatus.Waiting);
                    }
                },
                null),
            TaskScheduler.Default);
    }

    /// <summary>
    /// Forgets the Wi-Fi and USB pairings, and tells the owner when they could not be. Every way of forgetting phones
    /// (Forget all, Reset PIN in the Security window) goes through here.
    /// </summary>
    private void ForgetPairedLinks(IWin32Window owner)
    {
        if (pairedLinks.Forget())
        {
            return;
        }

        MessageBox.Show(
            owner,
            $"Choose Forget all again, or delete paired-links.txt yourself from:\n\n{pin.Folder}\n\n"
                + "The Wi-Fi and USB pairings could not be forgotten: their file could not be removed or emptied. "
                + "Paired phones cannot reconnect over Wi-Fi or USB while Edgepad stays running, but they will "
                + "be able to again the next time it starts.",
            "Security",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    /// <summary>
    /// The Security window, with connections refused while it is open: a connected phone is a keyboard and could
    /// otherwise answer it, or change the level it is held by. The window says so. This is also where trusted phones are
    /// listed and forgotten.
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
            var phones = new PhoneActions(TrustedPhones, ForgetPhone, ForgetAllPhones);
            using var window = new SecurityWindow(pin, trust, phones, ForgetPairedLinks, HangUpForgotten);
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
                    // The dialog is kept above other windows, but a game or full-screen video may still hide it,
                    // so the notification is there too.
                    Balloon($"{phone} wants to connect. Choose Trust or Don't trust.", ToolTipIcon.Info);
                    answer.TrySetResult(ShowTrustPrompt(phone, address, deadline, ended));
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
    /// The question itself: "Trust NAME?" with the address as secondary text, Trust or Don't trust, and Don't trust the
    /// default so a stray Enter trusts nothing. It stays above other windows, counts the seconds down, and answers
    /// itself with Don't trust at <paramref name="deadline"/>, when the session stops waiting, or once
    /// <paramref name="ended"/> is cancelled.
    /// </summary>
    private static bool ShowTrustPrompt(string phone, string address, long deadline, CancellationToken ended)
    {
        var yes = new TaskDialogButton("Trust");
        var no = new TaskDialogButton("Don't trust");
        string Words() =>
            (phone == address ? string.Empty : $"{address}\n\n")
            + "A paired phone is asking to control this laptop's pointer and keyboard. Trust it only if it is yours. "
            + $"With no answer in {PairingPrompt.SecondsLeft(deadline, Environment.TickCount64)} seconds, it is not trusted.";
        var page = new TaskDialogPage
        {
            Caption = "Edgepad",
            Heading = $"Trust {phone}?",
            Text = Words(),
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
                return;
            }

            page.Text = Words();
        };
        timeout.Start();

        // A tray app cannot take the foreground, so the dialog gets a topmost, invisible owner: a dialog stays above
        // its owner, and the question is no longer opened behind whatever has focus while its clock runs.
        using var owner = new Form
        {
            TopMost = true,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            Opacity = 0,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
        };
        try
        {
            owner.Show();
            return TaskDialog.ShowDialog(owner, page, TaskDialogStartupLocation.CenterScreen) == yes;
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

    /// <summary>
    /// Shows the tile that suits the taskbar, with a dot in the corner while a phone is connected, since across the room
    /// the icon is all the owner sees. Anything missing or failing leaves the icon as it is.
    /// </summary>
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
            var oldDot = dotted;
            var oldHandle = dottedHandle;
            dotted = null;
            dottedHandle = 0;
            if (wasConnected)
            {
                using var bitmap = next.ToBitmap();
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var size = Math.Max(4, bitmap.Width * 0.42f);
                    var dot = new RectangleF(bitmap.Width - size, bitmap.Height - size, size - 1, size - 1);
                    using var ring = new SolidBrush(Palette.TaskbarIsDark ? Color.Black : Color.White);
                    using var fill = new SolidBrush(Color.FromArgb(0x22, 0xA5, 0x59));
                    graphics.FillEllipse(ring, RectangleF.Inflate(dot, 1.2f, 1.2f));
                    graphics.FillEllipse(fill, dot);
                }

                dottedHandle = bitmap.GetHicon();
                dotted = Icon.FromHandle(dottedHandle);
                icon.Icon = dotted;
            }
            else
            {
                icon.Icon = next;
            }

            themed?.Dispose();
            themed = next;
            oldDot?.Dispose();
            if (oldHandle != 0)
            {
                Theme.DestroyIcon(oldHandle);
            }
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
            VirtualPad.DriverMissing -= OnDriverMissing;
            icon.Dispose();
            themed?.Dispose();
            dotted?.Dispose();
            if (dottedHandle != 0)
            {
                Theme.DestroyIcon(dottedHandle);
            }

            menu.Dispose();
            startWithWindows.Dispose();
            macrosRow.Dispose();
            securityRow.Dispose();
            networkRow.Dispose();
            networkProblem.Dispose();
            firewallRow.Dispose();
            bluetoothRow.Dispose();
            clipboardRow.Dispose();
            checkUpdates.Dispose();
            openLog.Dispose();
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

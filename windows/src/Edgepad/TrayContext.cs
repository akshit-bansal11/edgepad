using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Edgepad.Blob;
using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Dispatch;
using Edgepad.Gamepad;
using Edgepad.Injection;
using Edgepad.Macros;
using Edgepad.Net;
using Edgepad.Network;
using Edgepad.Startup;
using Edgepad.Transfer;
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

    // An ampersand marks the row's access key (Windows menu convention); each letter is used once.
    private readonly MenuRow startWithWindows = new(Strings.Menu_StartWithWindows, Icons.Power) { CheckOnClick = true, Switch = true };
    private readonly MenuRow macrosRow = new(Strings.Menu_Macros, Icons.Macro);
    private readonly MenuRow securityRow = new(Strings.Menu_Security, Icons.Smartphone);
    private readonly MenuRow networkRow = new(Strings.Menu_Network, Icons.Wifi) { CheckOnClick = true, Switch = true };
    private readonly MenuRow networkProblem = new(Strings.Network_Title, Icons.Wifi) { Danger = true, Available = false };
    private readonly MenuRow firewallRow = new(Strings.Menu_Firewall, Icons.Shield) { Available = false };
    private readonly MenuRow restartAdminRow = new(Strings.Menu_RestartAdmin, Icons.Shield)
    {
        Available = !Environment.IsPrivilegedProcess,
        ToolTipText = Strings.Menu_RestartAdminTip,
    };

    private readonly MenuRow bluetoothRow = new(Strings.Menu_BluetoothSettings, Icons.Smartphone) { Available = false };
    private readonly MenuRow clipboardRow = new(Strings.Menu_ShareClipboard, Icons.Clipboard)
    {
        CheckOnClick = true,
        Switch = true,
        ToolTipText = Strings.Menu_ShareClipboardTip,
    };

    private readonly MenuRow clipHistoryRow = new(Strings.Menu_ClipboardHistory, Icons.Clipboard) { ToolTipText = Strings.Menu_ClipboardHistoryTip };

    // In memory only, and only while Share clipboard is on. UI thread only.
    private readonly ClipboardHistory history = new();
    private readonly ClipboardHotkey? hotkey;
    private ClipboardPanel? clipPanel;

    private readonly MenuRow sendFileRow = new(Strings.Menu_SendFile, Icons.Download);
    private readonly SendPipe? sendPipe;

    // Sending and receiving files both report to one feed; the toast shows it. Built in the constructor, on the UI thread.
    private readonly TransferFeed feed = new();
    private readonly SendQueue queue;
    private readonly TransferToast toast;

    // A receive batch stays open until 2 s after its last file; the timer exists only while one is open. UI thread only.
    private readonly System.Windows.Forms.Timer receiveEnd = new() { Interval = 2000 };
    private bool receiving;
    private string? receivingName;
    private readonly MenuRow checkUpdates = new(Strings.Menu_CheckUpdates, Icons.Download) { CheckOnClick = true, Switch = true };
    private readonly MenuRow openLog = new(Strings.Menu_OpenLog, Icons.FileText);
    private readonly TrayMenu settingsMenu = new(withHeader: false);
    private readonly MenuRow settingsRow = new(Strings.Menu_Settings, Icons.Sliders);
    private readonly MenuRow update = new(Strings.Menu_Update, Icons.Download) { Available = false };
    private readonly MenuRow installRow = new(Strings.Menu_UpdateInstall, Icons.Download) { Available = false };
    private readonly MenuRow laterRow = new(Strings.Menu_UpdateLater, Icons.Download) { Available = false };
    private readonly MenuRow skipRow = new(Strings.Menu_UpdateSkip, Icons.Download) { Available = false };
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
    private readonly UpdateChoice updateChoice;

    // The release the update rows offer, whether the tray icon carries the update dot, and whether an install is under way. UI thread only.
    private Version? offered;
    private bool updateReady;
    private bool installing;
    private readonly WindowsClipboard clipboard;

    // What is wrong with the Wi-Fi and USB listener, if anything (NetworkStatus). Only the UI thread touches it.
    private string? networkTrouble;

    // True once the firewall step did not go in during this run: its row is shown until a later step does. UI thread only.
    private bool firewallRefused;

    // True from the question being put until the firewall step is over: nothing may listen before the rule exists, or
    // Windows' own alert appears as well. Only the UI thread touches it.
    private bool allowingNetwork;

    private readonly TrayStatusMemory statusMemory = new();
    private readonly long startedAt = Environment.TickCount64;
    private bool wasConnected;
    private bool announcedDriver;

    // What the header says now, so the quiet period's line can be taken down when it ends.
    private LaptopStatus? shownNow;

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
        updateChoice = new UpdateChoice(Path.Combine(pin.Folder, "update-choice.txt"));

        // An install from before the first-run question asked about the clipboard had it on without a file. It keeps
        // what it had, written down so it is a stored value from now on; only an install that never answered asks.
        if (networkSettings.HasChoice && !clipboardSetting.HasChoice)
        {
            clipboardSetting.Set(true);
        }

        // Click, not CheckedChanged: the menu sets Checked from the registry each time it opens, and that must
        // read the key without writing it back — least of all deleting it after a failed read.
        startWithWindows.Click += (_, _) => SaveStartWithWindows(startWithWindows.Checked);
        sendFileRow.Click += (_, _) => PickAndSendFile();
        macrosRow.Click += (_, _) => MacroEditor.Show(macros);
        securityRow.Click += (_, _) => ShowSecurity();
        networkRow.Click += (_, _) => ToggleNetwork(networkRow.Checked);
        firewallRow.Click += (_, _) => AllowFirewall(ReportFirewall);
        restartAdminRow.Click += (_, _) => RestartAsAdministrator();
        clipboardRow.Click += (_, _) => SaveClipboard(clipboardRow.Checked);
        clipHistoryRow.Click += (_, _) => ShowClipboardPanel(paste: false);
        checkUpdates.Click += (_, _) => SaveCheckUpdates(checkUpdates.Checked);
        update.Click += (_, _) => Open(UpdateCheck.LatestUrl, "the release page");
        installRow.Click += (_, _) => _ = InstallUpdateAsync();
        laterRow.Click += (_, _) =>
        {
            updateChoice.Later(DateTimeOffset.UtcNow);
            HideUpdateRows();
        };
        skipRow.Click += (_, _) =>
        {
            if (offered is { } version)
            {
                updateChoice.Skip(version);
            }

            HideUpdateRows();
        };
        bluetoothRow.Click += (_, _) => Open("ms-settings:bluetooth", "Bluetooth settings");
        networkProblem.Click += (_, _) => OpenLog();
        openLog.Click += (_, _) => OpenLog();
        VirtualPad.DriverMissing += OnDriverMissing;

        // The rows set once and left alone live one level down, so the menu itself is the things used every day.
        settingsMenu.Items.Add(startWithWindows);
        settingsMenu.Items.Add(checkUpdates);
        settingsMenu.Items.Add(openLog);
        settingsRow.DropDown = settingsMenu;
        settingsMenu.Opening += (_, _) => settingsMenu.Restyle();

        // The header is the menu's own. Under it, in the order the plan fixes: the update, then whatever is wrong or
        // needs doing (rows that stay hidden while all is well), then the switches, the windows, the help, and Quit.
        menu.Items.Add(update);
        menu.Items.Add(installRow);
        menu.Items.Add(laterRow);
        menu.Items.Add(skipRow);
        menu.Items.Add(bluetoothRow);
        menu.Items.Add(networkProblem);
        menu.Items.Add(firewallRow);
        menu.Items.Add(restartAdminRow);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(networkRow);
        menu.Items.Add(clipboardRow);
        menu.Items.Add(clipHistoryRow);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(sendFileRow);
        menu.Items.Add(macrosRow);
        menu.Items.Add(securityRow);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(Row(Strings.Menu_Documentation, Icons.BookOpen, () => Open(DocumentationUrl, "the documentation"), new Uri(DocumentationUrl).Host));
        menu.Items.Add(settingsRow);
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(Row(Strings.Menu_Quit, Icons.Close, ExitThread));

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
            // The panel only lists what sharing recorded, so it is off with sharing (owner, 2026-11-18).
            clipHistoryRow.Enabled = clipboardSetting.Enabled;
            var keyed = hotkey?.Registered == true;
            clipHistoryRow.Hint = keyed ? ClipboardHotkey.Display : null;
            clipHistoryRow.ToolTipText = keyed ? Strings.Menu_ClipboardHistoryTip : Strings.Clip_HotkeyTaken;
            checkUpdates.Checked = updateSetting.Enabled;
            openLog.Enabled = File.Exists(Log.FilePath);
            macrosRow.Hint = Strings.Menu_MacrosCount.Fill(macros.Macros.Count, MacroStore.MaxMacros);
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
        hotkey = new ClipboardHotkey(() => ShowClipboardPanel(paste: true));

        // The taskbar's tile, light or dark, now and whenever Windows' theme changes (that arrives as General).
        ApplyTrayIcon();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;

        // Built here and nowhere else: the overlay captures this thread's synchronisation context, and the
        // frames that ask it to show something arrive on a Bluetooth thread that has none of its own.
        overlay = new LevelOverlay();
        var identity = LinkIdentity.ForCurrentUser(pin.Folder);

        // Here for the same reason: its window, and every clipboard call after it, belong to this thread.
        clipboard = new WindowsClipboard(ui, () => clipboardSetting.Enabled);
        clipboard.Watch(OnLaptopCopy);
        clipboard.PhoneTextWritten += text => ui.Post(_ => history.Record(text, ClipSide.Phone), null);
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
            new WindowsForeground(ui),
            overlay,
            identity,
            clipboard,
            () => clipboardSetting.Enabled,
            AnnounceMacro,
            ui,
            OnSessionEnded,
            onFileArrived: path => ui.Post(_ => OnReceived(path), null),
            onFileProgress: (name, held, size) => ui.Post(_ => OnReceiveProgress(name, held, size), null),
            onFileFailed: (name, code) => ui.Post(_ => OnReceiveFailed(name, code), null));

        // Handlers run under the queue's lock: they only post to the toast, and never call back into the queue.
        toast = new TransferToast
        {
            OnCancel = CancelSends,
            OnRetry = Retry,
            OnClose = () => feed.Dismiss(),
        };
        feed.Changed += toast.Show;
        receiveEnd.Tick += (_, _) => EndReceiveBatch();
        queue = new SendQueue(
            (path, _, ct) => host.SendFile(path, sent => feed.Bytes(sent), ct),
            feed,
            path => File.Exists(path) ? new FileInfo(path).Length : null);
        SendPipe.EnsureShortcut();
        sendPipe = new SendPipe((paths, skipped) => ui.Post(_ => SendFiles(paths, skipped), null));
        server = new RfcommServer(SetStatus, host);
        _ = StartServerAsync();

        // Starts once the owner has said yes (network.txt); only a phone that proves itself is handed to the host.
        network = new NetworkServer(
            identity,
            pairedLinks,
            trust,
            new PairingPrompt(ui, AnnouncePairing),
            host.Accept,
            name => SetStatus(LaptopStatus.Paired(name)),
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

        updateTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        if (!networkSettings.HasChoice)
        {
            // Posted, so it waits for the tray to be up and running rather than holding up this constructor. The update
            // check waits for the answer: its balloon must not be the first thing a new owner sees.
            ui.Post(
                _ =>
                {
                    AskFirstRun();
                    StartUpdateChecks();
                },
                null);
        }
        else
        {
            StartUpdateChecks();
        }

        _ = StartMediaAsync();

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
    /// Asks GitHub which release is newest and, when it is newer than this one and not skipped or put off, shows the
    /// update rows and says so once per version. With no update key embedded the row opens the release page instead.
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

            ui.Post(_ => ShowUpdate(latest), null);
        }
        catch (Exception e)
        {
            // Top-level boundary of a fire-and-forget task: an escaped exception here would be lost silently.
            Log.Write($"The update check failed: {e}");
        }
    }

    /// <summary>Shows the rows for <paramref name="version"/> and the tray dot, and says so once per version. UI thread.</summary>
    private void ShowUpdate(Version version)
    {
        var decision = UpdatePolicy.Decide(version, Program.Version, null, UpdateKey.IsSet);
        if (decision == UpdateDecision.NotNewer || !updateChoice.ShouldShow(version, DateTimeOffset.UtcNow))
        {
            HideUpdateRows();
            return;
        }

        offered = version;
        var canInstall = decision != UpdateDecision.NoKey;
        var text = version.ToString();
        update.Text = Strings.Menu_UpdateVersion.Fill(text);
        update.Available = !canInstall;
        installRow.Text = Strings.Menu_UpdateInstall.Fill(text);
        installRow.Available = canInstall;
        laterRow.Available = true;
        skipRow.Available = true;
        if (!updateReady)
        {
            updateReady = true;
            ApplyTrayIcon();
        }

        if (!updateChoice.WasNotified(version))
        {
            updateChoice.MarkNotified(version);
            Log.Write($"Edgepad {text} is available");
            Balloon(
                canInstall ? Strings.Balloon_UpdateReady.Fill(text) : Strings.Balloon_UpdateAvailable.Fill(text),
                ToolTipIcon.Info,
                canInstall ? () => _ = InstallUpdateAsync() : () => Open(UpdateCheck.LatestUrl, "the release page"));
        }
    }

    private void HideUpdateRows()
    {
        update.Available = false;
        installRow.Available = false;
        laterRow.Available = false;
        skipRow.Available = false;
        if (updateReady)
        {
            updateReady = false;
            ApplyTrayIcon();
        }
    }

    /// <summary>The Install row: confirm a major step, then verify, download and start the new copy. Any failure installs nothing and offers the release page.</summary>
    private async Task InstallUpdateAsync()
    {
        if (installing || offered is not { } version || Environment.ProcessPath is not { } running)
        {
            return;
        }

        installing = true;
        try
        {
            var decision = UpdatePolicy.Decide(version, Program.Version, null, UpdateKey.IsSet);
            if (decision is not (UpdateDecision.Install or UpdateDecision.InstallNeedsMajorConfirm))
            {
                Open(UpdateCheck.LatestUrl, "the release page");
                return;
            }

            if (decision == UpdateDecision.InstallNeedsMajorConfirm
                && !Ask.Confirm(null, Strings.Update_MajorWarning, "Edgepad", MessageBoxIcon.Question, Strings.Update_Install, Strings.Update_NotNow))
            {
                return;
            }

            var outcome = await WindowsUpdater.InstallAsync(version, running, CancellationToken.None);
            if (outcome == UpdateOutcome.Unverified)
            {
                Balloon(Strings.Balloon_UpdateUnverified.Fill(version.ToString()), ToolTipIcon.Warning);
            }
            else if (outcome == UpdateOutcome.Failed)
            {
                Balloon(
                    Strings.Balloon_UpdateFailed.Fill(version.ToString()),
                    ToolTipIcon.Warning,
                    () => Open(UpdateCheck.LatestUrl, "the release page"));
            }
        }
        finally
        {
            installing = false;
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
            SetStatus(LaptopStatus.BluetoothUnavailable);
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

    private void PickAndSendFile()
    {
        using var dialog = new OpenFileDialog { CheckFileExists = true, Multiselect = true };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var (valid, skipped) = SendPipe.ValidateAll(dialog.FileNames);
            SendFiles(valid, skipped);
        }
    }

    /// <summary>Queues files for the phone; a second call while one batch runs adds to it. UI thread.</summary>
    private void SendFiles(IReadOnlyList<string> paths, int skipped)
    {
        if (paths.Count == 0)
        {
            return;
        }

        // No phone: the queue would end at once with every file unsent; a notice is kinder than a toast flash.
        if (host.CurrentAddress is null && !queue.IsRunning)
        {
            overlay.ShowNotice(Strings.Xfer_NoPhone, dropped: true);
            Log.Write("Send to phone: no phone connected");
            return;
        }

        queue.Enqueue(paths, skipped);
    }

    // Receiving. The protocol has no file count, so the batch is a running count that closes 2 s after its last file.
    // While a send runs its events are not shown (one view at a time); an arrival is then told by a balloon.
    private bool ReceiveHidden() => queue.IsRunning;

    private void OpenReceiveBatch(string name, ulong size)
    {
        if (!receiving)
        {
            receiving = true;
            feed.BeginBatch(sending: false, 0);
        }

        if (receivingName is null)
        {
            receivingName = name;
            feed.StartFile(name, size);
        }

        receiveEnd.Stop();
        receiveEnd.Start();
    }

    private void OnReceiveProgress(string name, ulong held, ulong size)
    {
        if (ReceiveHidden())
        {
            return;
        }

        OpenReceiveBatch(name, size);
        feed.Bytes(held);
    }

    private void OnReceived(string path)
    {
        var name = Path.GetFileName(path);
        if (ReceiveHidden() || toast.Stopped)
        {
            Balloon(Strings.Balloon_FileReceived.Fill(FileNames.Middle(name, 32)), ToolTipIcon.Info);
            if (ReceiveHidden())
            {
                return;
            }
        }

        OpenReceiveBatch(name, 0);
        feed.FileDone(receivingName ?? name, null);
        receivingName = null;
    }

    private void OnReceiveFailed(string name, XferCode code)
    {
        Log.Write($"Receive from phone: {code}");
        if (ReceiveHidden())
        {
            return;
        }

        OpenReceiveBatch(name, 0);
        feed.FileDone(receivingName ?? name, TransferOutcome.Of(code) ?? TransferFailure.LinkLost);
        receivingName = null;
    }

    private void EndReceiveBatch()
    {
        receiveEnd.Stop();
        if (!receiving)
        {
            return;
        }

        receiving = false;
        receivingName = null;
        if (!queue.IsRunning)
        {
            feed.EndBatch();
        }
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
        Balloon(Strings.Balloon_StartWithWindowsNotSaved, ToolTipIcon.Warning);
    }

    /// <summary>
    /// Once now and once a day after, for a tray app that is left running for weeks; neither when the owner has turned
    /// "Check for updates daily" off.
    /// </summary>
    private void StartUpdateChecks()
    {
        if (updateSetting.Enabled)
        {
            updateTimer.Start();
            _ = CheckForUpdateAsync();
        }
    }

    /// <summary>
    /// The Wi-Fi and USB switch. Turning it on says first what will happen; a no there leaves it off.
    /// </summary>
    private void ToggleNetwork(bool on)
    {
        // The question or the elevation prompt is still open: a second toggle would start a second AllowThenListen.
        if (allowingNetwork)
        {
            networkRow.Checked = !on;
            return;
        }

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
            Balloon(Strings.Balloon_ShareClipboardNotSaved, ToolTipIcon.Warning);
        }

        // A dialog holds one tick box, so this third choice is its own short question. Off unless chosen.
        if (AskStartWithWindows() && !RunAtLogin.Set(true))
        {
            Balloon(Strings.Balloon_StartWithWindowsNotSaved, ToolTipIcon.Warning);
        }

        SaveNetwork(chosen.On);
    }

    /// <summary>The first run's third question, with Not now the default. False when declined or when it cannot be put.</summary>
    private static bool AskStartWithWindows()
    {
        var start = new TaskDialogButton(Strings.Dialog_StartWithWindows);
        var notNow = new TaskDialogButton(Strings.Common_NotNow);
        var page = new TaskDialogPage
        {
            Caption = "Edgepad",
            Heading = Strings.Dialog_StartHeading,
            Text = Strings.Dialog_StartText,
            Icon = TaskDialogIcon.Information,
            RightToLeftLayout = Theme.IsRtl,
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
        var allow = new TaskDialogButton(firstRun ? Strings.Dialog_Allow : Strings.Dialog_TurnOn);
        var notNow = new TaskDialogButton(firstRun ? Strings.Common_NotNow : Strings.Common_Cancel);
        var share = firstRun ? new TaskDialogVerificationCheckBox(Strings.Dialog_ShareClipboard) : null;
        var page = new TaskDialogPage
        {
            Caption = firstRun ? "Edgepad" : Strings.Network_Title,
            Heading = firstRun ? Strings.Dialog_NetworkFirstHeading : Strings.Dialog_NetworkHeading,
            Text = firstRun ? $"{Strings.Network_Notice}\n\n{Strings.Network_ClipboardNotice}\n\n{Strings.Network_WhereItLives}" : Strings.Network_Notice,
            Icon = TaskDialogIcon.Shield,
            RightToLeftLayout = Theme.IsRtl,
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
            Ask.Message(
                null,
                Strings.Error_SettingNotSaved,
                Strings.Network_Title,
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
        Balloon(Strings.Balloon_NewFile, ToolTipIcon.Info);
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
        ui.Post(_ => Balloon(Strings.Balloon_MacroAdded.Fill(name), ToolTipIcon.Info), null);

    /// <summary>One notice when the rule did not go in, a declined prompt among the reasons. Asks nothing again.</summary>
    private void ReportFirewall(bool allowed)
    {
        firewallRefused = !allowed;
        if (!allowed)
        {
            Balloon(
                Strings.Balloon_FirewallNotChanged,
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
            Balloon(Strings.Balloon_ShareClipboardNotSaved, ToolTipIcon.Warning);
        }

        // Nothing is kept while sharing is off.
        if (!on)
        {
            history.Clear();
            clipPanel?.Close();
        }
    }

    private void OnLaptopCopy()
    {
        if (clipboardSetting.Enabled && clipboard.ReadText() is { } text)
        {
            history.Record(text, ClipSide.Laptop);
        }
    }

    /// <summary>
    /// Win+Alt+V (paste) or the tray row (copy only). A second call while the panel is open closes it. The window to paste
    /// into is taken before the panel activates.
    /// </summary>
    private void ShowClipboardPanel(bool paste)
    {
        if (!clipboardSetting.Enabled)
        {
            return;
        }

        if (clipPanel is { IsDisposed: false } open)
        {
            open.Close();
            return;
        }

        var target = paste ? NativeMethods.GetForegroundWindow() : 0;
        var panelHost = new ClipPanelHost(
            clipboardSetting.Enabled,
            () => shownNow?.IsConnected == true,
            entry => Pick(entry, target),
            async entry =>
            {
                var sent = await host.SendClipboardText(entry.Text) == true;
                overlay.ShowNotice(sent ? Strings.Clip_Sent : Strings.Clip_SendFailed, dropped: !sent);
                return sent;
            },
            entry => history.Remove(entry.Text),
            history.Clear);
        var panel = new ClipboardPanel(history.Items, panelHost);
        panel.Location = ClipPanelPlacement.Place(Cursor.Position, panel.Size, LevelOverlay.Metrics.Where(), 8);
        panel.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(clipPanel, panel))
            {
                clipPanel = null;
            }

            panel.Dispose();
        };
        clipPanel = panel;
        panel.Show();
    }

    /// <summary>
    /// A row was picked: close the panel, put the text on the clipboard without telling the phone, and, when a window
    /// had the focus, give it back and press Ctrl+V once Win/Alt/Shift are up. Otherwise say it is only copied.
    /// </summary>
    private void Pick(ClipEntry entry, nint target)
    {
        clipPanel?.Close();
        clipboard.SetTextQuietly(entry.Text);
        history.Record(entry.Text, entry.Side);
        if (target == 0)
        {
            overlay.ShowNotice(Strings.Clip_CopiedOnly);
            return;
        }

        NativeMethods.SetForegroundWindow(target);
        var timer = new System.Windows.Forms.Timer { Interval = 80 };
        var waited = 0;
        timer.Tick += (_, _) =>
        {
            waited += timer.Interval;
            if (waited < 500 && ModifierDown())
            {
                return;
            }

            timer.Dispose();
            var keys = PasteKeys.CtrlV();
            if (NativeMethods.SendInput((uint)keys.Length, keys, Marshal.SizeOf<NativeInput>()) == 0)
            {
                overlay.ShowNotice(Strings.Clip_CopiedOnly);
            }
        };
        timer.Start();
    }

    // Methods, not lambdas over the field: the toast is built in the constructor before the queue is assigned.
    private void Retry() => queue.Enqueue(queue.FailedPaths);

    private void CancelSends() => queue.Cancel();

    // The high bit (a negative short) means the key is down now.
    private static bool ModifierDown() =>
        NativeMethods.GetAsyncKeyState((int)Keys.LWin) < 0 || NativeMethods.GetAsyncKeyState((int)Keys.RWin) < 0
            || NativeMethods.GetAsyncKeyState((int)Keys.Menu) < 0 || NativeMethods.GetAsyncKeyState((int)Keys.ShiftKey) < 0;

    /// <summary>
    /// The Check for updates daily switch. Off stops the daily timer; on starts it and checks once now. It holds from
    /// this moment whether or not it could be saved.
    /// </summary>
    private void SaveCheckUpdates(bool on)
    {
        if (!updateSetting.Set(on))
        {
            Balloon(Strings.Balloon_CheckUpdatesNotSaved, ToolTipIcon.Warning);
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
    private void AnnouncePairing(string phoneName, Action front) => ui.Post(
        _ => Balloon(Strings.Balloon_WantsToPair.Fill(phoneName), ToolTipIcon.Info, front),
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
            Balloon(Strings.Balloon_DriverMissing, ToolTipIcon.Info, () => Open(DriverUrl, "the driver page"));
        },
        null);

    private void SetStatus(LaptopStatus status) => ui.Post(_ => ApplyStatus(status), null);

    /// <summary>The header, the tooltip, the icon's dot and the connect/drop notice, for one status. On the UI thread only.</summary>
    private void ApplyStatus(LaptopStatus status)
    {
        var now = Environment.TickCount64;
        if (statusMemory.ShouldAnnounce(status, now))
        {
            Balloon(status.Text, ToolTipIcon.Warning);
        }

        var shown = statusMemory.Resolve(status, now);
        shownNow = shown;
        menu.Header.SetStatus(shown);
        icon.Text = TrayStatus.Tooltip(shown.Text, MaxTooltipLength);

        var connected = shown.IsConnected;
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
            overlay.ShowNotice(Strings.Phone_Connected);
        }
        else if (!securing && now >= ownHangUpUntil)
        {
            overlay.ShowNotice(Strings.Phone_Disconnected, dropped: true);
        }
    }

    /// <summary>
    /// Starts a copy with administrator rights and quits this one, so the lock is free for it. Declining the UAC prompt
    /// (error 1223) leaves this copy running.
    /// </summary>
    private void RestartAsAdministrator()
    {
        var sid = SingleInstance.CurrentUserSid();
        var path = Environment.ProcessPath;
        if (sid is null || path is null)
        {
            Log.Write("Restart as administrator: the user or the exe path is unknown");
            return;
        }

        try
        {
            using var started = Process.Start(new ProcessStartInfo(path, SingleInstance.RestartArguments(sid))
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception e) when (e.NativeErrorCode == SingleInstance.UacDeclined)
        {
            return;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Log.Write($"Restart as administrator: {e.Message}");
            return;
        }

        ExitThread();
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
        var last = trust.Connections.Snapshot();
        return [.. trust.TrustedPhones.Select(address => PhoneEntry.Describe(address, pairings, host.Shown) with { Last = last(address) })];
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
            Balloon(Strings.Balloon_ForgotOne.Fill(phone.Name, (int)QuietAfterForget.TotalSeconds), ToolTipIcon.Info);
            return true;
        }

        Ask.Message(
            owner,
            Strings.Error_ForgetOne.Fill(pin.Folder, phone.Name),
            Strings.Security_Title,
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
        var forgotten = trust.Forget();

        // A forgotten phone that had entered the PIN would otherwise skip it at level 2 after one mistaken yes.
        var verifiedForgotten = pin.ForgetVerified();

        // Started before the hold ends, so there is no instant in which a connection would be taken.
        HangUpForgotten();
        if (!forgotten)
        {
            Ask.Message(
                owner,
                Strings.Error_ForgetAllTrust.Fill(trust.FilePath),
                Strings.Security_Title,
                MessageBoxIcon.Error);
        }

        // The Wi-Fi and USB pairings too, or a forgotten phone would resume its link and only meet the trust
        // question, not a new pairing.
        ForgetPairedLinks(owner);

        if (!verifiedForgotten)
        {
            Ask.Message(
                owner,
                Strings.Error_ForgetAllPin.Fill(pin.Folder),
                Strings.Security_Title,
                MessageBoxIcon.Error);
        }

        if (forgotten)
        {
            Balloon(Strings.Balloon_ForgotAll.Fill((int)QuietAfterForget.TotalSeconds), ToolTipIcon.Info);
        }
    }

    /// <summary>Hangs up on the phones and says, in the header, when they may connect again.</summary>
    private void HangUpForgotten()
    {
        host.HangUp(QuietAfterForget);
        var until = Environment.TickCount64 + (long)QuietAfterForget.TotalMilliseconds;
        ownHangUpUntil = until + 2000;
        statusMemory.Clear();
        statusMemory.Quiet(LaptopStatus.QuietUntil($"{DateTime.Now.Add(QuietAfterForget):t}"), until);

        // Nothing posts a status when the quiet period ends, so a line that names a time would outlive it.
        _ = Task.Delay(QuietAfterForget + TimeSpan.FromSeconds(1)).ContinueWith(
            t => ui.Post(
                _ =>
                {
                    if (shownNow?.Kind == StatusKind.QuietUntil)
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

        Ask.Message(
            owner,
            Strings.Error_ForgetLinks.Fill(pin.Folder),
            Strings.Security_Title,
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

        // Windows' name for a Bluetooth phone is looked up as it connects and may not be back yet. This is the session's
        // own thread, which is about to block on the owner anyway, so a short wait costs nothing; no name by then, the
        // address stands in as before. The wait is kept (the balloon and the dialog are built from the name, and a
        // question that renames itself under the owner's eyes is worse), but the header says "asking" at once.
        SetStatus(LaptopStatus.Asking(phone));
        for (var tries = 0; tries < 10 && phone == address && !ended.IsCancellationRequested; tries++)
        {
            Thread.Sleep(200);
            phone = host.Shown(address);
        }

        if (phone != address)
        {
            SetStatus(LaptopStatus.Asking(phone));
        }

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
                    Balloon(Strings.Balloon_WantsToConnect.Fill(phone), ToolTipIcon.Info);
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
        var yes = new TaskDialogButton(Strings.Dialog_Trust);
        var no = new TaskDialogButton(Strings.Dialog_DontTrust);
        string Words() =>
            (phone == address ? string.Empty : $"{address}\n\n")
            + Strings.Dialog_TrustText.Fill(PairingPrompt.SecondsLeft(deadline, Environment.TickCount64));
        var page = new TaskDialogPage
        {
            Caption = "Edgepad",
            Heading = Strings.Dialog_TrustHeading.Fill(phone),
            Text = Words(),
            Icon = TaskDialogIcon.Shield,
            RightToLeftLayout = Theme.IsRtl,
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
            if (wasConnected || updateReady)
            {
                using var bitmap = next.ToBitmap();
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var size = Math.Max(4, bitmap.Width * 0.42f);
                    using var ring = new SolidBrush(Palette.TaskbarIsDark ? Color.Black : Color.White);
                    if (wasConnected)
                    {
                        var dot = new RectangleF(bitmap.Width - size, bitmap.Height - size, size - 1, size - 1);
                        using var fill = new SolidBrush(Theme.Connected);
                        graphics.FillEllipse(ring, RectangleF.Inflate(dot, 1.2f, 1.2f));
                        graphics.FillEllipse(fill, dot);
                    }

                    if (updateReady)
                    {
                        // Top right, opposite the connected dot, so both can show at once.
                        var dot = new RectangleF(bitmap.Width - size, 0, size - 1, size - 1);
                        using var fill = new SolidBrush(Palette.Current.Accent);
                        graphics.FillEllipse(ring, RectangleF.Inflate(dot, 1.2f, 1.2f));
                        graphics.FillEllipse(fill, dot);
                    }
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
            hotkey?.Dispose();
            clipPanel?.Dispose();
            history.Clear();
            clipHistoryRow.Dispose();
            icon.Dispose();
            themed?.Dispose();
            dotted?.Dispose();
            if (dottedHandle != 0)
            {
                Theme.DestroyIcon(dottedHandle);
            }

            menu.Dispose();
            startWithWindows.Dispose();
            sendFileRow.Dispose();
            sendPipe?.Dispose();
            macrosRow.Dispose();
            securityRow.Dispose();
            networkRow.Dispose();
            networkProblem.Dispose();
            firewallRow.Dispose();
            restartAdminRow.Dispose();
            bluetoothRow.Dispose();
            clipboardRow.Dispose();
            checkUpdates.Dispose();
            openLog.Dispose();
            update.Dispose();
            installRow.Dispose();
            laterRow.Dispose();
            skipRow.Dispose();
            updateTimer.Dispose();
            receiveEnd.Dispose();
            toast.Dispose();
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

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Edgepad.Dashboard;
using Edgepad.Platform;
using Edgepad.Updates;

namespace Edgepad.Ui;

/// <summary>What the tray menu reads and does. Handlers run on the UI thread and none may throw.</summary>
internal sealed record TrayHooks(
    Func<string> SecurityHint,
    Func<string> MacrosHint,
    Func<bool> NetworkOn,
    Func<string> NetworkHint,
    Func<bool> ClipboardOn,
    Func<bool> NotifyOn,
    Func<bool> DeveloperOn,
    Func<bool> AutostartOn,
    Func<bool> UpdatesOn,
    Action Security,
    Action Macros,
    Action SendFile,
    Action ToggleNetwork,
    Action ToggleClipboard,
    Action ToggleNotify,
    Action ToggleDeveloper,
    Action DeveloperIntegrations,
    Action ToggleAutostart,
    Action ToggleUpdates,
    Action InstallUpdate,
    Action LaterUpdate,
    Action SkipUpdate,
    Action ShowTransfers,
    Action Quit);

/// <summary>
/// The tray icon and its menu, in the order every laptop uses: status, Update available, a problem row, then Send a file…, Macros…,
/// and the Clipboard, Connections, Integrations and Settings submenus, then Quit Edgepad.
/// Every handler runs on the UI thread and none may throw: an exception there ends the whole app, and with it the
/// Bluetooth service.
/// </summary>
internal sealed class Tray : IDisposable
{
    // The documentation site, as on every laptop and the phone.
    private const string DocumentationUrl = FirstRunWindow.DocumentationUrl;

    // The shell's tooltip is short on some desktops; stay under the oldest limit the Windows tray uses.
    private const int MaxTooltipLength = 63;

    // How often the desktop is asked whether it prefers dark.
    private static readonly TimeSpan SchemeEvery = TimeSpan.FromSeconds(2);

    private readonly NativeMenuItem statusRow = new(CoreStrings.Status_Starting) { IsEnabled = false };
    private readonly NativeMenuItem updateRow = new(LinuxStrings.Menu_Update) { IsVisible = false };
    private readonly NativeMenuItem installRow = new(LinuxStrings.Common_Install) { IsVisible = false };
    private readonly NativeMenuItem laterRow = new(LinuxStrings.Menu_UpdateLater) { IsVisible = false };
    private readonly NativeMenuItem skipRow = new(LinuxStrings.Menu_UpdateSkip) { IsVisible = false };
    private readonly NativeMenu updateMenu = new();
    private readonly NativeMenuItem problemRow = new("") { IsEnabled = false, IsVisible = false };
    private readonly NativeMenuItem networkRow = Toggle(LinuxStrings.Menu_Network);
    private readonly NativeMenuItem clipboardRow = Toggle(LinuxStrings.Menu_ShareClipboard);
    private readonly NativeMenuItem notifyRow = Toggle(LinuxStrings.Menu_Notifications);
    private readonly NativeMenuItem autostartRow = Toggle(LinuxStrings.Menu_StartAtLogin);
    private readonly NativeMenuItem updatesRow = Toggle(LinuxStrings.Menu_CheckUpdates);
    private readonly NativeMenuItem sendRow = new(LinuxStrings.Menu_SendFile);
    private readonly NativeMenuItem macrosRow = new(LinuxStrings.Menu_Macros);
    private readonly NativeMenuItem transfersRow = new(LinuxStrings.Menu_Transfers) { IsVisible = false };
    private readonly NativeMenuItem integrationsRow = new(LinuxStrings.Menu_StatsWeather) { IsVisible = LinuxDashboard.Shared is not null };
    private readonly NativeMenuItem developerRow = Toggle(LinuxStrings.Menu_DeveloperMode);
    private readonly NativeMenuItem developerIntegrationsRow = new(LinuxStrings.Menu_ForDevelopers);
    private readonly NativeMenuItem securityRow = new(LinuxStrings.Menu_Security);
    private readonly TrayIcon icon;
    private readonly Bus portal = Bus.OnSession();
    private readonly Timer scheme;
    private readonly TrayHooks hooks;
    private bool dark;

    public Tray(TrayHooks hooks)
    {
        this.hooks = hooks;

        // With an update key the row opens Install, Later and Skip; without one it has no submenu and opens the release page.
        updateRow.Click += (_, _) => Safe(() => OpenWithDesktop(UpdateCheck.LatestUrl, LinuxStrings.What_ReleasePage), "Update available");
        installRow.Click += (_, _) => Safe(hooks.InstallUpdate, "Install update");
        laterRow.Click += (_, _) => Safe(hooks.LaterUpdate, "Later");
        skipRow.Click += (_, _) => Safe(hooks.SkipUpdate, "Skip this version");
        networkRow.Click += (_, _) => Safe(hooks.ToggleNetwork, "Wi-Fi and USB connections");
        clipboardRow.Click += (_, _) => Safe(hooks.ToggleClipboard, "Share clipboard");
        notifyRow.Click += (_, _) => Safe(hooks.ToggleNotify, "Forward notifications to phone");
        autostartRow.Click += (_, _) => Safe(hooks.ToggleAutostart, "Start at login");
        updatesRow.Click += (_, _) => Safe(hooks.ToggleUpdates, "Check for updates daily");
        sendRow.Click += (_, _) => Safe(hooks.SendFile, "Send a file");
        macrosRow.Click += (_, _) => Safe(hooks.Macros, "Macros");
        transfersRow.Click += (_, _) => Safe(hooks.ShowTransfers, "Transfers");
        integrationsRow.Click += (_, _) => Safe(() => LinuxDashboard.Shared?.ShowWindow(developer: false), "Integrations");
        developerRow.IsVisible = LinuxDashboard.Shared is not null;
        developerRow.Click += (_, _) => Safe(hooks.ToggleDeveloper, "Developer mode");
        developerIntegrationsRow.Click += (_, _) => Safe(hooks.DeveloperIntegrations, "Developer integrations");
        securityRow.Click += (_, _) => Safe(hooks.Security, "Security");
        var docs = new NativeMenuItem(LinuxStrings.Menu_Documentation);
        docs.Click += (_, _) => Safe(() => OpenWithDesktop(DocumentationUrl, LinuxStrings.What_Documentation), "Documentation");
        var log = new NativeMenuItem(LinuxStrings.Menu_OpenLog);
        log.Click += (_, _) => Safe(() => OpenWithDesktop(Log.FilePath, LinuxStrings.What_Log), "Open log");
        var exit = new NativeMenuItem(LinuxStrings.Menu_Quit);
        exit.Click += (_, _) => Safe(hooks.Quit, "Quit");

        updateMenu.Add(installRow);
        updateMenu.Add(laterRow);
        updateMenu.Add(skipRow);

        var integrationsGroup = Group(LinuxStrings.Menu_Group_Integrations, integrationsRow, developerRow, developerIntegrationsRow);
        integrationsGroup.IsVisible = LinuxDashboard.Shared is not null;

        var menu = new NativeMenu();
        menu.Add(statusRow);
        menu.Add(updateRow);
        menu.Add(problemRow);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(sendRow);
        menu.Add(macrosRow);
        menu.Add(transfersRow);
        menu.Add(Group(LinuxStrings.Menu_Group_Clipboard, clipboardRow));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Group(LinuxStrings.Menu_Group_Connections, networkRow, securityRow));
        menu.Add(integrationsGroup);
        menu.Add(Group(LinuxStrings.Menu_Group_Settings, notifyRow, autostartRow, updatesRow, docs, log));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        // Read the settings each time the menu opens, so it never shows a stale state. Whether the desktop raises
        // this for a StatusNotifierItem menu is UNKNOWN (docs/LINUX.md); the rows are also refreshed on status changes.
        menu.Opening += (_, _) => Safe(Refresh, "Menu opening");

        icon = new TrayIcon { Icon = LoadMark(dark: false), ToolTipText = LinuxStrings.App_Name, Menu = menu, IsVisible = true };
        Refresh();

        // The desktop's light or dark preference, polled (see ColorScheme) on the thread pool: a portal call may
        // block for its deadline, which the UI thread must not. Only a change reaches the UI thread.
        scheme = new Timer(_ => Safe(CheckScheme, "Colour scheme"), null, TimeSpan.Zero, SchemeEvery);
    }

    /// <summary>Puts the icon on the application's tray. Call on the UI thread once the app is initialised.</summary>
    public void Attach(Application app) => TrayIcon.SetIcons(app, new TrayIcons { icon });

    public void SetStatus(string text)
    {
        statusRow.Header = text;
        var tooltip = $"{LinuxStrings.App_Name}: {text}";
        icon.ToolTipText = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
        Refresh();
    }

    /// <summary>Shows "Update available: VERSION" under the status. Opens the release page when clicked.</summary>
    public void SetUpdate(string version, bool canInstall)
    {
        updateRow.Header = Say.Format(LinuxStrings.Menu_UpdateVersion, version);
        installRow.Header = Say.Format(LinuxStrings.Menu_UpdateInstall, version);
        updateRow.Menu = canInstall ? updateMenu : null;
        updateRow.IsVisible = true;
        installRow.IsVisible = canInstall;
        laterRow.IsVisible = true;
        skipRow.IsVisible = true;
    }

    /// <summary>While an install runs the update row names the download and its percent and the rows under it are off; after, they come back.</summary>
    public void SetInstallProgress(string version, int percent, bool busy)
    {
        updateRow.Header = busy
            ? Say.Format(LinuxStrings.Menu_UpdateDownloading, version, percent)
            : Say.Format(LinuxStrings.Menu_UpdateVersion, version);
        installRow.IsEnabled = !busy;
        laterRow.IsEnabled = !busy;
        skipRow.IsEnabled = !busy;
    }

    /// <summary>Hides the update rows: the owner chose Later or Skip, or the install began.</summary>
    public void ClearUpdate()
    {
        updateRow.IsVisible = false;
        installRow.IsVisible = false;
        laterRow.IsVisible = false;
        skipRow.IsVisible = false;
    }

    /// <summary>A problem the owner can fix (input blocked, Wi-Fi not listening), or null for none.</summary>
    public void SetProblem(string? text)
    {
        problemRow.Header = text ?? "";
        problemRow.IsVisible = text is not null;
    }

    /// <summary>Shows the "Transfers" row, which brings the card back, only while a file moves.</summary>
    public void SetTransfers(bool running) => transfersRow.IsVisible = running;

    public void Dispose()
    {
        scheme.Dispose();
        portal.Dispose();
        icon.IsVisible = false;
        icon.Dispose();
    }

    public void Refresh()
    {
        securityRow.Header = $"{LinuxStrings.Menu_Security} ({hooks.SecurityHint()})";
        macrosRow.Header = $"{LinuxStrings.Menu_Macros} ({hooks.MacrosHint()})";
        networkRow.Header = $"{LinuxStrings.Menu_Network} ({hooks.NetworkHint()})";
        networkRow.IsChecked = hooks.NetworkOn();
        clipboardRow.IsChecked = hooks.ClipboardOn();
        notifyRow.IsChecked = hooks.NotifyOn();
        developerRow.IsChecked = hooks.DeveloperOn();
        autostartRow.IsChecked = hooks.AutostartOn();
        updatesRow.IsChecked = hooks.UpdatesOn();
    }

    private static NativeMenuItem Toggle(string header) => new(header) { ToggleType = MenuItemToggleType.CheckBox };

    // A row that opens a submenu of the given rows.
    private static NativeMenuItem Group(string header, params NativeMenuItem[] rows)
    {
        var sub = new NativeMenu();
        foreach (var row in rows)
        {
            sub.Add(row);
        }

        return new NativeMenuItem(header) { Menu = sub };
    }

    private void CheckScheme()
    {
        var next = ColorScheme.PrefersDark(portal);
        if (next != dark)
        {
            dark = next;
            Dispatcher.UIThread.Post(() => Safe(() => icon.Icon = LoadMark(next) ?? icon.Icon, "Tray icon"));
        }
    }

    /// <summary>The Edgepad mark as a 64 px PNG (Skia does not decode an .ico), the dark tile or the light. Null (logged) if it will not decode; a scheme change then keeps the icon it had.</summary>
    private static WindowIcon? LoadMark(bool dark)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Edgepad.Linux/Assets/{(dark ? "edgepad-64-dark" : "edgepad-64")}.png"));
            return new WindowIcon(stream);
        }
        catch (Exception e)
        {
            Log.Write($"The tray icon could not be loaded: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Hands a file or a link to whatever the desktop opens it with, as a double-click would (xdg-open). When it cannot,
    /// a notification says why, since the tray menu has closed and nothing else would show it.
    /// </summary>
    internal static void OpenWithDesktop(string target, string what)
    {
        if (!target.StartsWith("https://", StringComparison.Ordinal) && !File.Exists(target))
        {
            Notifier.Send(LinuxStrings.App_Name, Say.Format(LinuxStrings.Open_Nothing, what, target));
            return;
        }

        var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(target);
        try
        {
            // Not waited for: xdg-open hands off to the opener and exits, and a menu item must not block the UI.
            using var _ = Process.Start(start);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No xdg-utils installed, or no handler. A menu item must never take the tray app down with it.
            Log.Write($"Could not open {target}: {e.Message}");
            Notifier.Send(LinuxStrings.App_Name, Say.Format(LinuxStrings.Open_InstallXdg, what));
        }
    }

    private static void Safe(Action run, string what)
    {
        try
        {
            run();
        }
        catch (Exception e)
        {
            Log.Write($"{what} failed: {e.Message}");
        }
    }
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
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
    Func<bool> AutostartOn,
    Action Security,
    Action Macros,
    Action ToggleNetwork,
    Action ToggleClipboard,
    Action ToggleAutostart,
    Action Quit);

/// <summary>
/// The tray icon and its menu, in the order every laptop uses: status, Update available, a problem row, then Wi-Fi and
/// USB connections, Share clipboard, Start at login, Macros…, Security…, Documentation, Open log and Quit Edgepad.
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

    private readonly NativeMenuItem statusRow = new("Starting…") { IsEnabled = false };
    private readonly NativeMenuItem updateRow = new("Update available") { IsVisible = false };
    private readonly NativeMenuItem problemRow = new("") { IsEnabled = false, IsVisible = false };
    private readonly NativeMenuItem networkRow = Toggle("Wi-Fi and USB connections");
    private readonly NativeMenuItem clipboardRow = Toggle("Share clipboard");
    private readonly NativeMenuItem autostartRow = Toggle("Start at login");
    private readonly NativeMenuItem macrosRow = new("Macros…");
    private readonly NativeMenuItem securityRow = new("Security…");
    private readonly TrayIcon icon;
    private readonly Bus portal = Bus.OnSession();
    private readonly Timer scheme;
    private readonly TrayHooks hooks;
    private bool dark;

    public Tray(TrayHooks hooks)
    {
        this.hooks = hooks;

        updateRow.Click += (_, _) => Safe(() => OpenWithDesktop(UpdateCheck.LatestUrl, "the release page"), "Update available");
        networkRow.Click += (_, _) => Safe(hooks.ToggleNetwork, "Wi-Fi and USB connections");
        clipboardRow.Click += (_, _) => Safe(hooks.ToggleClipboard, "Share clipboard");
        autostartRow.Click += (_, _) => Safe(hooks.ToggleAutostart, "Start at login");
        macrosRow.Click += (_, _) => Safe(hooks.Macros, "Macros");
        securityRow.Click += (_, _) => Safe(hooks.Security, "Security");
        var docs = new NativeMenuItem("Documentation");
        docs.Click += (_, _) => Safe(() => OpenWithDesktop(DocumentationUrl, "the documentation"), "Documentation");
        var log = new NativeMenuItem("Open log");
        log.Click += (_, _) => Safe(() => OpenWithDesktop(Log.FilePath, "the log"), "Open log");
        var exit = new NativeMenuItem("Quit Edgepad");
        exit.Click += (_, _) => Safe(hooks.Quit, "Quit");

        var menu = new NativeMenu();
        menu.Add(statusRow);
        menu.Add(updateRow);
        menu.Add(problemRow);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(networkRow);
        menu.Add(clipboardRow);
        menu.Add(autostartRow);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(macrosRow);
        menu.Add(securityRow);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(docs);
        menu.Add(log);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        // Read the settings each time the menu opens, so it never shows a stale state. Whether the desktop raises
        // this for a StatusNotifierItem menu is UNKNOWN (docs/LINUX.md); the rows are also refreshed on status changes.
        menu.Opening += (_, _) => Safe(Refresh, "Menu opening");

        icon = new TrayIcon { Icon = LoadMark(dark: false), ToolTipText = "Edgepad", Menu = menu, IsVisible = true };
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
        var tooltip = $"Edgepad: {text}";
        icon.ToolTipText = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
        Refresh();
    }

    /// <summary>Shows "Update available: VERSION" under the status. Opens the release page when clicked.</summary>
    public void SetUpdate(string version)
    {
        updateRow.Header = $"Update available: {version}";
        updateRow.IsVisible = true;
    }

    /// <summary>A problem the owner can fix (input blocked, Wi-Fi not listening), or null for none.</summary>
    public void SetProblem(string? text)
    {
        problemRow.Header = text ?? "";
        problemRow.IsVisible = text is not null;
    }

    public void Dispose()
    {
        scheme.Dispose();
        portal.Dispose();
        icon.IsVisible = false;
        icon.Dispose();
    }

    public void Refresh()
    {
        securityRow.Header = $"Security… ({hooks.SecurityHint()})";
        macrosRow.Header = $"Macros… ({hooks.MacrosHint()})";
        networkRow.Header = $"Wi-Fi and USB connections ({hooks.NetworkHint()})";
        networkRow.IsChecked = hooks.NetworkOn();
        clipboardRow.IsChecked = hooks.ClipboardOn();
        autostartRow.IsChecked = hooks.AutostartOn();
    }

    private static NativeMenuItem Toggle(string header) => new(header) { ToggleType = NativeMenuItemToggleType.CheckBox };

    private void CheckScheme()
    {
        var next = ColorScheme.PrefersDark(portal);
        if (next != dark)
        {
            dark = next;
            Dispatcher.UIThread.Post(() => Safe(() => icon.Icon = LoadMark(next), "Tray icon"));
        }
    }

    /// <summary>The Edgepad mark as a 64 px PNG (Skia does not decode an .ico), the dark tile or the light. Null (no icon, logged) if it will not decode.</summary>
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
    private static void OpenWithDesktop(string target, string what)
    {
        if (!target.StartsWith("https://", StringComparison.Ordinal) && !File.Exists(target))
        {
            Notifier.Send("Edgepad", $"Could not open {what}: there is nothing at {target} yet.");
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
            Log.Write($"Could not open {what}: {e.Message}");
            Notifier.Send("Edgepad", $"Could not open {what}: install xdg-utils.");
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

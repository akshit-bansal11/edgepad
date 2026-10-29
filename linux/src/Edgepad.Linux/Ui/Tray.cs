using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Edgepad.Platform;

namespace Edgepad.Ui;

/// <summary>
/// The tray icon and its menu, rows as on Windows: status, Security…, Wi-Fi and USB connections, Open log, Documentation, Forget trusted
/// phones, Quit and the version. Every handler runs on the UI thread and none may throw: an exception there ends
/// the whole app, and with it the Bluetooth service.
/// </summary>
internal sealed class Tray : IDisposable
{
    // The documentation site, as in the Windows tray.
    private const string DocumentationUrl = "https://edgepad.akshitbansal.dev";

    // The shell's tooltip is short on some desktops; stay under the oldest limit the Windows tray uses.
    private const int MaxTooltipLength = 63;

    // How often the desktop is asked whether it prefers dark.
    private static readonly TimeSpan SchemeEvery = TimeSpan.FromSeconds(2);

    private readonly NativeMenuItem statusRow = new("Starting") { IsEnabled = false };
    private readonly NativeMenuItem securityRow = new("Security…");
    private readonly NativeMenuItem networkRow = new("Wi-Fi and USB connections") { ToggleType = NativeMenuItemToggleType.CheckBox };
    private readonly NativeMenuItem forgetRow = new("Forget trusted phones");
    private readonly TrayIcon icon;
    private readonly Bus portal = Bus.OnSession();
    private readonly Timer scheme;
    private bool dark;
    private readonly Func<string> levelHint;
    private readonly Func<bool> canForget;
    private readonly Func<bool> networkOn;
    private readonly Func<string> networkHint;

    public Tray(
        string version,
        Func<string> levelHint,
        Func<bool> canForget,
        Func<bool> networkOn,
        Func<string> networkHint,
        Action security,
        Action toggleNetwork,
        Action forget,
        Action quit)
    {
        this.levelHint = levelHint;
        this.canForget = canForget;
        this.networkOn = networkOn;
        this.networkHint = networkHint;

        securityRow.Click += (_, _) => Safe(security, "Security");
        networkRow.Click += (_, _) => Safe(toggleNetwork, "Wi-Fi and USB connections");
        forgetRow.Click += (_, _) => Safe(forget, "Forget trusted phones");
        var log = new NativeMenuItem("Open log");
        log.Click += (_, _) => Safe(() => OpenWithDesktop(Log.FilePath, "the log"), "Open log");
        var docs = new NativeMenuItem($"Documentation ({new Uri(DocumentationUrl).Host})");
        docs.Click += (_, _) => Safe(() => OpenWithDesktop(DocumentationUrl, "the documentation"), "Documentation");
        var exit = new NativeMenuItem($"Quit Edgepad {version}");
        exit.Click += (_, _) => Safe(quit, "Quit");

        var menu = new NativeMenu();
        menu.Add(statusRow);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(securityRow);
        menu.Add(networkRow);
        menu.Add(log);
        menu.Add(docs);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(forgetRow);
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

    public void Dispose()
    {
        scheme.Dispose();
        portal.Dispose();
        icon.IsVisible = false;
        icon.Dispose();
    }

    private void CheckScheme()
    {
        var next = ColorScheme.PrefersDark(portal);
        if (next != dark)
        {
            dark = next;
            Dispatcher.UIThread.Post(() => Safe(() => icon.Icon = LoadMark(next), "Tray icon"));
        }
    }

    public void Refresh()
    {
        securityRow.Header = $"Security… ({levelHint()})";
        networkRow.Header = $"Wi-Fi and USB connections ({networkHint()})";
        networkRow.IsChecked = networkOn();
        forgetRow.IsEnabled = canForget();
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

    /// <summary>Hands a file or a link to whatever the desktop opens it with, as a double-click would (xdg-open).</summary>
    private static void OpenWithDesktop(string target, string what)
    {
        if (!target.StartsWith("https://", StringComparison.Ordinal) && !File.Exists(target))
        {
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

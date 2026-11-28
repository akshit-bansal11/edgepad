using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Edgepad.Platform;
using Edgepad.Updates;
using Tmds.DBus.Protocol;

namespace Edgepad.Ui;

/// <summary>
/// The tray's job in a window, for a desktop with no tray host (stock GNOME): the status, the same toggles and
/// Macros… and Security…, Quit, and the one line that says why there is no icon. Closing it minimises it instead:
/// the app keeps serving phones, and a window in the task list is the only handle the owner has. A logout or the
/// app's own shutdown closes it.
/// </summary>
internal sealed class FallbackWindow
{
    private readonly TrayHooks hooks;
    private readonly Window window;
    private readonly TextBlock status;
    private readonly TextBlock update;
    private readonly StackPanel updateButtons;
    private readonly Button install;
    private readonly TextBlock problem;
    private readonly CheckBox network;
    private readonly CheckBox clipboard;
    private readonly CheckBox autostart;
    private readonly CheckBox updates;
    private readonly Button macros;
    private readonly Button security;
    private bool refreshing;
    private bool installable;

    public FallbackWindow(TrayHooks hooks, string initialStatus)
    {
        this.hooks = hooks;
        var look = Look.Current;
        status = look.Text(initialStatus, 14);
        update = look.Text("", 14);
        update.IsVisible = false;

        install = look.Button(LinuxStrings.Common_Install, primary: true);
        install.Click += (_, _) =>
        {
            if (installable)
            {
                hooks.InstallUpdate();
            }
            else
            {
                Tray.OpenWithDesktop(UpdateCheck.LatestUrl, LinuxStrings.What_ReleasePage);
            }
        };
        var later = look.Button(LinuxStrings.Menu_UpdateLater);
        later.Click += (_, _) => hooks.LaterUpdate();
        var skip = look.Button(LinuxStrings.Menu_UpdateSkip);
        skip.Click += (_, _) => hooks.SkipUpdate();
        updateButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, IsVisible = false };
        updateButtons.Children.Add(install);
        updateButtons.Children.Add(later);
        updateButtons.Children.Add(skip);
        problem = look.Text("", 14);
        problem.Foreground = look.Danger;
        problem.IsVisible = false;
        network = Check(look, LinuxStrings.Menu_Network, () => hooks.ToggleNetwork());
        clipboard = Check(look, LinuxStrings.Menu_ShareClipboard, () => hooks.ToggleClipboard());
        autostart = Check(look, LinuxStrings.Menu_StartAtLogin, () => hooks.ToggleAutostart());
        updates = Check(look, LinuxStrings.Menu_CheckUpdates, () => hooks.ToggleUpdates());
        macros = look.Button(LinuxStrings.Menu_Macros);
        macros.Click += (_, _) => hooks.Macros();
        security = look.Button(LinuxStrings.Menu_Security);
        security.Click += (_, _) => hooks.Security();
        var quit = look.Button(LinuxStrings.Menu_Quit, danger: true);
        quit.Click += (_, _) => hooks.Quit();

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        body.Children.Add(look.Text(LinuxStrings.Fallback_Running, 18, FontWeight.Bold));
        body.Children.Add(status);
        body.Children.Add(update);
        body.Children.Add(updateButtons);
        body.Children.Add(problem);
        body.Children.Add(network);
        body.Children.Add(clipboard);
        body.Children.Add(autostart);
        body.Children.Add(updates);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(macros);
        buttons.Children.Add(security);
        buttons.Children.Add(quit);
        body.Children.Add(buttons);
        body.Children.Add(look.Text(
            LinuxStrings.Fallback_NoTray,
            13,
            dim: true));

        window = look.Dialog(LinuxStrings.App_Name, 480, body);
        window.Closing += (_, e) =>
        {
            // Not an exit: the window is all the owner has to come back to. A logout or Quit goes through.
            if (MinimisesOnClose(e.CloseReason))
            {
                e.Cancel = true;
                window.WindowState = WindowState.Minimized;
            }
        };
        Refresh();
    }

    /// <summary>Whether a close is the owner's click, kept as a minimise, rather than the app or the OS shutting down.</summary>
    internal static bool MinimisesOnClose(WindowCloseReason reason) =>
        reason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown);

    /// <summary>
    /// Whether the session bus has no StatusNotifierWatcher, so a tray icon would go nowhere. A bus that cannot be asked
    /// counts as having one. Waits for the bus deadline: never call on the UI thread.
    /// </summary>
    public static bool NoTrayHost()
    {
        using var bus = Bus.OnSession();
        var asked = bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: "org.freedesktop.DBus",
                    path: "/org/freedesktop/DBus",
                    @interface: "org.freedesktop.DBus",
                    member: "NameHasOwner",
                    signature: "s");
                writer.WriteString("org.kde.StatusNotifierWatcher");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadBool(),
            out var has);
        return asked && !has;
    }

    public void Show() => window.Show();

    public void SetStatus(string text) => status.Text = text;

    public void SetUpdate(string version, bool canInstall)
    {
        update.Text = Say.Format(LinuxStrings.Menu_UpdateVersion, version);
        installable = canInstall;
        install.Content = canInstall ? Say.Format(LinuxStrings.Menu_UpdateInstall, version) : LinuxStrings.Menu_UpdateOpenPage;
        update.IsVisible = true;
        install.IsVisible = true;
        updateButtons.IsVisible = true;
    }

    /// <summary>Install in progress: the text carries the percent and the buttons are dimmed so a second click is not offered.</summary>
    public void SetInstallProgress(string version, int percent, bool busy)
    {
        update.Text = busy
            ? Say.Format(LinuxStrings.Menu_UpdateDownloading, version, percent)
            : Say.Format(LinuxStrings.Menu_UpdateVersion, version);
        updateButtons.IsEnabled = !busy;
    }

    public void ClearUpdate()
    {
        update.IsVisible = false;
        updateButtons.IsVisible = false;
    }

    public void SetProblem(string? text)
    {
        problem.Text = text ?? "";
        problem.IsVisible = text is not null;
    }

    public void Refresh()
    {
        refreshing = true;
        network.IsChecked = hooks.NetworkOn();
        network.Content = $"{LinuxStrings.Menu_Network} ({hooks.NetworkHint()})";
        clipboard.IsChecked = hooks.ClipboardOn();
        autostart.IsChecked = hooks.AutostartOn();
        updates.IsChecked = hooks.UpdatesOn();
        macros.Content = $"{LinuxStrings.Menu_Macros} ({hooks.MacrosHint()})";
        security.Content = $"{LinuxStrings.Menu_Security} ({hooks.SecurityHint()})";
        refreshing = false;
    }

    private CheckBox Check(Look look, string text, Action toggle)
    {
        var box = new CheckBox { Content = text, FontFamily = Look.Lato, FontSize = 14, Foreground = look.Ink };

        // The hook flips the setting and refreshes this window; the box only reports the click.
        box.IsCheckedChanged += (_, _) =>
        {
            if (!refreshing)
            {
                toggle();
            }
        };
        return box;
    }
}

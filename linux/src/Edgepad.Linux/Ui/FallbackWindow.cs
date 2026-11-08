using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Edgepad.Platform;
using Tmds.DBus.Protocol;

namespace Edgepad.Ui;

/// <summary>
/// The tray's job in a window, for a desktop with no tray host (stock GNOME): the status, the same toggles and
/// Macros… and Security…, Quit, and the one line that says why there is no icon. Closing it minimises it instead:
/// the app keeps serving phones, and a window in the task list is the only handle the owner has.
/// </summary>
internal sealed class FallbackWindow
{
    private readonly TrayHooks hooks;
    private readonly Window window;
    private readonly TextBlock status;
    private readonly TextBlock update;
    private readonly TextBlock problem;
    private readonly CheckBox network;
    private readonly CheckBox clipboard;
    private readonly CheckBox autostart;
    private readonly Button macros;
    private readonly Button security;
    private bool refreshing;

    public FallbackWindow(TrayHooks hooks, string initialStatus)
    {
        this.hooks = hooks;
        var look = Look.Current;
        status = look.Text(initialStatus, 14);
        update = look.Text("", 14);
        update.IsVisible = false;
        problem = look.Text("", 14);
        problem.Foreground = look.Danger;
        problem.IsVisible = false;
        network = Check(look, "Wi-Fi and USB connections", () => hooks.ToggleNetwork());
        clipboard = Check(look, "Share clipboard", () => hooks.ToggleClipboard());
        autostart = Check(look, "Start at login", () => hooks.ToggleAutostart());
        macros = look.Button("Macros…");
        macros.Click += (_, _) => hooks.Macros();
        security = look.Button("Security…");
        security.Click += (_, _) => hooks.Security();
        var quit = look.Button("Quit Edgepad", danger: true);
        quit.Click += (_, _) => hooks.Quit();

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        body.Children.Add(look.Text("Edgepad is running", 18, FontWeight.Bold));
        body.Children.Add(status);
        body.Children.Add(update);
        body.Children.Add(problem);
        body.Children.Add(network);
        body.Children.Add(clipboard);
        body.Children.Add(autostart);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(macros);
        buttons.Children.Add(security);
        buttons.Children.Add(quit);
        body.Children.Add(buttons);
        body.Children.Add(look.Text(
            "Your desktop shows no tray icons. On GNOME, install the 'AppIndicator and KStatusNotifierItem Support' extension to get one.",
            13,
            dim: true));

        window = look.Dialog("Edgepad", 480, body);
        window.Closing += (_, e) =>
        {
            // Not an exit: the window is all the owner has to come back to.
            e.Cancel = true;
            window.WindowState = WindowState.Minimized;
        };
        Refresh();
    }

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

    public void SetUpdate(string version)
    {
        update.Text = $"Update available: {version}";
        update.IsVisible = true;
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
        network.Content = $"Wi-Fi and USB connections ({hooks.NetworkHint()})";
        clipboard.IsChecked = hooks.ClipboardOn();
        autostart.IsChecked = hooks.AutostartOn();
        macros.Content = $"Macros… ({hooks.MacrosHint()})";
        security.Content = $"Security… ({hooks.SecurityHint()})";
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

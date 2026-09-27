using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Macros;
using Edgepad.Startup;
using Edgepad.Trust;
using Edgepad.Ui;
using NAudio.CoreAudioApi;

namespace Edgepad;

/// <summary>The whole UI: a tray icon whose menu shows connection status and the few settings there are.</summary>
internal sealed class TrayContext : ApplicationContext
{
    // NotifyIcon.Text is capped by the shell; stay under the oldest limit.
    private const int MaxTooltipLength = 63;

    // The documentation site. The phone's Settings › Help offers the same link, so the
    // two halves point at one page rather than each explaining itself.
    private const string DocumentationUrl = "https://edgepad.vercel.app";

    private readonly MenuRow startWithWindows = new("Start with Windows", Icons.Power) { CheckOnClick = true, Switch = true };
    private readonly MenuRow macrosRow = new("Macros…", Icons.Macro);
    private readonly MenuRow forget = new("Forget trusted phone", Icons.Unlink) { Danger = true };
    private readonly TrayMenu menu = new();
    private readonly NotifyIcon icon;
    private readonly SynchronizationContext ui;

    private readonly TrustStore trust = TrustStore.ForCurrentUser();
    private readonly AudioEndpoint speakers = new(DataFlow.Render);
    private readonly AudioEndpoint microphone = new(DataFlow.Capture);
    private readonly BrightnessControl brightness = new();
    private readonly MediaSessions media = new();
    private readonly DisplayModes display = new();
    private readonly MacroStore macros = MacroStore.ForCurrentUser();
    private readonly LevelOverlay overlay;
    private readonly RfcommServer server;

    public TrayContext(EventWaitHandle quit)
    {
        // Click, not CheckedChanged: the menu sets Checked from the registry each time it opens, and that must
        // read the key without writing it back — least of all deleting it after a failed read.
        startWithWindows.Click += (_, _) => RunAtLogin.Set(startWithWindows.Checked);
        forget.Click += (_, _) => trust.Forget();
        macrosRow.Click += (_, _) => MacroEditor.Show(macros);

        // The header is the menu's own. The rest follow in the design's order, the destructive pair after a rule.
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(startWithWindows);
        menu.Items.Add(macrosRow);
        menu.Items.Add(Row("Open log", Icons.FileText, OpenLog));
        menu.Items.Add(Row("Documentation", Icons.BookOpen, OpenDocumentation, "edgepad.vercel.app"));
        menu.Items.Add(new MenuSeparator());
        menu.Items.Add(forget);
        menu.Items.Add(Row("Quit Edgepad", Icons.Close, ExitThread, Program.Version));

        // Read the settings each time the menu opens, so it never shows a stale state.
        menu.Opening += (_, _) =>
        {
            startWithWindows.Checked = RunAtLogin.IsEnabled;
            forget.Enabled = trust.Trusted is not null;
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

        // Built here and nowhere else: the overlay captures this thread's synchronisation context, and the
        // frames that ask it to show something arrive on a Bluetooth thread that has none of its own.
        overlay = new LevelOverlay();
        server = new RfcommServer(SetStatus, trust, speakers, microphone, brightness, media, display, macros, overlay);
        _ = StartServerAsync();
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
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
    }

    private static void OpenDocumentation()
    {
        try
        {
            using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DocumentationUrl) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // No default browser, or the shell refused the handler. A menu item must never
            // take the tray app down with it: an unhandled exception here ends the process.
            Log.Write($"Could not open the documentation: {e.Message}");
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
            icon.Dispose();
            menu.Dispose();
            startWithWindows.Dispose();
            macrosRow.Dispose();
            forget.Dispose();
            server.Dispose();
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

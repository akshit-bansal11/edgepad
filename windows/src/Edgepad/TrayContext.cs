using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Macros;
using Edgepad.Startup;
using Edgepad.Trust;
using NAudio.CoreAudioApi;

namespace Edgepad;

/// <summary>The whole UI: a tray icon whose menu shows connection status and the few settings there are.</summary>
internal sealed class TrayContext : ApplicationContext
{
    // NotifyIcon.Text is capped by the shell; stay under the oldest limit.
    private const int MaxTooltipLength = 63;

    // The documentation site. The phone's Settings › Help offers the same link, so the
    // two halves point at one page rather than each explaining itself.
    private const string DocumentationUrl = "https://edgepad-docs.vercel.app";

    private readonly ToolStripMenuItem status = new("Starting…") { Enabled = false };
    private readonly ToolStripMenuItem startWithWindows = new("Start with Windows") { CheckOnClick = true };
    private readonly ToolStripMenuItem forget = new("Forget trusted phone");
    private readonly ContextMenuStrip menu = new();
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
        startWithWindows.CheckedChanged += (_, _) => RunAtLogin.Set(startWithWindows.Checked);
        forget.Click += (_, _) => trust.Forget();

        menu.Items.Add($"Edgepad {Program.Version}").Enabled = false;
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startWithWindows);
        menu.Items.Add("Macros…", null, (_, _) => MacroEditor.Show(macros));
        menu.Items.Add(forget);
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add("Documentation", null, (_, _) => OpenDocumentation());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        // Read the settings each time the menu opens, so it never shows a stale state.
        menu.Opening += (_, _) =>
        {
            startWithWindows.Checked = RunAtLogin.IsEnabled;
            forget.Enabled = trust.Trusted is not null;
        };

        // The exe's own icon (ApplicationIcon in the project), so the tray shows the Edgepad mark.
        var mark = Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
        icon = new NotifyIcon { Icon = mark ?? SystemIcons.Application, Text = "Edgepad", ContextMenuStrip = menu, Visible = true };

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
            status.Text = text;
            var tooltip = $"Edgepad: {text}";
            icon.Text = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
        },
        null);

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
            status.Dispose();
            startWithWindows.Dispose();
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

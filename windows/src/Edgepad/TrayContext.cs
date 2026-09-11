using Edgepad.Bluetooth;
using Edgepad.Controls;
using Edgepad.Startup;
using Edgepad.Trust;
using NAudio.CoreAudioApi;

namespace Edgepad;

/// <summary>The whole UI: a tray icon whose menu shows connection status and the few settings there are.</summary>
internal sealed class TrayContext : ApplicationContext
{
    // NotifyIcon.Text is capped by the shell; stay under the oldest limit.
    private const int MaxTooltipLength = 63;

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
    private readonly RfcommServer server;

    public TrayContext()
    {
        startWithWindows.CheckedChanged += (_, _) => RunAtLogin.Set(startWithWindows.Checked);
        forget.Click += (_, _) => trust.Forget();

        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startWithWindows);
        menu.Items.Add(forget);
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());

        // Read the settings each time the menu opens, so it never shows a stale state.
        menu.Opening += (_, _) =>
        {
            startWithWindows.Checked = RunAtLogin.IsEnabled;
            forget.Enabled = trust.Trusted is not null;
        };

        icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "Edgepad", ContextMenuStrip = menu, Visible = true };

        // Creating the menu installed the WinForms context on this thread; status updates arrive from
        // Bluetooth threads and are marshalled back here.
        ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        server = new RfcommServer(SetStatus, trust, speakers, microphone, brightness);
        _ = StartServerAsync();
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

    protected override void ExitThreadCore()
    {
        icon.Visible = false;
        server.Dispose();
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
            brightness.Dispose();
            speakers.Dispose();
            microphone.Dispose();
        }

        base.Dispose(disposing);
    }
}

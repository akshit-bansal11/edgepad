using System.Drawing.Imaging;
using System.Reflection;
using Edgepad.Annotation;
using Edgepad.Dashboard;
using Edgepad.Net;
using Edgepad.Transfer;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// Renders Windows surfaces to PNG for design review. Does nothing unless EDGEPAD_GALLERY_OUT names a folder, so a normal
/// test run is unaffected. To add a surface: build it, then call <see cref="Save"/> from inside <see cref="Shoot"/>.
/// </summary>
public sealed class DesignGallery
{
    private const string OutVariable = "EDGEPAD_GALLERY_OUT";

    [Fact]
    public void RenderSurfaces()
    {
        var folder = Environment.GetEnvironmentVariable(OutVariable);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var cases = new (string Name, Action Draw)[]
        {
            ("toast", () => Shoot(folder)),
            ("laser", () => SaveLaser(folder, "laser-medium", 15)),
            ("tray", () => SaveTray(folder)),
            ("dev-window", () => SaveDevWindow(folder, "dev-window", codeOpen: false, Palette.Light)),
            ("dev-window-dark", () => SaveDevWindow(folder, "dev-window-dark", codeOpen: false, Palette.Dark)),
            ("dev-window-code", () => SaveDevWindow(folder, "dev-window-code", codeOpen: true, Palette.Light)),
        };
        foreach (var (name, draw) in cases)
        {
            RunCase(name, draw);
        }
    }

    // Each case gets its own STA thread and 20 s: one that fails or hangs is logged and skipped, the rest still render. A hung
    // thread is a background thread, so it cannot keep the test process alive.
    private static void RunCase(string name, Action draw)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                draw();
            }
            catch (Exception e)
            {
                failure = e;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(20)))
        {
            Console.WriteLine($"Gallery case {name} skipped: it took more than 20 s.");
        }
        else if (failure is not null)
        {
            Console.WriteLine($"Gallery case {name} skipped: {failure}");
        }
    }

    private static void Shoot(string folder)
    {
        using var toast = new TransferToast
        {
            OnPause = _ => { },
            OnResume = _ => { },
            OnStop = _ => { },
            OnRetry = () => { },
            PhoneName = () => "Pixel 8",
        };
        var running = new TransferView(TransferPhase.Running, true, 2, 5, "holiday-photos-final.zip", 37, 100, 4194304, 1, [], 0, false);
        var finished = new TransferView(TransferPhase.Finished, true, 5, 5, "a.pdf", 0, 0, 0, 5, [], 0, false);
        var failed = new TransferView(
            TransferPhase.Finished, true, 1, 1, "holiday-photos-final.zip", 0, 0, 0, 0, [new TransferFailed("holiday-photos-final.zip", TransferFailure.LinkLost)], 0, false);
        Save(folder, "toast-run", toast, running);
        Save(folder, "toast-paused", toast, running with { Paused = true });
        Save(folder, "toast-failed", toast, failed);
        Save(folder, "toast-finished", toast, finished);
    }

    // The For developers window with stub actions: Claude Code, OpenCode and GitHub connected, the rest not. With
    // codeOpen, GitHub is signed out and its sign-in panel is open, so both states of the row show. It is sized to all
    // its content, whatever the runner's screen, then drawn into a bitmap without being shown.
    private static void SaveDevWindow(string folder, string name, bool codeOpen, Palette palette)
    {
        using var dir = new TempFolder();
        var settings = new IntegrationSettings(Path.Combine(dir.Path, IntegrationSettings.FileName));
        var secrets = new MemorySecrets();
        _ = settings.SetFlag(IntegrationSettings.Developer, true);
        _ = settings.SetFlag(IntegrationSettings.Claude, true);
        _ = settings.SetFlag("opencode", true);
        if (!codeOpen)
        {
            _ = settings.SetFlag(IntegrationSettings.GitHub, true);
            _ = settings.Set(IntegrationSettings.GitHubLogin, "akshit-bansal11");
            _ = secrets.Set(GitHubApp.TokenName, "gho_abcdefghijklmnop123456");
        }

        var heard = new DateTimeOffset(DateTime.Today.AddHours(9).AddMinutes(56)).ToUnixTimeSeconds() / 60;
        var actions = new IntegrationActions(
            settings,
            new GitHubSignIn(secrets),
            id => new AgentStatus(settings.Flag(id) ? AgentState.Connected : AgentState.NotConnected),
            _ => Task.FromResult(new AgentStatus(AgentState.Connected)),
            _ => Task.FromResult(new AgentStatus(AgentState.NotConnected)),
            () => { },
            _ => heard,
            _ => Task.FromResult<string?>("akshit-bansal11"),
            _ => { },
            (_, _) => { },
            _ => { },
            _ => { },
            _ => false,
            (_, _, _, _, _) => Task.FromResult(new GitSaveResult(GitSaveStatus.Saved, "akshit")),
            _ => { });
        using var window = new DevelopersWindow(actions, palette);
        window.ShowInFull();
        if (codeOpen)
        {
            window.PreviewCode("WDJB-MJHT");
        }

        using var bitmap = window.Render();
        bitmap.Save(System.IO.Path.Combine(folder, name + ".png"), ImageFormat.Png);
    }

    private sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> values = [];

        public string? Get(string name) => values.GetValueOrDefault(name);

        public bool Set(string name, string value)
        {
            values[name] = value;
            return true;
        }

        public void Delete(string name) => values.Remove(name);
    }

    private sealed class TempFolder : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("edgepad-gallery-");

        public string Path => directory.FullName;

        public void Dispose() => directory.Delete(recursive: true);
    }

    // The tray menu is built here with stub callbacks and the same strings, icons and grouping as TrayContext; the
    // context itself owns the Bluetooth server and the whole app, so it is not constructed. A submenu is a second
    // TrayMenu, so it is drawn beside its parent at the row's height, as Windows places it.
    private static void SaveTray(string folder)
    {
        using var main = new TrayMenu();
        using var clipboardMenu = new TrayMenu(withHeader: false);
        using var connectionsMenu = new TrayMenu(withHeader: false);
        using var integrationsMenu = new TrayMenu(withHeader: false);
        using var settingsMenu = new TrayMenu(withHeader: false);
        main.Header.SetStatus(LaptopStatus.Paired("Pixel 8"));

        var clipboardGroup = new MenuRow(Strings.Menu_Group_Clipboard, Icons.Clipboard) { DropDown = clipboardMenu };
        clipboardMenu.Items.Add(new MenuRow(Strings.Menu_ShareClipboard, Icons.Clipboard) { Switch = true, Checked = true });
        clipboardMenu.Items.Add(new MenuRow(Strings.Menu_ClipboardHistory, Icons.Clipboard) { Hint = "Ctrl+Alt+V" });
        var connectionsGroup = new MenuRow(Strings.Menu_Group_Connections, Icons.Wifi) { DropDown = connectionsMenu };
        connectionsMenu.Items.Add(new MenuRow(Strings.Menu_Network, Icons.Wifi) { Switch = true, Checked = true });
        connectionsMenu.Items.Add(new MenuRow(Strings.Menu_Security, Icons.Smartphone) { Hint = "PIN" });
        var integrationsGroup = new MenuRow(Strings.Menu_Group_Integrations, Icons.Sliders) { DropDown = integrationsMenu };
        integrationsMenu.Items.Add(new MenuRow(Strings.Menu_StatsWeather, Icons.Sliders));
        integrationsMenu.Items.Add(new MenuRow(Strings.Menu_DeveloperMode, Icons.Sliders) { Switch = true, Checked = true });
        integrationsMenu.Items.Add(new MenuRow(Strings.Menu_ForDevelopers, Icons.Sliders));
        var settingsRow = new MenuRow(Strings.Menu_Settings, Icons.Sliders) { DropDown = settingsMenu };
        settingsMenu.Items.Add(new MenuRow(Strings.Menu_StartWithWindows, Icons.Power) { Switch = true });
        settingsMenu.Items.Add(new MenuRow(Strings.Menu_CheckUpdates, Icons.Download) { Switch = true, Checked = true });
        settingsMenu.Items.Add(new MenuRow(Strings.Menu_OpenLog, Icons.FileText));
        settingsMenu.Items.Add(new MenuRow(Strings.Menu_Documentation, Icons.BookOpen) { Hint = "getedgepad.com" });

        main.Items.Add(new MenuRow(Strings.Menu_Transfers, Icons.FileText));
        main.Items.Add(new MenuSeparator());
        main.Items.Add(new MenuRow(Strings.Menu_SendFile, Icons.Download));
        main.Items.Add(new MenuRow(Strings.Menu_Macros, Icons.Macro) { Hint = Strings.Menu_MacrosCount.Fill(3, 20) });
        main.Items.Add(clipboardGroup);
        main.Items.Add(new MenuSeparator());
        main.Items.Add(connectionsGroup);
        main.Items.Add(integrationsGroup);
        main.Items.Add(settingsRow);
        main.Items.Add(new MenuSeparator());
        main.Items.Add(new MenuRow(Strings.Menu_Quit, Icons.Close));

        using var mainShot = Shot(main);
        mainShot.Save(Path.Combine(folder, "tray-main.png"), ImageFormat.Png);

        using var subShot = Shot(integrationsMenu);
        const int gap = 4;
        using var both = new Bitmap(mainShot.Width + gap + subShot.Width, mainShot.Height);
        using (var g = Graphics.FromImage(both))
        {
            g.Clear(Color.FromArgb(0x1B, 0x1C, 0x20));
            g.DrawImageUnscaled(mainShot, 0, 0);
            g.DrawImageUnscaled(subShot, mainShot.Width + gap, integrationsGroup.Bounds.Top);
        }

        both.Save(Path.Combine(folder, "tray-integrations.png"), ImageFormat.Png);
    }

    // A drop-down that was never shown has no window yet; making the handle lets it paint into a bitmap.
    private static Bitmap Shot(TrayMenu menu)
    {
        _ = menu.Handle;
        menu.Restyle();
        menu.Size = menu.GetPreferredSize(Size.Empty);
        var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, menu.Size));
        return bitmap;
    }

    // The overlay's canvas is a private nested class over a DIB, so it is built and drawn directly, then composited
    // over a dark background: the laser is per-pixel alpha and would otherwise be saved on transparent.
    private static void SaveLaser(string folder, string name, int width)
    {
        const int w = 640;
        const int h = 300;
        var stroke = new Stroke(StrokeTool.LaserTrail, new PenStyle(0xFF0000, width, 100));
        const int steps = 12;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            var x = (ushort)(65535 * (0.15f + (0.7f * t)));
            var y = (ushort)(65535 * (0.65f - (0.4f * MathF.Sin(t * MathF.PI))));
            stroke.Extend(new NormPoint(x, y), i * 20);
        }

        var now = steps * 20L;
        var frame = LaserFrame.Of(stroke, now, w, h) ?? throw new InvalidOperationException("No laser frame.");
        var canvasType = typeof(AnnotationOverlay).GetNestedType("Canvas", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AnnotationOverlay.Canvas is gone.");
        using var canvas = (IDisposable)Activator.CreateInstance(canvasType, new Size(w, h))!;
        canvasType.GetMethod("Draw")!.Invoke(canvas, [Array.Empty<Stroke>(), frame]);
        var layer = (Bitmap)canvasType.GetField("bitmap", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(canvas)!;
        using var output = new Bitmap(w, h);
        using (var g = Graphics.FromImage(output))
        {
            g.Clear(Color.FromArgb(0x1B, 0x1C, 0x20));
            g.DrawImageUnscaled(layer, 0, 0);
        }

        output.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
    }

    // The toast's window is a private nested class that only Show() builds, and Show() posts to a message loop this test
    // does not run, so the window is built and fed directly.
    private static void Save(string folder, string name, TransferToast toast, TransferView view)
    {
        var type = typeof(TransferToast).GetNestedType("ToastWindow", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("TransferToast.ToastWindow is gone.");
        using var window = (Form)Activator.CreateInstance(type, toast)!;
        type.GetMethod("Present")!.Invoke(window, [view]);
        Application.DoEvents();
        using var bitmap = new Bitmap(window.Width, window.Height);
        window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
        bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
    }
}

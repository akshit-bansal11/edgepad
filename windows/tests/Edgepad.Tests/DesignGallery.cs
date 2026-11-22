using System.Drawing.Imaging;
using System.Reflection;
using Edgepad.Annotation;
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
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Shoot(folder);
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("The gallery failed.", failure);
        }
    }

    private static void Shoot(string folder)
    {
        using var toast = new TransferToast { OnCancel = () => { }, OnRetry = () => { } };
        var running = new TransferView(TransferPhase.Running, true, 2, 5, "holiday-photos-final.zip", 37, 100, 4194304, 1, [], 0, false);
        var finished = new TransferView(TransferPhase.Finished, true, 5, 5, "a.pdf", 0, 0, 0, 5, [], 0, false);
        Save(folder, "toast-run", toast, running);
        Save(folder, "toast-finished", toast, finished);
        SaveLaser(folder, "laser-medium", 15);
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

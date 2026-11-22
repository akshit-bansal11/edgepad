using System.Drawing.Imaging;
using System.Reflection;
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

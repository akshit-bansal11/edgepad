using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Edgepad.Injection;

namespace Edgepad.Annotation;

/// <summary>
/// The layer the strokes are drawn on: borderless, topmost, over the whole primary monitor, click-through and
/// never activated, so the owner keeps working under the ink. It is a per-pixel-alpha layered window drawn with
/// UpdateLayeredWindow, because WinForms' Opacity and TransparencyKey are all-or-nothing per window or per
/// colour and cannot show the highlighter's partial alpha over what is underneath.
/// </summary>
internal sealed class AnnotationOverlay : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;

    private Canvas? canvas;

    public AnnotationOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;

        // The layer is sized in pixels from the screen's own bounds; WinForms scaling would only fight that.
        AutoScaleMode = AutoScaleMode.None;
    }

    /// <summary>
    /// WS_EX_LAYERED for the per-pixel alpha, and the level readout's three for the rest: WS_EX_TRANSPARENT lets
    /// every click reach the window underneath, WS_EX_NOACTIVATE keeps focus where it was, and WS_EX_TOOLWINDOW
    /// keeps the layer out of Alt-Tab and the taskbar.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    /// <summary>The other half of not stealing focus: WinForms shows this with SW_SHOWNOACTIVATE.</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>Repaints the whole layer with <paramref name="strokes"/> and shows it if it is not up yet.</summary>
    public void Present(IReadOnlyList<Stroke> strokes)
    {
        // Read every time, so a resolution change or a new primary display is followed on the next point.
        var screen = Annotator.PrimaryBounds();
        if (canvas is null || canvas.Size != screen.Size)
        {
            canvas?.Dispose();
            canvas = null;
            canvas = new Canvas(screen.Size);
        }

        canvas.Draw(strokes);
        canvas.Push(Handle, screen.Location);
        if (!Visible)
        {
            // Pushed first, so the layer appears with its ink rather than as one empty frame.
            Bounds = screen;
            Show();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            canvas?.Dispose();
            canvas = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// A screen-sized DIB section held for the life of the layer, with a GDI+ bitmap over the same memory. Each
    /// repaint draws straight into the pixels UpdateLayeredWindow reads, so a stroke costs a clear and a redraw
    /// rather than a fresh 8 MB (33 MB at 4K) bitmap per point.
    /// </summary>
    private sealed class Canvas : IDisposable
    {
        private const uint DibRgbColours = 0;
        private const byte AcSrcOver = 0;
        private const byte AcSrcAlpha = 1;
        private const uint UlwAlpha = 2;

        private readonly nint dc;
        private readonly nint dib;
        private readonly nint previous;
        private readonly Bitmap bitmap;

        public Canvas(Size size)
        {
            Size = size;
            dc = NativeMethods.CreateCompatibleDC(0);
            if (dc == 0)
            {
                throw new InvalidOperationException("CreateCompatibleDC failed");
            }

            // A negative height is a top-down DIB: row 0 is the top, as GDI+ expects of the memory it is handed.
            var header = new BitmapInfoHeader(size.Width, -size.Height);
            dib = NativeMethods.CreateDIBSection(dc, in header, DibRgbColours, out var bits, 0, 0);
            if (dib == 0)
            {
                _ = NativeMethods.DeleteDC(dc);
                throw new InvalidOperationException($"CreateDIBSection failed for {size.Width}x{size.Height}");
            }

            previous = NativeMethods.SelectObject(dc, dib);

            // Premultiplied, which is what UpdateLayeredWindow's per-pixel alpha takes, so GDI+ writes it as is.
            bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
        }

        public Size Size { get; }

        public void Draw(IReadOnlyList<Stroke> strokes)
        {
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            foreach (var stroke in strokes)
            {
                SignatureExport.Draw(g, stroke, Size.Width, Size.Height);
            }
        }

        public void Push(nint window, Point at)
        {
            var position = new NativePoint(at.X, at.Y);
            var size = new NativeSize(Size.Width, Size.Height);
            var origin = new NativePoint(0, 0);
            var blend = new BlendFunction(AcSrcOver, 255, AcSrcAlpha);
            if (!NativeMethods.UpdateLayeredWindow(window, 0, in position, in size, dc, in origin, 0, in blend, UlwAlpha))
            {
                throw new Win32Exception();
            }
        }

        public void Dispose()
        {
            bitmap.Dispose();
            _ = NativeMethods.SelectObject(dc, previous);
            _ = NativeMethods.DeleteObject(dib);
            _ = NativeMethods.DeleteDC(dc);
        }
    }
}

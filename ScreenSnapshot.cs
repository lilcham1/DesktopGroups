using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>
/// The frosted-glass backdrop: a blurred snapshot of a monitor. Taken before the panel appears, or, when a group switches to
/// Frosted glass while its panel is open, with the app's own windows excluded from the capture.
/// </summary>
static class ScreenSnapshot
{
    const double Downscale = 0.125; // blur a small copy: cheap, and upscaling it smooths it further
    const double BlurRadius = 8;    // pixels of the small copy

    public static BitmapSource BlurredMonitor(RECT monitor, params IntPtr[] excludedWindows)
    {
        foreach (var window in excludedWindows)
        {
            if (!SetWindowDisplayAffinity(window, WDA_EXCLUDEFROMCAPTURE))
                throw new Win32Exception();
        }
        try
        {
            DwmFlush(); // let the exclusion reach the screen before copying it
            return Blur(Capture(monitor));
        }
        finally
        {
            foreach (var window in excludedWindows)
            {
                if (!SetWindowDisplayAffinity(window, WDA_NONE))
                    throw new Win32Exception();
            }
        }
    }

    static BitmapSource Capture(RECT monitor)
    {
        var width = monitor.Right - monitor.Left;
        var height = monitor.Bottom - monitor.Top;

        BitmapSource screen;
        using (var bitmap = new System.Drawing.Bitmap(width, height))
        {
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(monitor.Left, monitor.Top, 0, 0, bitmap.Size);
            var hbitmap = bitmap.GetHbitmap();
            try
            {
                screen = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }

        return screen;
    }

    static BitmapSource Blur(BitmapSource screen)
    {
        var small = new TransformedBitmap(screen, new ScaleTransform(Downscale, Downscale));
        var size = new Size(small.PixelWidth, small.PixelHeight);
        var image = new Image { Source = small, Stretch = Stretch.None, Effect = new BlurEffect { Radius = BlurRadius } };
        image.Measure(size);
        image.Arrange(new Rect(size));

        var blurred = new RenderTargetBitmap(small.PixelWidth, small.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        blurred.Render(image);
        blurred.Freeze();
        return blurred;
    }
}

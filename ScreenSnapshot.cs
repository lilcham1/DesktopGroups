using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>The frosted-glass backdrop: a blurred snapshot of a monitor, taken before the panel appears on it.</summary>
static class ScreenSnapshot
{
    const double Downscale = 0.125; // blur a small copy: cheap, and upscaling it smooths it further
    const double BlurRadius = 8;    // pixels of the small copy

    public static BitmapSource BlurredMonitor(RECT monitor)
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

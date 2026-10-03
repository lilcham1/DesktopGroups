using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>Gets the Shell's display name and icon for a file system path.</summary>
static class ShellIcons
{
    public static GroupItem Get(string path, int iconPixels)
    {
        var iid = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        item.GetDisplayName(SIGDN_NORMALDISPLAY, out var name);

        var factory = (IShellItemImageFactory)item;
        Marshal.ThrowExceptionForHR(factory.GetImage(new SIZE { cx = iconPixels, cy = iconPixels }, SIIGBF_ICONONLY, out var hbitmap));
        try
        {
            return new GroupItem(path, name, ToBitmapSource(hbitmap));
        }
        finally
        {
            DeleteObject(hbitmap);
        }
    }

    /// <summary>Copies a 32bpp premultiplied-alpha HBITMAP into a frozen WPF bitmap.</summary>
    static BitmapSource ToBitmapSource(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bitmap) == 0)
            throw new Win32Exception();

        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = bitmap.bmWidth,
            biHeight = -bitmap.bmHeight, // negative = top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var stride = bitmap.bmWidth * 4;
        var pixels = new byte[stride * bitmap.bmHeight];

        var hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbitmap, 0, (uint)bitmap.bmHeight, pixels, ref header, DIB_RGB_COLORS) == 0)
                throw new Win32Exception();
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        var source = BitmapSource.Create(bitmap.bmWidth, bitmap.bmHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }
}

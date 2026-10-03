using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopGroups;

/// <summary>Renders a group's iPhone-style folder tile (up to 9 mini icons) into an .ico file.</summary>
static class GroupIcon
{
    const int Size = 256;     // pixels, the largest icon size Explorer uses
    const int Padding = 30;
    const int Gap = 10;
    const int Columns = 3;
    const int MaxIcons = Columns * Columns;
    const int Cell = (Size - 2 * Padding - (Columns - 1) * Gap) / Columns;

    static readonly Pen TileStroke = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF))), 2));

    /// <summary>
    /// Returns the path of the icon for the group's current contents and style, writing it if needed.
    /// The file name is a fingerprint of both, so a changed group gets a new path and Explorer cannot show a cached old icon.
    /// </summary>
    public static string Write(string group)
    {
        var items = GroupItem.LoadAll(GroupStore.FolderFor(group), Cell).Take(MaxIcons).ToList();
        var style = GroupStyles.Get(group);
        var folder = GroupStore.IconFolderFor(group);
        var path = Path.Combine(folder, Fingerprint(items, style) + ".ico");

        Directory.CreateDirectory(folder);
        if (!File.Exists(path))
            File.WriteAllBytes(path, ToIco(Render(style, dc => DrawItems(dc, items))));

        foreach (var stale in Directory.EnumerateFiles(folder).Where(file => file != path))
            File.Delete(stale);
        return path;
    }

    static string Fingerprint(List<GroupItem> items, GroupStyle style)
    {
        var text = string.Join("\n", items.Select(item => item.Path)) + "\n" + style;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 6);
    }


    static void DrawItems(DrawingContext dc, List<GroupItem> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var x = Padding + i % Columns * (Cell + Gap);
            var y = Padding + i / Columns * (Cell + Gap);
            dc.DrawImage(items[i].Icon, new Rect(x, y, Cell, Cell));
        }
    }

    /// <summary>Draws the tile in the style's color and roundness, lets <paramref name="drawContents"/> draw on it, and encodes the result as PNG.</summary>
    static byte[] Render(GroupStyle style, Action<DrawingContext> drawContents)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var tile = new Rect(3, 3, Size - 6, Size - 6);
            var edge = style.Stroke == null ? TileStroke : new Pen(style.Stroke, 6);
            dc.DrawRoundedRectangle(style.TileFill, edge, tile, style.TileRadius, style.TileRadius);
            if (style.Overlay != null)
                dc.DrawRoundedRectangle(style.Overlay, null, tile, style.TileRadius, style.TileRadius);
            drawContents(dc);
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Wraps one 256x256 PNG in an .ico container (PNG-compressed entries are valid since Windows Vista).</summary>
    static byte[] ToIco(byte[] png)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0);   // reserved
        writer.Write((short)1);   // type: icon
        writer.Write((short)1);   // image count
        writer.Write((byte)0);    // width 256
        writer.Write((byte)0);    // height 256
        writer.Write((byte)0);    // palette size
        writer.Write((byte)0);    // reserved
        writer.Write((short)1);   // color planes
        writer.Write((short)32);  // bits per pixel
        writer.Write(png.Length); // image size
        writer.Write(22);         // image offset: 6-byte header + one 16-byte entry
        writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

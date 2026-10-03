using System.IO;
using System.Windows.Media.Imaging;

namespace DesktopGroups;

/// <summary>One file, folder, or shortcut inside a group.</summary>
public sealed record GroupItem(string Path, string Name, BitmapSource Icon)
{
    /// <summary>Every visible entry of <paramref name="folder"/>, sorted by display name.</summary>
    public static List<GroupItem> LoadAll(string folder, int iconPixels) =>
        GroupStore.Entries(folder)
            .Select(entry => ShellIcons.Get(entry.FullName, iconPixels))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}

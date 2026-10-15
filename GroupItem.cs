using System.IO;
using System.Windows.Media.Imaging;

namespace DesktopGroups;

/// <summary>One file, folder, or shortcut inside a group.</summary>
public sealed record GroupItem(string Path, string Name, BitmapSource Icon)
{
    // Items already read from the Shell, per folder and icon size, keyed by path and last write time.
    // Icons are frozen bitmaps, so items are shared between the UI and publisher threads.
    static readonly Dictionary<(string Folder, int Pixels), Dictionary<(string Path, DateTime Written), GroupItem>> Cache = new();
    static readonly object Gate = new();

    /// <summary>
    /// Every visible entry of <paramref name="folder"/>, sorted by display name. Only entries that are new or changed since
    /// the last call for the same folder and size ask the Shell for their icon; entries that are gone drop out of the cache.
    /// </summary>
    public static List<GroupItem> LoadAll(string folder, int iconPixels)
    {
        Dictionary<(string, DateTime), GroupItem>? previous;
        lock (Gate)
            Cache.TryGetValue((folder, iconPixels), out previous);

        var current = new Dictionary<(string, DateTime), GroupItem>();
        foreach (var entry in GroupStore.Entries(folder))
        {
            var key = (entry.FullName, entry.LastWriteTimeUtc);
            current[key] = previous != null && previous.TryGetValue(key, out var cached) ? cached : ShellIcons.Get(entry.FullName, iconPixels);
        }

        lock (Gate)
            Cache[(folder, iconPixels)] = current;
        return current.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

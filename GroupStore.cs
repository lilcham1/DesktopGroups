using System.IO;

namespace DesktopGroups;

/// <summary>
/// Owns the on-disk layout and every operation on it:
/// contents in %AppData%\DesktopGroups\&lt;group&gt;\, generated icons in &lt;app folder&gt;\Icons\&lt;group&gt;\,
/// and the group's shortcut on the user's desktop.
/// </summary>
static class GroupStore
{
    public static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopGroups");

    // Not under AppData: on this machine Explorer's desktop shows a blank icon for new icon files anywhere under AppData.
    static readonly string IconsRoot = Path.Combine(AppContext.BaseDirectory, "Icons");

    static readonly string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string FolderFor(string group) => Path.Combine(Root, group);

    public static string IconFolderFor(string group) => Path.Combine(IconsRoot, group);

    public static string ShortcutFor(string group) => Path.Combine(Desktop, group + ".lnk");

    public static IEnumerable<string> Groups() =>
        Directory.EnumerateDirectories(Root).Select(path => Path.GetFileName(path));

    /// <summary>The entries a group shows: everything in its folder except hidden files.</summary>
    public static IEnumerable<FileSystemInfo> Entries(string folder) =>
        new DirectoryInfo(folder).EnumerateFileSystemInfos().Where(entry => !entry.Attributes.HasFlag(FileAttributes.Hidden));

    /// <summary>Why <paramref name="name"/> can't be used for a new or renamed group, or null if it can.</summary>
    public static string? NameProblem(string name)
    {
        if (name.Length == 0)
            return "Enter a name.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Names can't contain \\ / : * ? \" < > |";
        if (name.EndsWith('.'))
            return "Names can't end with a period.";
        if (Directory.Exists(FolderFor(name)))
            return "A group with that name already exists.";
        if (Path.Exists(ShortcutFor(name)))
            return "Your desktop already has a shortcut with that name.";
        return null;
    }

    public static void Create(string group)
    {
        Directory.CreateDirectory(FolderFor(group));
        Publish(group);
    }

    /// <summary>Renames the folder and the desktop shortcut in place, so the shortcut keeps its spot on the desktop.</summary>
    public static void Rename(string group, string newName)
    {
        Directory.Move(FolderFor(group), FolderFor(newName));
        File.Move(ShortcutFor(group), ShortcutFor(newName));
        Directory.Delete(IconFolderFor(group), recursive: true);
        GroupStyles.Rename(group, newName);
        Publish(newName);
    }

    /// <summary>Names of the group's entries that already exist on the desktop and would block <see cref="Delete"/>.</summary>
    public static List<string> DesktopConflicts(string group) =>
        Entries(FolderFor(group)).Select(entry => entry.Name).Where(name => Path.Exists(Path.Combine(Desktop, name))).ToList();

    /// <summary>Moves every entry back to the desktop, then removes the group's folder, icons, and shortcut.</summary>
    public static void Delete(string group)
    {
        foreach (var entry in Entries(FolderFor(group)))
        {
            var destination = Path.Combine(Desktop, entry.Name);
            if (entry is DirectoryInfo directory)
                directory.MoveTo(destination);
            else
                ((FileInfo)entry).MoveTo(destination);
        }

        Directory.Delete(FolderFor(group), recursive: true); // only hidden leftovers such as desktop.ini remain
        Directory.Delete(IconFolderFor(group), recursive: true);
        File.Delete(ShortcutFor(group));
        GroupStyles.Remove(group);
    }

    /// <summary>Why <paramref name="paths"/> can't be moved into <paramref name="group"/>, or null if they can.</summary>
    public static string? MoveInProblem(string group, IEnumerable<string> paths)
    {
        var folder = FolderFor(group);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (IsGroupShortcut(path))
                return $"\"{Path.GetFileNameWithoutExtension(path)}\" is a group. Groups can't go inside groups.";
            if (string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase))
                return $"\"{name}\" is already in {group}.";
            if (Path.Exists(Path.Combine(folder, name)))
                return $"{group} already has an item named \"{name}\".";
        }
        return null;
    }

    static bool IsGroupShortcut(string path) =>
        string.Equals(Path.GetDirectoryName(path), Desktop, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(FolderFor(Path.GetFileNameWithoutExtension(path)));

    /// <summary>Moves files or folders into the group's folder, keeping their names. Callers check <see cref="MoveInProblem"/> first.</summary>
    public static void MoveIn(string group, IEnumerable<string> paths)
    {
        var folder = FolderFor(group);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Group folder not found: {folder}");

        foreach (var path in paths)
        {
            var destination = Path.Combine(folder, Path.GetFileName(path));
            if (Directory.Exists(path))
                Directory.Move(path, destination);
            else
                File.Move(path, destination);
        }
    }

    /// <summary>Regenerates the group's tile icon and points its desktop shortcut at it.</summary>
    public static void Publish(string group) => DesktopShortcut.Write(group, GroupIcon.Write(group));
}

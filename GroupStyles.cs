using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopGroups;

/// <summary>
/// Each group's <see cref="GroupStyle"/>, persisted in %AppData%\DesktopGroups\groups.json, plus the background images styles use.
/// Groups never styled use the default style.
/// </summary>
static class GroupStyles
{
    const string ImagePrefix = "background-";

    static readonly string FilePath = Path.Combine(GroupStore.Root, "groups.json");
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    static Dictionary<string, GroupStyle> _styles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads groups.json. A missing file means nothing has been styled yet; a malformed one throws.</summary>
    public static void Load()
    {
        if (!File.Exists(FilePath))
            return;
        var loaded = JsonSerializer.Deserialize<Dictionary<string, GroupStyle>>(File.ReadAllText(FilePath), Options)
            ?? throw new InvalidDataException($"{FilePath} contains null.");
        _styles = new Dictionary<string, GroupStyle>(loaded, StringComparer.OrdinalIgnoreCase);
    }

    public static GroupStyle Get(string group) => _styles.GetValueOrDefault(group) ?? new GroupStyle();

    public static void Set(string group, GroupStyle style)
    {
        _styles[group] = style;
        Save();
    }

    public static void Rename(string group, string newName)
    {
        if (_styles.Remove(group, out var style))
            Set(newName, style);
    }

    public static void Remove(string group)
    {
        if (_styles.Remove(group))
            Save();
    }

    /// <summary>
    /// Copies a picture into the data folder so the style keeps working if the original moves. Returns the stored file name.
    /// Named by content, so importing the same picture twice stores it once.
    /// </summary>
    public static string ImportImage(string sourcePath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)), 0, 6);
        var name = ImagePrefix + hash + Path.GetExtension(sourcePath).ToLowerInvariant();
        var destination = Path.Combine(GroupStore.Root, name);
        if (!File.Exists(destination))
            File.Copy(sourcePath, destination);
        return name;
    }

    /// <summary>Writes groups.json and deletes imported images no style uses anymore.</summary>
    static void Save()
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_styles, Options));

        var used = _styles.Values.Select(style => style.Image).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var image in Directory.EnumerateFiles(GroupStore.Root, ImagePrefix + "*"))
        {
            if (!used.Contains(Path.GetFileName(image)))
                File.Delete(image);
        }
    }
}

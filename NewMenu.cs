using Microsoft.Win32;

namespace DesktopGroups;

/// <summary>
/// Puts "Desktop group" in the desktop's right-click > New menu, pointing at this executable (per user, no admin).
/// Explorer lists a file type in that menu when its extension has a ShellNew key; a "Command" value there runs
/// the command instead of creating a file. The menu shows the type's name and DefaultIcon (the app's own icon).
/// </summary>
static class NewMenu
{
    const string Extension = @"Software\Classes\.desktopgroup";
    const string ProgId = "DesktopGroups.Group";

    // Explorer's cached list of New-menu file types. It isn't refreshed when a type is registered,
    // so it's deleted whenever the registration changes; Explorer rebuilds it the next time it starts.
    const string ExplorerCache = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Discardable\PostSetup\ShellNew";

    /// <summary>Registers (or re-points) the entry at this executable. Does nothing when it's already current.</summary>
    public static void Register()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown.");
        var command = $"\"{exe}\" --new-group";
        var icon = $"{exe},0"; // DefaultIcon is "path,index", unquoted

        using (var shellNew = Registry.CurrentUser.OpenSubKey(Extension + @"\ShellNew"))
        using (var defaultIcon = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
        {
            if (shellNew?.GetValue("Command") as string == command && defaultIcon?.GetValue("") as string == icon)
                return;
        }

        using (var extension = Registry.CurrentUser.CreateSubKey(Extension))
        {
            extension.SetValue("", ProgId);
            using var shellNew = extension.CreateSubKey("ShellNew");
            shellNew.SetValue("Command", command);
        }
        using (var type = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            type.SetValue("", "Desktop group");
            using var defaultIcon = type.CreateSubKey("DefaultIcon");
            defaultIcon.SetValue("", icon);
        }

        Registry.CurrentUser.DeleteSubKeyTree(ExplorerCache, throwOnMissingSubKey: false);
    }
}

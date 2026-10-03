using Microsoft.Win32;

namespace DesktopGroups;

/// <summary>
/// Puts "Desktop group" in the desktop's right-click > New menu, pointing at this executable (per user, no admin).
/// Explorer lists a file type in that menu when its extension has a ShellNew key; a "Command" value there runs
/// the command instead of creating a file, and the menu shows the type's name.
/// </summary>
static class NewMenu
{
    const string Extension = @"Software\Classes\.desktopgroup";
    const string ProgId = "DesktopGroups.Group";

    public static void Register()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown.");

        using (var extension = Registry.CurrentUser.CreateSubKey(Extension))
        {
            extension.SetValue("", ProgId);
            using var shellNew = extension.CreateSubKey("ShellNew");
            shellNew.SetValue("Command", $"\"{exe}\" --new-group");
        }

        using var type = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
        type.SetValue("", "Desktop group");
    }
}

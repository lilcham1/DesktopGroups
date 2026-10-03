using System.Runtime.InteropServices.ComTypes;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>Writes a group's .lnk on the desktop: it launches this app with the group name and shows the group's tile icon.</summary>
static class DesktopShortcut
{
    public static void Write(string group, string iconPath)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown."));
        link.SetArguments($"--group \"{group}\"");
        link.SetIconLocation(iconPath, 0);
        link.SetDescription($"{group} (DesktopGroups)");

        var path = GroupStore.ShortcutFor(group);
        ((IPersistFile)link).Save(path, true);
        SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, path, IntPtr.Zero);
    }
}

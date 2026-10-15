using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>Writes a group's .lnk on the desktop: it launches this app with the group name and shows the group's tile icon.</summary>
static class DesktopShortcut
{
    const int MaxPath = 32767;
    const uint SLGP_RAWPATH = 0x4;
    const int STGM_READ = 0x0;

    /// <summary>
    /// Saves the shortcut and tells Explorer to redraw it, unless it already says exactly this:
    /// rewriting an unchanged shortcut would make Explorer redraw it on every start for nothing.
    /// </summary>
    public static void Write(string group, string iconPath)
    {
        var path = GroupStore.ShortcutFor(group);
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown.");
        var arguments = $"--group \"{group}\"";
        if (File.Exists(path) && Matches(path, target, arguments, iconPath))
            return;

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        link.SetArguments(arguments);
        link.SetIconLocation(iconPath, 0);
        link.SetDescription($"{group} (DesktopGroups)");
        ((IPersistFile)link).Save(path, true);
        SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, path, IntPtr.Zero);
    }

    static bool Matches(string path, string target, string arguments, string iconPath)
    {
        var link = (IShellLinkW)new ShellLink();
        ((IPersistFile)link).Load(path, STGM_READ);

        var text = new StringBuilder(MaxPath);
        link.GetPath(text, text.Capacity, IntPtr.Zero, SLGP_RAWPATH);
        if (!string.Equals(text.ToString(), target, StringComparison.OrdinalIgnoreCase))
            return false;

        link.GetArguments(text.Clear(), text.Capacity);
        if (text.ToString() != arguments)
            return false;

        link.GetIconLocation(text.Clear(), text.Capacity, out var index);
        return index == 0 && string.Equals(text.ToString(), iconPath, StringComparison.OrdinalIgnoreCase);
    }
}

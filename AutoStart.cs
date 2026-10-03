using Microsoft.Win32;

namespace DesktopGroups;

/// <summary>"Start with Windows": a value under HKCU\...\Run pointing at this executable.</summary>
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "DesktopGroups";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey) ?? throw new InvalidOperationException($"Registry key missing: HKCU\\{RunKey}");
        return key.GetValue(ValueName) != null;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? throw new InvalidOperationException($"Registry key missing: HKCU\\{RunKey}");
        if (enabled)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName);
    }
}

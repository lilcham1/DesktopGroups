using System.Windows.Automation;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>Finds where an icon sits on the desktop, through UI Automation on Explorer's desktop list view.</summary>
static class DesktopIcons
{
    /// <summary>Screen rectangle (physical pixels) of the desktop icon labelled <paramref name="name"/>.</summary>
    public static RECT BoundsOf(string name)
    {
        var defView = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        if (listView == IntPtr.Zero)
            throw new InvalidOperationException("The desktop icon list (Progman > SHELLDLL_DefView > SysListView32) was not found.");

        var icon = AutomationElement.FromHandle(listView).FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                new PropertyCondition(AutomationElement.NameProperty, name)))
            ?? throw new InvalidOperationException($"The desktop has no icon named \"{name}\".");

        var bounds = icon.Current.BoundingRectangle;
        return new RECT { Left = (int)bounds.Left, Top = (int)bounds.Top, Right = (int)bounds.Right, Bottom = (int)bounds.Bottom };
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>
/// The iPhone-style panel showing a group's contents. Opens beside the group's desktop icon.
/// Closes on a click elsewhere (unless that click became a drag dropped onto the panel) or when another app takes focus by keyboard.
/// Drop files on it to move them in; drag items out to move them back.
/// Right-click it for Settings, Rename, Delete, New group, Start with Windows, and Quit (the app has no other window or tray icon).
/// </summary>
public partial class GroupPanel : Window
{
    const int GridColumns = 4;
    const double WindowMargin = 24; // DIPs, the transparent margin around the panel in the XAML (room for its shadow)
    const double IconGap = 8;       // DIPs between the desktop icon and the panel

    static GroupPanel? _open;

    readonly RECT _icon;
    readonly RECT _monitor;
    readonly RECT _workArea;
    readonly GlobalMouse _mouse = new();
    string _group;
    GroupStyle _style;
    bool _closing;
    bool _modal;            // one of our dialogs is open; losing activation to it must not close the panel
    bool _pressedOutside;   // a mouse button went down outside our windows and hasn't come up yet
    bool _droppedOnPanel;   // ...and the drag it started was dropped onto this panel
    Point? _dragStart;      // where a press on an item began, until it becomes a drag or ends

    GroupPanel(string group, RECT icon, MONITORINFO monitor, BitmapSource? backdrop)
    {
        var folder = GroupStore.FolderFor(group);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Group folder not found: {folder}");

        InitializeComponent();
        _group = group;
        _style = GroupStyles.Get(group);
        _icon = icon;
        _monitor = monitor.rcMonitor;
        _workArea = monitor.rcWork;
        GroupName.Text = group;
        Backdrop.Source = backdrop; // null unless the group is Frosted glass
        var startWithWindows = new MenuItem { Header = "Start with Windows", IsCheckable = true };
        startWithWindows.Click += (_, _) => AutoStart.Set(startWithWindows.IsChecked);
        Panel.ContextMenu = new ContextMenu
        {
            Items =
            {
                Command("Settings…", ShowSettings),
                Command("Rename group", BeginRename),
                Command("Delete group", DeleteGroup),
                new Separator(),
                Command("New group…", () =>
                {
                    CloseOnce();
                    App.NewGroup();
                }),
                startWithWindows,
                Command("Quit DesktopGroups", () => Application.Current.Shutdown()),
            },
        };
        Panel.ContextMenuOpening += (_, _) => startWithWindows.IsChecked = AutoStart.IsEnabled();
        ApplyStyle();

        // The window sizes itself to its content, which can still change after Loaded (and does on every style change),
        // so placement follows every size change instead of being done once.
        Loaded += (_, _) =>
        {
            LoadItems();
            UpdateLayout();
            PlaceBesideIcon();
            UpdateBackdrop();
            UpdateEffect();
            SizeChanged += (_, _) =>
            {
                PlaceBesideIcon();
                UpdateBackdrop(); // the clip follows the new panel size even when the position didn't change
                UpdateEffect();
            };
            LocationChanged += (_, _) => UpdateBackdrop();
        };
        _mouse.Pressed += point =>
        {
            if (IsOurWindow(point))
                return;
            _pressedOutside = true;
            _droppedOnPanel = false;
        };
        _mouse.Released += _ =>
        {
            if (!_pressedOutside)
                return;
            _pressedOutside = false;
            if (!_droppedOnPanel && !_modal)
                Dispatcher.InvokeAsync(CloseOnce); // not from inside the hook callback
        };
        Closing += (_, _) => _closing = true;
        Closed += (_, _) =>
        {
            Dispatcher.InvokeAsync(ReleaseMemory, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            _mouse.Dispose();
            _open = _open == this ? null : _open;
        };
        // A click outside is handled on release (it may be the start of a drag onto the panel); this covers Alt+Tab and the Windows key.
        Deactivated += (_, _) =>
        {
            if (!_modal && !_pressedOutside)
                CloseOnce();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
                CloseOnce();
        };
    }

    public string Group => _group;

    public GroupStyle CurrentStyle => _style;

    /// <summary>Finds the group's desktop icon, snapshots its monitor for frosted glass, then opens the panel beside the icon.</summary>
    public static void Open(string group)
    {
        _open?.CloseOnce();

        var icon = DesktopIcons.BoundsOf(group);
        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromRect(ref icon, MONITOR_DEFAULTTONEAREST), ref monitor))
            throw new Win32Exception();

        // Only frosted glass needs the snapshot; take it before the panel exists so the panel isn't in it.
        var backdrop = GroupStyles.Get(group).Theme == Theme.Frosted ? ScreenSnapshot.BlurredMonitor(monitor.rcMonitor) : null;
        _open = new GroupPanel(group, icon, monitor, backdrop);
        _open.Show();
        _open.Activate();
    }

    void CloseOnce()
    {
        if (!_closing)
            Close();
    }

    /// <summary>True when the window under <paramref name="point"/> belongs to this app: the panel, its menu, or one of our dialogs.</summary>
    static bool IsOurWindow(POINT point)
    {
        GetWindowThreadProcessId(WindowFromPoint(point), out var processId);
        return processId == Environment.ProcessId;
    }

    void LoadItems()
    {
        var iconPixels = (int)Math.Round(_style.IconPixels * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var items = GroupItem.LoadAll(GroupStore.FolderFor(_group), iconPixels);
        Items.ItemsSource = items;
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Pushes the style into the dynamic resources and elements the XAML leaves open.</summary>
    void ApplyStyle()
    {
        var style = _style;
        var text = new SolidColorBrush(style.Text);
        Resources["TextBrush"] = text;
        Resources["HoverBrush"] = new SolidColorBrush(style.Hover);
        Resources["IconSize"] = style.IconPixels;
        Resources["CellWidth"] = style.CellWidth;
        Resources["ItemMargin"] = new Thickness(style.ItemMargin);
        Resources["PanelWidth"] = GridColumns * (style.CellWidth + 2 * style.ItemMargin);

        var grid = style.View == ViewMode.Grid;
        Items.ItemTemplate = (DataTemplate)Resources[grid ? "GridItem" : "ListItem"];
        Items.ItemsPanel = (ItemsPanelTemplate)Resources[grid ? "GridPanel" : "ListPanel"];

        var corners = new CornerRadius(style.PanelRadius);
        PanelBackground.Background = style.Fill;
        PanelBackground.BorderBrush = style.Stroke;
        PanelBackground.BorderThickness = new Thickness(style.Stroke == null ? 0 : 2);
        PanelBackground.CornerRadius = corners;
        PanelBackground.Effect = style.Shadow;
        PanelOverlay.Background = style.Overlay;
        PanelOverlay.CornerRadius = corners;
        PanelDecoration.Background = style.Decoration;
        PanelDecoration.CornerRadius = corners;
        DropHint.CornerRadius = corners;
        Backdrop.Visibility = style.Theme == Theme.Frosted ? Visibility.Visible : Visibility.Collapsed;
        PanelContent.Margin = new Thickness(style.PanelPadding);
        EmptyHint.Foreground = text;

        var below = style.NamePosition == NamePosition.Below;
        DockPanel.SetDock(NameArea, below ? Dock.Bottom : Dock.Top);
        NameArea.Margin = below ? new Thickness(0, 12, 0, 0) : new Thickness(0, 0, 0, 12);
        NameArea.Visibility = style.NamePosition == NamePosition.Hidden ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Applies and saves a new style; the open panel and the desktop tile update immediately.</summary>
    public void ChangeStyle(GroupStyle style)
    {
        // Switched to frosted glass while open: snapshot the screen now, leaving this app's windows out of it.
        if (style.Theme == Theme.Frosted && Backdrop.Source == null)
        {
            var ourWindows = Application.Current.Windows.Cast<Window>().Select(window => new WindowInteropHelper(window).Handle).ToArray();
            Backdrop.Source = ScreenSnapshot.BlurredMonitor(_monitor, ourWindows);
        }

        _style = style;
        GroupStyles.Set(_group, style);
        ApplyStyle();
        LoadItems();
        UpdateLayout();
        PlaceBesideIcon(); // a new placement doesn't change the size, so SizeChanged won't do it
        UpdateBackdrop();
        UpdateEffect();
        GroupStore.Publish(_group);
    }

    /// <summary>
    /// Puts the visible panel <see cref="IconGap"/> away from the desktop icon on the side the style asks for,
    /// centered on the icon along the other axis, and kept inside the work area of the icon's monitor.
    /// </summary>
    void PlaceBesideIcon()
    {
        // WPF's size, not GetWindowRect: SizeChanged fires before the native window has been resized.
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var inset = (int)Math.Round((WindowMargin - IconGap) * dpi.DpiScaleX); // window edge to icon edge
        var centerX = (_icon.Left + _icon.Right) / 2;
        var centerY = (_icon.Top + _icon.Bottom) / 2;

        var (x, y) = _style.Placement switch
        {
            PanelPlacement.Right => (_icon.Right - inset, centerY - height / 2),
            PanelPlacement.Left => (_icon.Left + inset - width, centerY - height / 2),
            PanelPlacement.Below => (centerX - width / 2, _icon.Bottom - inset),
            _ => (centerX - width / 2, _icon.Top + inset - height),
        };
        x = Math.Clamp(x, _workArea.Left, _workArea.Right - width);
        y = Math.Clamp(y, _workArea.Top, _workArea.Bottom - height);

        if (!SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE))
            throw new Win32Exception();
    }

    /// <summary>Lines the monitor snapshot up with the real screen behind the window and clips it to the panel's rounded shape.</summary>
    void UpdateBackdrop()
    {
        if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var window))
            throw new Win32Exception();

        var dpi = VisualTreeHelper.GetDpi(this);
        var offsetX = (_monitor.Left - window.Left) / dpi.DpiScaleX;
        var offsetY = (_monitor.Top - window.Top) / dpi.DpiScaleY;
        Canvas.SetLeft(Backdrop, offsetX);
        Canvas.SetTop(Backdrop, offsetY);
        Backdrop.Width = (_monitor.Right - _monitor.Left) / dpi.DpiScaleX;
        Backdrop.Height = (_monitor.Bottom - _monitor.Top) / dpi.DpiScaleY;

        var panel = PanelBackground.TranslatePoint(new Point(), Root);
        var radius = _style.PanelRadius;
        Backdrop.Clip = new RectangleGeometry(
            new Rect(panel.X - offsetX, panel.Y - offsetY, PanelBackground.ActualWidth, PanelBackground.ActualHeight), radius, radius);
    }

    /// <summary>Restarts the animated effect at the panel's current size, clipped to its rounded shape.</summary>
    void UpdateEffect()
    {
        var size = new Size(PanelBackground.ActualWidth, PanelBackground.ActualHeight);
        EffectLayer.Clip = new RectangleGeometry(new Rect(size), _style.PanelRadius, _style.PanelRadius);
        PanelEffects.Run(EffectLayer, _style.Effect, size, _style.HasLightText);
    }

    static MenuItem Command(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// A panel open leaves screen-sized bitmaps and icon images behind. Collect them (compacting the large-object heap,
    /// where bitmaps live) once the panel is gone, so the app idles small instead of waiting for the GC to get to it.
    /// </summary>
    static void ReleaseMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Opens the settings window beside the panel; the panel stays open behind it as a live preview.</summary>
    void ShowSettings()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var workArea = new Rect(
            _workArea.Left / dpi.DpiScaleX, _workArea.Top / dpi.DpiScaleY,
            (_workArea.Right - _workArea.Left) / dpi.DpiScaleX, (_workArea.Bottom - _workArea.Top) / dpi.DpiScaleY);
        var settings = new SettingsWindow(this, workArea);

        _modal = true;
        try
        {
            settings.ShowDialog();
        }
        finally
        {
            _modal = false;
        }

        if (settings.DeleteRequested)
            DeleteGroup();
        else
            Activate();
    }

    /// <summary>Double-click opens the item; a single press may become a drag out (see <see cref="Item_MouseMove"/>).</summary>
    void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            _dragStart = e.GetPosition(this);
            return;
        }

        _dragStart = null;
        var item = (GroupItem)((FrameworkElement)sender).DataContext;
        Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        CloseOnce();
    }

    /// <summary>
    /// Drags the item out as a file. Whoever it's dropped on (the desktop, a folder, another group's icon) moves it there;
    /// the panel then shows what's left.
    /// </summary>
    void Item_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            return;
        }

        var moved = e.GetPosition(this) - start;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStart = null;
        var item = (GroupItem)((FrameworkElement)sender).DataContext;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DataFormats.FileDrop, new[] { item.Path }), DragDropEffects.Move | DragDropEffects.Copy);
        LoadItems();
    }

    void Panel_DragOver(object sender, DragEventArgs e)
    {
        var problem = DropProblem(e.Data);
        e.Effects = problem == null ? DragDropEffects.Move : DragDropEffects.None;
        DropHintText.Text = problem ?? $"Drop to add to {_group}";
        DropHint.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    void Panel_DragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    void Panel_Drop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (DropProblem(e.Data) != null)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        GroupStore.MoveIn(_group, (string[])e.Data.GetData(DataFormats.FileDrop));
        _droppedOnPanel = true;
        e.Effects = DragDropEffects.None; // we moved the files ourselves; the drag source must not move or delete them again
        LoadItems();
    }

    string? DropProblem(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] paths
            ? GroupStore.MoveInProblem(_group, paths)
            : "Only files and folders can be added.";

    void GroupName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            BeginRename();
    }

    /// <summary>Shows the name editor, even when the style hides the group name.</summary>
    void BeginRename()
    {
        NameBox.Text = _group;
        NameProblem.Visibility = Visibility.Collapsed;
        NameArea.Visibility = Visibility.Visible;
        GroupName.Visibility = Visibility.Collapsed;
        NameEditor.Visibility = Visibility.Visible;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    void EndRename()
    {
        NameEditor.Visibility = Visibility.Collapsed;
        GroupName.Visibility = Visibility.Visible;
        NameArea.Visibility = _style.NamePosition == NamePosition.Hidden ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Enter renames (if the name is allowed), Esc cancels. Handled here so Esc doesn't also close the panel.</summary>
    void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndRename();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var problem = Rename(NameBox.Text.Trim());
            NameProblem.Text = problem ?? "";
            NameProblem.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
            if (problem == null)
                EndRename();
        }
    }

    /// <summary>Renames the group, or returns why <paramref name="name"/> can't be used. Keeping the current name is a no-op.</summary>
    public string? Rename(string name)
    {
        if (name == _group)
            return null;

        var problem = GroupStore.NameProblem(name);
        if (problem != null)
            return problem;

        GroupStore.Rename(_group, name);
        _group = name;
        GroupName.Text = name;
        LoadItems();
        return null;
    }

    /// <summary>Closes the panel first so the confirmation isn't fighting the panel's close-on-deactivate.</summary>
    void DeleteGroup()
    {
        var group = _group;
        CloseOnce();

        var conflicts = GroupStore.DesktopConflicts(group);
        if (conflicts.Count > 0)
        {
            MessageBox.Show(
                $"Can't delete \"{group}\": your desktop already has {string.Join(", ", conflicts)}. Rename or move those first.",
                "DesktopGroups", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var answer = MessageBox.Show(
            $"Delete the group \"{group}\"? Its items go back to your desktop.",
            "DesktopGroups", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            GroupStore.Delete(group);
    }
}

using System.IO;
using System.Windows;

namespace DesktopGroups;

public partial class App : Application
{
    FileSystemWatcher? _watcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
            MessageBox.Show(args.Exception.ToString(), "DesktopGroups crashed", MessageBoxButton.OK, MessageBoxImage.Error);

        // The only UI is one small, short-lived panel: drawing it on the CPU avoids loading the GPU driver into the process.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        NewMenu.Register(); // re-pointed at this copy on every start, like the group shortcuts below
        Directory.CreateDirectory(GroupStore.Root);
        GroupStyles.Load();
        // Tiles are checked and, if needed, redrawn on the publisher thread; startup doesn't wait for them.
        foreach (var group in GroupStore.Groups())
            GroupPublisher.Request(group);

        _watcher = new FileSystemWatcher(GroupStore.Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };
        _watcher.Created += (_, args) => OnGroupContentChanged(args.FullPath);
        _watcher.Deleted += (_, args) => OnGroupContentChanged(args.FullPath);
        _watcher.Renamed += (_, args) => OnGroupContentChanged(args.FullPath);
        _watcher.EnableRaisingEvents = true;

        SingleInstance.Listen(args => Dispatcher.InvokeAsync(() => HandleCommand(args)));
        HandleCommand(e.Args);

        // Once idle: warm up what the first panel open would otherwise pay for (panel XAML and window, item icons, UI Automation).
        Dispatcher.InvokeAsync(GroupPanel.Warm, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        GroupPublisher.Queue(DesktopIcons.Warm);
    }

    /// <summary>
    /// No arguments: plain start (sign-in), stay in the background.
    /// "--new-group": ask for a name and create the group (desktop New menu, or launching the app again).
    /// "--group &lt;name&gt;": open the group's panel.
    /// "--group &lt;name&gt; &lt;paths...&gt;": files were dropped on the group's shortcut; move them in.
    /// </summary>
    static void HandleCommand(string[] args)
    {
        if (args.Length == 0)
            return;
        if (args is ["--new-group"])
        {
            NewGroup();
            return;
        }
        if (args.Length < 2 || args[0] != "--group")
            throw new ArgumentException($"Unknown arguments: {string.Join(' ', args)}");

        var group = args[1];
        var dropped = args[2..];
        if (dropped.Length == 0)
        {
            GroupPanel.Open(group);
            return;
        }

        var problem = GroupStore.MoveInProblem(group, dropped);
        if (problem != null)
            MessageBox.Show(problem, "DesktopGroups", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            GroupStore.MoveIn(group, dropped);
    }

    public static void NewGroup()
    {
        var name = NameDialog.Ask();
        if (name != null)
            GroupStore.Create(name);
    }

    /// <summary>
    /// Changes inside a group folder redraw that group's tile (coalesced by the publisher) and refresh its panel if it's open.
    /// Top-level changes (whole groups) are handled by the actions that make them.
    /// </summary>
    static void OnGroupContentChanged(string fullPath)
    {
        var segments = Path.GetRelativePath(GroupStore.Root, fullPath).Split(Path.DirectorySeparatorChar);
        if (segments.Length < 2)
            return;

        var group = segments[0];
        GroupPublisher.Request(group);
        Current.Dispatcher.InvokeAsync(() => GroupPanel.ContentsChanged(group));
    }
}

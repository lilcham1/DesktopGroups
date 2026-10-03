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

        if (!SingleInstance.TryClaim())
        {
            // Launching the app again while it runs (it has no window or tray icon) means "make a new group".
            SingleInstance.SendToPrimary(e.Args.Length == 0 ? ["--new-group"] : e.Args);
            Shutdown();
            return;
        }

        NewMenu.Register(); // re-pointed at this copy on every start, like the group shortcuts below
        Directory.CreateDirectory(GroupStore.Root);
        GroupStyles.Load();
        foreach (var group in GroupStore.Groups())
            GroupStore.Publish(group);

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
    /// Changes inside a group folder republish that group. Top-level changes (whole groups) are handled by the actions that make them.
    /// Events still queued for a group that has since been deleted or renamed are dropped: there is nothing left to publish.
    /// </summary>
    void OnGroupContentChanged(string fullPath)
    {
        var segments = Path.GetRelativePath(GroupStore.Root, fullPath).Split(Path.DirectorySeparatorChar);
        if (segments.Length < 2)
            return;

        var group = segments[0];
        Dispatcher.InvokeAsync(() =>
        {
            if (Directory.Exists(GroupStore.FolderFor(group)))
                GroupStore.Publish(group);
        });
    }
}

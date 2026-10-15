using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace DesktopGroups;

/// <summary>
/// The one thread that writes group tiles and shortcuts (<see cref="GroupStore.Publish"/>), so the UI never waits for
/// icons, tile rendering, or Explorer. A requested group is published once it has been quiet for <see cref="QuietMs"/>,
/// so a burst of changes (ten dropped files, clicks through Settings) renders its tile once.
/// Group create, rename, and delete also run here, so they can never overlap a tile being written.
/// </summary>
static class GroupPublisher
{
    const int QuietMs = 250;

    static readonly Dispatcher Worker = StartWorker();

    // Only touched on the worker thread.
    static readonly Dictionary<string, DispatcherTimer> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Publishes <paramref name="group"/> after it has been quiet for a moment. Returns immediately.</summary>
    public static void Request(string group) => Worker.InvokeAsync(() =>
    {
        if (!Pending.TryGetValue(group, out var timer))
        {
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(QuietMs), DispatcherPriority.Normal, (_, _) => PublishPending(group), Worker);
            Pending[group] = timer;
        }
        timer.Stop();
        timer.Start();
    });

    /// <summary>Runs <paramref name="work"/> on the publisher thread and waits for it.</summary>
    public static void Invoke(Action work) => Worker.Invoke(work);

    /// <summary>Queues low-priority <paramref name="work"/> on the publisher thread, such as warming caches at startup.</summary>
    public static void Queue(Action work) => Worker.InvokeAsync(work, DispatcherPriority.Background);

    /// <summary>A group deleted or renamed since it was requested has nothing left to publish.</summary>
    static void PublishPending(string group)
    {
        Pending.Remove(group, out var timer);
        timer!.Stop();
        if (Directory.Exists(GroupStore.FolderFor(group)))
            GroupStore.Publish(group);
    }

    /// <summary>
    /// An STA thread (tile rendering and IShellLink need one) running its own dispatcher. Its unhandled exceptions are
    /// rethrown on the UI thread, where the app reports crashes.
    /// </summary>
    static Dispatcher StartWorker()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, args) =>
            {
                var failure = ExceptionDispatchInfo.Capture(args.Exception);
                args.Handled = true;
                Application.Current.Dispatcher.InvokeAsync(failure.Throw);
            };
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "DesktopGroups publisher",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}

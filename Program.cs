using System.Runtime.CompilerServices;

namespace DesktopGroups;

/// <summary>
/// Entry point. Every double-click on a group, file drop on it, or New-menu click starts this exe again; that second
/// launch only hands its arguments to the running instance, so it returns before any WPF assembly is loaded.
/// </summary>
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (!SingleInstance.TryClaim())
        {
            // Launching the app again while it runs (it has no window or tray icon) means "make a new group".
            SingleInstance.SendToPrimary(args.Length == 0 ? ["--new-group"] : args);
            return;
        }
        RunApp();
    }

    // Separate and never inlined, so WPF is only loaded when this method first runs.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

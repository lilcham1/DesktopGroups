using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using static DesktopGroups.NativeMethods;

namespace DesktopGroups;

/// <summary>
/// One running instance per user session. Later launches (double-clicking a group shortcut, dropping files on it)
/// hand their command-line arguments to the running instance over a named pipe and exit.
/// </summary>
static class SingleInstance
{
    const string MutexName = @"Local\DesktopGroups.Instance";
    const string PipeName = "DesktopGroups.Commands";
    const int ConnectTimeoutMs = 5000;

    static Mutex? _mutex;

    public static bool TryClaim()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        return createdNew;
    }

    public static void SendToPrimary(string[] args)
    {
        using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        pipe.Connect(ConnectTimeoutMs);

        // This launch came from a user action, so it may pass foreground rights on; the running instance needs them to activate its panel.
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var primaryProcessId) || !AllowSetForegroundWindow(primaryProcessId))
            throw new Win32Exception();

        using var writer = new StreamWriter(pipe);
        writer.Write(JsonSerializer.Serialize(args));
    }

    /// <summary>Receives commands on a background thread and passes each one to <paramref name="onCommand"/>.</summary>
    public static void Listen(Action<string[]> onCommand)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                pipe.WaitForConnection();
                using var reader = new StreamReader(pipe);
                var args = JsonSerializer.Deserialize<string[]>(reader.ReadToEnd())
                    ?? throw new InvalidDataException("Empty command from another DesktopGroups launch.");
                onCommand(args);
            }
        })
        {
            IsBackground = true,
            Name = "DesktopGroups command pipe",
        };
        thread.Start();
    }
}

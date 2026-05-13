using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Fdp.Toolkit.Runner;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Background REPL. Reads stdin on a dedicated background thread and dispatches
/// commands as <see cref="Action{SubsystemOrchestrator}"/> delegates via
/// <see cref="OnCommandDispatched"/>. The main loop must call
/// <see cref="SubsystemOrchestrator.DrainConsoleActions"/> each tick to execute them.
/// </summary>
public sealed class ConsoleCommandService : IDisposable
{
    private readonly TextReader _input;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    // Exposed for registering additional commands (e.g. from integration tests).
    private readonly Dictionary<string, (string Description, Action<SubsystemOrchestrator> Command)>
        _commands = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised when a command is parsed. Subscribers enqueue the action into the main thread.
    /// Typically wired as: <c>svc.OnCommandDispatched += orchestrator.EnqueueConsoleAction</c>.
    /// </summary>
    public event Action<Action<SubsystemOrchestrator>>? OnCommandDispatched;

    /// <summary>
    /// Initialises the service. Uses <see cref="Console.In"/> when <paramref name="input"/>
    /// is null (production). Pass a <see cref="StringReader"/> for unit tests.
    /// </summary>
    public ConsoleCommandService(TextReader? input = null)
    {
        _input = input ?? Console.In;
        RegisterBuiltins();
    }

    private void RegisterBuiltins()
    {
        _commands["help"] = ("Show available commands", _ =>
        {
            Console.WriteLine("Available commands:");
            foreach (var (name, (desc, _)) in _commands)
                Console.WriteLine($"  {name,-12} {desc}");
        });

        _commands["open"] = ("Open the local Raylib window", orch =>
        {
            // The actual work is wired by Program.cs when it registers the open command.
            Console.WriteLine("[Runner] 'open' command dispatched.");
        });

        _commands["close"] = ("Close the local Raylib window", orch =>
        {
            Console.WriteLine("[Runner] 'close' command dispatched.");
        });

        _commands["exit"] = ("Shut down the process", orch =>
        {
            Console.WriteLine("[Runner] Initiating shutdown...");
            orch.Stop();
        });
    }

    /// <summary>
    /// Registers (or replaces) a named command. May be called after construction to override
    /// a built-in command with a concrete implementation.
    /// </summary>
    public void RegisterCommand(string name, string description, Action<SubsystemOrchestrator> action)
        => _commands[name] = (description, action);

    /// <summary>
    /// Starts the background stdin reader thread. Safe to call once.
    /// </summary>
    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "ConsoleCommandService" };
        _thread.Start();
    }

    private void ReadLoop()
    {
        try
        {
            ReadLoopCore();
        }
        catch (ObjectDisposedException)
        {
            // CancellationTokenSource was disposed between the loop condition check and
            // accessing the token -- this is expected during shutdown.
        }
    }

    private void ReadLoopCore()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = _input.ReadLine();
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (line == null) break; // EOF (stream closed or piped input exhausted)

            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (_commands.TryGetValue(line, out var entry))
                OnCommandDispatched?.Invoke(entry.Command);
            else
                Console.WriteLine($"[Runner] Unknown command: '{line}'. Type 'help' for a list.");
        }
    }

    public void Dispose()
    {
        if (!_cts.IsCancellationRequested)
            _cts.Cancel();
        // Do NOT call _thread.Join() -- the background thread is blocked on ReadLine() and
        // will exit naturally when the process shuts down (IsBackground = true). Joining
        // would block the test teardown for the full ReadLine timeout.
        _cts.Dispose();
    }
}

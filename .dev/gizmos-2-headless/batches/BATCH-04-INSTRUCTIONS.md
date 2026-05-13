# BATCH-04 Instructions

**Sprint:** Gizmos-2 Headless  
**Tasks:** GZH-012, GZH-013  
**Design ref:** [DESIGN.md](../DESIGN.md) §8.2, §8.3, §9  
**Task details:** [TASK-DETAILS.md](../TASK-DETAILS.md#gzh-012--openlocalwindow-and-closelocalwindow)  

---

## Context

BATCH-01 through BATCH-03 are committed and passing. You are implementing the final two remaining
tasks of the Gizmos-2 Headless sprint:

- **GZH-012**: Extract the `if (!config.Headless)` Raylib/ImGui bootstrap in `Program.cs` into
  callable `OpenLocalWindow()` and `CloseLocalWindow()` methods with a testable seam.
- **GZH-013**: Create `ConsoleCommandService` — a background REPL that reads stdin and dispatches
  actions to the main thread.

---

## BATCH-04 Task 1: GZH-012 — `OpenLocalWindow()` / `CloseLocalWindow()`

### 1.1 Design overview

DESIGN.md §8.2 specifies `OpenLocalWindow()` and `CloseLocalWindow()` methods. Because Raylib
calls (`InitWindow`, `CloseWindow`, `rlImGui.Setup`, etc.) cannot run in unit tests, you **must**
introduce a testable seam — an `IPresentationShell` interface that wraps all Raylib/ImGui calls.

### 1.2 `IPresentationShell` interface

**Location:** `Hrot/Runner/Hrot.ClusterRunner/Presentation/IPresentationShell.cs`

```csharp
namespace Hrot.ClusterRunner.Presentation;

/// <summary>
/// Testable seam for Raylib and ImGui window operations.
/// </summary>
internal interface IPresentationShell
{
    void InitWindow(int width, int height, string title, int targetFps);
    void SetupImGui();
    void ShutdownImGui();
    void CloseWindow();
    void UnloadAtlasTexture();
    Fdp.Presentation.Icons.IconAtlas LoadIconAtlas();
}
```

### 1.3 `RaylibPresentationShell` production implementation

**Location:** `Hrot/Runner/Hrot.ClusterRunner/Presentation/RaylibPresentationShell.cs`

Wraps the existing Raylib calls from `Program.cs`. Example:

```csharp
internal sealed class RaylibPresentationShell : IPresentationShell
{
    private Raylib_cs.Texture2D _atlasTexture;

    public void InitWindow(int width, int height, string title, int targetFps)
    {
        Raylib_cs.Raylib.SetConfigFlags(Raylib_cs.ConfigFlags.ResizableWindow | Raylib_cs.ConfigFlags.Msaa4xHint);
        Raylib_cs.Raylib.InitWindow(width, height, title);
        Raylib_cs.Raylib.SetExitKey(Raylib_cs.KeyboardKey.Null);
        Raylib_cs.Raylib.SetTargetFPS(targetFps);
    }

    public void SetupImGui()
    {
        rlImGui_cs.rlImGui.Setup(true);
        ImGuiNET.ImGui.GetIO().ConfigFlags |= ImGuiNET.ImGuiConfigFlags.DockingEnable;
    }

    public void ShutdownImGui() => rlImGui_cs.rlImGui.Shutdown();

    public void CloseWindow()
    {
        if (_atlasTexture.Id != 0)
            Raylib_cs.Raylib.UnloadTexture(_atlasTexture);
        Raylib_cs.Raylib.CloseWindow();
    }

    public void UnloadAtlasTexture()
    {
        if (_atlasTexture.Id != 0)
        {
            Raylib_cs.Raylib.UnloadTexture(_atlasTexture);
            _atlasTexture = default;
        }
    }

    public Fdp.Presentation.Icons.IconAtlas LoadIconAtlas()
    {
        byte[] pngBytes = Fdp.Presentation.Icons.EmbeddedAtlasResources.GetSilkAtlasPngBytes();
        var img = Raylib_cs.Raylib.LoadImageFromMemory(".png", pngBytes);
        _atlasTexture = Raylib_cs.Raylib.LoadTextureFromImage(img);
        Raylib_cs.Raylib.UnloadImage(img);
        return new Fdp.Presentation.Icons.IconAtlas(
            (nint)_atlasTexture.Id, _atlasTexture.Width, _atlasTexture.Height, 16f);
    }
}
```

### 1.4 `LocalWindowController` class

**Location:** `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs`

This class owns `OpenLocalWindow()` and `CloseLocalWindow()` and manages the `_isLocalWindowOpen`
flag. It is constructed in `Program.cs` (non-static context) and passed to the orchestrator run
section.

```csharp
namespace Hrot.ClusterRunner.Presentation;

internal sealed class LocalWindowController
{
    private readonly IPresentationShell _shell;
    private readonly IReadOnlyList<ISubsystem> _subsystems;
    private readonly RunnerOptions _options;
    private readonly PerspectiveCoordinatorSystem _coordinator;

    private bool _isLocalWindowOpen;
    internal bool IsLocalWindowOpen => _isLocalWindowOpen;

    internal Fdp.Presentation.WindowManager.WindowManager? WindowManager { get; private set; }

    internal LocalWindowController(
        IPresentationShell shell,
        IReadOnlyList<ISubsystem> subsystems,
        RunnerOptions options,
        PerspectiveCoordinatorSystem coordinator)
    {
        _shell       = shell;
        _subsystems  = subsystems;
        _options     = options;
        _coordinator = coordinator;
    }

    internal void OpenLocalWindow()
    {
        if (_isLocalWindowOpen) return;

        _shell.InitWindow(_options.WindowWidth, _options.WindowHeight, "HROT Cluster Runner", _options.TargetFps);
        _shell.SetupImGui();

        var atlas = _shell.LoadIconAtlas();
        var wm = new Fdp.Presentation.WindowManager.WindowManager(atlas);

        // Message log
        var messageLogRegistry = new Fdp.Presentation.Windows.MessageLogRegistry();
        messageLogRegistry.RegisterSource(NLogMessageLogTarget.SharedInstance);
        var msgLogWindow = new Fdp.Presentation.Windows.MessageLogWindow(messageLogRegistry);
        wm.RegisterWindow(msgLogWindow);
        wm.MessageLogRegistry = messageLogRegistry;

        // Register subsystem windows
        foreach (var sub in _subsystems)
            if (sub is IWindowRegistrar registrar)
                registrar.RegisterWindows(wm);

        wm.OnPerspectiveChanged += (oldPersp, newPersp) =>
        {
            _coordinator.Enqueue(new TogglePerspectiveEvent(oldPersp, newPersp));
            Console.WriteLine($"[Runner] Perspective changed: {oldPersp} -> {newPersp}");
        };

        wm.StatusBar.RegisterSection("system_health", sortOrder: 0, () =>
        {
            ImGuiNET.ImGui.Text("System OK");
        });
        var msgLogSection = new Fdp.Presentation.WindowManager.MessageLogStatusBarSection(msgLogWindow, wm);
        wm.StatusBar.RegisterSection("msg_log_notify", sortOrder: 90, msgLogSection.Render);

        string? persisted = wm.LoadSettings();
        var first = _subsystems.Skip(1).FirstOrDefault();
        string defaultPersp = first?.Name ?? "Default";
        bool valid = !string.IsNullOrEmpty(persisted) && _subsystems.Any(s => s.Name == persisted);
        wm.SwitchPerspective(valid ? persisted! : defaultPersp);

        WindowManager = wm;
        _isLocalWindowOpen = true;
    }

    internal void CloseLocalWindow()
    {
        if (!_isLocalWindowOpen) return;

        WindowManager?.SaveSettings();
        WindowManager = null;

        _shell.ShutdownImGui();
        _shell.CloseWindow();

        _isLocalWindowOpen = false;
    }
}
```

> **Note:** Do NOT add `controller.AddListener()` / `controller.RemoveListener()` here yet —
> GZH-012 only extracts the existing interactive path. The LocalTerminalModule installation is
> part of future work (DEBT-003). The existing code does not call `AddListener` on open, so
> preserve that behaviour.

### 1.5 Modify `Program.cs`

Replace the `if (!config.Headless)` block in `Program.cs` with the `LocalWindowController`.

**Before `orchestrator.Initialize()`** — keep the existing Raylib pre-init block:
```csharp
if (!config.Headless)
{
    Raylib_cs.Raylib.SetConfigFlags(...);
    Raylib_cs.Raylib.InitWindow(...);
    ...
    rlImGui_cs.rlImGui.Setup(true);
    ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
}
```
**REMOVE** these lines (they will be moved into `RaylibPresentationShell.InitWindow` and
`RaylibPresentationShell.SetupImGui`).

**After creating `coordinator`**, construct `LocalWindowController`:
```csharp
var shell = new RaylibPresentationShell();
var windowCtrl = new LocalWindowController(shell, subsystems, options, coordinator);

if (!config.Headless)
    windowCtrl.OpenLocalWindow();
```

**Replace the non-headless block** (atlas loading, WindowManager creation, render loop) with:
```csharp
if (windowCtrl.IsLocalWindowOpen)
{
    // 4. The proper non-headless Render Loop
    while (!Raylib_cs.Raylib.WindowShouldClose())
    {
        float dt = Raylib_cs.Raylib.GetFrameTime();
        orchestrator.Update(dt);

        Raylib_cs.Raylib.BeginDrawing();
        Raylib_cs.Raylib.ClearBackground(Raylib_cs.Color.Black);
        orchestrator.DrawWorldAll();

        rlImGui_cs.rlImGui.Begin();
        // ... existing dockspace setup ...
        windowCtrl.WindowManager!.Render();
        orchestrator.DrawUIAll();
        rlImGui_cs.rlImGui.End();

        Raylib_cs.Raylib.EndDrawing();
    }
}
else
{
    orchestrator.Run();
}
```

**In the `finally` block** replace the teardown with:
```csharp
orchestrator.Shutdown();
if (windowCtrl.IsLocalWindowOpen)
    windowCtrl.CloseLocalWindow();
```

### 1.6 Tests for GZH-012

**Location:** `Hrot/Runner/Hrot.ClusterRunner.Tests/Presentation/LocalWindowControllerTests.cs`

Use a `FakePresentationShell` that records call counts:

```csharp
internal sealed class FakePresentationShell : IPresentationShell
{
    public int InitWindowCallCount { get; private set; }
    public int SetupImGuiCallCount { get; private set; }
    public int ShutdownImGuiCallCount { get; private set; }
    public int CloseWindowCallCount { get; private set; }
    public int LoadAtlasCallCount { get; private set; }

    public void InitWindow(int w, int h, string t, int fps) => InitWindowCallCount++;
    public void SetupImGui() => SetupImGuiCallCount++;
    public void ShutdownImGui() => ShutdownImGuiCallCount++;
    public void CloseWindow() => CloseWindowCallCount++;
    public void UnloadAtlasTexture() { }
    public Fdp.Presentation.Icons.IconAtlas LoadIconAtlas()
    {
        LoadAtlasCallCount++;
        // Return a zeroed-out atlas — tests don't need real GPU data.
        return new Fdp.Presentation.Icons.IconAtlas(nint.Zero, 1, 1, 16f);
    }
}
```

**Test class `GZH012_Tests`:**

```
GZH012_1_OpenLocalWindow_SetsIsOpen_AndCallsShell
  - Construct LocalWindowController with FakePresentationShell and empty subsystems list.
  - Call OpenLocalWindow().
  - Assert IsLocalWindowOpen == true.
  - Assert shell.InitWindowCallCount == 1.
  - Assert shell.SetupImGuiCallCount == 1.
  - Assert shell.LoadAtlasCallCount == 1.

GZH012_2_OpenLocalWindow_IsIdempotent
  - Call OpenLocalWindow() twice.
  - Assert shell.InitWindowCallCount == 1 (second call is a no-op).

GZH012_3_CloseLocalWindow_ClearsIsOpen_AndCallsShell
  - Call OpenLocalWindow() then CloseLocalWindow().
  - Assert IsLocalWindowOpen == false.
  - Assert shell.ShutdownImGuiCallCount == 1.
  - Assert shell.CloseWindowCallCount == 1.

GZH012_4_CloseLocalWindow_IsIdempotent
  - Call OpenLocalWindow() then CloseLocalWindow() twice.
  - Assert shell.CloseWindowCallCount == 1.
```

> **Construction note for tests**: `LocalWindowController` needs a
> `PerspectiveCoordinatorSystem` in its constructor (or make it nullable). Since
> `PerspectiveCoordinatorSystem` requires an orchestrator, use a stub or make the
> coordinator parameter nullable with null guard in `OpenLocalWindow`. You can make
> the `PerspectiveCoordinatorSystem _coordinator` field nullable and skip wiring
> `OnPerspectiveChanged` when it is null (only used in tests).

---

## BATCH-04 Task 2: GZH-013 — `ConsoleCommandService`

### 2.1 Design overview

DESIGN.md §9 specifies a background REPL that dispatches `Action<SubsystemOrchestrator>` via an
event. See TASK-DETAILS.md `GZH-013` section for full spec.

### 2.2 Add `EnqueueConsoleAction` to `SubsystemOrchestrator` (FDP submodule)

**Location:** `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs`

Add a `ConcurrentQueue` field and the enqueue method:

```csharp
private readonly System.Collections.Concurrent.ConcurrentQueue<Action<SubsystemOrchestrator>>
    _pendingConsoleActions = new();

/// <summary>
/// Thread-safe enqueue for console-dispatched actions. Called by
/// <see cref="ConsoleCommandService"/> from the background stdin thread.
/// The main loop drains this queue by calling <see cref="DrainConsoleActions"/> each tick.
/// </summary>
public void EnqueueConsoleAction(Action<SubsystemOrchestrator> action)
    => _pendingConsoleActions.Enqueue(action);

/// <summary>
/// Drains all pending console actions on the calling thread (must be the main thread).
/// </summary>
public void DrainConsoleActions()
{
    while (_pendingConsoleActions.TryDequeue(out var action))
        action(this);
}
```

### 2.3 Create `ConsoleCommandService`

**Location:** `Hrot/Runner/Hrot.ClusterRunner/Services/ConsoleCommandService.cs`

Full implementation:

```csharp
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
        _cts.Cancel();
        // Do NOT call _thread.Join() — the background thread is blocked on ReadLine() and
        // will exit naturally when the process shuts down (IsBackground = true). Joining
        // would block the test teardown for the full ReadLine timeout.
        _cts.Dispose();
    }
}
```

> **IMPORTANT**: The `_commands["open"]` and `_commands["close"]` actions above are stubs.
> In `Program.cs`, they must be **replaced** after constructing `ConsoleCommandService`
> so that they call `windowCtrl.OpenLocalWindow()` / `windowCtrl.CloseLocalWindow()`:
>
> ```csharp
> var consoleSvc = new ConsoleCommandService();
> consoleSvc.RegisterCommand("open",  "Open the local Raylib window",
>     _ => windowCtrl.OpenLocalWindow());
> consoleSvc.RegisterCommand("close", "Close the local Raylib window",
>     _ => windowCtrl.CloseLocalWindow());
> consoleSvc.OnCommandDispatched += orchestrator.EnqueueConsoleAction;
> consoleSvc.Start();
> ```
>
> This requires making `RegisterCommand` public:
> ```csharp
> public void RegisterCommand(string name, string description, Action<SubsystemOrchestrator> action)
>     => _commands[name] = (description, action);
> ```

### 2.4 `SubsystemOrchestrator` already has `Stop()` — no new method needed

`SubsystemOrchestrator` already has `public void Stop() => _running = false;` which terminates
the `Run()` loop. The `exit` command should call `orch.Stop()`, not a new method:

```csharp
_commands["exit"] = ("Shut down the process", orch =>
{
    Console.WriteLine("[Runner] Initiating shutdown...");
    orch.Stop();
});
```

**Do NOT add `_stopRequested` or `RequestStop()` to `SubsystemOrchestrator`.**

### 2.5 Wire `ConsoleCommandService` into `Program.cs`

After constructing `windowCtrl`, add:

```csharp
using var consoleSvc = new ConsoleCommandService();
consoleSvc.RegisterCommand("open",  "Open the local Raylib window",
    _ => windowCtrl.OpenLocalWindow());
consoleSvc.RegisterCommand("close", "Close the local Raylib window",
    _ => windowCtrl.CloseLocalWindow());
consoleSvc.OnCommandDispatched += orchestrator.EnqueueConsoleAction;
consoleSvc.Start();
```

Drain actions in the main loop. **For the non-headless case**, add at the top of the render
`while (!Raylib.WindowShouldClose())` body:

```csharp
orchestrator.DrainConsoleActions();
```

**For the headless case**, the existing `orchestrator.Run()` handles ticking internally. To
support console commands in headless mode, drain must happen inside `Run()` as well. The simplest
approach: call `DrainConsoleActions()` at the start of each tick inside `Run()`.

### 2.6 Tests for GZH-013

**Location:** `Hrot/Runner/Hrot.ClusterRunner.Tests/Services/ConsoleCommandServiceTests.cs`

**Test class `GZH013_Tests`:**

```
GZH013_1_KnownCommand_DispatchesAction
  - Create a StringReader with "open\n".
  - Create ConsoleCommandService(input: reader).
  - Capture dispatched actions via OnCommandDispatched.
  - Call Start().
  - Wait up to 500 ms for the background thread to process the line (use SpinWait or
    Thread.Sleep(100) in a loop checking count > 0).
  - Assert exactly one action was dispatched.
  - Assert the action matches the registered "open" command (e.g., the description contains "open"
    or use a tracking flag from RegisterCommand override).

GZH013_2_UnknownCommand_DoesNotDispatch
  - Create a StringReader with "nonexistent\n".
  - Create ConsoleCommandService(input: reader).
  - Call Start().
  - Wait 200 ms.
  - Assert OnCommandDispatched was never raised.

GZH013_3_Dispose_CompletesWithin500ms
  - Create a ConsoleCommandService with Console.In or a blocking PipeReader stub.
  - Call Start().
  - Record start time. Call Dispose().
  - Assert elapsed < 500 ms.
  - (The background thread is IsBackground = true, so Dispose() should return immediately
    after cancelling CTS — no Join() required.)

GZH013_4_ExitCommand_StopsOrchestrator
  - Create a StringReader with "exit\n".
  - Create a real SubsystemOrchestrator via CreateHeadlessOrchestrator with a MockSubsystem.
  - Wire: svc.OnCommandDispatched += orch.EnqueueConsoleAction.
  - Initialize the orchestrator. Start the service and wait for dispatch (100 ms).
  - Call orch.DrainConsoleActions().
  - Assert: running the orchestrator for one more frame returns without looping
    (verify _running == false via RunFrames(0) completing instantly, or by checking
    that orch.Stop() was effectively called — use the existing SubsystemTests helpers
    pattern where a RunFrames assertion or a short RunAsync is used).
```

> **Note on GZH013_3**: since `ConsoleCommandService` uses `IsBackground = true` thread and
> never calls `Thread.Join()` in `Dispose()`, disposal should be essentially instant. The
> 500 ms limit is a generous safety margin. If the test is flaky, increase the limit.

---

## FDP Submodule Changes Summary

These are the only changes to the FDP submodule for BATCH-04:

1. `SubsystemOrchestrator.cs`: add `_pendingConsoleActions`, `EnqueueConsoleAction`, `DrainConsoleActions`.
2. `Run()`: call `DrainConsoleActions()` at the start of each iteration (before `Update(dt)`).

The existing `Stop()` / `_running` mechanism is reused as-is for the `exit` command.

---

## File Summary

### New files (Hrot parent repo)
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/IPresentationShell.cs`
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/RaylibPresentationShell.cs`
- `Hrot/Runner/Hrot.ClusterRunner/Presentation/LocalWindowController.cs`
- `Hrot/Runner/Hrot.ClusterRunner/Services/ConsoleCommandService.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Tests/Presentation/LocalWindowControllerTests.cs`
- `Hrot/Runner/Hrot.ClusterRunner.Tests/Services/ConsoleCommandServiceTests.cs`

### Modified files (Hrot parent repo)
- `Hrot/Runner/Hrot.ClusterRunner/Program.cs` — refactored render loop, window ctrl, consoleSvc wiring

### Modified files (FDP submodule)
- `FDP/Toolkits/Fdp.Toolkits/Runner/SubsystemOrchestrator.cs`

---

## Test Build Commands

```powershell
# From workspace root:
dotnet test "Hrot\Runner\Hrot.ClusterRunner.Tests\Hrot.ClusterRunner.Tests.csproj" --filter "FullyQualifiedName~GZH012|FullyQualifiedName~GZH013" -q
dotnet test "FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj" --filter "FullyQualifiedName~Diagnostics.Gizmos" -q
```

> **Build note**: If `dotnet build` fails with a "Question build" error on CycloneDDS, use:
> `dotnet msbuild <project.csproj> /p:BuildProjectReferences=false /t:Build -verbosity:minimal`
> This is a pre-existing environment issue unrelated to BATCH-04 changes.

---

## Quality Checklist

Before writing the batch report, verify:

- [ ] `LocalWindowController.OpenLocalWindow()` is idempotent (double-call no-ops)
- [ ] `LocalWindowController.CloseLocalWindow()` is idempotent (double-call no-ops)
- [ ] `ConsoleCommandService` background thread has `IsBackground = true`
- [ ] `ConsoleCommandService.Dispose()` does NOT call `Thread.Join()`
- [ ] `SubsystemOrchestrator.Run()` loop correctly exits when `_stopRequested` is set
- [ ] All 4 GZH-012 tests and all 4 GZH-013 tests pass
- [ ] All existing PerspectiveCoordinator tests (11) still pass
- [ ] FDP gizmo tests (187) still pass
- [ ] Program.cs non-headless path is functionally identical to the current code

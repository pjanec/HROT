# Hrot.ClusterRunner

**Project path:** `Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj`
**Output type:** Executable (`Hrot.ClusterRunner.exe`)
**Target framework:** net8.0
**Root namespace:** `Hrot.ClusterRunner` / entry-point namespace `Hrot.Runner`
**Date:** 2026-05-23

---

## README Validation

**Status: Missing**

No `README.md` exists in `Hrot/Runner/Hrot.ClusterRunner/`. The launch profiles in
`Properties/launchSettings.json` and the inline XML doc on `Program` are the primary
references for quick-start guidance. This document supersedes both.

---

## Executive Overview

`Hrot.ClusterRunner` is the **single executable entry point** for the entire HROT
simulation cluster. It is a polyglot runner: depending on the `--mode` argument it
can host any combination of the simulation subsystems (Orchestrator, SimHost, IG,
ExCon, CGF) in a single OS process, or run only one subsystem when each process is
launched on a dedicated machine or isolated by a shell script.

### Cluster nodes managed

| Mode token       | Subsystem class                  | Role in the cluster                        |
|------------------|----------------------------------|--------------------------------------------|
| `orchestrator`   | `OrchestratorSubsystem`          | Cluster state machine, checkpoint manager |
| `simhost`        | `SimHostSubsystem`               | Physics / kinematics, ground truth         |
| `ig`             | `IgSubsystem`                    | Image Generator (3-D rendering)            |
| `excon` / `ios`  | `ExConSubsystem`                 | Exercise controller (legacy alias: ios)    |
| `cgf`            | `CgfSubsystem`                   | Computer-generated forces / AI             |
| `editor`         | `EditorSubsystem`                | Scenario / behaviour tree editor           |
| `stridemock`     | `StrideMockSubsystem`            | Fake Stride renderer for integration tests |
| `replaybrowser`  | `ReplayBrowserSubsystem`         | Standalone replay review tool              |
| `ci`             | `CiSubsystem`                    | Headless deterministic CI harness          |
| `migrate`        | `MigrateMode` (not a subsystem)  | Batch JSON schema migration of a file tree |
| `all` / `demo`   | orchestrator+simhost+ig+excon+cgf| Full cluster in one process                |

### Startup flow (simplified)

1. Parse CLI arguments into `HrotRunnerConfiguration`.
2. Merge optional JSON config file.
3. Validate and expand the mode string into `RequestedSubsystems`.
4. In **CI mode**: build a minimal `SubsystemOrchestrator` with `CiSubsystem`, run it,
   then `Environment.Exit`.
5. In **normal mode**:
   a. Eagerly load all `Hrot.*` and `Fdp.*` assemblies from the deployment directory.
   b. Reflect over all loaded types to find non-abstract `ISubsystem` implementations.
   c. For each discovered subsystem create an isolated DDS participant + network factory.
   d. Instantiate the requested subsystems in order.
   e. Create a `SubsystemOrchestrator` with `PerspectiveUpdateSubsystem` prepended.
   f. Open a Raylib window (unless `--headless`).
   g. Enter the main render loop or the headless `orchestrator.Run()` loop.
   h. On exit: `orchestrator.Shutdown()`, close window.

---

## Architecture

### Subsystem Discovery and Instantiation

The runner uses **reflection-based plugin loading** so that subsystem assemblies can be
added or replaced without changing `Program.cs`. The discovery pipeline is:

```
LoadReferencedAssemblies()
  |
  v
Scan AppDomain for ISubsystem implementations
  |
  v
For each type:
  - create isolated NetworkEntityMap, GeoTransform, FdpEventBus
  - create DDS participant with per-node SenderIdentityConfig
  - create INetworkFactory (NED or BDC)
  - TryCreateSubsystem(type, networkFactory)
  |
  v
Filter to requested subsystem names
  |
  v
SubsystemOrchestrator([PerspectiveUpdateSubsystem] + requestedSubsystems)
```

Each subsystem receives its own isolated state (entity map, geo-transform, event bus,
DDS participant). Multiple subsystems co-hosted in one process are therefore as isolated
as if they ran in separate processes, sharing only the in-process frame clock.

### Node ID Assignment

Every subsystem receives a unique integer node ID computed as:

```
nodeId = baseNodeId + offset

Offsets:
  SimHost      +1
  IG           +100
  ExCon        +200
  Orchestrator +300
  CGF          +400
  CI           +500
  StrideMock   +700
  (other)      +600
```

When `--node-id 0` (legacy default), all offsets resolve to 0, preserving backwards
compatibility with single-node dev setups.

### Network Factory Selection

Two network protocols are supported, selected via `--network`:

- **ned** (default) -- `NedNetworkFactory` backed by CycloneDDS via `Hrot.Network.NED`
- **bdc** -- `BdcNetworkFactory` backed by a custom binary transport via `Hrot.Network.BDC`

### Perspective System

The multi-subsystem window displays one "perspective" (top-down map view) at a time.
Switching perspectives is driven by the `WindowManager.OnPerspectiveChanged` event on
the UI thread. To avoid races, events are enqueued into `PerspectiveCoordinatorSystem`
(a `ConcurrentQueue`) and drained on the main simulation thread at the top of each frame
by `PerspectiveUpdateSubsystem.Update()`.

### Headless / CI Mode

When `--headless` is set, or when `--mode ci` is used, no Raylib window is opened. The
orchestrator's internal blocking `Run()` loop drives the simulation. In CI mode the
`CiSubsystem` wraps a `ScenarioSubsystem` which calls `orchestrator.Stop()` when the
scenario terminates, then `Environment.Exit(exitCode)`.

### Console Command REPL

`ConsoleCommandService` reads `stdin` on a background thread and dispatches named actions
as `Action<SubsystemOrchestrator>` delegates. The main loop calls
`orchestrator.DrainConsoleActions()` at the start of each frame to execute them safely on
the simulation thread. Built-in commands: `help`, `open`, `close`, `exit`.

---

## ASCII Block Diagrams

### Diagram 1: Process Structure (single-process full-cluster mode)

```
+------------------------------------------------------+
|              Hrot.ClusterRunner.exe                  |
|                                                      |
|  +------------------+   +------------------------+  |
|  | PerspectiveUpdate|   |  SubsystemOrchestrator |  |
|  |   Subsystem      |<--|  (Fdp.Toolkits)        |  |
|  | (frame-0 update) |   +------------------------+  |
|  +------------------+             |                  |
|                                   | drives            |
|         +-------------------------+                  |
|         |         |         |         |              |
|         v         v         v         v              |
|  +----------+ +------+ +-------+ +-------+          |
|  |Orchestr. | |SimHost| |  IG   | | ExCon |          |
|  |Subsystem | |Subsy. | |Subsy. | |Subsy. |          |
|  +----------+ +------+ +-------+ +-------+          |
|       |            |        |         |              |
|  DDS  |       DDS  |   DDS  |    DDS  |              |
|  Part.|       Part.|   Part.|    Part.|              |
+------------------------------------------------------+
         |            |        |         |
   [CycloneDDS domain 0 (or --domain N)]
```

### Diagram 2: Startup Sequence

```
+-------------+     +-------------------+     +--------------------+
|  Program    |     | HrotRunnerConfig  |     |  SubsystemOrch.    |
|  Main()     |     |                   |     |                    |
+------+------+     +--------+----------+     +---------+----------+
       |                     |                          |
       |--ParseArgs--------->|                          |
       |<--config------------|                          |
       |--MergeJsonFile()--->|                          |
       |--Validate()-------->|                          |
       |                     |                          |
       |--LoadReferencedAssemblies()                    |
       |--ScanForSubsystems()                           |
       |--TryCreateSubsystem(type, netFactory) x N      |
       |                                                |
       |--new SubsystemOrchestrator(subsystems)-------->|
       |--orchestrator.Initialize()-------------------->|
       |                                                |
       |--LocalWindowController.OpenLocalWindow()       |
       |--ConsoleCommandService.Start()                 |
       |                                                |
       |====RENDER LOOP (Raylib)====================>   |
       |  orchestrator.DrainConsoleActions()            |
       |  orchestrator.Update(dt)---------------------->|
       |  Raylib.BeginDrawing()                         |
       |  orchestrator.DrawWorldAll()------------------>|
       |  ImGui dockspace + WindowManager.Render()      |
       |  orchestrator.DrawUIAll()--------------------->|
       |  Raylib.EndDrawing()                           |
       |====END LOOP================================>   |
       |                                                |
       |--orchestrator.Shutdown()---------------------->|
       |--LocalWindowController.CloseLocalWindow()      |
```

### Diagram 3: Perspective Switch (UI thread -> simulation thread)

```
+---------------+      +------------------------+      +-----------------------+
| WindowManager |      | PerspectiveCoordinator |      | PerspectiveUpdate     |
| (UI thread)   |      | System                 |      | Subsystem             |
|               |      | (thread-safe queue)    |      | (sim thread, frame 0) |
+-------+-------+      +----------+-------------+      +----------+------------+
        |                         |                               |
        |--OnPerspectiveChanged-->|                               |
        |  (UI thread event)      |--Enqueue(TogglePerspective)   |
        |                         |   (ConcurrentQueue<>)         |
        |                         |                               |
        |                         |<--ProcessPendingEvents()------|
        |                         |   (called each frame)         |
        |                         |--SwitchMapOwner(subsysName)-->|
        |                         |  (SubsystemOrchestrator)      |
        |                         |--RemoveListener/AddListener() |
        |                         |  (IGizmoControllable)         |
```

### Diagram 4: CI Mode Flow

```
+----------+    +------------+    +-----------------+    +------------------+
| Program  |    | CiSubsystem|    | ScenarioSubsystem|   | MinimalCIScenario|
| Main()   |    |            |    |                  |   |                  |
+----+-----+    +------+-----+    +--------+---------+   +--------+---------+
     |                 |                   |                       |
     |--new CiSubsystem|                   |                       |
     |--AttachOrch.-->|                   |                       |
     |--Initialize()-->|                   |                       |
     |                 |--new ScenarioSub->|                       |
     |                 |--AttachOrch.----->|                       |
     |                 |--Initialize()---->|                       |
     |                 |                   |--scenario.Configure() |
     |                 |                   |                       |
     |--orchestrator.Run()                 |                       |
     |   (headless loop)                   |                       |
     |       tick N:   |                   |                       |
     |                 |--Update(dt)------->|                       |
     |                 |                   |--EvaluateTick()------->|
     |                 |                   |<--true (at tick 600)---|
     |                 |                   |--orchestrator.Stop()   |
     |                 |                   |--Environment.Exit(0)   |
```

---

## Source Structure

### Namespace / File / Class Map

```
Hrot.Runner
  Program.cs
    class Program (internal, static entry point)
      - Main(string[] args) : int
      - ResolveAppNodeId(string, int) : int
      - LoadReferencedAssemblies() : void
      - ScanForSubsystems() : IEnumerable<Type>
      - TryCreateSubsystem(Type, INetworkFactory) : ISubsystem?

Hrot.ClusterRunner.Configuration
  HrotRunnerConfiguration.cs
    class HrotRunnerConfiguration : RunnerConfiguration
      - ModeString : string        [CLI: -m/--mode]
      - ScenarioName : string      [CLI: -s/--scenario]
      - NetworkProtocol : string   [CLI: --network]
      - LogDirectory : string      [CLI: --log-dir]
      - ConfigFile : string        [CLI: -c/--config]
      - AiBehaviorsProjectPath : string[]
      - RequestedSubsystems : HashSet<string>  (parsed)
      - Validate() : void
      - MergeFromJsonFile(string) : void

Hrot.ClusterRunner.Services
  PerspectiveUpdateSubsystem.cs
    class PerspectiveUpdateSubsystem : ISubsystem  (internal, sealed)
      - Coordinator : PerspectiveCoordinatorSystem?
      - Name : string
      - TitleBarColor : Vector4
      - Initialize(SubsystemConfig) : void
      - Update(float) : void
      - DrawWorld() / DrawUI() / Shutdown() : void

  EyesAndMuscleSubsystem.cs
    class EyesAndMuscleSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar (public, sealed)
      - Name : string
      - TitleBarColor : Vector4
      - World : EntityRepository?
      - Module : EyesAndMuscleModule?
      - Initialize(SubsystemConfig) : void
      - Update(float) / DrawWorld() / DrawUI() / Shutdown() : void

  ConsoleCommandService.cs
    class ConsoleCommandService : IDisposable  (public, sealed)
      - OnCommandDispatched : event Action<Action<SubsystemOrchestrator>>?
      - RegisterCommand(string, string, Action<SubsystemOrchestrator>) : void
      - Start() : void
      - Dispose() : void

Hrot.ClusterRunner.Systems
  PerspectiveCoordinatorSystem.cs
    class PerspectiveCoordinatorSystem  (public, sealed)
      - CurrentPerspective : string
      - Enqueue(TogglePerspectiveEvent) : void
      - ProcessPendingEvents() : void

Hrot.ClusterRunner.Presentation
  IPresentationShell.cs
    interface IPresentationShell  (internal)
      - InitWindow(int, int, string, int) : void
      - SetupImGui() : void
      - ShutdownImGui() : void
      - CloseWindow() : void
      - UnloadAtlasTexture() : void
      - LoadIconAtlas() : IconAtlas

  RaylibPresentationShell.cs
    class RaylibPresentationShell : IPresentationShell  (internal, sealed)
      - InitWindow / SetupImGui / ShutdownImGui / CloseWindow / UnloadAtlasTexture
      - LoadIconAtlas() : IconAtlas

  LocalWindowController.cs
    class LocalWindowController  (internal, sealed)
      - IsLocalWindowOpen : bool
      - WindowManager : WindowManager?
      - OpenLocalWindow() : void
      - CloseLocalWindow() : void

Hrot.ClusterRunner.Scenarios
  CiSubsystem.cs
    class CiSubsystem : ISubsystem  (internal, sealed)
      - Name : string  ("CI")
      - AttachOrchestrator(SubsystemOrchestrator) : void
      - Initialize / Update / DrawWorld / DrawUI / Shutdown : ISubsystem

  MinimalCIScenario.cs
    class MinimalCIScenario : IScenario  (internal, sealed)
      - Key : const string  ("minimalci_01")
      - TargetTicks : const int  (600)
      - FinalEntitySnapshot : (Entity, Entity)
      - ScenarioName : string
      - Configure(EntityRepository, ModuleHostKernel) : void
      - EvaluateTick(uint, EntityRepository) : bool
      - ConfigureVisuals(MapCanvas?, EntityRepository) : void

Hrot.ClusterRunner.Testing
  HrotActionHandlers.cs
    struct MovingTestTag  [ComponentId(219)]
      - VelocityX : float

    class SpawnActionHandler : ITestActionHandler  (public, sealed)
      - ActionName : string  ("spawn")
      - ExecuteAsync(Dictionary<string,object>) : Task<object?>

    class MoveActionHandler : ITestActionHandler  (public, sealed)
      - ActionName : string  ("move")
      - ExecuteAsync(Dictionary<string,object>) : Task<object?>

    class AssertPositionActionHandler : ITestActionHandler  (public, sealed)
      - ActionName : string  ("assert_position")
      - ExecuteAsync(Dictionary<string,object>) : Task<object?>

  OrchestratorActionHandlers.cs
    class ClusterOpActionHandler : ITestActionHandler  (public, sealed)
      - ActionName : string  ("clusterop")
      - ExecuteAsync(Dictionary<string,object>) : Task<object?>

    class AssertEntityCountActionHandler : ITestActionHandler  (public, sealed)
      - ActionName : string  ("assert_entity_count")
      - ExecuteAsync(Dictionary<string,object>) : Task<object?>

    class AddMovingTagActionHandler : ITestActionHandler  (public, sealed)
      - ActionName : string  ("add_moving_tag")
      - ExecuteAsync(Dictionary<string,object>) : Task<object?>
```

---

## Public API Reference

### `HrotRunnerConfiguration` (public class)

Extends `Fdp.Toolkit.Runner.RunnerConfiguration`.

| Member | Kind | Description |
|--------|------|-------------|
| `ModeString` | `string` prop | Raw CLI mode string (required). |
| `ScenarioName` | `string` prop | CI scenario name (`--scenario`). |
| `NetworkProtocol` | `string` prop | `ned` or `bdc` (`--network`). Default: `ned`. |
| `LogDirectory` | `string` prop | NLog file output directory (`--log-dir`). |
| `ConfigFile` | `string` prop | JSON override file (`-c/--config`). |
| `AiBehaviorsProjectPath` | `string[]` prop | Relative path to AI behaviors .csproj for hot-reload. |
| `RequestedSubsystems` | `HashSet<string>` prop | Parsed subsystem set. Populated by `Validate()`. |
| `Validate()` | `void` | Parses mode/wait-for strings, enforces constraints. Throws `InvalidOperationException`. |
| `MergeFromJsonFile(string)` | `void` | Merges non-default values from a JSON file. Throws `FileNotFoundException`. |

Inherited from `RunnerConfiguration` (selection):

| Member | Description |
|--------|-------------|
| `DomainId` | DDS domain ID (`-d/--domain`). Default: 0. |
| `Headless` | Skip window creation (`--headless`). |
| `NoWait` | Skip Waiting Room synchronisation (`--no-wait`). |
| `WaitForString` | Comma-separated peers to wait for (`--wait-for`). |
| `NodeId` | Base node ID for the process. |
| `WaitForPeers` | Parsed peer set, populated by `Validate()`. |

---

### `PerspectiveCoordinatorSystem` (public class)

| Member | Description |
|--------|-------------|
| `PerspectiveCoordinatorSystem(orchestrator, perspToSubsys, gizmoControllables?)` | Constructor. |
| `CurrentPerspective` | Last successfully processed perspective name. |
| `Enqueue(TogglePerspectiveEvent)` | Thread-safe enqueue from UI thread. |
| `ProcessPendingEvents()` | Drains queue; call from simulation thread each frame. |

---

### `ConsoleCommandService` (public class)

| Member | Description |
|--------|-------------|
| `ConsoleCommandService(TextReader? input = null)` | Constructor. Uses `Console.In` in production. |
| `OnCommandDispatched` | `event Action<Action<SubsystemOrchestrator>>?` |
| `RegisterCommand(name, description, action)` | Register or override a named command. |
| `Start()` | Start background stdin reader thread. |
| `Dispose()` | Cancel background thread. |

Built-in commands registered in constructor:

| Command | Description |
|---------|-------------|
| `help` | Print available commands to stdout. |
| `open` | Overridden by `Program` to open the Raylib window. |
| `close` | Overridden by `Program` to close the Raylib window. |
| `exit` | Calls `orchestrator.Stop()`. |

---

### `EyesAndMuscleSubsystem` (public class)

Implements `ISubsystem`, `IMapCameraProvider`, `IWindowRegistrar`.

| Member | Description |
|--------|-------------|
| `Name` | `"EyesAndMuscle"` |
| `TitleBarColor` | Teal-green `(0.15, 0.40, 0.25, 1)`. |
| `World` | `EntityRepository?` after `Initialize`; null before. |
| `Module` | `EyesAndMuscleModule?` after `Initialize`; null before. |
| `Initialize(SubsystemConfig)` | Builds `HrotNodeContext` via `HrotNodeBuilder`, registers modules, initializes kernel. |
| `Update(float)` | Ticks the kernel. |
| `DrawWorld() / DrawUI()` | Delegates to kernel. |
| `Shutdown()` | Shuts down kernel. |

---

### Testing Action Handlers

All handlers implement `Fdp.Toolkit.Runner.Testing.ITestActionHandler`.

#### `SpawnActionHandler`
- **Action name:** `spawn`
- **Args:** `x` (double), `y` (double), `z` (double)
- **Returns:** `{"entity_id": N}`

#### `MoveActionHandler`
- **Action name:** `move`
- **Args:** `entity_id` (int), `x`, `y`, `z` (double)
- **Returns:** `{"moved": 1}` on success, `{"moved": 0}` if no `SimTransform`.

#### `AssertPositionActionHandler`
- **Action name:** `assert_position`
- **Args:** `entity_id` (int)
- **Returns:** `{"x": F, "y": F, "z": F}`

#### `ClusterOpActionHandler`
- **Action name:** `clusterop`
- **Args:** `TargetState` (string or int), optional `ExerciseId`, `ScenarioId`, `TargetWallTicks`
- Special values for `TargetState`: `"TakeCheckpoint"`, `"ReplaySeek"`
- Polls `DdsReader<ClusterOpStatus>` until the operation completes or timeout expires.

#### `AssertEntityCountActionHandler`
- **Action name:** `assert_entity_count`
- **Args:** `expected` (int)
- **Returns:** `{"entity_count": N}`; throws on mismatch.

#### `AddMovingTagActionHandler`
- **Action name:** `add_moving_tag`
- **Args:** `entity_id` (int), `velocity_x` (float)
- **Returns:** `{"tagged": 1}`

#### `MovingTestTag` (ECS component)
- `[ComponentId(219)]` -- reserved test range 200-255.
- Field: `VelocityX : float` -- metres/second along X axis.

---

### CI Scenario: `MinimalCIScenario`

| Member | Value |
|--------|-------|
| `Key` | `"minimalci_01"` |
| `TargetTicks` | `600` (10 s at 60 Hz) |
| `FinalEntitySnapshot` | `(Entity E1, Entity E2)` captured at tick 600 |

Exit codes: `0` = both entities alive after 600 ticks; `1` = entity died early.

---

## Dependencies

### Project References

| Assembly | Role |
|----------|------|
| `Fdp.Toolkits` | `ISubsystem`, `SubsystemOrchestrator`, `RunnerConfiguration`, `SubsystemConfig`, `INetworkFactory`, `ITestActionHandler`, `ScenarioSubsystem` |
| `Fdp.Presentation` | `IWindowRegistrar`, `WindowManager`, `MessageLogWindow`, `IconAtlas` |
| `Hrot.Network.NED` | `NedNetworkFactory`, NED/DDS topology descriptors |
| `Hrot.Network.BDC` | `BdcNetworkFactory`, binary transport |
| `Hrot.Common` | `HrotNodeBuilder`, `HrotNodeContext`, `HrotEnvironment`, `NodeRole`, `IGizmoControllable` |
| `Hrot.Orchestrator` | `OrchestratorSubsystem`, `ClusterMaster` |
| `Hrot.SimHost` | `SimHostSubsystem`, `SimHostComponentRegistry` |
| `Hrot.IG` | `IgSubsystem` |
| `Hrot.ExCon` | `ExConSubsystem` |
| `Hrot.CGF` | `CgfSubsystem` |
| `Hrot.Editor` | `EditorSubsystem` |
| `Hrot.StrideMock` | `StrideMockSubsystem` |
| `Hrot.Presentation` | HROT-specific window registrations |
| `Hrot.ReplayBrowser` | `ReplayBrowserSubsystem` |
| `Hrot.AI.Behaviors` | Copy-local only -- loaded into collectible ALC for hot-reload; no static type use |
| `Fdp.Examples.Common` | `IScenario`, `ScenarioSubsystem`, `ScenarioFailureException` |

### NuGet Packages

| Package | Version | Role |
|---------|---------|------|
| `CommandLineParser` | 2.9.1 | CLI argument parsing via `[Option]` attributes |
| `CycloneDDS.NET` | 0.2.2 | DDS participant, `DdsReader<T>`, sender tracking |
| `Microsoft.Extensions.Logging` | 8.0.0 | `ILogger` abstractions used in test handlers |
| `Newtonsoft.Json` | 13.0.3 | JSON config file deserialization (`MergeFromJsonFile`) |
| `Raylib-cs` | 7.0.2 | Window creation, frame loop, texture loading |
| `rlImGui-cs` | 3.2.0 | ImGui integration for Raylib |

### InternalsVisibleTo

| Assembly |
|----------|
| `Hrot.ClusterRunner.Tests` |
| `Hrot.ClusterRunner.Integration.Tests` |

---

## Batch Migration Mode (`--mode migrate`)

`--mode migrate` is a special non-subsystem execution path. It does not start a DDS cluster,
open a window, or load subsystem assemblies. Instead it enumerates all `*.json` files under
`--input-dir`, attempts to migrate each file that has a `$meta` envelope to the current
registered schema version, and reports progress line-by-line to stdout.

### Activation

When `Program.cs` detects `"migrate"` in `RequestedSubsystems` it branches before the
normal subsystem bootstrap:

```
Program.Main()
  |
  +-- RequestedSubsystems.Contains("migrate") == true
        |
        v
  HrotMigrationBootstrap.BuildClusterRunnerMigrate()
        |
        v
  MigrateMode(services, inputDirectory, targetVersion, dryRun)
        |
        v
  MigrateMode.RunAsync()  -> returns 0 (success) or 1 (one or more failures)
        |
        v
  Environment.Exit(exitCode)
```

### `MigrateMode` (internal sealed class)

```
Hrot/Runner/Hrot.ClusterRunner/Migration/MigrateMode.cs
```

| Constructor parameter | Type | Description |
|---|---|---|
| `services` | `MigrationServices` | From `HrotMigrationBootstrap.BuildClusterRunnerMigrate()`. |
| `inputDirectory` | `string` | Root directory to scan. Falls back to `cwd` if blank. |
| `targetVersion` | `int` | Target schema version, or `-1` to use the current registered version. |
| `dryRun` | `bool` | If true, no files are written; reports only. |
| `output` | `TextWriter?` | Progress stream. Defaults to `Console.Out`. |

`RunAsync()` enumerates `*.json` files recursively under `inputDirectory`. For each file:

1. Attempts `PersistentMigrationAdapter.LoadAndMigrateAsync`.
2. If `$meta` is absent or the doc type is unregistered, marks the file as `SKIPPED`.
3. If already at the target version, marks `SKIPPED (already current)`.
4. If migration succeeds and `!dryRun`, calls `PersistentMigrationAdapter.SaveAsync`.
5. Prints `{N}/{total}: {filename} -- MIGRATED v{from}->v{to}` or `FAILED: {message}`.

Returns exit code `0` if no files failed; `1` if one or more files could not be migrated.

### CLI syntax

```
Hrot.ClusterRunner.exe --mode migrate --input-dir <path> [--dry-run]
```

```
Hrot.ClusterRunner.exe --mode migrate --input-dir test-data/scenario-corpus/multi-version/v1_complete --dry-run
```

The `--dry-run` flag reports what would be migrated without writing any files or sidecars.

---

## Usage Examples

### Command Line

Run the full cluster in a single process:
```
Hrot.ClusterRunner.exe --mode all
```

Run each subsystem in its own process (example shell script):
```
Hrot.ClusterRunner.exe --mode orchestrator --node-id 1000 --no-wait
Hrot.ClusterRunner.exe --mode simhost      --node-id 1000 --wait-for orchestrator
Hrot.ClusterRunner.exe --mode ig           --node-id 1000 --wait-for orchestrator
Hrot.ClusterRunner.exe --mode excon        --node-id 1000 --wait-for orchestrator
Hrot.ClusterRunner.exe --mode cgf          --node-id 1000 --wait-for orchestrator
```

Run on an alternative DDS domain with BDC transport:
```
Hrot.ClusterRunner.exe --mode all --domain 5 --network bdc
```

Run headless (no window):
```
Hrot.ClusterRunner.exe --mode simhost,ig --headless --no-wait
```

Run the CI deterministic harness:
```
Hrot.ClusterRunner.exe --mode ci --scenario MinimalCI_01
```

Open the standalone editor:
```
Hrot.ClusterRunner.exe --mode editor --no-wait
```

Open the replay browser:
```
Hrot.ClusterRunner.exe --mode replaybrowser --no-wait
```

Run SimHost + IG with the Stride mock renderer:
```
Hrot.ClusterRunner.exe --mode orchestrator,excon,cgf,stridemock --no-wait
```

Batch-migrate all scenarios in a directory tree to the current schema version:
```
Hrot.ClusterRunner.exe --mode migrate --input-dir \\nas\scenarios
```

Dry-run migration (report only, no files written):
```
Hrot.ClusterRunner.exe --mode migrate --input-dir \\nas\scenarios --dry-run
```

### JSON Config File

A JSON config file merges over CLI defaults. Only non-default values need to be present.

```json
{
  "ModeString": "orchestrator,simhost,ig,excon,cgf",
  "DomainId": 3,
  "Headless": false,
  "NetworkProtocol": "ned",
  "LogDirectory": "C:\\Logs\\HROT",
  "AiBehaviorsProjectPath": [
    "Subsystems",
    "Hrot.AI.Behaviors",
    "Hrot.AI.Behaviors.csproj"
  ]
}
```

Pass the file with:
```
Hrot.ClusterRunner.exe --mode all --config cluster.json
```

### Launch Profiles (`Properties/launchSettings.json`)

The following profiles are defined for debugging in Visual Studio / Rider:

| Profile | Args |
|---------|------|
| `All (ExCon+IG+SIM+CGF)` | `-m all` |
| `All w/Stride (ExCon+CGF+StrideMock)` | `-m orchestrator,excon,cgf,stridemock --no-wait` |
| `IG` | `-m ig --no-wait` |
| `SimHost` | `-m simhost --no-wait` |
| `ExCon` | `-m excon --no-wait` |
| `Editor` | `-m editor --no-wait` |
| `ReplayBrowser` | `-m replaybrowser --no-wait` |

### Console REPL (runtime)

While the runner is executing, type commands into stdin:

```
help           -- list available commands
open           -- open local Raylib window (if headless was toggled)
close          -- close local Raylib window
exit           -- initiate graceful shutdown
```

### Registering Test Action Handlers (integration tests)

```csharp
// In an integration test fixture:
var spawnHandler  = new SpawnActionHandler(subsystem.World, logger);
var moveHandler   = new MoveActionHandler(subsystem.World, logger);
var clusterOp     = new ClusterOpActionHandler(clusterMaster, statusReader, logger);

var executor = new HeadlessTestExecutor(orchestrator);
executor.RegisterHandler(spawnHandler);
executor.RegisterHandler(moveHandler);
executor.RegisterHandler(clusterOp);

await executor.RunScriptAsync("test-scripts/my-scenario.yaml");
```

### Implementing a New Subsystem (plugin pattern)

```csharp
// In a new assembly Hrot.MySubsystem:
public sealed class MySubsystem : ISubsystem
{
    public string Name => "MySubsystem";
    public Vector4 TitleBarColor => new(0.5f, 0.2f, 0.8f, 1f);

    private readonly INetworkFactory _netFactory;

    // Must have this constructor signature for TryCreateSubsystem() to find it.
    public MySubsystem(INetworkFactory networkFactory)
    {
        _netFactory = networkFactory;
    }

    public void Initialize(SubsystemConfig config) { /* ... */ }
    public void Update(float deltaTime)             { /* ... */ }
    public void DrawWorld()                         { /* ... */ }
    public void DrawUI()                            { /* ... */ }
    public void Shutdown()                          { /* ... */ }
}
```

Add a `<ProjectReference>` to `Hrot.ClusterRunner.csproj`. The assembly is then
discoverable via `ScanForSubsystems()` and selectable as `--mode mysubsystem`.

---

## Best Practices

### Subsystem Isolation

Each subsystem receives its own `NetworkEntityMap`, `GeoTransform`, `FdpEventBus`, and
DDS participant. Do not share these objects across subsystem constructors. Cross-subsystem
communication must happen over DDS topics or the orchestrator event bus.

### AI Behaviors Hot-Reload

`Hrot.AI.Behaviors.dll` must **not** be loaded into the Default AssemblyLoadContext.
`LoadReferencedAssemblies()` explicitly skips it. The `FbtAssemblyHotReloader` in
`EditorSubsystem` loads it into a collectible ALC so it can be unloaded and replaced
without restarting the runner. Do not add static type references to `Hrot.AI.Behaviors`
in `Hrot.ClusterRunner` source.

### Node ID Assignment

Always supply `--node-id` when running multiple instances of the runner on the same
host or DDS domain. Node ID 0 is the legacy single-node fallback; it disables
per-subsystem offset assignment, causing all subsystems to share node ID 0.

### Waiting Room Synchronisation

When launching subsystems in separate processes:
- The orchestrator should use `--no-wait` (it does not need to wait for anyone).
- All other subsystems should use `--wait-for orchestrator` so they do not start
  publishing before the orchestrator is ready.
- Use `--no-wait` during development to skip synchronisation and reduce startup time.

### Mode Constraints

| Mode | Constraints |
|------|-------------|
| `ci` | Always standalone; cannot combine with other modes; requires `--scenario`. |
| `editor` | Cannot combine with `ig`, `excon`, `orchestrator`, or `cgf`. |
| `replaybrowser` | Must run in isolation (count == 1). |
| Combined modes without `all` | `--wait-for` is required unless `--no-wait` is set. |

### Perspective Priority

`PerspectiveUpdateSubsystem` is always inserted as the first subsystem in the
orchestrator list (index 0). This is required by architecture constraint WM-S703:
perspective transitions enqueued during `DrawUI` (UI thread) must be processed before
any subsystem's `Update` runs in the following frame.

### NLog File Rotation

Log files are written to `<AppBase>/logs` (or `--log-dir`) and are named
`{subsystem}_{nodeId}.log`. The runner archives up to 10 rotated files with a 50 MB
size limit. Do not redirect the NLog `FileTarget` layout -- the `[Node-{nodeId}]`
field is essential for correlating multi-subsystem logs.

### Test Action Handlers

`ITestActionHandler` implementations in `Hrot.ClusterRunner.Testing` are **not**
wired into any production boot path. They are constructed and registered only by
`Hrot.ClusterRunner.Integration.Tests` fixtures. Do not instantiate them in `Program.cs`.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.FakeStrideApp` | Sibling runner in `Hrot/Runner/`. Standalone fake Stride rendering app, used for integration testing the Stride presentation path without a real Stride engine. |
| `Fdp.Toolkits` | Provides the orchestration spine: `ISubsystem`, `SubsystemOrchestrator`, `SubsystemConfig`, `RunnerConfiguration`, `RunnerOptions`, `ITestActionHandler`, `ScenarioSubsystem`, `HeadlessTestExecutor`. |
| `Fdp.Presentation` | Window manager, perspective switching UI, message log, status bar, icon atlas. |
| `Hrot.Common` | `HrotNodeBuilder` (node construction), `HrotEnvironment` (DDS participant factory, geo-transform), `NodeRole` flags, `IGizmoControllable`. |
| `Hrot.Orchestrator` | `OrchestratorSubsystem`, `ClusterMaster`, `ClusterState` state machine, checkpoint/replay infrastructure. |
| `Hrot.SimHost` | Ground-truth physics / kinematics subsystem. |
| `Hrot.IG` | Image Generator subsystem (3-D scene rendering). |
| `Hrot.ExCon` | Exercise controller subsystem. |
| `Hrot.CGF` | Computer-Generated Forces / AI subsystem. |
| `Hrot.Editor` | Scenario and behaviour tree editor subsystem. |
| `Hrot.StrideMock` | Fake Stride renderer subsystem for CI/integration tests. |
| `Hrot.ReplayBrowser` | Standalone replay review subsystem. |
| `Hrot.AI.Behaviors` | Hot-reloadable AI behaviour tree assembly. Delivered as copy-local only; loaded by `EditorSubsystem` in a collectible ALC. |
| `Hrot.Network.NED` | NED/DDS network factory and topology descriptors. |
| `Hrot.Network.BDC` | BDC binary-transport network factory. |
| `Hrot.ClusterRunner.Tests` | Unit tests for runner internals (`InternalsVisibleTo`). |
| `Hrot.ClusterRunner.Integration.Tests` | Integration / E2E tests using `HeadlessTestExecutor` and the action handlers in `Hrot.ClusterRunner.Testing`. |

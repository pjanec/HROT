# Fdp.Examples.Runner

| Field | Value |
|---|---|
| **Project path** | `FDP/Examples/Fdp.Examples.Runner/Fdp.Examples.Runner.csproj` |
| **Assembly name** | `fdp-demo-runner` |
| **Output type** | Executable (`<OutputType>Exe</OutputType>`) |
| **Target framework** | net8.0 |
| **InternalsVisibleTo** | `Fdp.Examples.Scenarios.Tests` |
| **Date documented** | 2026-05-23 |

## README Validation

**Missing** — No README.md exists in the project folder. This document serves as the
primary reference.

---

## Executive Overview

`Fdp.Examples.Runner` is the **CLI host process** for all FDP example scenarios. It is the
single binary (`fdp-demo-runner`) that:

1. Parses command-line arguments (scenario name, tick budget, DDS domain, visualization
   flag, fixed delta time).
2. Configures NLog programmatically — no external config file required for CI pipelines.
3. Resolves the named scenario from `ScenarioRegistry`.
4. Wires it into the `SubsystemOrchestrator` via `ScenarioSubsystem`.
5. Runs the simulation loop and exits with a well-defined process exit code.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Scenario passed all assertions |
| `1` | Bad arguments / unknown scenario name |
| `2` | Timeout — tick budget exhausted before scenario returned `true` |
| Other | Scenario threw an exception |

### Key learning objectives

1. **Testable entry points** — `Program.RunMain` accepts injectable `stdout` and
   `exitCallback`, enabling test code to assert exit codes without calling
   `Environment.Exit`.
2. **NLog programmatic configuration** — file + console targets, per-scenario log files,
   `${scopeproperty:scenario}` layout renderer.
3. **ScenarioRegistry without reflection** — explicit `switch` maps string keys to
   concrete `IScenario` instances, keeping startup fast and errors obvious.
4. **Headless vs. visualized runs** — `--attach-vis2d` spawns a Raylib 2D map window;
   omitting it runs fully headless.

---

## Architecture

### Runner Flow

```
+-------------------------------------------------------------------+
|  fdp-demo-runner --scenario autodrive --max-ticks 500             |
+-------------------------------------------------------------------+
                            |
                            v
+-------------------------------------------------------------------+
|  Program.RunMain(args, Console.Out, Environment.Exit)             |
|                                                                   |
|  CommandLine.Parser.ParseArguments<DemoRunnerOptions>(args)       |
|            |                                                      |
|            v                                                      |
|  Program.Execute(opts, stdout, exitCallback)                      |
|    1. ScenarioRegistry.Create(opts.Scenario)                      |
|    2. ConfigureNLog(scenarioName)                                  |
|    3. Build RunnerOptions {Headless, DomainId, Deterministic, dt} |
|    4. new ScenarioSubsystem(scenario, maxTicks, exitCallback, dt) |
|    5. new SubsystemOrchestrator([sub], runnerOptions)             |
|    6. orch.Initialize()                                           |
|    7. orch.Run()  <-- simulation loop                             |
|    8. orch.Shutdown()                                             |
|    9. LogManager.Flush() / Shutdown()                             |
+-------------------------------------------------------------------+
```

### Scenario Resolution

```
+----------------------+         +------------------------+
|  DemoRunnerOptions   |         |  ScenarioRegistry      |
|  .Scenario = "auto   |         |  .Create("autodrive")  |
|             drive"   |-------->|                        |
+----------------------+         |  switch (name) {       |
                                  |    "autodrive" =>      |
                                  |      new AutoDrive-    |
                                  |      Scenario()        |
                                  |    "ballisticsandhit"=>|
                                  |      new Ballistics-   |
                                  |      AndHitScenario()  |
                                  |    ...                 |
                                  |    _ => throw Arg-     |
                                  |         Exception      |
                                  |  }                     |
                                  +------------------------+
```

### NLog Configuration

```
+----------------------------------------------+
|  ConfigureNLog(scenarioName)                  |
|                                               |
|  FileTarget "logfile"                         |
|    FileName: logs/demo-{scenario}-{date}-     |
|             {time}.log                        |
|    Level: Trace and above                     |
|    Layout: datetime|level|logger|tick|message |
|                                               |
|  ConsoleTarget "console"                      |
|    Level: Info and above                      |
|    Layout: level | logger (short) | message   |
+----------------------------------------------+
```

### Module Visibility for Tests

```
+-----------------------------+     InternalsVisibleTo     +-----------------------------+
|   fdp-demo-runner           |<-------------------------->|  Fdp.Examples.Scenarios.    |
|   (Fdp.Examples.Runner)     |                            |  Tests                      |
|                             |                            |                             |
|   Program.RunMain(          |                            |  Can call RunMain() with    |
|     args, stdout, exit)     |                            |  custom stdout/exitCallback |
|   [internal]                |                            |  to assert exit codes and   |
+-----------------------------+                            |  log output                 |
                                                           +-----------------------------+
```

---

## Source Structure

```
FDP/Examples/Fdp.Examples.Runner/
+-- Fdp.Examples.Runner.csproj
+-- Program.cs                           namespace Fdp.Examples.Runner
|     static class Program
|       public  static int Main(string[])
|       internal static int RunMain(string[], TextWriter, Action<int>)
|       private static int Execute(DemoRunnerOptions, TextWriter, Action<int>)
|       private static string ConfigureNLog(string)
+-- DemoRunnerOptions.cs                 namespace Fdp.Examples.Runner
|     class DemoRunnerOptions : RunnerConfiguration
+-- ScenarioRegistry.cs                  namespace Fdp.Examples.Runner
|     static class ScenarioRegistry
|       public static IScenario Create(string)
+-- PlaceholderScenario.cs               namespace Fdp.Examples.Runner
      sealed class PlaceholderScenario : IScenario
```

---

## Public API Reference

### `DemoRunnerOptions`

```csharp
public class DemoRunnerOptions : RunnerConfiguration
```

Extends `RunnerConfiguration` (from `Fdp.Toolkit.Runner`) with demo-specific CLI flags.

| Property | CLI flag | Default | Description |
|---|---|---|---|
| `Scenario` | `--scenario` | required | Scenario name (case-insensitive) |
| `MaxTicks` | `--max-ticks` | `500` | Tick budget before timeout (exit 2) |
| `AttachVis2d` | `--attach-vis2d` | `false` | Spawn a Raylib Vis2D visualization window |
| `DomainId` | `--domain-id` (inherited) | `0` | DDS domain ID |
| `Deterministic` | `--deterministic` (inherited) | `false` | Force deterministic simulation |
| `FixedDeltaSeconds` | `--fixed-dt` (inherited) | varies | Fixed simulation timestep in seconds |

### `ScenarioRegistry`

```csharp
public static class ScenarioRegistry
{
    public static IScenario Create(string name);
}
```

Maps scenario name strings to `IScenario` instances. Registration is explicit; no
reflection is used.

**Registered scenarios:**

| Key (case-insensitive) | Class | Phase |
|---|---|---|
| `placeholder` | `PlaceholderScenario` | Phase 0 |
| `autodrive` | `AutoDriveScenario` | Phase 2 |
| `componentdamage` | `ComponentDamageScenario` | Phase 2 |
| `ballisticsandhit` | `BallisticsAndHitScenario` | Phase 3 |
| `behaviorvalidation` | `BehaviorValidationScenario` | Phase 3 |
| `sensorgrid` | `SensorGridScenario` | Phase 3 |
| `missioncommand` | `MissionCommandScenario` | Phase 4 |
| `terrainclamping` | `TerrainClampingScenario` | Phase 4 |
| `parallelepisodes` | `ParallelEpisodesScenario` | Phase 4 |
| `distributedtank` | `DistributedTankScenario` | Phase 5 |
| `urbancombat` | `UrbanCombatNewScenario` | Phase 6 |

**Throws:** `ArgumentException` when the name is not registered.

### `PlaceholderScenario`

```csharp
internal sealed class PlaceholderScenario : IScenario
{
    public string ScenarioName => "placeholder";
    public void Configure(EntityRepository world, ModuleHostKernel kernel);
    public bool EvaluateTick(uint currentTick, EntityRepository world);
    public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world);
}
```

A minimal scenario used to validate runner plumbing (NLog setup, CLI parsing, log file
creation) without requiring any toolkit modules. Returns `true` (success) at tick 1.

### `Program` (internal members)

```csharp
internal static int RunMain(
    string[] args,
    TextWriter stdout,
    Action<int> exitCallback);
```

Testable entry point. Allows injecting a custom `stdout` writer and an `exitCallback`
so tests can capture exit codes without calling `Environment.Exit`. Used by
`Fdp.Examples.Scenarios.Tests`.

---

## Dependencies

### NuGet packages

| Package | Version | Purpose |
|---|---|---|
| `NLog` | 5.2.8 | Structured logging to file and console |
| `CommandLineParser` | 2.9.1 | Declarative CLI argument parsing via `[Option]` attributes |

### Project references

| Project | Purpose |
|---|---|
| `Fdp.Examples.Common` | `IScenario`, `ScenarioSubsystem`, `SubsystemOrchestrator`, `RunnerOptions`, `RunnerConfiguration` |
| `Fdp.Examples.Scenarios` | All concrete `IScenario` implementations |

---

## Usage Examples

### Example 1 — Running a scenario from the command line

```bash
# Run the AutoDrive scenario with 1000-tick budget
dotnet run --project FDP/Examples/Fdp.Examples.Runner -- \
    --scenario autodrive \
    --max-ticks 1000

# Expected output:
# [RUNNER] Log: logs/demo-autodrive-2026-05-23-143022.log
# INFO | AutoDriveScenario | Phase 1 PASSED tick=50
# INFO | AutoDriveScenario | Phase 2 PASSED tick=120
# INFO | AutoDriveScenario | Scenario SUCCESS tick=200
# Exit code: 0
```

### Example 2 — Running with visualization enabled

```bash
dotnet run --project FDP/Examples/Fdp.Examples.Runner -- \
    --scenario sensorgrid \
    --max-ticks 500 \
    --attach-vis2d

# Opens a Raylib window with a 2D map showing sensor ranges
```

### Example 3 — Writing a test that asserts the exit code

```csharp
using Fdp.Examples.Runner;
using System.IO;

[Test]
public void PlaceholderScenario_ExitsWithCode0()
{
    int capturedCode = -1;
    var writer = new StringWriter();

    Program.RunMain(
        args: ["--scenario", "placeholder"],
        stdout: writer,
        exitCallback: code => capturedCode = code);

    Assert.AreEqual(0, capturedCode);
    StringAssert.Contains("[RUNNER] Log:", writer.ToString());
}
```

### Example 4 — Adding a new scenario to the registry

```csharp
// 1. Implement IScenario in Fdp.Examples.Scenarios:
public sealed class MyNewScenario : IScenario
{
    public string ScenarioName => ScenarioNames.MyNew;

    public void Configure(EntityRepository world, ModuleHostKernel kernel)
    {
        // Register components, systems, create entities
    }

    public bool EvaluateTick(uint tick, EntityRepository world)
    {
        // Return true when the scenario completes successfully
        return tick >= 100;
    }

    public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }
}

// 2. Add constant to ScenarioNames:
public static class ScenarioNames
{
    public const string MyNew = "mynew";
}

// 3. Register in ScenarioRegistry.Create():
"mynew" => new MyNewScenario(),
```

### Example 5 — Running in CI with deterministic mode

```bash
dotnet fdp-demo-runner.dll \
    --scenario ballisticsandhit \
    --max-ticks 10 \
    --deterministic

# Deterministic mode uses a fixed delta time and disables wall-clock timing
# Exit 0 = pass, Exit 2 = timeout (increase --max-ticks)
```

---

## Best Practices

### 1. Use `--deterministic` in CI

Headless runs automatically enable deterministic mode. Scenarios that depend on
wall-clock timing should use `FdpConfig.EnforceExplicitComponentIds = true` (already set
in `Program.Main`) and avoid `DateTime.Now` in their logic.

### 2. Keep scenario names lowercase and hyphen-free

The registry normalizes to lowercase via `name.ToLowerInvariant()`. Avoid hyphens in
scenario keys — they are harder to type reliably across shells. Use concatenated words
(e.g. `"ballisticsandhit"`, not `"ballistics-and-hit"`).

### 3. Log tick numbers for CI traceability

`ConfigureNLog` includes `tick=${event-properties:tick}` in the file layout. Scenarios
should use `FdpLog<T>.Info("[phase] PASSED tick={0}", currentTick)` (using the NLog
`{tick}` event property) so the tick appears in the log file for post-mortem analysis.

### 4. Prefer throwing `ScenarioFailureException` over returning `false` indefinitely

A scenario that never passes will consume its entire tick budget and exit with code 2
(timeout). If a required pre-condition is violated, throw `ScenarioFailureException` with
a descriptive phase number and message to produce a clear failure log entry.

### 5. The exit callback is injected, not called directly

`Execute` invokes `exitCallback(code)` before returning, so test code can capture the
code via a local variable. Never call `Environment.Exit` directly inside a scenario.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Examples.Scenarios` | Library of all `IScenario` implementations run by this runner |
| `Fdp.Examples.Common` | Defines `IScenario`, `SubsystemOrchestrator`, `ScenarioSubsystem`, `RunnerOptions` |
| `Fdp.Examples.Scenarios.Tests` | Test project that calls `Program.RunMain` to assert scenario exit codes |
| `Fdp.Toolkit.Runner` | Base `RunnerConfiguration` class that `DemoRunnerOptions` extends |
| `Fdp.ModuleHost` | Provides `ModuleHostKernel` used to wire systems inside scenarios |

---

## Architecture Deep Dive

### Exit Code Contract

The runner communicates scenario outcomes entirely through process exit codes. This design
makes it trivial to integrate with any CI pipeline (GitHub Actions, Jenkins, MSTest, etc.)
without parsing stdout:

```
+-------------------+--------------------------------------------+
| Exit code         | Meaning                                    |
+-------------------+--------------------------------------------+
| 0                 | All phase assertions passed; SUCCESS       |
| 1                 | Bad CLI args, unknown scenario name, or    |
|                   | ScenarioFailureException thrown            |
| 2                 | Tick budget exhausted (timeout)            |
| Other non-zero    | Unhandled exception from scenario code     |
+-------------------+--------------------------------------------+
```

### Testability Architecture

The `Program.RunMain` / `Program.Execute` split is the key testability pattern:

```
+---------------------+      +------------------------+
| Program.Main(args)  |      | Test code              |
|                     |      |                        |
| FdpConfig setup     |      | Program.RunMain(       |
| RunMain(args,       |      |   ["--scenario",       |
|   Console.Out,      |      |    "autodrive"],       |
|   Environment.Exit) |      |   new StringWriter(),  |
+---------------------+      |   code => captured=code|
                             | )                      |
                             +------------------------+
```

Both call paths go through the same `Execute()` method. The only difference is which
`TextWriter` and exit delegate are injected. This makes the entire runner behavior testable
without spawning a subprocess.

`InternalsVisibleTo("Fdp.Examples.Scenarios.Tests")` allows the test project to call the
`internal` `RunMain` method.

### NLog Programmatic Configuration Detail

The `ConfigureNLog` method avoids external config files, making the runner portable across
machines without deployment of `NLog.config`:

```
File target layout:
  ${longdate}|${level:uppercase=true}|${logger}|tick=${event-properties:tick}|
  ${message} ${exception:format=tostring}

File name pattern:
  logs/demo-${scopeproperty:scenario}-${shortdate}-${cached:cached=true:
  inner=${date:format=HHmmss}}.log

Console target layout:
  ${level:uppercase=true} | ${logger:shortName=true} | ${message}
```

The `${cached:cached=true:inner=${date:format=HHmmss}}` layout renderer freezes the
timestamp at the moment the first log message is written, preventing the time component
of the filename from changing as the simulation runs past midnight.

`ScopeContext.PushProperty("scenario", opts.Scenario)` is set before the file target is
configured. This ensures the `${scopeproperty:scenario}` layout renderer in the filename
is populated when the file is opened.

### ScenarioRegistry Design: Explicit Over Reflection

The registry uses a `switch` expression rather than reflection-based discovery:

```csharp
public static IScenario Create(string name) => name.ToLowerInvariant() switch
{
    "autodrive"     => new AutoDriveScenario(),
    "sensorgrid"    => new SensorGridScenario(),
    // ...
    _ => throw new ArgumentException(...)
};
```

**Advantages of explicit registration:**
- Startup time is O(1) — no assembly scanning.
- Compile-time safety — removing a scenario class produces a build error.
- Clear error messages — the `ArgumentException` message lists the invalid name and hints
  at `ScenarioNames` for valid values.

**Disadvantage:** Adding a new scenario requires a manual registry entry. This is
intentional; it prevents scenarios from being silently discovered and run without being
explicitly reviewed and approved.

### RunnerConfiguration Inheritance

`DemoRunnerOptions` extends `RunnerConfiguration` from `Fdp.Toolkit.Runner`. This provides
inherited CLI flags common to all FDP runner applications:

| Inherited flag | Source | Description |
|---|---|---|
| `--domain-id` | `RunnerConfiguration` | DDS domain ID for network scenarios |
| `--deterministic` | `RunnerConfiguration` | Force deterministic fixed-step mode |
| `--fixed-dt` | `RunnerConfiguration` | Override the simulation timestep |

`DemoRunnerOptions` adds three demo-specific flags on top:
- `--scenario` (required)
- `--max-ticks` (default 500)
- `--attach-vis2d` (default false)

### Headless vs. Deterministic vs. Fixed-DT

The three related flags interact as follows:

```
--attach-vis2d = false  => headless = true
headless = true         => deterministic = true (override)
--deterministic         => deterministic = true (explicit)

headless mode:          uses fixed delta time, disables wall-clock sync
                        no Raylib window, no rendering overhead
                        ideal for CI: reproducible tick-by-tick behavior

deterministic mode:     same as headless for simulation purposes
                        can still have a vis2d window open
                        ensures same result across runs given same initial state
```

### Log File Location

Log files are written to `logs/` relative to the executable directory (`AppContext.
BaseDirectory`). In a typical `dotnet run` invocation this is the project's `bin/Debug/
net8.0/` directory. The directory is created if it does not exist.

Log file name format: `demo-{scenario}-{date}-{time}.log`

Example: `logs/demo-autodrive-2026-05-23-143022.log`

### PlaceholderScenario Purpose

`PlaceholderScenario` exists to validate runner infrastructure without any real simulation
logic. It is used in:

1. **CI smoke tests** — verifies that the runner binary starts, parses arguments, sets up
   NLog, and exits cleanly without needing any toolkit dependencies to be functional.
2. **Runner unit tests** — `TestRunMain_Placeholder_ExitsZero()` confirms the testable
   entry point wiring works correctly.
3. **New developer onboarding** — the first command a new developer runs to verify their
   build environment is working.

Running `fdp-demo-runner --scenario placeholder` should always produce exit code 0 with
a log line containing `"[placeholder] Phase 1 PASSED tick=1"`.

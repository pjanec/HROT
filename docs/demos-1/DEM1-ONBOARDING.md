# DEM1 — Onboarding Guide

Welcome to the **DEM1 workstream** — the FDP Demo Framework and CI-testable demo suite.

---

## What Are We Building?

We are creating a **self-contained, headless, CI-friendly demo suite** that lives entirely inside the `FDP/` folder (namespace `Fdp.Examples.*`). The suite:

- Proves every FDP toolkit works correctly — in isolation and combined.
- Runs as a **console executable** (`fdp-demo-runner.exe`) with an exit code: `0` = pass, non-zero = fail.
- Supports optional **2D map rendering** (`--attach-vis2d`) for human debugging.
- Uses **deterministic fixed-step time** (1/60 s per tick) so results are identical across all CI hardware.
- Writes a **structured NLog trace log** every run so AI coding agents can diagnose failures without interactive debugging.
- Has **zero dependency on `Bagira.*`** — pure Cartesian math, no geodetic coordinates, no Entity Master.

---

## Key Design & Task Documents

| Document | Purpose |
|----------|---------|
| [DEM1-DESIGN.md](./DEM1-DESIGN.md) | Architecture, folder layout, all phases and scenario specs |
| [DEM1-TASK-DETAIL.md](./DEM1-TASK-DETAIL.md) | Per-task implementation specs with success conditions (unit tests) |
| [DEM1-TASK-TRACKER.md](./DEM1-TASK-TRACKER.md) | Progress checklist — update when tasks complete |
| [DEM1-design-talk.md](./DEM1-design-talk.md) | The original design conversation (source of truth for design intent) |

**Read the DESIGN doc first**, then the TASK-DETAIL for your assigned task.

---

## Developer Workflow

This project uses the batch-based development workflow defined in:

```
FDP/.dev-workstream/guides/DEV-GUIDE.md
```

Key points:
- Work is assigned in batches via `FDP/.dev-workstream/batches/`
- Submit your work via a batch report in `FDP/.dev-workstream/reports/`
- Ask questions via `FDP/.dev-workstream/questions/`
- Code standards are in `FDP/.dev-workstream/guides/CODE-STANDARDS.md`

---

## Folder Layout

### New Projects You Will Create

```
FDP/Examples/
├── Fdp.Examples.DDS/          ← [DEM1-I001] Cartesian DDS schemas only
├── Fdp.Examples.Common/       ← [DEM1-F002, I002] IScenario, ScenarioSubsystem, helpers
├── Fdp.Examples.Scenarios/    ← [DEM1-D001..D010] All IScenario implementations
└── Fdp.Examples.Runner/       ← [DEM1-F003] CLI executable (fdp-demo-runner)
```

### Legacy Projects (Do Not Modify!)

```
FDP/Examples/
├── Fdp.Examples.CarKinem/           ← legacy, keep intact
├── Fdp.Examples.CarKinem.Tests/     ← legacy
├── Fdp.Examples.IdAllocatorDemo/    ← legacy
├── Fdp.Examples.NetworkDemo/        ← legacy
├── Fdp.Examples.NetworkDemo.Tests/  ← legacy
├── Fdp.Examples.UrbanCombat/        ← legacy (reference for patterns!)
└── Fdp.Examples.UrbanCombat.Tests/  ← legacy (reference for test patterns!)
```

> **Tip:** `Fdp.Examples.UrbanCombat` is an excellent reference. It shows how to build a `HeadlessDemoApp`, `ScenarioDirector`, register doctrines, and use `FDP.Toolkit.Behavior`.

### Framework and Toolkits You Will Use

| Path | Purpose |
|------|---------|
| `FDP/Framework/FDP.Framework.Runner/` | `ISubsystem`, `SubsystemOrchestrator`, `RunnerOptions`, `SubsystemConfig` |
| `FDP/Toolkits/FDP.Toolkit.Time/` | `SteppingTimeController`, `TimeControllerFactory` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/` | BTree/HSM cognitive pipeline |
| `FDP/Toolkits/FDP.Toolkit.CarKinem/` | Vehicle kinematics + RVO avoidance |
| `FDP/Toolkits/FDP.Toolkit.Navigation/` | Pathfinding, road networks |
| `FDP/Toolkits/FDP.Toolkit.Combat/` | Ballistics, damage, weapons |
| `FDP/Toolkits/FDP.Toolkit.Physics/` | Raycasts, colliders, spatial hash |
| `FDP/Toolkits/FDP.Toolkit.Perception/` | LOS, broadphase, TargetMemory |
| `FDP/Toolkit.Geographic/` | Terrain clamping, Z-height queries |
| `FDP/Toolkits/FDP.Toolkit.Replay/` | AAR recording and replay |
| `FDP/Toolkits/FDP.Toolkit.Replication/` | Network component authority |
| `FDP/Kernel/Fdp.Kernel/` | ECS: EntityRepository, ModuleHostKernel, FdpLog |
| `FDP/ModuleHost/ModuleHost.Core/` | IModule, system groups |

---

## How to Build

```powershell
# From FDP/ directory:
cd D:\Work\IOS-IG-SimHost-FDP-2\FDP
dotnet build FDP.sln

# Or build just the runner:
dotnet build Examples/Fdp.Examples.Runner/Fdp.Examples.Runner.csproj
```

---

## How to Run a Scenario

```powershell
# Headless + deterministic (CI mode):
dotnet run --project Examples/Fdp.Examples.Runner -- --scenario autodrive --headless --deterministic --max-ticks 250

# With 2D visualization (human debugging):
dotnet run --project Examples/Fdp.Examples.Runner -- --scenario autodrive --attach-vis2d

# Check exit code in PowerShell:
$LASTEXITCODE  # 0 = passed, 1 = assertion failed, 2 = timed out
```

---

## How to Run Tests

```powershell
# All scenario tests:
dotnet test Examples/Fdp.Examples.Scenarios.Tests/Fdp.Examples.Scenarios.Tests.csproj -v minimal

# Single test:
dotnet test --filter "AutoDrive_RunToCompletion_ExitsZero"
```

---

## Understanding the Log Output

Every run writes a trace log to `logs/demo-<scenario>-<timestamp>.log`. The structure is:

```
INFO  | ScenarioSubsystem | [autodrive] === SCENARIO START tick=0
TRACE | ScenarioSubsystem | [autodrive] tick=20 AlphaPos=(20.1,0.0) AlphaVel=18.5 phase=1
INFO  | ScenarioSubsystem | [autodrive] Phase 1 PASSED tick=20
...
INFO  | ScenarioSubsystem | [autodrive] === CI SUCCESS tick=198
```

On failure:
```
ERROR | ScenarioSubsystem | [autodrive] Phase 2 FAILED tick=70: [Phase 2 Failed] RVO not active. Y=0.12 expected >2.0
ERROR | ScenarioSubsystem | [autodrive] === CI FAILURE Phase=2: [Phase 2 Failed] RVO not active. Y=0.12 expected >2.0
```

**The log file path is printed to stdout before anything else:**
```
[RUNNER] Log: logs/demo-autodrive-20260317-143022.log
```

---

## Key Concepts to Understand Before Implementing

### IScenario and ScenarioSubsystem

- `IScenario.Configure()` — register toolkits, spawn entities (called once)
- `IScenario.EvaluateTick(tick, world)` — called BEFORE `kernel.Update()` each frame to inject events; returns `true` on success; throws `ScenarioFailureException` on failure
- `ScenarioSubsystem` wraps an `IScenario` as `ISubsystem` and handles exit codes, logging, and tick budget

### Deterministic Time

All scenarios must produce identical results across hardware. `ScenarioSubsystem` injects `GlobalTime{DeltaTime=1/60}` before each kernel update using `SteppingTimeController.Step(1f/60f)`.

Never use `Thread.Sleep`, `DateTime.Now`, `Stopwatch`, or `Raylib.GetFrameTime()` in scenario logic.

### Exit Callbacks (not Environment.Exit in tests)

When running via xUnit, `ScenarioSubsystem` uses an injected `Action<int>` instead of `Environment.Exit`. The `ScenarioTestHarness.Run()` static helper handles this correctly — always use it in tests.

### No Bagira / No Entity Master / No Geodetic Math

The new demos must never reference `Bagira.*` namespaces. Use the `Fdp.Examples.Common` DDS schemas (`DemoSpawnMsg`, `DemoTransformMsg`, etc.) for networking. No WGS84/geodetic conversions — pure Cartesian coordinates only.

---

## Phase 0 Must Be Done First

Everything depends on `Fdp.Examples.Common` (the `IScenario` interface, `ScenarioSubsystem`) and the deterministic runner foundation. Complete **DEM1-F001 through DEM1-F005** before starting any scenario tasks.

---

## Questions?

Post questions in `FDP/.dev-workstream/questions/` using the batch question template from `FDP/.dev-workstream/guides/DEV-GUIDE.md`.

# BATCH-02: Create Fdp.Engine (FDP Toolkit Consolidation)

**Batch Number:** BATCH-02
**Tasks:** TASK-P1-002
**Phase:** Phase 1 — FDP Layer Consolidation
**Estimated Effort:** 16–20 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (Fdp.Core must exist)

---

## Onboarding & Workflow

### Developer Instructions

This batch consolidates all `FDP.Toolkit.*` projects and `FDP.Framework.Runner` into
a single `Fdp.Engine` assembly. It also strips the Raylib/ImGui rendering code from
`SubsystemOrchestrator` and deletes dead code.

**Important:** `FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui`, and `FDP.Framework.Raylib` are
NOT part of this batch — they go to `Fdp.Presentation` in BATCH-03.

### Required Reading (IN ORDER)

1. **Task Definition:** `.dev/modular-2/TASK-DETAIL.md#task-p1-002-create-fdpengine`
2. **Design Document:** `.dev/modular-2/DESIGN.md` — Section "FDP Layer (4 assemblies)"
3. **Previous Review:** `.dev/modular-2/reviews/BATCH-01-REVIEW.md`

### Source Code Locations

- **Toolkits to absorb (not Vis2D/ImGui):** `FDP/Toolkits/FDP.Toolkit.*/`
- **Runner to absorb:** `FDP/Framework/FDP.Framework.Runner/`
- **Target new project:** `FDP/Toolkits/Fdp.Engine/Fdp.Engine.csproj`
- **Target test project:** `FDP/Toolkits/Fdp.Engine.Tests/Fdp.Engine.Tests.csproj`
- **FDP solution file:** `FDP/FDP.sln`
- **Top-level solution file:** `IOS-IG-SimHost.sln`

### Report Submission

When done, submit your report to: `.dev/modular-2/reports/BATCH-02-REPORT.md`

---

## Context

BATCH-01 created `Fdp.Core`. Now we create `Fdp.Engine`, which contains:
- All simulation domain toolkits (physics, behavior, combat, navigation, etc.)
- The runner loop types (`ISubsystem`, `SubsystemOrchestrator`, `SubsystemConfig`, etc.)
- **NOT** the rendering layer (Vis2D, ImGui, Raylib — those go to `Fdp.Presentation`)

The key design constraint is: **`Fdp.Engine.csproj` must have zero references to
Raylib-cs, rlImGui-cs, ImGui.NET, or CycloneDDS.**

---

## Batch Objectives

1. Create `FDP/Toolkits/Fdp.Engine/Fdp.Engine.csproj` containing all non-rendering
   toolkits and the cleaned-up runner types.
2. Strip Raylib/ImGui from `SubsystemOrchestrator` and move into it only the pure
   simulation loop logic.
3. Delete dead code: `WaitingRoomCoordinator.cs`, `SubsystemStatusAnnounce.cs`,
   `SubsystemPeerInfo.cs`.
4. Create `FDP/Toolkits/Fdp.Engine.Tests/Fdp.Engine.Tests.csproj` absorbing all
   toolkit test projects (except Vis2D.Tests and ImGui.Tests).
5. Update all project references and solution files.

---

## Tasks

### Task 1: Create Fdp.Engine.csproj

**File:** `FDP/Toolkits/Fdp.Engine/Fdp.Engine.csproj` (NEW)

The new project must:
- Target `net8.0`, enable `ImplicitUsings`, `Nullable`, `AllowUnsafeBlocks`, LangVersion 12.0
- Reference `Fdp.Core` (the consolidated kernel from BATCH-01)
- NuGet packages from merged projects union:
  - `Newtonsoft.Json` (from FDP.Framework.Runner)
  - `CommandLineParser` (from FDP.Framework.Runner — for `RunnerOptions` / `RunnerConfiguration`)
  - `Microsoft.Extensions.Logging` (from FDP.Framework.Runner)
  - NuGet packages from any Toolkit project that uses them (check each csproj)
  - `FastBTree` / `Fbt.Kernel` project reference (for B-tree data structures)
  - `FastHSM` / `Fhsm.Kernel` project reference (for state machines)
- **ZERO** `PackageReference` to:
  - `Raylib-cs`, `rlImGui-cs`, `ImGui.NET` — forbidden
  - `CycloneDDS.Runtime`, `CycloneDDS.Schema`, `CycloneDDS.Core` — forbidden
- Consolidate `InternalsVisibleTo` attributes from all merged projects.
  Include at minimum: `Fdp.Engine.Tests`

### Task 2: Move source files for toolkit projects

**Projects to absorb into `Fdp.Engine/`** (all non-rendering toolkits):

| Old Project | Subfolder in Fdp.Engine/ |
|---|---|
| `FDP.Toolkit.Behavior` | `Toolkits/Behavior/` |
| `FDP.Toolkit.CarKinem` | `Toolkits/CarKinem/` |
| `FDP.Toolkit.Combat.Contracts` | `Toolkits/Combat/` |
| `FDP.Toolkit.Combat` | `Toolkits/Combat/` |
| `FDP.Toolkit.Commands` | `Toolkits/Commands/` |
| `FDP.Toolkit.DER` | `Toolkits/DER/` |
| `Fdp.Toolkit.Geographic` | `Toolkits/Geographic/` |
| `FDP.Toolkit.Lifecycle` | `Toolkits/Lifecycle/` |
| `FDP.Toolkit.Navigation.Contracts` | `Toolkits/Navigation/` |
| `FDP.Toolkit.Navigation` | `Toolkits/Navigation/` |
| `FDP.Toolkit.NetworkSpawning` | `Toolkits/NetworkSpawning/` |
| `FDP.Toolkit.Orchestration` | `Toolkits/Orchestration/` |
| `FDP.Toolkit.Perception` | `Toolkits/Perception/` |
| `FDP.Toolkit.Physics` | `Toolkits/Physics/` |
| `FDP.Toolkit.Replay` | `Toolkits/Replay/` |
| `FDP.Toolkit.Replication` | `Toolkits/Replication/` |
| `FDP.Toolkit.Scenario` | `Toolkits/Scenario/` |
| `FDP.Toolkit.Time` | `Toolkits/Time/` |
| `FDP.Toolkit.Tkb` | `Toolkits/Tkb/` |

Do NOT move: `FDP.Toolkit.Vis2D`, `FDP.Toolkit.ImGui` (those go to Fdp.Presentation).
Do NOT move: `FDP.Toolkit.DER.Examples` (this is an examples project, not a library).

**Namespaces must be preserved exactly** — no changes to any `namespace` declarations.

### Task 3: Move FDP.Framework.Runner source files (with cleanup)

Move these files from `FDP/Framework/FDP.Framework.Runner/` into
`FDP/Toolkits/Fdp.Engine/Runner/`:
- `ISubsystem.cs` → `Runner/`
- `IWindowRegistrar.cs` → `Runner/`
- `IMapCameraProvider.cs` → `Runner/`
- `SubsystemConfig.cs` → `Runner/`
- `RunnerOptions.cs` → `Runner/`
- `RunnerConfiguration.cs` → `Runner/`
- `Testing/HeadlessTestExecutor.cs` → `Runner/Testing/`
- `Testing/ITestActionHandler.cs` → `Runner/Testing/`
- `Testing/TestMetricsCollector.cs` → `Runner/Testing/`
- `Testing/TestReport.cs` → `Runner/Testing/`
- `Testing/TestScript.cs` → `Runner/Testing/`

**DO NOT move (delete them instead):**
- `WaitingRoomCoordinator.cs` — dead code, delete it
- `SubsystemStatusAnnounce.cs` — dead code, delete it
- `SubsystemPeerInfo.cs` — dead code, delete it

**`SubsystemOrchestrator.cs` — move with modifications** (see Task 4).

**Namespace:** The original namespace is `FDP.Framework.Runner`. Move ALL runner types to
namespace `Fdp.Engine.Runner`. Update the `namespace` declaration in each moved file.

**Important:** After changing namespaces, update any `using FDP.Framework.Runner;`
directives in callers. Since `ISubsystem` is the main consumed type, search the entire
codebase for `using FDP.Framework.Runner` and `FDP.Framework.Runner.ISubsystem` and
replace with `using Fdp.Engine.Runner`.

### Task 4: Refactor SubsystemOrchestrator

Edit `SubsystemOrchestrator.cs` before moving it:

1. **Remove these using directives:**
   ```
   using ImGuiNET;
   using Raylib_cs;
   using rlImGui_cs;
   using FDP.Toolkit.Vis2D.Components;
   using FDP.Toolkit.ImGui.Icons;
   using WM = FDP.Toolkit.ImGui.WindowManager.WindowManager;
   ```

2. **Remove window initialization code from `Initialize()`:**
   Remove all lines that touch:
   - `Raylib.SetConfigFlags`
   - `Raylib.InitWindow`
   - `Raylib.SetExitKey`
   - `Raylib.SetTargetFPS`
   - `rlImGui.Setup`
   - `ImGui.GetIO().ConfigFlags`
   The `Initialize()` method should only call `subsystem.Initialize(cfg)` for each
   subsystem. The render window is now opened by the Composition Root (Program.cs, later).

3. **Simplify the main `Run()` loop:**
   - Remove `Raylib.WindowShouldClose()` from the loop condition.
   - Remove all Raylib/ImGui draw calls:
     - `Raylib.BeginDrawing`, `Raylib.ClearBackground`, `Raylib.EndDrawing`
     - `rlImGui.Begin`, `rlImGui.End`
     - All `ImGui.*` docking setup calls
     - The `DrawFrame()` method (if it only does presentation work)
   - The pure run loop becomes: `while (_running) { UpdateDeltaTime(); UpdateAll(dt); if (!_headless) { DrawWorldAll(); DrawUIAll(); } }`
   - Keep `_headless` field and `HeadlessMode` property if they exist.
   - Any method like `DrawWorldAll()` or `DrawUIAll()` that calls `DrawWorld()` /
     `DrawUI()` on subsystems should be kept but should NOT call Raylib/ImGui directly.

4. **Remove window teardown from `Shutdown()`:**
   Remove `rlImGui.Shutdown()` and `Raylib.CloseWindow()`.
   `Shutdown()` should only call `subsystem.Shutdown()` in reverse order.

5. Move cleaned `SubsystemOrchestrator.cs` to `FDP/Toolkits/Fdp.Engine/Runner/`.

### Task 5: Consolidate test projects into Fdp.Engine.Tests

Create `FDP/Toolkits/Fdp.Engine.Tests/Fdp.Engine.Tests.csproj` merging:
- `FDP.Toolkit.Behavior.Tests`
- `FDP.Toolkit.CarKinem.Tests`
- `FDP.Toolkit.Combat.Tests`
- `FDP.Toolkit.Commands.Tests`
- `FDP.Toolkit.DER.Tests`
- `Fdp.Toolkit.Geographic.Tests`
- `FDP.Toolkit.Lifecycle.Tests`
- `FDP.Toolkit.Navigation.Tests`
- `FDP.Toolkit.NetworkSpawning.Tests`
- `FDP.Toolkit.Orchestration.Tests`
- `FDP.Toolkit.Perception.Tests`
- `FDP.Toolkit.Physics.Tests`
- `FDP.Toolkit.Replay.Tests`
- `FDP.Toolkit.Replication.Tests`
- `FDP.Toolkit.Scenario.Tests`
- `FDP.Toolkit.Time.Tests`
- `FDP.Toolkit.Tkb.Tests`
- `FDP.Framework.Runner.Tests`

Place each toolkit's test files in a subdirectory matching the toolkit name to avoid
filename conflicts (e.g. `Behavior/`, `Physics/`, etc.).

**Do NOT merge:** `FDP.Toolkit.Vis2D.Tests`, `FDP.Toolkit.ImGui.Tests` — those go to
`Fdp.Presentation.Tests` in BATCH-03.

As per DESIGN.md: `FDP.Framework.Raylib.Tests` and `FDP.Framework.Runner.Tests` are
deleted/superseded. `FDP.Framework.Runner.Tests` test content moves into
`Fdp.Engine.Tests`.

### Task 6: Update all project references

Search the entire repository for `<ProjectReference` entries pointing to any of the
merged projects. Grep command:
```
grep -r "FDP.Toolkit\|FDP.Framework.Runner\|FDP.Framework.Raylib" --include="*.csproj" .
```
Replace all such references with a single reference to `Fdp.Engine.csproj`.
Exception: Keep references from `Fdp.Presentation` projects pointing to their own
sources (Vis2D, ImGui, Raylib) for now — those are handled in BATCH-03.

### Task 7: Update both solution files

Same pattern as BATCH-01:
- Remove solution entries for all merged projects
- Add `Fdp.Engine` and `Fdp.Engine.Tests`
- Update build configurations

---

## Mandatory Workflow: Test-Driven Task Progression

**Before starting:**
```
dotnet build IOS-IG-SimHost.sln
```
Confirm zero errors (baseline from BATCH-01).

**After each task:**
```
dotnet build IOS-IG-SimHost.sln
```
Never leave the build broken.

**Final verification:**
```
dotnet build IOS-IG-SimHost.sln
dotnet test FDP/Toolkits/Fdp.Engine.Tests/Fdp.Engine.Tests.csproj
dotnet test IOS-IG-SimHost.sln
```

---

## Testing Requirements

- All tests that were in the merged toolkit test projects must still pass in
  `Fdp.Engine.Tests`.
- The namespace change from `FDP.Framework.Runner` to `Fdp.Engine.Runner` should not
  break any existing tests — only update the namespace declarations in the moved files
  and the using directives in callers.
- The SubsystemOrchestrator changes (removing Raylib calls) must not break any test
  that uses the orchestrator in headless mode.

---

## Report Requirements

Submit `.dev/modular-2/reports/BATCH-02-REPORT.md` covering:

1. **What was done:** Summary of merges, file moves, and code changes.
2. **Issues encountered:** Any namespace collisions, dependency conflicts, or
   unexpected Raylib/ImGui usages outside SubsystemOrchestrator.
3. **Weak points spotted:** Code quality issues seen during work.
4. **Design decisions made beyond spec:** Any deviations and rationale.
5. **Test results:** Output of `dotnet test Fdp.Engine.Tests`.
6. **Files changed list:** All modified `.csproj` and solution files.

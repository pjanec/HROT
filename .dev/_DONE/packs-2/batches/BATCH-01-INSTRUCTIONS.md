# BATCH-01: Logic Pack Composite Wrappers & RunMode Extensions

**Batch Number:** BATCH-01  
**Tasks:** PACK2-P001, PACK2-R001  
**Phase:** Phase 0 (Pack Wrappers) + Phase 6 part A (RunMode)  
**Estimated Effort:** 6–8 hours  
**Priority:** HIGH — foundational, blocks Phase 5 composition root (PACK2-C001) and Phase 6 tests (PACK2-R002)  
**Dependencies:** None — both tasks are independent  

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch lays two pieces of groundwork:

1. **PACK2-P001** — Create named composite `IEcsModule` wrappers that group existing simulation
   modules by their architectural tier. These wrappers are the building blocks for the HROT
   Editor composition root and the Feature Switch in Phase 5.
2. **PACK2-R001** — Extend the `RunMode` enum and CLI parser to support `Editor` and `Demo`
   modes, with a validation guard preventing illegal combinations.

### Required Reading (IN ORDER)

1. **Design document:** `.dev/packs-2/DESIGN.md` — read §Phase 0 (Pack Wrappers) and §Phase 6 A carefully.
2. **Task definitions:** `.dev/packs-2/TASK-DETAIL.md` — read sections PACK2-P001 and PACK2-R001 in full.
3. **Existing module architecture:**
   - `Hrot.SimHost/Modules/SimulationLogicModule.cs` — understand the existing role-based module grouping pattern.
   - `Hrot.SimHost/NodeBootstrapper.cs` — see how sub-modules are constructed and composed.
   - `FDP/ModuleHost/ModuleHost.Core/Abstractions/IEcsModule.cs` — read the IEcsModule interface contract.
   - `FDP/Toolkits/FDP.Toolkit.Perception/Modules/AutonomousPerceptionModule.cs` — **this is the canonical example** of a proper `IEcsModule` wrapper living inside a toolkit. Study it.
   - `Hrot.SimHost/Modules/SimHostModule.cs` — another `IEcsModule` implementation in `Hrot.SimHost`.
4. **RunMode files:**
   - `Hrot.ClusterRunner/Configuration/RunMode.cs`
   - `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`

### Source Code Locations

| Area | Path |
|------|------|
| New `SimHostCoreLogicPack` | `Hrot.SimHost/SimHostCoreLogicPack.cs` (NEW) |
| New `CgfLogicPack` | `Hrot.CGF/CgfLogicPack.cs` (NEW) |
| New `OrchestrationLogicPack` | `Hrot.Orchestrator/OrchestrationLogicPack.cs` (NEW) |
| `RunMode` enum | `Hrot.ClusterRunner/Configuration/RunMode.cs` |
| `HrotRunnerConfiguration` | `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` |
| SimHost tests | `Hrot.SimHost.Tests/` |
| ClusterRunner tests | `Hrot.ClusterRunner.Tests/` |
| Integration tests | `Hrot.ClusterRunner.Integration.Tests/` |

### Report Submission

**When complete, write your report to:**  
`.dev/packs-2/reports/BATCH-01-REPORT.md`

**If you have a blocking design question, create:**  
`.dev/packs-2/questions/BATCH-01-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests. Do NOT stop to ask before running tests or fixing compilation errors.**

1. **Task 1 (PACK2-P001):** Implement Logic Pack wrappers → Write unit tests → Fix all issues → **ALL tests pass** ✅  
2. **Task 2 (PACK2-R001):** Implement RunMode extensions → Write unit tests → Fix all issues → **ALL tests pass** ✅  
3. Run **full solution build** + **all affected test projects** — confirm zero red tests.  
4. Write your report.

Do not stop after one task hoping for feedback — finish both, make everything green, then report.

---

## Context

These are pre-requisite tasks. `SimHostCoreLogicPack`, `CgfLogicPack`, and `OrchestrationLogicPack`
are consumed in Phase 5 (PACK2-C001 composition root) and Phase 6 (PACK2-R002 `CgfSubsystem`
initialization). `RunMode.Editor` is needed for Phase 6 test harnesses.

**The packs are thin wrappers — no module logic changes, no new projects.**

### ⚠️ Architecture Investigation Required for PACK2-P001

Before writing any code for PACK2-P001, study these two patterns carefully:

**Pattern A — IEcsModule implementing RegisterSystems(ISystemRegistry):**
Used by `AutonomousPerceptionModule`, `SimHostModule`, `GeographicModule`, etc. This is the
standard pattern. The `ISystemRegistry.RegisterSystem<T>(T)` method accepts any
`IEcsModuleSystem`.

**Pattern B — Non-IEcsModule with custom RegisterSystems(SystemGroup, ...):**
Used by `CombatModule`, `GroundKinematicsModule`, `MissionControlModule`, `CognitiveRuntimeModule`,
`ActionDispatchModule`, `DamageAssessmentModule`. These are **not** `IEcsModule` — they expose
a `RegisterSystems` overload that takes `SystemGroup` objects directly.

For PACK2-P001, you must choose the correct wrapping strategy for each pack:
- Where sub-modules already implement `IEcsModule` (like `AutonomousPerceptionModule`), the
  composite can delegate to `sub.RegisterSystems(registry)`.
- Where sub-modules use the `SystemGroup` pattern, the composite wrapper must either:
  - Create a `SystemGroup`/`SystemPhase` internally and call the sub-module's `RegisterSystems`,
    or use `Tick(ISimulationView view, float deltaTime)` for delegation
  - **Check if there is an existing bridge/adapter already in the codebase before inventing one.**

**Look at how `SimHostModule.RegisterSystems` registers `NetworkSpawningSystem` via
`ISystemRegistry` as a concrete example to follow.**

---

## ✅ Tasks

---

### Task 1: Create Logic Pack Composite Wrappers (PACK2-P001)

**Task Definitions:** See [TASK-DETAIL.md §PACK2-P001](../TASK-DETAIL.md#pack2-p001--create-logic-pack-composite-wrappers)  
**Design Reference:** [DESIGN.md §0.A](../DESIGN.md#0a--logic-pack-composite-wrappers)

#### 1.1 — `SimHostCoreLogicPack` in `Hrot.SimHost/`

**File:** `Hrot.SimHost/SimHostCoreLogicPack.cs` (NEW)

Creates a composite `IEcsModule` wrapping the Muscle-tier modules:
- `GroundKinematicsModule` (`FDP.Toolkit.CarKinem.Modules`)
- `CombatModule` (`Hrot.SimHost.Modules`)
- `DamageAssessmentModule` (`FDP.Toolkit.Combat.Modules`)
- `AutonomousPerceptionModule` (`FDP.Toolkit.Perception.Modules`)

**Key constraints:**
- `ExecutionPolicy.Synchronous()` unless a sub-module dictates otherwise.
- The constructor must accept the same parameters that the sub-modules require (e.g.
  `BehaviorRegistry`, `RoadNetworkBlob`, `NetworkEntityMap`, trajectory pool, etc.).
  Refer to how `SimulationLogicModule` constructs these sub-modules with role `MuscleGround`.
- `RegisterSystems` must call each sub-module's `RegisterSystems` in the same order as
  `SimulationLogicModule` currently does for the `MuscleGround` role.
- No changes to the sub-modules themselves.

**Tests required:**
- Unit test: install `SimHostCoreLogicPack` into a minimal test kernel.
  Assert that all systems belonging to each of the four sub-modules are registered
  (use kernel introspection: `kernel.RegisteredModules`, or verify via `_registeredModules`
  in `NodeBootstrapper` if it tracks by type).

#### 1.2 — `CgfLogicPack` in `Hrot.CGF/`

**File:** `Hrot.CGF/CgfLogicPack.cs` (NEW)

Creates a composite `IEcsModule` wrapping the Brain-tier modules:
- `CognitiveRuntimeModule` (`FDP.Toolkit.Behavior.Modules`)
- `MissionControlModule` (`FDP.Toolkit.Behavior.Modules`)
- `ActionDispatchModule` (`Hrot.SimHost.Modules` or `FDP.Toolkit.Behavior.Modules` — check actual location)

**Key constraints:**
- The constructor must accept `BehaviorRegistry` and `NetworkEntityMap` (or whatever the sub-modules require).
- `RegisterSystems` delegates to sub-module `RegisterSystems` in correct execution order
  (Mission → Cognitive → ActionDispatch, matching current `SimulationLogicModule` Brain order).
- No changes to the sub-modules.

**Tests required:**
- Unit test: install `CgfLogicPack` into a test kernel. Assert the three sub-modules are registered.

#### 1.3 — `OrchestrationLogicPack` in `Hrot.Orchestrator/`

**File:** `Hrot.Orchestrator/OrchestrationLogicPack.cs` (NEW)

Creates a composite `IEcsModule` wrapping the cluster-sync modules/handlers:
- `MasterSyncController` / `SlaveSyncController` — check `Hrot.Orchestrator/` for exact class names.  
  Also check `FDP.Toolkit.Orchestration` for `ClusterMaster` / `ClusterSlave` abstractions.
- Relevant cluster state handlers currently registered in `NodeBootstrapper.BuildOrchestration`.

**Key constraints:**
- Investigate `NodeBootstrapper.BuildOrchestration` to identify exactly which modules/systems
  belong in this pack.
- `RegisterSystems` or `Tick` delegates to the same systems/controllers as
  `BuildOrchestration` currently installs.

**Tests required:**
- Unit test: install `OrchestrationLogicPack` into a test kernel. Assert the orchestration
  modules/systems are registered.
- Regression: all existing `Hrot.ClusterRunner.Integration.Tests` pass with zero changes.

**Build verification after Task 1:**
```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost.Tests --no-build
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Tests --no-build
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Integration.Tests --no-build
```

---

### Task 2: Extend RunMode with Editor and Demo (PACK2-R001)

**Task Definitions:** See [TASK-DETAIL.md §PACK2-R001](../TASK-DETAIL.md#pack2-r001--extend-runmode-with-editor-and-demo-update-configuration-validation)  
**Design Reference:** [DESIGN.md §6.A](../DESIGN.md#phase-6-cgf-subsystem-execution-profile--headless-integration-tests)

#### 2.1 — Update `RunMode.cs`

**File:** `Hrot.ClusterRunner/Configuration/RunMode.cs` (UPDATE)

Add:
```csharp
/// <summary>Run the standalone HROT Editor (offline, no DDS participant).</summary>
Editor = 1 << 6,   // 64

/// <summary>Run all subsystems in one aggregated process (alias for All, human-readable name).</summary>
Demo = Orchestrator | SimHost | IG | ExCon | CGF,
```

**Constraints:**
- Keep `All` unchanged (or as an alias for `Demo`). First do a workspace-wide search:
  `Select-String -Path "d:\Work\IOS-IG-SimHost-FDP-2\**\*.cs" -Pattern "RunMode\.All"` —
  if there are existing usages, keep `All` as an explicit alias (`All = Demo`).
- `CI = 1 << 5` (32) is unchanged.

#### 2.2 — Update `HrotRunnerConfiguration.cs`

**File:** `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` (UPDATE)

In `ParseModeString`:
```csharp
if (lower == "editor") return RunMode.Editor;
if (lower == "demo")   return RunMode.Demo;
```

In `Validate()`:
```csharp
// Editor mode is always standalone — must not be combined with distributed flags.
if (ParsedMode.HasFlag(RunMode.Editor) &&
    (ParsedMode & (RunMode.IG | RunMode.ExCon | RunMode.Orchestrator | RunMode.CGF)) != 0)
{
    throw new InvalidOperationException(
        "RunMode.Editor must not be combined with distributed flags (IG, ExCon, Orchestrator, CGF).");
}
```

Also verify in `Hrot.ClusterRunner/Program.cs` that the DDS participant initialization is
NOT entered when `mode == RunMode.Editor` (the Editor must not open a DDS socket).
If there is a DDS init path, add a check: `if (!config.ParsedMode.HasFlag(RunMode.Editor))`.

#### 2.3 — Tests for PACK2-R001

Add to the existing `Hrot.ClusterRunner.Tests/` project (or create
`Hrot.ClusterRunner.Tests/Configuration/RunModeTests.cs`):

```csharp
[Fact]
public void ParseModeString_Editor_ReturnsEditorFlag()
{
    var cfg = new HrotRunnerConfiguration { ModeString = "editor" };
    cfg.Validate();
    Assert.Equal(RunMode.Editor, cfg.ParsedMode);
}

[Fact]
public void ParseModeString_Demo_ReturnsDemoFlags()
{
    var cfg = new HrotRunnerConfiguration { ModeString = "demo", NoWait = true };
    cfg.Validate();
    Assert.Equal(RunMode.Demo, cfg.ParsedMode);
}

[Fact]
public void Validate_EditorCombinedWithIg_ThrowsInvalidOperation()
{
    // editor,ig — an invalid combination
    var ex = Assert.Throws<InvalidOperationException>(() =>
    {
        var cfg = new HrotRunnerConfiguration { ModeString = "editor,ig", NoWait = true };
        cfg.Validate();
    });
    Assert.Contains("Editor", ex.Message);
}
```

**Build verification after Task 2:**
```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Tests
```

---

## 🧪 Final Testing Checklist

Before writing your report, verify ALL of the following are green:

```
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost.Tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Tests
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Integration.Tests
```

Zero compilation errors. Zero red tests (existing green tests must remain green).

---

## 🎯 Success Criteria

This batch is DONE when:

- [ ] `SimHostCoreLogicPack`, `CgfLogicPack`, `OrchestrationLogicPack` each compile and their unit tests pass.
- [ ] `RunMode.Editor` and `RunMode.Demo` parse correctly from CLI strings.
- [ ] `RunMode.Editor | RunMode.IG` combination throws `InvalidOperationException` with a descriptive message.
- [ ] Full solution builds with zero errors.
- [ ] All integration tests pass unchanged (regression check).
- [ ] Report submitted to `.dev/packs-2/reports/BATCH-01-REPORT.md`.

---

## ⚠️ Pitfalls to Avoid

- **Do not change existing module logic** — these are purely additive wrappers.
- **Do not remove `RunMode.All`** until you have confirmed no existing code references it.
- **Do not add `Hrot.NED` or `CycloneDDS` dependencies** to the new pack classes.
- **For `SimHostCoreLogicPack`**: if `SystemGroup` bridging is tricky, study how the
  existing `SimHostModule.RegisterSystems(ISystemRegistry)` works in
  `Hrot.SimHost/Modules/SimHostModule.cs` to understand the ISystemRegistry-to-SystemGroup
  relationship before creating your own.

---

## 📊 Report Requirements

Submit `.dev/packs-2/reports/BATCH-01-REPORT.md` answering:

**Q1:** What implementation strategy did you choose for `SimHostCoreLogicPack` and why?
Specifically: how did you bridge the `SystemGroup`-based sub-modules to the `IEcsModule`
`RegisterSystems(ISystemRegistry)` interface?

**Q2:** What issues did you encounter and how did you solve them?

**Q3:** Did you spot any weak points in the existing codebase? What would you improve?

**Q4:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q5:** Are there any concerns about the existing `SimulationLogicModule` that the new packs may
duplicate or conflict with? What migration path do you recommend?

**Q6:** Suggested git commit message for this batch.

---

## 📚 Reference Materials

- **Task Defs:** `.dev/packs-2/TASK-DETAIL.md` — §PACK2-P001, §PACK2-R001
- **Design:** `.dev/packs-2/DESIGN.md` — §Phase 0, §Phase 6 header
- **IEcsModule:** `FDP/ModuleHost/ModuleHost.Core/Abstractions/IEcsModule.cs`
- **Canonical IEcsModule example:** `FDP/Toolkits/FDP.Toolkit.Perception/Modules/AutonomousPerceptionModule.cs`
- **SimulationLogicModule:** `Hrot.SimHost/Modules/SimulationLogicModule.cs`
- **NodeBootstrapper:** `Hrot.SimHost/NodeBootstrapper.cs`
- **RunMode:** `Hrot.ClusterRunner/Configuration/RunMode.cs`
- **HrotRunnerConfiguration:** `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`

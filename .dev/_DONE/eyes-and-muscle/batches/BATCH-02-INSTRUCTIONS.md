# BATCH-02: EyesAndMuscle Subsystem — Shell, Module, Integration Tests (Phase 3)

**Batch Number:** BATCH-02
**Tasks:** [Corrective-0] Fix NedReplicationModule P2 debt, EAM-E001, EAM-E002, EAM-E003
**Phase:** Phase 3 — EyesAndMuscle Subsystem (+ corrective P2 fix from Phase 2)
**Estimated Effort:** 14–18 hours
**Priority:** HIGH — delivers the EyesAndMuscle SoD async PoC
**Dependencies:** BATCH-01 (HrotNodeBuilder + NedReplicationModule must be committed)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch builds the EyesAndMuscle Subsystem using the Phase 1+2 building blocks. You must:
1. **Corrective-0:** Fix a P2 debt from BATCH-01 before proceeding with Phase 3 work.
2. **EAM-E001:** Create `EyesAndMuscleSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar`.
3. **EAM-E002:** Create `EyesAndMuscleModule : IEcsModule` (async SoD PoC).
4. **EAM-E003:** Write integration tests in `EyesAndMuscleIntegrationTests.cs`.

### Required Reading (IN ORDER)

1. **Previous batch review:** `.dev/eyes-and-muscle/reviews/BATCH-01-REVIEW.md` — understand P2 debt items
2. **Design Document:** `.dev/eyes-and-muscle/DESIGN.md` — §Phase 3 and §EyesAndMuscleModule SoD async design
3. **Task Definitions:** `.dev/eyes-and-muscle/TASK-DETAIL.md` — EAM-E001, EAM-E002, EAM-E003

### Key Source Files to Understand Before Coding

| File | Why it matters |
|---|---|
| `Hrot.ClusterRunner/Infrastructure/HrotNodeBuilder.cs` | BATCH-01 output: the builder you'll call in `Initialize()` |
| `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` | BATCH-01 output: the module you'll register |
| `Hrot.ClusterRunner/Infrastructure/HrotNodeContext.cs` | The context record produced by HrotNodeBuilder |
| `Hrot.ClusterRunner/Infrastructure/HrotNodeConfig.cs` | Config type for HrotNodeBuilder |
| `Hrot.ClusterRunner/Services/SimHostSubsystem.cs` | Existing thin-adapter pattern — NOT what you're doing (you build directly, no inner App class) |
| `Hrot.SimHost/SimHostApp.cs` line ~390 | `RegisterSimComponents(world)` call — understand what component sets exist |
| `Hrot.SimHost/NodeBootstrapper.cs` `BuildSimulationLogic()` | Creates the SimulationLogicModule for Muscle role |
| `FDP/ModuleHost/ModuleHost.Core/Abstractions/ExecutionPolicy.cs` | `ExecutionPolicy.SlowBackground(hz)` → async SoD at given hz |
| `FDP/ModuleHost/ModuleHost.Core/Abstractions/IEcsModule.cs` | Interface your EyesAndMuscleModule implements |
| `FDP/ModuleHost/ModuleHost.Core/Abstractions/ISimulationView.cs` | What `Tick(view, dt)` receives |
| `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` | Integration test harness pattern |
| `FDP.Framework.Runner/ISubsystem.cs` | The interface EyesAndMuscleSubsystem implements |
| `FDP.Framework.Runner/SubsystemConfig.cs` | Config passed to `Initialize()` |

### Report Submission

**When done, submit your report to:**
`.dev/eyes-and-muscle/reports/BATCH-02-REPORT.md`

---

## Context

BATCH-01 built `HrotNodeBuilder` (DRY init infrastructure) and `NedReplicationModule` (NED ACL
bundling). BATCH-02 proves they work end-to-end by building a brand-new combined Muscle+Eyes
subsystem that uses them. The centerpiece is `EyesAndMuscleModule` — an async SoD PoC that
validates the thread-safe snapshot pattern the future Stride engine will rely on.

**Important:** `EyesAndMuscleSubsystem` is NOT built as a thin adapter wrapping an inner App class
(unlike `SimHostSubsystem` → `SimHostApp`). It DIRECTLY uses `HrotNodeBuilder` and constructs
all modules inline in `Initialize()`. This is the clean pattern that future subsystems will follow.

---

## ⚠️ Critical Technical Notes

1. **ExecutionPolicy for `EyesAndMuscleModule`:** Use `ExecutionPolicy.SlowBackground(60)` which
   creates `Mode = RunMode.Asynchronous, Strategy = DataStrategy.SoD, TargetFrequencyHz = 60`.
   The TASK-DETAIL mentions `ModuleExecutionPolicy.Asynchronous(...)` — that's a different older
   struct. Use the `ExecutionPolicy.SlowBackground(60)` factory from `ExecutionPolicy.cs`.
   Study `FDP/ModuleHost/ModuleHost.Core/Abstractions/ExecutionPolicy.cs` before implementing.

2. **HrotNodeBuilder `WithRole` takes `Hrot.SimHost.NodeRole`** — The BATCH-01 builder internally
   stores the subsystem name only (the `role` param is stored for future use but not used in
   `Build()`). Pass `NodeRole.MuscleGround | NodeRole.ImageGenerator` in the call but do NOT
   expect `HrotNodeContext` to reflect the role — pass it separately to `NedReplicationModule`.

3. **HrotNodeConfig vs SubsystemConfig:** The builder takes `HrotNodeConfig`, not `SubsystemConfig`.
   Map fields: `SubsystemConfig.DomainId → HrotNodeConfig.DomainId`, `SubsystemConfig.NodeId →
   HrotNodeConfig.NodeId`, `SubsystemConfig.Headless → HrotNodeConfig.Headless`.
   Check `Hrot.ClusterRunner/Infrastructure/HrotNodeConfig.cs` for the exact fields.

4. **Component registration is per-subsystem (not in builder):** After `HrotNodeBuilder.Build()`,
   the subsystem must explicitly register component types:
   - `SimHostComponentRegistry.RegisterAll(context.World)` — kinematic + behavior components
   - For combined Eyes+Muscle: also register IG components if needed (check `IgApplication` for
     `RegisterIgComponents` or equivalent)

5. **`SimulationLogicModule` (Muscle) creation:** For the Muscle role, use
   `NodeBootstrapper.BuildSimulationLogic(NodeRole.MuscleGround, behaviorRegistry, entityMap, ...)`.
   The `NodeBootstrapper` for this batch does NOT need full behavior population — a minimal
   `BehaviorRegistry` with no entries is sufficient for the PoC.

6. **Headless mode in integration tests:** `SubsystemConfig.Headless = true` skips Raylib window
   creation. When `HrotNodeConfig.Headless = true`, the builder also skips DDS participant creation.
   For integration tests that include OrchestratorSubsystem (with DDS), pass `Headless = false` in
   `HrotNodeConfig` but headless window = true in `SubsystemConfig`. Study how `SimHostSubsystem`
   handles this — it passes `headless: config.Headless` to `InitializeEmbedded` but DDS still runs.
   
   **For BATCH-02 integration tests:** Use a simplified EAM-only harness with `HrotNodeConfig.Headless = true` (NO DDS, no EnsureIdAllocatorRouting wait). The integration tests can run without the orchestrator by setting headless=true in HrotNodeConfig. This avoids a 30s timeout if the orchestrator is absent.

7. **`IEcsModule.GetRequiredComponents()` may not exist** in this codebase. Check the `IEcsModule`
   interface. If it doesn't exist, skip that property — the TASK-DETAIL may have spec'd a feature
   not yet implemented in FDP.

8. **`EyesAndMuscleModule.Tick` receives `ISimulationView` not `EntityRepository`.** The view is
   a read-only snapshot. Use `view.Query()...Build()` for entity iteration and
   `view.GetCommandBuffer()` for writes. DO NOT store the view after `Tick` returns.

9. **Async thread assertion (EAM-E002 SC1):** To test that `Tick` runs on a non-main thread,
   record `System.Threading.Thread.CurrentThread.ManagedThreadId` inside `Tick()` and expose it
   via a test property `public int? LastTickThreadId { get; private set; }`. Compare against the
   main thread ID calling `Update()`.

---

## 🎯 Batch Objectives

1. Fix `NetworkLifecycleSystemGroup` omission in `NedReplicationModule` (Corrective-0).
2. Implement `EyesAndMuscleSubsystem` using `HrotNodeBuilder`, `NedReplicationModule`,
   `SimulationLogicModule`, and IG presentation modules.
3. Implement `EyesAndMuscleModule` as async SoD PoC with `EyesTicks`/`MuscleTicks` counters.
4. Integration tests confirm boot, tick execution, and module async behavior.

---

## ✅ Tasks

### Task 0 — Fix `NetworkLifecycleSystemGroup` in NedReplicationModule (Corrective P2)

**File to modify:** `Hrot.ClusterRunner/Replication/NedReplicationModule.cs`

**What to add:** In `RegisterSystems`, immediately after registering `GhostCreationSystem`:
```csharp
registry.RegisterSystem(new NetworkLifecycleSystemGroup(GhostCreationSystem));
```

**Constraint:** `NetworkLifecycleSystemGroup` is in `ModuleHost.Core.Scheduling`. Verify the exact
constructor signature in `FDP/ModuleHost/ModuleHost.Core/Scheduling/NetworkLifecycleSystemGroup.cs`.

**Tests:** Re-run `NedReplicationModuleTests` — all 5 existing tests must still pass. Add a test
`NedReplicationModule_RegistersNetworkLifecycleSystemGroup` to verify it is now registered.

---

### Task 1 — `EyesAndMuscleSubsystem` shell (EAM-E001)

**File to create:** `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs`

**Task Definition:** See [TASK-DETAIL.md — EAM-E001](../TASK-DETAIL.md#eam-e001--eyesandmusclesubsystem-shell)

**Class declaration:**
```csharp
public sealed class EyesAndMuscleSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar
{
    public string Name => "EyesAndMuscle";
    public System.Numerics.Vector4 TitleBarColor => new(0.15f, 0.40f, 0.25f, 1f); // teal-green
}
```

**Fields to store:**
```csharp
private HrotNodeContext? _context;
private NedReplicationModule? _nedReplicationModule;
private SimulationLogicModule? _simLogicModule;
private EyesAndMuscleModule? _eyesAndMuscleModule;
private bool _initialized;
```

**`Initialize(SubsystemConfig config)` sequence:**

```
1.  Build HrotNodeContext:
    var nodeCfg = new HrotNodeConfig {
        DomainId = config.DomainId ?? 0,
        NodeId   = config.NodeId,
        Headless = config.Headless,     // headless: skip DDS participant + allocator wait
    };
    _context = new HrotNodeBuilder(nodeCfg)
        .WithRole("EyesAndMuscle", NodeRole.MuscleGround | NodeRole.ImageGenerator)
        .Build();

2.  Register component types (domain-specific, NOT in builder):
    SimHostComponentRegistry.RegisterAll(_context.World);
    // Optionally: IgComponentRegistry.RegisterAll(context.World) if it exists

3.  Register base modules on kernel:
    foreach (var m in _context.BaseModules)
        _context.Kernel.RegisterModule(m);

4.  Create and register NedReplicationModule:
    _nedReplicationModule = new NedReplicationModule(
        _context.Participant,
        NodeRole.MuscleGround | NodeRole.ImageGenerator,
        _context.EntityMap,
        HrotEnvironment.CreateGeoTransform(),   // geo transform for NED coord convert
        _context.EventBus,
        localNodeId: config.NodeId,
        domainId: config.DomainId ?? 0);
    _context.Kernel.RegisterModule(_nedReplicationModule);

5.  Create behavior registry (domain-specific, stays here):
    var behaviorRegistry = new BehaviorRegistry();    // empty for PoC

6.  Create and register SimulationLogicModule (Muscle subset):
    var bootstrapper = new NodeBootstrapper();
    _simLogicModule = bootstrapper.BuildSimulationLogic(
        NodeRole.MuscleGround, behaviorRegistry, _context.EntityMap);
    _context.Kernel.RegisterModule(_simLogicModule);

7.  Register IG presentation modules (after checking what's available):
    Check what modules IgApplication registers and register the same here.
    At minimum: EntityStatesIngressPack is handled by NedReplicationModule.

8.  Create and register EyesAndMuscleModule:
    _eyesAndMuscleModule = new EyesAndMuscleModule(
        NodeRole.MuscleGround | NodeRole.ImageGenerator);
    _context.Kernel.RegisterModule(_eyesAndMuscleModule);

9.  Initialize kernel:
    _context.Kernel.Initialize();

10. If not headless: create MapCanvas, wire visualization (skip for PoC)
    _initialized = true;
```

**`Update(float deltaTime)` sequence:**
```csharp
_context.SlaveTranslator?.Tick();  // DDS → bus
_context.ClusterSlave.Tick();      // cluster state machine
_context.Kernel.Update(deltaTime);
_context.EventBus.SwapBuffers();
```

**`DrawWorld()`** — `_canvas?.Draw()` (null-safe).

**`DrawUI()`** — ImGui panel showing entity count and module state (minimal stub is fine for PoC).

**`Shutdown()`** — dispose in reverse: kernel, participant, any canvas.

**`GetMapCamera()`** — return `null` for now (no MapCanvas in PoC).

**`RegisterWindows(WindowManager wm)`** — stub (empty body).

**World property for tests:**
```csharp
public EntityRepository? World => _context?.World;
```

**`EyesAndMuscleModule` accessor for tests:**
```csharp
public EyesAndMuscleModule? Module => _eyesAndMuscleModule;
```

**Tests Required:**

*SC1 — Boots without exception (headless, no DDS):*
```csharp
var sub = new EyesAndMuscleSubsystem();
sub.Initialize(new SubsystemConfig { Headless = true, NodeId = 55 });
Assert.NotNull(sub.World);
```

*SC2 — Update does not throw on empty world:*
```csharp
// After Initialize, call Update 10 times
for (int i = 0; i < 10; i++) sub.Update(0.016f);
```

*SC3 — Shutdown disposes cleanly:*
```csharp
sub.Initialize(...); sub.Shutdown();
// Second Shutdown must not throw (idempotent)
sub.Shutdown();
```

---

### Task 2 — `EyesAndMuscleModule` async SoD PoC (EAM-E002)

**File to create:** `Hrot.ClusterRunner/Services/EyesAndMuscleModule.cs`

**Task Definition:** See [TASK-DETAIL.md — EAM-E002](../TASK-DETAIL.md#eam-e002--eyesandmusclemodule-sod-async-poc)

**Class definition:**
```csharp
public sealed class EyesAndMuscleModule : IEcsModule
{
    public string Name => "EyesAndMuscle";
    public ExecutionPolicy Policy => ExecutionPolicy.SlowBackground(60);
    
    private readonly NodeRole _role;
    private readonly bool _muscleActive;
    
    // Test seams — counters incremented in Tick
    public int EyesTicks { get; private set; }
    public int MuscleTicks { get; private set; }
    public int? LastTickThreadId { get; private set; }  // for async thread assertion

    public EyesAndMuscleModule(NodeRole role)
    {
        _role = role;
        _muscleActive = (role & NodeRole.MuscleGround) != 0 || role == NodeRole.AllInOne;
    }

    public void RegisterSystems(ISystemRegistry registry) { }  // Direct Execution pattern

    public void Tick(ISimulationView view, float deltaTime)
    {
        LastTickThreadId = Thread.CurrentThread.ManagedThreadId;
        
        // THE EYES — always runs
        var eyesQuery = view.Query()
            .With<SimTransform>()
            .With<NetworkIdentity>()
            .Build();
        
        foreach (var entity in eyesQuery)
        {
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            // In PoC: increment counter (in Stride: push to StrideDataBridge)
        }
        EyesTicks++;
        
        if (!_muscleActive) return;
        
        // THE MUSCLE — only when MuscleGround role active
        var cmd = view.GetCommandBuffer();
        var muscleQuery = view.Query()
            .With<NavigationIntent>()
            .With<SimTransform>()
            .Build();
        
        foreach (var entity in muscleQuery)
        {
            ref readonly var intent = ref view.GetComponentRO<NavigationIntent>(entity);
            ref readonly var tf     = ref view.GetComponentRO<SimTransform>(entity);
            
            // Simplified step toward destination (DirectPoint mode only)
            if (intent.Mode == NavigationMode.DirectPoint)
            {
                var dest3d = new Vector3(intent.FinalDestination.X, tf.Position.Y, intent.FinalDestination.Y);
                var delta  = dest3d - tf.Position;
                if (delta.Length() > 0.01f)
                {
                    var step = Vector3.Normalize(delta) * (deltaTime * 5.0f);
                    var newPos = tf.Position + step;
                    cmd.SetComponent(entity, new SimTransform { Position = newPos, Rotation = tf.Rotation });
                }
            }
        }
        MuscleTicks++;
    }
}
```

**Important:** Check the actual field names on `NavigationIntent` and `SimTransform` by reading
`Hrot.SimHost/Components/` before finalizing the Tick implementation. The spec uses
`NavigationMode.DirectPoint` and `FinalDestination` — verify these exist.

**If `Query()` / `GetCommandBuffer()` API differs** from what's shown above, study an existing
system (e.g., `DeadReckoningSyncSystem.cs`) or `ISimulationView.cs` for the actual API.

**Tests Required:**

*SC1 — EyesTicks increments after pumping frames:*
```csharp
// Create headless subsystem, Initialize, pump frames
// Assert: module.EyesTicks >= 1
```
Note: async module may not tick immediately on frame 1 — pump at least 10-20 frames.

*SC2 — MuscleTicks is 0 when role is ImageGenerator only:*
```csharp
var module = new EyesAndMuscleModule(NodeRole.ImageGenerator);
// Register with test kernel, pump frames
// Assert: module.MuscleTicks == 0
```

*SC3 — Role suppression (ImageGenerator only: no Muscle):*
Already covered by SC2.

*SC4 — View not held after Tick (code review):*
Verify no field on `EyesAndMuscleModule` captures the `ISimulationView` argument.

---

### Task 3 — EyesAndMuscle integration tests (EAM-E003)

**File to create:** `Hrot.ClusterRunner.Integration.Tests/EyesAndMuscleIntegrationTests.cs`

**Task Definition:** See [TASK-DETAIL.md — EAM-E003](../TASK-DETAIL.md#eam-e003--eyesandmuscle-integration-test)

**Harness pattern:** Create a simple inline harness using `EyesAndMuscleSubsystem` directly
(headless mode, no DDS). Do NOT require `HrotRunnerHarness` or `OrchestratorSubsystem` for
these tests — use subystem directly.

```csharp
public class EyesAndMuscleIntegrationTests : IDisposable
{
    private readonly EyesAndMuscleSubsystem _sub;
    
    public EyesAndMuscleIntegrationTests()
    {
        _sub = new EyesAndMuscleSubsystem();
        _sub.Initialize(new SubsystemConfig
        {
            Headless = true,
            NodeId   = 55,
            DomainId = null   // or DomainId = 0
        });
    }
    
    public void Dispose() => _sub.Shutdown();
    
    private void PumpFrames(int n)
    {
        for (int i = 0; i < n; i++)
            _sub.Update(0.016f);
    }
}
```

**Test 1 — Subsystem boots and runs:**
```csharp
[Fact]
public void Subsystem_BootsAndRuns_WithoutException()
{
    // arrange: subsystem already initialized in constructor
    // act
    PumpFrames(50);
    // assert
    Assert.NotNull(_sub.World);
    Assert.Equal(0, _sub.World.EntityCount);  // no entities spawned
}
```

**Test 2 — EyesTicks and MuscleTicks increment:**
```csharp
[Fact]
public void Module_EyesAndMuscleTicks_IncrementAfterPumping()
{
    PumpFrames(60);  // enough frames for async module to run
    Assert.True(_sub.Module!.EyesTicks > 0, $"EyesTicks expected > 0, was {_sub.Module.EyesTicks}");
    Assert.True(_sub.Module!.MuscleTicks > 0, $"MuscleTicks expected > 0, was {_sub.Module.MuscleTicks}");
}
```

**Test 3 — Async execution (thread ID differs):**
```csharp
[Fact]
public void Module_Tick_RunsOnNonMainThread()
{
    int mainThreadId = Thread.CurrentThread.ManagedThreadId;
    
    // Pump until async module has run
    int pumped = 0;
    while (_sub.Module!.LastTickThreadId == null && pumped < 200)
    {
        _sub.Update(0.016f);
        pumped++;
    }
    
    Assert.NotNull(_sub.Module.LastTickThreadId);
    Assert.NotEqual(mainThreadId, _sub.Module.LastTickThreadId);
}
```

**Notes:**
- All tests must be headless (no Raylib window, no DDS participant).
- Do NOT use `Thread.Sleep` — only frame-counting loops.
- The async module may need a few frames to start — 60-200 frame pump is safe.

---

## 🧪 Testing Requirements

### Minimum Test Coverage

| Task | Test Type | Minimum Assertions |
|---|---|---|
| Corrective-0 | Unit | NetworkLifecycleSystemGroup registered in NedReplicationModule |
| E001-SC1 | Unit | Subsystem boots headless without exception, World non-null |
| E001-SC2 | Unit | Update(0.016f) × 10 doesn't throw on empty world |
| E001-SC3 | Unit | Shutdown idempotent (second call no exception) |
| E002-SC1 | Unit | EyesTicks >= 1 after pumping frames |
| E002-SC2 | Unit | MuscleTicks == 0 for ImageGenerator-only role |
| E003-Test1 | Integration | Subsystem boots, entity count = 0 after 50 frames |
| E003-Test2 | Integration | EyesTicks > 0 AND MuscleTicks > 0 after 60 frames |
| E003-Test3 | Integration | Tick runs on non-main thread (LastTickThreadId ≠ main) |

### Test-Driven Task Progression

**MANDATORY WORKFLOW — Test-Driven Task Progression:**

> For each task, before writing production code:
> 1. Read the existing tests to understand what currently passes.
> 2. Write the failing test(s) first (unit or integration as specified).
> 3. Implement the production code to make the tests pass.
> 4. Run the full relevant test suite to confirm no regressions.
> 5. Only then mark the task done in your report.
>
> **Never consider a task complete until all its tests pass AND existing tests remain green.**

### Test commands to run before submitting report

```powershell
# Build
dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-restore

# Run new unit tests (including NedReplication fix and EyesAndMuscle unit tests)
dotnet test Hrot.ClusterRunner.Tests --no-build --filter "NedReplication|HrotNodeBuilder|EyesAndMuscle" --logger "console;verbosity=normal"

# Run integration tests
dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --filter "EyesAndMuscle" --logger "console;verbosity=normal"

# Full regression check
dotnet test Hrot.ClusterRunner.Tests --no-build --logger "console;verbosity=quiet"
dotnet test Hrot.ClusterRunner.Integration.Tests --no-build --logger "console;verbosity=quiet"
```

---

## 📊 Report Requirements

Submit to: `.dev/eyes-and-muscle/reports/BATCH-02-REPORT.md`

```markdown
# BATCH-02 Report - EyesAndMuscle Subsystem (Phase 3)

## Implementation Summary
[Per-task: what was done, key API decisions]

## Files Created / Modified
[List each file with path and summary of changes]

## Tests Added
[List test methods with file paths]

## Test Results
[Test run output with pass/fail counts]

## Developer Insights
1. **Issues Encountered:** What problems did you hit?
2. **Weak Points Spotted:** Fragile areas noticed in the codebase?
3. **Design Decisions Beyond the Spec:** Any decisions not in the spec?

## Deviations from Spec
[With justification]
```

---

## ⚠️ Important Notes

1. **Phase 4 is OUT OF SCOPE** — only the Corrective-0 fix and EAM-E001/E002/E003.
2. **Do NOT create a separate "App" class** — `EyesAndMuscleSubsystem` is self-contained and calls
   `HrotNodeBuilder` directly. No `EyesAndMuscleApp` wrapper.
3. **Minimal behavior registry** — a `new BehaviorRegistry()` with no entries is fine for the PoC.
4. **For integration tests, use headless=true in `HrotNodeConfig`** to skip DDS allocator routing.
   This means `ClusterSlave` runs in standalone-friendly state (no DDS heartbeats).
5. **Check `IEcsModule` interface** for the exact method signatures — some TASK-DETAIL method names
   may not match the actual interface. Study existing modules as reference.
6. **Check `NavigationIntent` component definition** in `Hrot.SimHost/Components/` — verify the
   enum `NavigationMode.DirectPoint` and `FinalDestination` field names before using them.
7. **`NedReplicationModule` IGeographicTransform:** The constructor takes `IGeographicTransform`.
   In `EyesAndMuscleSubsystem`, use `HrotEnvironment.CreateGeoTransform()` (same as SimHostApp).
   BUT if `config.Headless = true` in HrotNodeConfig, the `_context.Participant` is null.
   Pass `null` for participant when headless — `NedReplicationModule` handles null participant.

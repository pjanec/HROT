# BATCH-02: Core Integration Library (StrideNodeBootstrapper + SyncFdpToStrideScript + Visual Effects)

**Batch Number:** BATCH-02  
**Tasks:** SM-003 (StrideNodeBootstrapper), SM-004 (SyncFdpToStrideScript), SM-005 (Visual Effects Wiring)  
**Phase:** Phase 3  
**Estimated Effort:** 14-16 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 must be complete (SharedApplicationBootstrapper exists)

---

## Mandatory Workflow — Complete Without Stopping

**YOU MUST FINISH ALL THREE TASKS COMPLETELY BEFORE WRITING THE REPORT.**  
Do not stop to ask permission, do not stop after one task to "check if this is the right approach".  
Fix all build errors and test failures before submitting. No partial work.

**Mandatory task-progression (Test-Driven):**

1. **SM-003:** Implement `StrideNodeBootstrapper` → Write tests → **ALL tests pass** ✅  
2. **SM-004:** Implement `SyncFdpToStrideScript` + helpers → Write tests → **ALL tests pass** ✅  
3. **SM-005:** Wire visual effects in SM-003 → Verify SM-004 effect tests pass ✅

Do NOT move to the next task until the current one compiles and all its tests pass.

---

## Onboarding & Workflow

### Required Reading (IN ORDER)
1. `.dev/.guides/DEV-GUIDE.md` — project conventions
2. `.dev/stride-mock/DESIGN.md` — §5 (StrideNodeBootstrapper), §6 (SyncFdpToStrideScript), §6.4 (visual effects) — read these sections before writing any code
3. `.dev/stride-mock/TASK-DETAILS.md` — SM-003, SM-004, SM-005 sections with all success conditions
4. `.dev/stride-mock/reviews/BATCH-01-REVIEW.md` — context on what was built last batch

### Source Code Locations
- **Primary Work Area:** `Hrot\Subsystems\Hrot.StrideMock\` (new files here)
- **Test project:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\` (add tests here)
- **SharedApplicationBootstrapper (base class):** `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs`
- **SimHostApp (reference pattern for hooks):** `Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs`
- **NodeBootstrapper (needed for BuildOrchestration):** `Hrot\Subsystems\Hrot.SimHost\NodeBootstrapper.cs`
- **SimHostComponentRegistry (pattern for RegisterDomainComponents):** `Hrot\Subsystems\Hrot.SimHost\SimHostComponentRegistry.cs`
- **KinematicComponentRegistry:** `Hrot\Subsystems\Hrot.SimHost\KinematicComponentRegistry.cs`
- **SimHostCoreLogicPack (pattern for PopulateSystems):** `Hrot\Subsystems\Hrot.SimHost\SimHostCoreLogicPack.cs`
- **EventToEffectSystem + VisualEffectCleanupSystem:** `Hrot\Subsystems\Hrot.IG\Systems\EventToEffectSystem.cs`
- **EventEffectModule (pattern for module wrapping):** `Hrot\Subsystems\Hrot.IG\Modules\EventEffectModule.cs`
- **VisualEffectState + EffectType:** `Hrot\Subsystems\Hrot.IG\Components\VisualEffectState.cs`
- **TracerTarget:** `Hrot\Subsystems\Hrot.IG\Components\TracerTarget.cs` (find this file)
- **SimHostSubsystem (pattern):** `Hrot\Subsystems\Hrot.SimHost\SimHostSubsystem.cs`
- **OfflineNetworkFactory:** `Hrot\Subsystems\Hrot.Editor\OfflineNetworkFactory.cs` (headless test factory)
- **SharedApplicationBootstrapperTests.cs** — study the TestBootstrapper pattern for headless testing

### Report Submission
**When done, submit your report to:**  
`.dev/stride-mock/reports/BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev/stride-mock/questions/BATCH-02-QUESTIONS.md`

---

## Context

BATCH-01 created `SharedApplicationBootstrapper` (the abstract base). BATCH-02 creates the three concrete components that make up the `Hrot.StrideMock` library:

1. **StrideNodeBootstrapper** — implements all abstract hooks from `SharedApplicationBootstrapper` for the Stride/SimHost-equivalent role
2. **SyncFdpToStrideScript** — the ECS → rendering sync engine (2-pass differential synchronization)
3. **Visual effects** — wires `EventToEffectSystem` and `VisualEffectCleanupSystem` into the bootstrapper

**Key design principle from DESIGN.md §11:** `StrideNodeBootstrapper` must have ZERO references to Raylib, ImGui, or `IMapCameraProvider`. It is engine-agnostic.

---

## Task 1: SM-003 — Implement StrideNodeBootstrapper

**Reference:** [TASK-DETAILS.md SM-003](../TASK-DETAILS.md#sm-003--implement-stridenodebootstrapper)  
**Design Reference:** [DESIGN.md §5](../DESIGN.md#5-stridenodebootstrapper-hrotstrideMock)

### File: `Hrot\Subsystems\Hrot.StrideMock\StrideNodeBootstrapper.cs` (NEW)

Implement `StrideNodeBootstrapper` as a `sealed class` inheriting `SharedApplicationBootstrapper`. 

**Constructor:**
```csharp
public StrideNodeBootstrapper(
    IEcsModule kinematicsModule,
    IEcsModule perceptionModule,
    IEcsModule combatModule,
    IEcsModule? navigationModule = null)
```

**Node Role** (see DESIGN.md §5.3):
```csharp
private static readonly NodeRole Role =
    NodeRole.MuscleGround | NodeRole.Perception | NodeRole.NavigationSolver | NodeRole.ImageGenerator;
```

**Public properties after BootstrapNode():**
```csharp
public HrotNodeContext Context { get; private set; }
public DebugPrimitiveBuffer ProducerBuffer { get; } = new DebugPrimitiveBuffer();
public DebugPrimitiveBuffer ConsumerBuffer { get; } = new DebugPrimitiveBuffer();
public MapCamera Camera { get; } = new MapCamera();
// ITimeControlGateway? TimeControl — inherited from SharedApplicationBootstrapper
```

**Important:** `ProducerBuffer` and `ConsumerBuffer` are initialized at field initialization, NOT inside `BootstrapNode()`.

**`RegisterDomainComponents` hook** (Phase 2): See DESIGN.md §5.7 for the exact component list:
- `HrotSharedComponentRegistry.RegisterAll(world)` — do NOT call `SimHostComponentRegistry.RegisterAll` (that includes CognitiveComponentRegistry which must be excluded)
- `KinematicComponentRegistry.RegisterAll(world)` — vehicle physics + nav state
- `world.RegisterComponent<VisualEffectState>()` — required by SyncFdpToStrideScript
- `world.RegisterComponent<TracerTarget>()` — required for tracer endpoint resolution
- Register events: `world.RegisterEvent<WeaponFireNotification>()`, `world.RegisterEvent<DetonationNotification>()`
- Register Genesis Intent DTOs: the same set as in SimHostComponentRegistry (InitialVehicleIntent, InitialRouteIntent, InitialTargetsIntent, InitialPassengersIntent, InitialHierarchyIntent, InitialUnitSubordinateIntent) — required by GenesisMaterializationSystem during spawn pipeline

**DO NOT register `CognitiveComponentRegistry`** — brain AI data stays on the CGF node. `TkbTemplate.ApplyTo()` silently skips missing components.

**`BuildSerializer` hook** (Phase 3): Call `HrotScenarioSerializerFactory.Build(registry ?? new BehaviorRegistry())` — same as SimHostApp.

**`PopulateSystems` hook** (Phase 4a): Inject the 4 module packs into the system lists. Look at `SimHostCoreLogicPack` and `SimHostApp.OnLoad` to understand how systems are decomposed into input/sim/postSim lists. Add:
- Kinematics simulation systems → `sim` list
- Perception simulation systems → `sim` list  
- Combat simulation systems → `sim` list
- Navigation simulation systems → `sim` list
- `EventToEffectSystem` → **`sim` list** (SM-005: wrapped in TogglableSimulationGroup, disabled during replay)
- `VisualEffectCleanupSystem` → **`postSim` list** (SM-005: a post-sim system cannot be in the sim group)

**`BuildOrchestration` hook** (Phase 5): Call `NodeBootstrapper.BuildOrchestration(...)`. The key is passing `lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup`. Study `NodeBootstrapper.cs` for the exact signature. You will need a `CheckpointIOWorker` — instantiate it with the isolated temp root from `context.Config.LocalTempRoot`.

**`RegisterSpawningPipeline` hook** (Phase 6a): SimHost and StrideMock both use `NetworkSpawningSystem` + `GenesisMaterializationSystem`. Look at `SimHostApp.OnLoad` for the exact calls. StrideMock claims `MuscleGround` and needs to handle Initial Intent DTOs during scenario load.

**`RegisterNetworkTranslators` hook** (Phase 6b): Use the factory pattern. Call `configuredFactory.CreateSimHostAuxiliaryTranslators()` and register the translators. Study `SimHostApp.OnLoad` for the exact pattern.

**`Tick(float dt)` method:**
```csharp
public void Tick(float dt)
{
    ProducerBuffer.EndFrame(dt);
    ConsumerBuffer.Clear();

    Context.SlaveTranslator?.Tick();
    Context.ClusterSlave.Tick();

    Context.Kernel.Update();           // parameterless — SlaveSyncController provides dt
    Context.EventBus.SwapBuffers();

    // _gizmoIngress?.PollAndApply();  // fills ConsumerBuffer from DDS — wire in SM-006
}
```

**`Dispose()` method:** Dispose the DDS participant if it was created internally, and any other disposable resources.

**FORBIDDEN:** Do NOT manually register `DeadReckoningSyncSystem`. `NedReplicationModule` auto-registers it when it detects `NodeRole.ImageGenerator`. Manual registration causes double-tick interpolation corruption.

### Tests for SM-003

Add to `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\StrideNodeBootstrapperTests.cs` (NEW).

Required tests (all SC_SM003_x conditions from TASK-DETAILS.md):

- `SC_SM003_1`: `BootstrapNode_WithHeadlessFactory_DoesNotThrow` — `BootstrapNode()` completes against `OfflineNetworkFactory` (headless, no DDS)
- `SC_SM003_2`: `Context_ClusterSlave_NonNull_AfterBootstrap` — `Context.ClusterSlave` is non-null
- `SC_SM003_3`: `ProducerAndConsumerBuffers_AreDifferentInstances` — `!ReferenceEquals(ProducerBuffer, ConsumerBuffer)`
- `SC_SM003_4`: `Camera_NonNull_WithDefaultZoom` — `Camera != null && Camera.Zoom == 1f`
- `SC_SM003_5`: `TimeControl_AccessedViaInheritedProperty_NonNull` — `TimeControl` (inherited) is non-null; no duplicate field on `StrideNodeBootstrapper`
- `SC_SM003_6`: `KinematicComponents_RegisteredInWorld` — after bootstrap, `world.IsComponentTypeRegistered<VehicleState>()` is true (VehicleState is a KinematicComponentRegistry component)
- `SC_SM003_7`: `CognitiveComponents_NotRegisteredInWorld` — `world.IsComponentTypeRegistered<BrainHsm128>()` is false
- `SC_SM003_10`: `VisualEffectState_RegisteredInWorld` — `world.IsComponentTypeRegistered<VisualEffectState>()` is true
- `SC_SM003_11`: `TracerTarget_RegisteredInWorld` — `world.IsComponentTypeRegistered<TracerTarget>()` is true
- `SC_SM003_8`: `Tick_CanBeCalledRepeatedly_WithoutThrowing` — call `Tick(0.016f)` 3 times; `ConsumerBuffer` cleared each frame (Count == 0 after each Tick)

**Test pattern for SC_SM003_x:** Create the bootstrapper with default in-memory modules (using `OfflineNetworkFactory` for headless). Look at `SimHostAppTests.cs` for pattern reference.

---

## Task 2: SM-004 — Implement SyncFdpToStrideScript

**Reference:** [TASK-DETAILS.md SM-004](../TASK-DETAILS.md#sm-004--implement-syncfdptostridesscript)  
**Design Reference:** [DESIGN.md §6](../DESIGN.md#6-syncfdptostridesscript-hrotstrideMock) — read this entire section.

### Files (all NEW in `Hrot\Subsystems\Hrot.StrideMock\`):

1. **`FakeStrideScript.cs`** — abstract base
2. **`FakeStrideEntity.cs`** — mutable class tracking a live entity
3. **`FakeStrideEffect.cs`** — mutable class tracking a visual effect entity  
4. **`SyncFdpToStrideScript.cs`** — main implementation

**`FakeStrideScript.cs`:**
```csharp
namespace Hrot.StrideMock;

public abstract class FakeStrideScript
{
    public abstract void Start();
    public abstract void Update(float deltaTime);
}
```

**`FakeStrideEntity.cs`:**
```csharp
using System.Numerics;

namespace Hrot.StrideMock;

public sealed class FakeStrideEntity
{
    public Vector3 Position { get; set; }
    public float   Rotation { get; set; }
}
```

**`FakeStrideEffect.cs`** — see DESIGN.md §6.4:
```csharp
using System.Numerics;
using Hrot.IG.Components;

namespace Hrot.StrideMock;

public sealed class FakeStrideEffect
{
    public EffectType Type      { get; set; }
    public Vector3    Position  { get; set; }
    public Vector3    TracerEnd { get; set; }
    public float      Scale     { get; set; }
    public float      Alpha     { get; set; }
}
```

**`SyncFdpToStrideScript.cs`:** See DESIGN.md §6.1–§6.3 for the full spec.

Key implementation details:
- Constructor takes `StrideNodeBootstrapper core`
- Internal `_entities = new Dictionary<Entity, FakeStrideEntity>()` and `_effects = new Dictionary<Entity, FakeStrideEffect>()`
- Pre-allocated `_staleEntities = new List<Entity>(64)` — reused per frame (no GC alloc in Update)
- `ClusterState` subscription: read from `core.Context.ClusterSlave.CurrentState`
- `IsOperatingState(state)` helper: returns true for `OperatingLive | OperatingEdit | OperatingPreview | OperatingReplay`
- `Update(float dt)`:
  1. Gets current cluster state
  2. Updates `CurrentStateMessage` (non-empty if loading state)
  3. If `IsOperatingState`, calls `SyncStrideEntities()` and `SyncStrideEffects()`
- `SyncStrideEntities()` — 2-pass differential sync:
  - Pass 1 (destructions): iterate `_entities`; call `core.Context.World.IsAlive(entity)` for each; collect stale into `_staleEntities`; remove after iteration
  - Pass 2 (creations + updates): query ECS for `SimTransform` (excluding `VisualEffectState` to avoid mixing entities and effects); for each entity, if absent in dictionary → new `FakeStrideEntity`; update position/rotation from `SimTransform`
- `SyncStrideEffects()` — same 2-pass for `_effects`:
  - Pass 1: same stale check via `IsAlive`
  - Pass 2: query ECS for `SimTransform` + `VisualEffectState`; create/update `FakeStrideEffect` from state
- Expose: `IEnumerable<FakeStrideEntity> ActiveEntities`, `IEnumerable<FakeStrideEffect> ActiveEffects`, `string CurrentStateMessage`, `ClusterState CurrentClusterState`

**SimTransform:** Look for `SimTransform` component in the codebase — it's the common position component.

### Tests for SM-004

Add to `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\SyncFdpToStrideScriptTests.cs` (NEW).

**Test fixture setup:** Create a `StrideNodeBootstrapper` in headless mode, call `BootstrapNode`, create `SyncFdpToStrideScript`, call `Start()`. Then manually manipulate the ECS world and event bus to drive behavior.

Required tests (all SC_SM004_x conditions from TASK-DETAILS.md):

- `SC_SM004_1`: `SpawnedEntity_WithSimTransform_AppearsInActiveEntities_AfterUpdate` — spawn an ECS entity, add `SimTransform`, call `Update(dt)`, assert `ActiveEntities` contains one entry
- `SC_SM004_2`: `DestroyedEntity_RemovedFromActiveEntities_AfterUpdate` — spawn then destroy via `EntityRepository.DestroyEntity()`, call `Update(dt)`, assert `ActiveEntities` is empty
- `SC_SM004_3`: `RecycledEntity_OldEntryRemoved_NewEntryCreated_GenerationalSafety` — destroy entity at index N, spawn new at same index (higher generation), call `Update(dt)`, assert one entry in `ActiveEntities` (the new one)
- `SC_SM004_4`: `LoadingState_SyncStrideEntities_NotCalled_SplashMessageNonEmpty` — manually force cluster state to `LoadingLive`; call `Update(dt)`; assert `ActiveEntities` is empty and `CurrentStateMessage` is non-empty
- `SC_SM004_5`: `OperatingState_SyncResumes_SplashMessageEmpty` — transition to `OperatingLive`; spawn entity; call `Update(dt)`; assert entry present and message empty
- `SC_SM004_6`: `WeaponFireNotification_ResultsInFakeStrideEffect_InActiveEffects` — publish `WeaponFireNotification` to event bus, swap buffers, call `Update(dt)` (EventToEffectSystem should spawn `VisualEffectState` entity), assert `ActiveEffects` has one `EffectType.Tracer` entry  
  **Note:** This requires SM-005 visual effects to be wired (EventToEffectSystem must be registered). Implement SM-005 FIRST before writing this test.
- `SC_SM004_7`: `ExpiredEffect_RemovedFromActiveEffects_AfterCleanup` — verify that after enough `Update` calls for the effect's lifetime to expire, `ActiveEffects` becomes empty
- `SC_SM004_8`: `StaleEntitiesList_ReusedAcrossFrames_NoGcAlloc` — verify `_staleEntities` field identity doesn't change across frames (same list instance reused); use reflection to get the field

**Quality bar:** Tests must manipulate actual ECS state and verify actual `ActiveEntities`/`ActiveEffects` membership, not just logging or mock calls.

---

## Task 3: SM-005 — Visual Effects Wiring

**Reference:** [TASK-DETAILS.md SM-005](../TASK-DETAILS.md#sm-005--visual-effects-wiring)  
**Design Reference:** [DESIGN.md §6.4](../DESIGN.md#64-visual-effects)

This is mostly done as part of SM-003's `PopulateSystems` hook — `EventToEffectSystem` goes into the `sim` list and `VisualEffectCleanupSystem` goes into the `postSim` list.

**DO THIS IN SM-003 BEFORE WRITING SM-004 TESTS** because SC_SM004_6 depends on `EventToEffectSystem` being registered.

Verify the 4 SM-005 success conditions:
- `SC_SM005_1`: Both systems are registered in the kernel (verify via reflection or kernel system enumeration)
- `SC_SM005_2`: `EventToEffectSystem` is in `sim` (TogglableSimulationGroup) and `VisualEffectCleanupSystem` is in `postSim` (TogglablePostSimulationGroup). Test by checking which system lists they appear in.
- `SC_SM005_3`: Visual (manual verification — write a note in the report)
- `SC_SM005_4`: No effect entities survive beyond their lifetime — verified by SC_SM004_7

Add SM-005 tests (SC_SM005_1 and SC_SM005_2) to `SyncFdpToStrideScriptTests.cs` or `StrideNodeBootstrapperTests.cs`:

- `SC_SM005_1`: `EventToEffectSystem_InSimGroup_VisualEffectCleanupSystem_InPostSimGroup`
- `SC_SM005_2`: `TogglableGroups_ContainCorrectSystems_ForReplaySafety`

---

## Quality Standards

**Tests NOT ACCEPTABLE:**
- Tests that only check `ActiveEntities.Count()` without verifying actual entity identity
- Tests that stub or bypass the ECS world (test must use actual `EntityRepository`)
- Tests where the cluster state never changes and effects of state are not verified

**Tests REQUIRED:**
- Tests that create actual ECS entities in the repository
- Tests that query `ActiveEntities` by iterating and checking properties
- Tests for SC_SM004_3 (generational safety) must demonstrate the SAME index being reused with different generation
- Test for SC_SM004_8 must use reflection to confirm the exact List<Entity> instance is reused

**Code quality:**
- No Raylib, ImGui, or `IMapCameraProvider` imports in `StrideNodeBootstrapper.cs` (architecture constraint)
- No per-frame heap allocation in `SyncFdpToStrideScript.Update()` beyond Dictionary operations
- `_staleEntities` initialized in the class field (capacity 64), not inside Update

---

## Testing Requirements

- **Minimum:** 10 tests for SM-003 + 8 tests for SM-004 + 2 tests for SM-005 = **20 tests total**
- ALL tests must pass before submitting the report
- Run: `dotnet test Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj`

---

## Success Criteria

This batch is DONE when:
- [ ] `StrideNodeBootstrapper.cs` implemented and builds
- [ ] `FakeStrideScript.cs`, `FakeStrideEntity.cs`, `FakeStrideEffect.cs`, `SyncFdpToStrideScript.cs` implemented and builds
- [ ] `EventToEffectSystem` in `sim` list, `VisualEffectCleanupSystem` in `postSim` list (SM-005 done as part of SM-003)
- [ ] `StrideMockPlaceholder.cs` removed (it was a scaffolding stub)
- [ ] All 20+ tests pass
- [ ] `dotnet build Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj` — 0 errors
- [ ] Report submitted

---

## Developer Insights (Report Questions)

**Q1:** What issues did you encounter implementing `StrideNodeBootstrapper.BuildOrchestration`? `NodeBootstrapper` requires several parameters — what did you discover about its API?

**Q2:** How did you handle the cluster state for the `SyncFdpToStrideScript` cluster state gating? Where does `CurrentClusterState` come from in the actual API?

**Q3:** What design decisions did you make beyond the spec? What alternatives did you consider for the 2-pass differential sync?

**Q4:** Were there any ECS API surprises (e.g., how `IsAlive()` works with the entity generation check)?

**Q5:** Suggested commit message?

---

## Reference Materials

- **Design:** `.dev/stride-mock/DESIGN.md` §5, §6, §6.4
- **Task Details:** `.dev/stride-mock/TASK-DETAILS.md` SM-003, SM-004, SM-005
- **Bootstrapper base:** `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs`
- **Pattern for hooks:** `Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs` (OnLoad method)
- **NodeBootstrapper:** `Hrot\Subsystems\Hrot.SimHost\NodeBootstrapper.cs`
- **Component registries:** `Hrot\Subsystems\Hrot.SimHost\SimHostComponentRegistry.cs`, `KinematicComponentRegistry.cs`
- **Visual effect systems:** `Hrot\Subsystems\Hrot.IG\Systems\EventToEffectSystem.cs`
- **Effect components:** `Hrot\Subsystems\Hrot.IG\Components\VisualEffectState.cs`, `TracerTarget.cs`
- **Test patterns:** `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\SharedApplicationBootstrapperTests.cs`

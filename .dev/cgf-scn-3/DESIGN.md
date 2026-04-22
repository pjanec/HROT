# CGF Scenario Fix 3 — Design

## Problem Statement

When a new entity is added in the HROT Editor and a mission is authored and committed via the
Mission Panel, saving the scenario produces a JSON that is missing the mission entirely. The entity
appears in the file but has no `ActiveMissionPlan` and no `MissionPlanQueue`. In addition, the
scenario JSON contains the raw `BrainBlackboard` bytes — a 128-element opaque integer array that
belongs to the runtime execution tier, not to declarative initial conditions.

Investigation revealed four independent root causes, plus one latent correctness defect in the
distributed load path.

---

## Architectural Boundaries (Non-Negotiable)

The following constraints govern every change in this workstream:

1. **State vs. Message boundary.** A scenario file is a declarative snapshot of initial ECS
   *component state*. Transient in-flight events (`MissionControlIntent`, etc.) must never appear
   in a scenario JSON — they are not state.
2. **Managed vs. unmanaged ECS API boundary.** `repo.SetComponent<T>` is strictly for unmanaged
   structs. Managed classes (`ActiveMissionPlan`) must use `repo.SetManagedComponent<T>` and
   `repo.RemoveManagedComponent<T>`.
3. **`[DataPolicy(DataPolicy.NoSave)]` boundary.** Runtime execution scratch-pads
   (`BrainBlackboard`, channel arbitration state) must be excluded from scenario serialization.
   They are deterministically reconstructed from the `ActiveMissionPlan` during load.
4. **Zero-delta-time freeze.** The offline editor freezes time by setting `dt = 0` through the
   time controller. ECS systems still tick and drain the event bus; only integration systems that
   multiply by `dt` are effectively halted. Groups must never be disabled as a freeze mechanism.
5. **Phase separation.** Command-processing systems (`MissionControlExecutionSystem`,
   `DoctrineIngressSystem`) belong in the Input phase. Cognitive runtime systems (B-Trees, mission
   director, kinematics) belong in the Simulation phase.

---

## Phase 1 — Core ECS Correctness

**Goal:** Fix the four immediate implementation bugs that cause mission data loss and scenario
pollution. These changes are purely within existing files; no new abstractions are introduced.

### 1.1 Managed Component API Fix in `MissionControlExecutionSystem`

`ActiveMissionPlan` is a managed class (`public class ActiveMissionPlan`), not an unmanaged
struct. The current code incorrectly calls `repo.SetComponent` and `repo.RemoveComponent<T>`,
which place the object in the wrong ECS table. `repo.HasManagedComponent<ActiveMissionPlan>`
therefore always returns `false`, causing `MissionPlanTranslator.CanTranslate` to skip the
entity entirely during the scenario save pass.

**Files:** `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs`

Fix `CMD_REPLACE_MISSION`: `repo.SetComponent(entity, new ActiveMissionPlan { ... })` → `repo.SetManagedComponent(entity, new ActiveMissionPlan { ... })`.

Fix `CMD_ABORT_ALL`: `repo.RemoveComponent<ActiveMissionPlan>(entity)` → `repo.RemoveManagedComponent<ActiveMissionPlan>(entity)`.

### 1.2 InlineArray Span Mutation Fix in `TryBuildQueue`

`MissionPlanQueue.Phases` is an `[InlineArray(8)]` struct. When the indexer `queue.Phases[i] =`
is applied to an `out` parameter, the C# 12 compiler may emit an `ldobj` instruction that creates
a defensive copy of the inline buffer on the evaluation stack. The mutation hits the copy and is
silently discarded, leaving `PhaseCount = 0` and all phases zeroed.

The `MissionPlanQueue` type documentation explicitly warns about this pattern. The safe pattern
is to cast the inline array to a `Span<MissionPhase>` before indexing.

**Files:** `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs`

In `TryBuildQueue`: extract `Span<MissionPhase> phases = queue.Phases;` before the for-loop.
Replace `queue.Phases[i] = new MissionPhase { ... }` with `phases[i] = new MissionPhase { ... }`.

### 1.3 BrainBlackboard Data Policy

`BrainBlackboard` is a 128-byte unmanaged scratch-pad written by the `DoctrineIngressSystem` when
a doctrine's `ParseParamsDelegate` initializes cognitive parameters. It is deterministically
reconstructed from `ActiveMissionPlan` on every load. Serializing it into a scenario JSON exposes
opaque execution-tier memory to the authoring domain and violates the State vs. Message boundary.

**Files:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs`

Add `[DataPolicy(DataPolicy.NoSave)]` to `BrainBlackboard`. The `FdpAutoSerializer` will exclude
the struct entirely. Binary checkpoint recording (LZ4 `.fdp` payloads) is unaffected because
`DataPolicy.NoSave` only suppresses scenario serialization, not `CheckpointIOWorker` recording.

### 1.4 SteppingTimeController Mode Reporting

`SteppingTimeController.GetMode()` currently returns `TimeMode.Continuous` with a comment
acknowledging the mismatch. Any diagnostic UI or coordinator that queries `GetMode()` will
display incorrect synchronization state. The controller is inherently deterministic/stepping by
design.

**Files:** `FDP/Toolkits/Fdp.Toolkits/Time/Controllers/SteppingTimeController.cs`

Change `GetMode()` to return `TimeMode.Deterministic`.

---

## Phase 2 — CGF Multi-Phase Architecture

**Goal:** Split the `CgfLogicPack` and its contained modules into explicit Input and Simulation
phase groups. This corrects two concurrency and ordering defects in the live distributed CGF
cluster while establishing the correct phase topology for the editor fix in Phase 3.

### 2.1 MissionControlModule Phase Split

`MissionControlModule.RegisterSystems(SystemGroup group)` currently routes both
`DoctrineIngressSystem` (an input-phase command consumer) and `MissionDirectorSystem` (a
simulation-phase state advancer) into the same generic group. This violates phase ordering.

**Files:** `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/MissionControlModule.cs`

Add `public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` overload:
- `inputGroup.AddSystem(new DoctrineIngressSystem(_registry))` — parses JSON, assigns ECS
  doctrines before the AI ticks.
- `simGroup.AddSystem(new MissionDirectorSystem())` — advances phases based on elapsed time
  and trigger conditions.

The existing `RegisterSystems(SystemGroup group)` single-group overload must remain for backward
compatibility with tests and any callers that register both systems into the same group (e.g.
within a single-threaded test harness that uses one group for everything).

### 2.2 CgfLogicPack Phase Split

`CgfLogicPack.RegisterSystems(SystemGroup simGroup)` currently places
`MissionControlExecutionSystem` — an input-phase command consumer — alongside cognitive runtime
and kinematics systems in the simulation group. In the live `CgfSubsystem`, this causes
command processing to execute on the background Simulation thread alongside B-Trees and physics,
violating the phase contract.

**Files:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`

Add `public void RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` overload:
- `inputGroup.AddSystem(_missionExecutionSystem)` — drains `MissionControlIntent` from bus.
- `simGroup.AddSystem(_missionAdapterSystem)` — bridges `MissionPlanQueue` phases to `DoctrineState`.
- `_missionControlModule.RegisterSystems(inputGroup, simGroup)` — uses the new two-group overload.
- `simGroup.AddSystem(new HealthApplicationSystem())` — unchanged.
- `simGroup.AddSystem(new CgfThreatEvaluationSystem())` — unchanged.
- `_cognitiveRuntimeModule.RegisterSystems(simGroup)` — unchanged.
- `_actionDispatchModule.RegisterSystems(simGroup)` — unchanged.
- `simGroup.AddSystem(new RouteContextSystem())` — unchanged.

The existing `RegisterSystems(SystemGroup simGroup)` single-group overload must remain for backward
compatibility.

### 2.3 CgfInputGroupAdapter (shared utility)

The `CgfSubsystem` and `EditorSubsystem` both need a lightweight wrapper that executes a
`SystemGroup` in the kernel's Input phase. The adapter is a three-line class but needs to exist
in a project accessible to both.

**Files:** new file `Hrot/Engine/Hrot.Common/Infrastructure/CgfInputGroupAdapter.cs`

```csharp
[UpdateInPhase(SystemPhase.Input)]
public sealed class CgfInputGroupAdapter : IEcsModuleSystem
{
    private readonly SystemGroup _group;
    public CgfInputGroupAdapter(SystemGroup group) => _group = group;
    public void Execute(ISimulationView view, float dt) => _group.Run();
}
```

`Hrot.Common` already has transitive access to `IEcsModuleSystem` and `SystemGroup` through
the `Fdp.Network.Cyclone` → `Fdp.ModuleHost` chain. No new project reference is required.

### 2.4 CgfSubsystem Registration Update

`CgfSubsystem.Initialize()` currently calls `cgfLogicPack.RegisterSystems(simGroup)`, placing
`MissionControlExecutionSystem` on the background Simulation thread.

**Files:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

Update the registration block:
1. Create a separate `inputGroup = new SystemGroup(); inputGroup.Create(_context.World);`.
2. Call `cgfLogicPack.RegisterSystems(inputGroup, simGroup)` (new two-group overload).
3. Register the input group with the kernel as a global system:
   `_context.Kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(inputGroup));`
4. The existing `_context.Kernel.RegisterModule(new CgfSimGroupModule(_simGroup))` call remains
   unchanged — it routes the simulation group into the Simulation phase.

---

## Phase 3 — Editor Composition Root

**Goal:** Fix the `EditorSubsystem` composition root so that CGF and SimHost systems actually
run, and replace the broken time controller with `MasterSyncController` to provide correct
authoring (frozen) and preview (live) time modes.

### 3.1 System Group Wiring in EditorSubsystem

`EditorSubsystem.Initialize()` currently calls `_kernel.RegisterModule(cgfLogicPackInst)` and
`_kernel.RegisterModule(simHostCorePack)`. Both packs' `RegisterSystems(ISystemRegistry)` overloads
are deliberate no-ops — their systems can only be registered via the explicit `SystemGroup` overloads.
As a result, `MissionControlExecutionSystem` (and all SimHost Muscle systems) are orphaned in
memory and never tick.

**Files:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

Replace the `_kernel.RegisterModule(cgfLogicPackInst)` and `_kernel.RegisterModule(simHostCorePack)`
calls with explicit group wiring:

1. Create `inputGroup`, `simGroup`, `postSimGroup` as `SystemGroup` instances and call
   `Create(_world)` on each.
2. Call `simHostCorePack.RegisterSystems(inputGroup, simGroup, postSimGroup)`.
3. Call `cgfLogicPackInst.RegisterSystems(inputGroup, simGroup)` (Phase 2 two-group overload).
4. Register the input group as a global system:
   `_kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(inputGroup));`
5. Register the simulation group as a module using a local `SimGroupModule` wrapper (analogous
   to `CgfSimGroupModule` in `CgfSubsystem`):
   `_kernel.RegisterModule(new SimGroupModule(simGroup));`
6. Register the post-simulation group similarly:
   `_kernel.RegisterModule(new PostSimGroupModule(postSimGroup));`

`SimGroupModule` and `PostSimGroupModule` are trivial `IEcsModule` implementations whose `Tick()`
calls `_group.Run()`. They can be private nested classes in `EditorSubsystem.cs`.

The `logicPacks` list (used by `EditorApplication.SwitchToExternalAsync`) should keep
`simHostCorePack` and `cgfLogicPackInst` as `IEcsModule` references — the list is passed to
`EditorApplication` which uses it to unregister and re-register packs during the
online/offline switch, not to directly tick them.

### 3.2 EditorSubsystem Time Controller Replacement

`EditorSubsystem` holds `private SteppingTimeController? _stepping` and calls
`_stepping?.Step(deltaTime)` in `Update()`, piping the variable Raylib frame delta directly into
a stepping controller. This violates determinism (variable delta) and produces incorrect `GetMode()`
reports (returns `Continuous`).

The editor requires two time modes:
- **Authoring mode (default):** frozen — `dt = 0`. Kinematics and B-Trees do not advance. The
  event bus drains normally, so authored commands (`MissionControlIntent`) are processed.
- **Preview mode:** variable wall-clock. The simulation runs at normal speed for dry-run testing.

`MasterSyncController` natively implements this state machine via `SwitchToDeterministic` /
`SwitchToContinuous`.

**Files:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

1. Replace the `private SteppingTimeController? _stepping` field with
   `private MasterSyncController? _timeController`.
2. In `Initialize()`, create the controller via `TimeControllerFactory`:
   ```csharp
   var timeConfig = new TimeControllerConfig { Role = TimeRole.Standalone };
   _timeController = (MasterSyncController)TimeControllerFactory.Create(_world.Bus, timeConfig);
   _kernel.SetTimeController(_timeController);
   _timeController.SwitchToDeterministic(new HashSet<int>());
   ```
   `TimeRole.Standalone` creates a `MasterSyncController` with a private bus (no DDS publishing).
   `SwitchToDeterministic` places it in Stepping mode immediately, yielding `dt = 0` on every
   `kernel.Update()` until the preview mode is entered.
3. In `Update()`, remove `_stepping?.Step(deltaTime)`. The `_kernel?.Update()` call remains
   unchanged — the kernel now drives the `MasterSyncController` automatically.

### 3.3 EditorPreviewController Time Mode Transitions

The `EditorPreviewController` nested class currently transitions ECS snapshot state
(`TriggerLoadingPreview` / `TriggerUnloadingPreview`) but does not transition the time domain.
Without a corresponding time mode switch, entering preview mode keeps time frozen, and exiting
preview mode does not restore the authoring freeze.

**Files:** `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

1. Add `MasterSyncController _timeController` field to `EditorPreviewController`.
2. Update `EditorPreviewController` constructor to accept `MasterSyncController timeController`.
3. In `EnterPreviewMode()`, call `_timeController.SwitchToContinuous()` after
   `_handler.TriggerLoadingPreview()`.
4. In `ExitPreviewMode()`, call `_timeController.SwitchToDeterministic(new HashSet<int>())`
   after `_handler.TriggerUnloadingPreview()`.
5. Update the construction site in `EditorSubsystem.Initialize()`:
   `_previewController = new EditorPreviewController(_world, _timeController);`

### 3.4 EditorHarness Fix

`EditorHarness` (the integration test harness) uses the same broken module registration pattern
as `EditorSubsystem`. After the Phase 2 changes to `CgfLogicPack`, the harness will have a
compilation error since the pack no longer runs its systems via `RegisterModule`. The harness must
be updated to match the composition root pattern established in §3.1.

**Files:** `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`

Apply the same group-wiring fix: create `inputGroup`, `simGroup`, `postSimGroup`, call
`RegisterSystems(...)` on both packs, register groups with the kernel. Also replace
`SteppingTimeController` with `MasterSyncController` started in Deterministic mode.
`PumpFrames` / `PumpUntil` must call `_timeController.Step(dt)` before `Kernel.Update()`.

---

## Phase 4 — Distributed Load Safety

**Goal:** Fix the silent network-ID corruption that affects multi-part entities (entities with
structural children, e.g. a turret or towed trailer) during scenario load into a live cluster.

### 4.1 StagingEntityExtractor Child Entity Remapping

`StagingEntityExtractor.Extract` performs a two-pass extraction. In Pass 2, root entity
component lists are correctly remapped: `ActiveMissionPlan` `BehaviorParams` JSON strings and
Intent DTO managed components (`InitialPassengersIntent`, `InitialVehicleIntent`, etc.) all have
their embedded network IDs patched from the old offline staging allocations to the new live IDs
pre-allocated in Pass 1.

However, the remapping loop only operates on `comps` — the root entity's component list. Child
entity components are harvested into `childBuffer` earlier in the loop and are assembled into
`ChildComponentOverrides` without any network ID remapping. Any child that carries an
`ActiveMissionPlan` or an Intent DTO will retain stale offline staging IDs. When the genesis
pipeline materializes the entity, those stale IDs fail to resolve, causing silent cognitive failure.

**Files:** `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs`

1. Extract the entire remapping block (the `behaviorRemapper` loop and the Intent DTO `for`
   loop, currently at approximately lines 439–501) into a private static helper method:
   ```csharp
   private static void RemapComponentNetworkIds(
       List<object> components,
       Dictionary<long, long> oldToNewMap,
       ScenarioBehaviorRemapper? behaviorRemapper)
   ```
2. Call `RemapComponentNetworkIds(comps, oldToNewMap, behaviorRemapper)` on the root entity
   component list (replacing the inline code).
3. In the `childBuffer` assembly block, iterate the harvested child components and call
   `RemapComponentNetworkIds` on each child's component list before assembling the override
   dictionary.

---

## Architectural Decision Log

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Use `MasterSyncController` (Standalone role) in the editor, not `SteppingTimeController` | `MasterSyncController` provides the Continuous/Deterministic state machine needed for preview vs. authoring transitions. `SteppingTimeController` cannot switch modes and reports `GetMode() = Continuous` incorrectly. |
| D2 | Freeze time via `SwitchToDeterministic` (0 dt), not by disabling groups | Disabled groups orphan events in the bus. A `dt = 0` kernel tick safely drains events while halting integration. |
| D3 | Keep existing `RegisterSystems(SystemGroup simGroup)` single-group overloads | Backward compat with tests and single-group callers (e.g. `SimulationLogicModule`). |
| D4 | Share `CgfInputGroupAdapter` via `Hrot.Common` | Avoids duplication between `CgfSubsystem` and `EditorSubsystem`. `Hrot.Common` already has transitive access to `IEcsModuleSystem` through `Fdp.Network.Cyclone → Fdp.ModuleHost`. |
| D5 | Do NOT move `DoctrineIngressSystem` phase in the existing `CgfSubsystem` path until Phase 2 | The single-group `RegisterSystems(simGroup)` path must remain valid for the distributed `SimulationLogicModule` which owns its own phase dispatch. |

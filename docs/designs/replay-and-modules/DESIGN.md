# Design: Replay Isolation and Modern Module System

## 1. Problem Statement

Two separate but intertwined problems are addressed together because their solutions share the same foundation.

### 1.1 Broken Replay Isolation

Replay is supposed to hermetically seal Brain and Muscle nodes from live network influence so historical ECS state can be replayed faithfully. However, the current implementation has at least three concrete gaps, all verified against the live codebase:

**Gap A — SimHostApp passes an empty group to the replay handler.**
`SimHostApp.OnLoad` creates `var simulationSystemGroup = new SimulationSystemGroup()` (empty, no systems ever added) and passes it to `NodeBootstrapper.BuildOrchestration`. The actual simulation systems live in `_kernelGroup` (a plain `SystemGroup` created right after). When the replay handler flips `simulationSystemGroup.Enabled = false`, it stops nothing because the group is empty. The real simulation systems keep running on top of replayed ECS data.

**Gap B — Input phase is never disabled during replay.**
The design requires that all input-phase systems (network ingress, behavior ingress, fire-processing queries, etc.) be suspended during replay so live operator commands and live DDS traffic cannot corrupt the historical state being played back. No code currently disables the input phase for any node.

**Gap C — CgfSubsystem passes `simGroup: null` to its replay handler.**
In `CgfSubsystem.Initialize`, the `ReferenceReplayLoadHandler` is constructed with `simGroup: null, lifecycleGroup: null`. This means the CGF node's replay handler disables nothing when replay begins.

### 1.2 Legacy Module System Accumulation

The FDP engine provides two module systems:

- **Legacy**: `ComponentSystem` (abstract base class) + `SystemGroup` (topological sorter) + five `StandardSystemGroups` (`InputSystemGroup`, `SimulationSystemGroup`, `PostSimulationSystemGroup`, `PresentationSystemGroup`, `ExportSystemGroup`). This was the first-generation design.
- **Modern**: `IEcsModuleSystem` + `ISystemRegistry` + `[UpdateInPhase]` + `ModuleHostKernel`. This is the current engine model. Toolkit systems (CycloneEgressSystem, PlaybackTickSystem, AutonomousPerceptionModule, etc.) already use it.

The two live side-by-side. The legacy system is still used by all Hrot game systems (CombatModule, GroundKinematicsModule, MissionControlModule, etc.) and is bridged into the modern kernel via adapter classes (`CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter`) in `Hrot.Common.Infrastructure`. These adapters are the primary source of complexity for the replay problems above.

The goal is to complete the migration: convert all remaining game systems to `IEcsModuleSystem`, delete the legacy base classes, and delete the adapter classes. This forces correct composition-root wiring by making incorrect wiring a compile error.

---

## 2. Replay Architecture: Final Decisions

### 2.1 What Runs During Replay

The following table reflects the final decisions from the design discussion:

| Phase / Group | During Live | During Replay | Reason |
|---|---|---|---|
| `TogglableInputGroup` | Enabled | **Disabled** | Block live DDS ingress and operator commands |
| `TogglableSimulationGroup` | Enabled | **Disabled** | Block AI, kinematics, combat logic |
| `TogglablePostSimulationGroup` (BallisticsSystem, LinearKinematicsSystem, CarKinematicsSystem) | Enabled | **Disabled** | Physics integration mutates SimTransform and would overwrite restored positions |
| `NetworkLifecycleSystemGroup` | Enabled | **Disabled** | Block ghost create/promote/destroy during playback |
| `GhostDestructionSystem` | Enabled | **Disabled** | Must be moved inside `NetworkLifecycleSystemGroup`; a stray DDS DISPOSE during replay would delete historical entities |
| `DeferredTakeoverSystem` | Enabled | **Disabled** | Must be moved inside `NetworkLifecycleSystemGroup`; would illegally mutate authority masks on historical entities |
| `PlaybackTickSystem` | Not registered | **Running** | Drives replay frame-by-frame restore of ECS state |
| Export phase (CycloneEgressSystem, SmartEgressSystem, OwnershipEgressSystem) | Runs normally | Runs normally | IG nodes receive historical state from network; timeline seek requires a forced-dirty workaround (see Section 3.10) |
| `RecorderTickSystem` | Runs when RecordingModule active | **Not registered** | RecordingModule is uninstalled at exercise end; it is mutually exclusive with ReplayModule |

### 2.2 IG Nodes During Replay

IG nodes use `ListenerRecordReplayController` which is a no-op: they do not record and do not replay. During a replay session on Brain/Muscle nodes, the IG nodes continue to receive ECS state from the network (via their normal DDS ingress) exactly as they would in a live session. The Brain/Muscle nodes restore historical state from disk and their Export-phase systems (CycloneEgressSystem, etc.) broadcast it over DDS. IGs receive and render it normally. No changes are needed on the IG side.

### 2.3 Plan A Recording (Record Everything)

Every Brain/Muscle node records its full ECS state — both owned components and unowned ghost components. No per-component ownership filter is applied during recording. This ensures that seeking and rewinding work correctly because the full world state is available at any frame on every node.

IG/ExCon nodes use `ListenerRecordReplayController` (a no-op): they are not affected by recording or replay transitions.

### 2.4 Input Group Systems (What Gets Disabled)

Systems that go into `TogglableInputGroup` on the CGF (Brain) node:
- `MissionControlExecutionSystem` (currently in inputGroup in CgfLogicPack two-group overload)
- `BehaviorIngressSystem` (from `MissionControlModule`, currently in inputGroup)

Systems that go into `TogglableInputGroup` on the SimHost (Muscle) node:
- `FireProcessingSystem`, `RaycastSolverSystem`, `HitResolutionSystem` (from `CombatModule`, currently in inputGroup)
- `PersonalRouteAuthoringSystem` (navigation, currently in inputGroup)
- Physics query systems (`TerrainQuerySystem`, etc.)
- Any DDS ingress systems registered at `SystemPhase.Input` from the composition root

### 2.5 PostSimulation Group Systems (What Gets Disabled)

Systems that go into `TogglablePostSimulationGroup` on the SimHost (Muscle) node:
- `BallisticsSystem` — integrates ballistic trajectories into SimTransform positions
- `LinearKinematicsSystem` — integrates velocity into SimTransform position for linear movers
- `CarKinematicsSystem` — integrates vehicle speed into SimTransform position

**Why these must be disabled:** When `PlaybackTickSystem` restores a historical frame, it blits raw ECS chunk data directly into the `NativeChunkTable`, which restores all `SimTransform` components to their recorded historical values. If any kinematic integration system then runs in the same frame (after `PlaybackTickSystem` fires in PostSimulation), it reads the newly restored velocity/speed components and advances the position forward again — corrupting the replay with a double-integration artifact.

Systems that remain running freely in PostSimulation during replay:
- `PlaybackTickSystem` (from `ReplayModule`) — this is what drives the replay; it must always run
- Terrain/coordinate query resolution systems that are read-only
- Any system that only reads ECS state (does not mutate SimTransform or other physics components)

Systems NOT registered during replay (naturally absent):
- `RecorderTickSystem` — belongs to `RecordingModule` which is not installed during replay

Systems that stay running in Export phase (must NOT be disabled):
- `CycloneEgressSystem`, `SmartEgressSystem`, `OwnershipEgressSystem` — broadcast historical state to IG nodes

---

## 3. Modern Architecture: Target State

### 3.1 Class Deletions (Forces Solution-Wide Migration)

**From `Fdp.Core`** (deleted files trigger compile errors across the solution):
- `ComponentSystem.cs` — abstract base class
- `SystemGroup.cs` — group + topological sort
- `StandardSystemGroups.cs` — five concrete groups (`InputSystemGroup`, `SimulationSystemGroup`, `PostSimulationSystemGroup`, `PresentationSystemGroup`, `ExportSystemGroup`)

**From `Hrot.Common.Infrastructure`** (deleted files trigger compile errors in Hrot subsystems):
- `CgfInputGroupAdapter.cs` — bridges SystemGroup to IEcsModuleSystem at Input phase
- `LegacySystemGroupAdapters.cs` — contains `LegacySystemGroupAdapterBase`, `SimulationGroupModule`, `PostSimulationGroupAdapter`

### 3.2 New Composition Wrappers (Togglable Groups)

Three new classes go in `Fdp.ModuleHost.Scheduling`. Unlike `NetworkLifecycleSystemGroup` (which is a plain class with an `ExecuteGroup` method), these three implement **`ISystemGroup`** (from `Fdp.ModuleHost.Abstractions`). `ISystemGroup` extends `IEcsModuleSystem` and adds `Name` and `GetSystems()`. The `SystemScheduler.ExecuteSystem` method checks `if (system is ISystemGroup group)` and, when true, calls `ExecuteGroup` on the group which profiles each inner system individually in the diagnostic UI. Without `ISystemGroup`, the group appears as a single black-box entry in the profiler.

**`TogglableInputGroup`** — registered in `SystemPhase.Input`:
```csharp
// Fdp.ModuleHost.Scheduling.TogglableInputGroup
[UpdateInPhase(SystemPhase.Input)]
public sealed class TogglableInputGroup : ISystemGroup
{
    private readonly IEcsModuleSystem[] _innerSystems;
    public bool Enabled { get; set; } = true;
    public string Name { get; }

    public TogglableInputGroup(string name, params IEcsModuleSystem[] innerSystems)
    {
        Name = name;
        _innerSystems = innerSystems;
    }

    public IReadOnlyList<IEcsModuleSystem> GetSystems() => _innerSystems;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!Enabled) return;
        foreach (var sys in _innerSystems)
            sys.Execute(view, deltaTime);
    }
}
```

**`TogglableSimulationGroup`** — registered in `SystemPhase.Simulation`:
```csharp
// Fdp.ModuleHost.Scheduling.TogglableSimulationGroup
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class TogglableSimulationGroup : ISystemGroup
{
    private readonly IEcsModuleSystem[] _innerSystems;
    public bool Enabled { get; set; } = true;
    public string Name { get; }

    public TogglableSimulationGroup(string name, params IEcsModuleSystem[] innerSystems)
    {
        Name = name;
        _innerSystems = innerSystems;
    }

    public IReadOnlyList<IEcsModuleSystem> GetSystems() => _innerSystems;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!Enabled) return;
        foreach (var sys in _innerSystems)
            sys.Execute(view, deltaTime);
    }
}
```

**`TogglablePostSimulationGroup`** — registered in `SystemPhase.PostSimulation`:
```csharp
// Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup
[UpdateInPhase(SystemPhase.PostSimulation)]
public sealed class TogglablePostSimulationGroup : ISystemGroup
{
    private readonly IEcsModuleSystem[] _innerSystems;
    public bool Enabled { get; set; } = true;
    public string Name { get; }

    public TogglablePostSimulationGroup(string name, params IEcsModuleSystem[] innerSystems)
    {
        Name = name;
        _innerSystems = innerSystems;
    }

    public IReadOnlyList<IEcsModuleSystem> GetSystems() => _innerSystems;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!Enabled) return;
        foreach (var sys in _innerSystems)
            sys.Execute(view, deltaTime);
    }
}
```

The replay handler acquires references to all three wrappers (plus `NetworkLifecycleSystemGroup`) and flips their `Enabled` flag during `PrepareReplay`/`FinalizeReplay`/`PrepareLive` commits.

**Note on `NetworkLifecycleSystemGroup`:** This existing class uses `ExecuteGroup` not `Execute` and is NOT an `IEcsModuleSystem`. It is called directly by the replication module's orchestration code. The three new togglable groups above are proper `IEcsModuleSystem` implementations registered with the kernel scheduler. Do not attempt to retrofit `NetworkLifecycleSystemGroup` into `ISystemGroup` — that is a separate workstream if needed.

### 3.3 System Migration: ComponentSystem to IEcsModuleSystem

Every system that currently extends `ComponentSystem` must be changed to implement `IEcsModuleSystem`. The mechanical change is:

**Before:**
```csharp
public class CarKinematicsSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        World.Query().With<SimTransform>().Each(...);
    }
}
```

**After:**
```csharp
[UpdateInPhase(SystemPhase.PostSimulation)]
public class CarKinematicsSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        view.Query().With<SimTransform>().Each(...);
    }
}
```

Key rules for the conversion:
- Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`.
- Replace `World.Query(...)` with `view.Query(...)`.
- Replace `DeltaTime` with `deltaTime` parameter.
- Add `[UpdateInPhase(SystemPhase.X)]` matching the group the system was previously placed in.
- Keep `[UpdateBefore]` / `[UpdateAfter]` attributes unchanged for intra-group ordering.
- Systems that require immediate structural mutation (not deferred through command buffer) must use the EntityRepository downcast and **throw** on failure:

```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    if (view is not EntityRepository repo)
        throw new InvalidOperationException(
            $"{GetType().Name} requires direct EntityRepository access and cannot run " +
            $"on a read-only snapshot view ({view.GetType().Name}). " +
            "Do not schedule this system on a background thread.");
    // direct mutation here
}
```

Throwing instead of silently returning is intentional. If a developer accidentally configures a direct-mutation system to run on a background thread against a read-only snapshot, the `ModuleHostKernel`'s circuit breaker catches the exception, immediately flags the module as failed, and surfaces the exact configuration error in the logs and the `ArchitectureDiagnosticsWindow`. A silent return would hide the misconfiguration permanently. `WrongPhaseException` (from `Fdp.Core.Phase`) is an alternative if the call site is always triggered by a phase mismatch, but `InvalidOperationException` is preferred here because the failure mode is a scheduling configuration error, not a phase protocol violation.

### 3.4 Composition Root Changes

**`SimHostCoreLogicPack`**:
- The overload `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)` is deleted.
- `RegisterSystems(ISystemRegistry registry)` is **NOT** used for the systems that need to be wrapped in togglable groups, because registering the same system instance into both the registry directly AND into a `TogglableSimulationGroup` that is also registered would cause a double-registration exception in `SystemScheduler`.
- Instead, `SimHostCoreLogicPack` exposes three read-only array properties:
  - `InputSystems` — returns the instantiated `IEcsModuleSystem[]` for `[UpdateInPhase(Input)]` systems
  - `SimulationSystems` — returns the instantiated `IEcsModuleSystem[]` for `[UpdateInPhase(Simulation)]` systems
  - `PostSimulationSystems` — returns the instantiated `IEcsModuleSystem[]` for `[UpdateInPhase(PostSimulation)]` systems
- `SimHostApp` reads these arrays and packs them into `TogglableInputGroup`, `TogglableSimulationGroup`, and `TogglablePostSimulationGroup` respectively, then registers the three wrapper groups with the kernel.
- `SimHostCoreLogicPack` continues to expose `RegisterSystems(ISystemRegistry registry)` for **non-toggled** systems (e.g., perception or diagnostic systems that should always run) but the three phase-specific arrays are the primary interface for composition.

**`CgfLogicPack`**:
- Adopts the exact same pattern as `SimHostCoreLogicPack` to avoid the double-registration trap. If `CgfLogicPack` called `registry.RegisterSystem()` directly AND `CgfSubsystem` also registered those same instances inside a `TogglableInputGroup`/`TogglableSimulationGroup`, the `SystemScheduler` would throw a duplicate-registration exception.
- Both overloads `RegisterSystems(SystemGroup simGroup)` and `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` are deleted.
- `RegisterSystems(ISystemRegistry registry)` is **not** used for the game-logic systems that need toggling.
- Instead, `CgfLogicPack` exposes two read-only array properties:
  - `InputSystems` — returns the instantiated `IReadOnlyList<IEcsModuleSystem>` for `[UpdateInPhase(Input)]` systems
  - `SimulationSystems` — returns the instantiated `IReadOnlyList<IEcsModuleSystem>` for `[UpdateInPhase(Simulation)]` systems
- `CgfSubsystem` reads these arrays and packs them into `TogglableInputGroup` and `TogglableSimulationGroup` respectively, then registers the wrappers with the kernel.
- `EditorSubsystem` reads the same arrays and calls `registry.RegisterSystem()` directly for each system (no toggling needed in the editor).

**Sub-modules** (`CombatModule`, `GroundKinematicsModule`, `MissionControlModule`, `CognitiveRuntimeModule`, `ActionDispatchModule`, `DamageAssessmentModule`):
- `RegisterSystems(SystemGroup group)` overloads are deleted.
- `RegisterSystems(ISystemRegistry registry)` becomes the real implementation using `registry.RegisterSystem(new XxxSystem(...))`.

### 3.5 ReferenceReplayLoadHandler Updates

The handler currently stores `SimulationSystemGroup? _simGroup` (legacy type). This changes to `TogglableSimulationGroup? _simGroup`. A new parameter `TogglableInputGroup? _inputGroup` is added. The `SetSystemsEnabled` helper toggles all three groups (`_inputGroup`, `_simGroup`, `_lifecycleGroup`) plus the bypass toggle.

```csharp
private void SetSystemsEnabled(bool enabled)
{
    if (_inputGroup     != null) _inputGroup.Enabled     = enabled;
    if (_simGroup       != null) _simGroup.Enabled       = enabled;
    if (_lifecycleGroup != null) _lifecycleGroup.Enabled = enabled;
}
```

### 3.6 NodeBootstrapper.BuildOrchestration Updates

The parameter `Fdp.Core.SimulationSystemGroup? simGroup` changes to `Fdp.ModuleHost.Scheduling.TogglableSimulationGroup? simGroup`. A new parameter `Fdp.ModuleHost.Scheduling.TogglableInputGroup? inputGroup = null` is added. The guard condition for registering `ReferenceReplayLoadHandler` changes accordingly:

```csharp
if (controller != null && (inputGroup != null || simGroup != null || lifecycleGroup != null))
{
    clusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
        controller, inputGroup, simGroup, lifecycleGroup, bypassToggle, localTempRoot));
}
```

### 3.7 SimHostApp Wiring

After the migration, `SimHostApp.OnLoad` will:
1. Instantiate all input-phase systems (from `_simCorePack`) as `IEcsModuleSystem` instances.
2. Pack them into `new TogglableInputGroup(inputSystems)`.
3. Instantiate all simulation-phase systems (from `_simCorePack`) as `IEcsModuleSystem` instances.
4. Pack them into `new TogglableSimulationGroup(simSystems)`.
5. Register these wrappers via `_kernel.RegisterGlobalSystem(inputGroup)` and `_kernel.RegisterGlobalSystem(simGroup)`.
6. Pass both to `NodeBootstrapper.BuildOrchestration`.
7. Delete the `_kernelGroup` field entirely — no more separate `SystemGroup` + `Run()`.

### 3.8 CgfSubsystem Wiring

After the migration, `CgfSubsystem.Initialize` will:
1. No longer create `_inputGroup` and `_simGroup` as `SystemGroup` objects.
2. Read `cgfLogicPack.InputSystems` and `cgfLogicPack.SimulationSystems` array properties; pack them into `new TogglableInputGroup(inputSystems)` and `new TogglableSimulationGroup(simSystems)` respectively.
3. No longer call `_context.Kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(_inputGroup))`.
4. No longer call `_context.Kernel.RegisterModule(new CgfSimGroupModule(_simGroup))`.
5. Register the new togglable wrappers directly with the kernel.
6. Pass them to `ReferenceReplayLoadHandler` (fixing the `simGroup: null` bug).

### 3.9 EditorSubsystem Wiring

The Editor does not participate in replay (it uses `MasterSyncController`), but it also currently uses legacy adapters (`CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter`). After the adapters are deleted, `EditorSubsystem` must register its systems directly via `ISystemRegistry` without any togglable group indirection (since replay toggling is not needed in the editor).

**EditorSubsystem directly instantiates both `CgfLogicPack` and `SimHostCoreLogicPack`**, so it must call their new APIs. Instead of creating dummy legacy `SystemGroup` objects, `EditorSubsystem.Initialize` extracts the `InputSystems`, `SimulationSystems`, and `PostSimulationSystems` properties from both packs and loops through them, calling `registry.RegisterSystem()` for each instance. Do **not** call `RegisterSystems(kernelRegistry)` on the packs for the toggled systems — those packs no longer implement that method for game-logic systems (only `SimHostCoreLogicPack` retains it for always-on non-toggled systems):

```csharp
// Inside EditorSubsystem.Initialize (after the migration):
// CgfLogicPack: register arrays directly (no toggling needed in the editor)
foreach (var sys in _cgfLogicPack.InputSystems)      kernelRegistry.RegisterSystem(sys);
foreach (var sys in _cgfLogicPack.SimulationSystems) kernelRegistry.RegisterSystem(sys);
// SimHostCoreLogicPack: same pattern for all three phases
foreach (var sys in _simCorePack.InputSystems)          kernelRegistry.RegisterSystem(sys);
foreach (var sys in _simCorePack.SimulationSystems)     kernelRegistry.RegisterSystem(sys);
foreach (var sys in _simCorePack.PostSimulationSystems) kernelRegistry.RegisterSystem(sys);
_simCorePack.RegisterSystems(kernelRegistry); // always-on systems (perception, diagnostics)
```

**`EditorSystemsModule`** — the module that currently executes `EditorCargoSystem` and other editor-specific systems by calling `_editorGroup.Run()` — must also be refactored. After the migration it registers its systems via `registry.RegisterSystem()` instead of holding a legacy `SystemGroup` and calling `Run()` on it.

`EditorHarness` (integration test harness) uses local `SimGroupModule` and `PostSimGroupModule` nested classes that wrap `SystemGroup`. These are also removed and replaced with direct system registration.

---

### 3.10 Deep Architectural Risks and Required Fixes

The following issues are not part of the module system migration per se, but they will break the replay experience if not addressed. They are captured here because they interact directly with the same subsystems being changed.

#### 3.10.1 SmartEgressSystem 10-Second Lag on Timeline Seek

`SmartEgressSystem` relies on systems calling `SmartEgressUtil.MarkDirty()` to instantly publish low-frequency declarative data (`EntityMission`, `WeaponState`, `EntityInfo`, etc.) when that data changes. During normal replay (frame-by-frame advance), simulation systems are disabled and `MarkDirty` is never called. `SmartEgressSystem` falls back to its 600-tick rolling heartbeat, meaning the IG receives a full state sync every 10 seconds. This is tolerable for sequential playback.

However, when an operator performs a timeline seek (`SeekToFrame`), the `PlaybackSystem` blits raw ECS chunk data directly into the `NativeChunkTable`. This bulk memory copy bypasses component setters and never triggers `MarkDirty`. The IG node displays stale pre-seek data for up to 10 seconds after the seek.

**Fix:** `PlaybackTickSystem` (or the replay orchestrator hook) must force-flag all active entities as dirty in `EgressPublicationState` immediately after executing a `SeekToFrame`. This triggers `SmartEgressSystem` to broadcast a full cluster state sync on the very next Export frame, snapping the IG to the new timeline position.

#### 3.10.2 GlobalTime Singleton Tug-of-War

In `ModuleHostKernel.Update()`, the live `TimeController` writes the current `GlobalTime` singleton into the ECS world every frame. During replay, `PlaybackSystem` runs in the `PostSimulation` phase and restores all recorded singletons from disk — including the historical `GlobalTime`.

This creates a per-frame overwrite cycle:
1. Kernel writes live `GlobalTime` (before Input phase)
2. Input and Simulation systems run with live time
3. `PlaybackSystem` runs in PostSimulation and overwrites `GlobalTime` with historical time
4. Export systems run with historical time

Time-dependent logic (velocity extrapolation, effect duration, TTL expiry) behaves inconsistently depending on which phase accesses the singleton.

**Fix:** When `ReplayModule` is active and `PrepareReplay` is committed, the `TimeController` must be instructed to stop writing live time into the world for the duration of the replay. `PlaybackSystem` then has full ownership of `GlobalTime`. When `FinalizeReplay` or `PrepareLive` is committed, the `TimeController` resumes.

#### 3.10.3 GhostDestructionSystem and DeferredTakeoverSystem Outside NetworkLifecycleSystemGroup

`NetworkLifecycleSystemGroup` is disabled during replay. However, two systems registered by `NedReplicationModule` are outside that group:

- `GhostDestructionSystem` (`[UpdateInPhase(SystemPhase.PostSimulation)]`) — registered outside the group. During replay, a stray or delayed network `DISPOSE` packet (from a late DDS delivery) would cause this system to delete a historical entity from the replaying world, creating an unfillable gap in playback.
- `DeferredTakeoverSystem` (`[UpdateInPhase(SystemPhase.BeforeSync)]`) — registered outside the group. During replay it could illegally mutate the `AuthorityMask` of historical entities based on network grant packets, corrupting the authority state.

**Fix:** Both systems must be moved inside `NetworkLifecycleSystemGroup` so they are automatically disabled when replay begins. This requires adding a `GhostDestructionSystem` slot and a `DeferredTakeoverSystem` slot to `NetworkLifecycleSystemGroup`'s constructor and its internal system array. No new wrapper class is needed and no new field is needed in `ReferenceReplayLoadHandler` — `NetworkLifecycleSystemGroup` is already wired to the handler.

#### 3.10.4 CycloneNetworkCleanupSystem Scrub Flood on Seek

`CycloneNetworkCleanupSystem` (`[UpdateInPhase(SystemPhase.Export)]`) maintains a `_trackedEntities` dictionary to detect when owned entities are destroyed and send DDS `DISPOSE` signals. This system runs during replay (Export phase is not disabled).

When an operator performs a timeline seek, the entire world state is annihilated and replaced by `PlaybackSystem`. `CycloneNetworkCleanupSystem` sees all previously-tracked entities as `IsAlive == false` and immediately sends `DISPOSE` for each of them — a mass-destruction DDS broadcast. This simultaneously tears down all IG ghost entities. The IG then immediately reconstructs them as the egress systems send fresh baseline publications. This spike causes severe DDS congestion and visual pop-in.

**Fix:** The `ReferenceReplayLoadHandler` (or `PlaybackTickSystem`) must expose a `SeekCompleted` callback or event. `CycloneNetworkCleanupSystem` registers with this callback and clears `_trackedEntities` when a seek completes, so it does not misinterpret the post-seek world state as mass destruction.

---

## 4. Migration Phases

### Phase 1 — Togglable Group Foundation

**Scope:** Pure additions. Nothing deleted. Existing code still compiles and runs.

1. Create `TogglableInputGroup` in `Fdp.ModuleHost.Scheduling`.
2. Create `TogglableSimulationGroup` in `Fdp.ModuleHost.Scheduling`.
3. Update `ReferenceReplayLoadHandler` to accept `TogglableInputGroup?` and `TogglableSimulationGroup?` (replacing `SimulationSystemGroup?`). Keep backward-compatible overloads during transition if needed.
4. Update `NodeBootstrapper.BuildOrchestration` signature to use new types.
5. Update existing tests that construct `ReferenceReplayLoadHandler` or call `BuildOrchestration` with the old types.

**Deliverable:** Both new classes exist and the replay handler can toggle them. Nothing else changes.

### Phase 2 — System Migration (Input/Simulation Systems)

**Scope:** Convert all `ComponentSystem`-based game systems to `IEcsModuleSystem`. This is the largest phase.

For each sub-module below, the change is:
- System classes: `ComponentSystem` → `IEcsModuleSystem`, `OnUpdate()` → `Execute(view, dt)`.
- Module's `RegisterSystems(ISystemRegistry)` becomes the real implementation; legacy `RegisterSystems(SystemGroup)` overloads are deleted.
- `[UpdateInPhase]` added to each system class.

**Sub-modules to convert (roughly ordered by complexity):**

| Module | Project | Key Systems |
|--------|---------|-------------|
| `CombatModule` | `Hrot.SimHost` | FireProcessingSystem (Input), RaycastSolverSystem (Input), HitResolutionSystem (Input), BallisticsSystem (PostSim) |
| `GroundKinematicsModule` | `Hrot.SimHost` | SpatialHashSystem (Sim), CarKinematicsSystem (PostSim), FormationTargetSystem (Sim), VehicleCommandSystem (Sim), NavigationExecutionSystem (Sim), LinearKinematicsSystem (PostSim) |
| `DamageAssessmentModule` | `Hrot.SimHost` or toolkit | Systems delivering authoritative damage |
| `MissionControlModule` | CGF toolkit | BehaviorIngressSystem (Input), MissionDirectorSystem (Sim) |
| `CognitiveRuntimeModule` | CGF toolkit | BTreeTickSystem (Sim), HsmTickSystem (Sim), ChannelArbitrationSystem (Sim), HsmDamageBridgeSystem (Sim) |
| `ActionDispatchModule` | CGF toolkit | LocomotionDispatcherSystem (Sim), WeaponDispatcherSystem (Sim) |
| Standalone CGF systems | `Hrot.CGF` | MissionControlExecutionSystem (Input), MissionAdapterSystem (Sim), HealthApplicationSystem (Sim), CgfThreatEvaluationSystem (Sim), RouteContextSystem (Sim) |
| Navigation bridges | Various | PersonalRouteAuthoringSystem (Input), NavigationIntentBridgeSystem (Sim), RouteTrajectorySyncSystem (Sim) |
| `GenesisMaterializationSystem` | `Hrot.SimHost` | Direct mutation, uses EntityRepository downcast |

**At the end of Phase 2:** All game systems implement `IEcsModuleSystem`. No system extends `ComponentSystem`. However, `ComponentSystem` and `SystemGroup` still exist — they just have no more subclasses in game code.

### Phase 3 — Application Wiring

**Scope:** Update composition roots to use new system types and togglable groups.

1. `SimHostCoreLogicPack`: activate `RegisterSystems(ISystemRegistry)`, delete legacy overloads.
2. `CgfLogicPack`: expose `InputSystems` and `SimulationSystems` array properties (same pattern as `SimHostCoreLogicPack`), delete legacy overloads.
3. `SimHostApp`: remove `_kernelGroup`, create and register `TogglableInputGroup` and `TogglableSimulationGroup`, wire to replay handler.
4. `CgfSubsystem`: remove legacy SystemGroup fields, wire togglable groups, fix `simGroup: null` bug.
5. `CgfApplication`: same as CgfSubsystem.
6. `EditorSubsystem`: remove adapter usage, register systems directly via `ISystemRegistry`.
7. `EditorHarness` and `SimHostInstance` test harnesses: remove SystemGroup usage.

**At the end of Phase 3:** The application wiring is clean. Replay isolation is correctly implemented. The legacy classes are still present but no longer used in production code.

### Phase 4 — Legacy Removal

**Scope:** Delete the legacy classes. This intentionally breaks any remaining references.

1. Delete `ComponentSystem.cs` from `Fdp.Core`.
2. Delete `SystemGroup.cs` from `Fdp.Core`.
3. Delete `StandardSystemGroups.cs` from `Fdp.Core`.
4. Delete `CgfInputGroupAdapter.cs` from `Hrot.Common.Infrastructure`.
5. Delete `LegacySystemGroupAdapters.cs` from `Hrot.Common.Infrastructure`.
6. Fix all remaining compile errors (tests that directly instantiate legacy types).
7. Confirm solution builds cleanly.

---

## 5. Key Invariants

### 5.1 Execution Order Preservation

The current execution order (`Input → Simulation → PostSimulation → Export`) is the responsibility of `[UpdateInPhase]` attributes. The modern `SystemScheduler` inside `ModuleHostKernel` reads these attributes and builds the correct topological execution graph. Legacy `[UpdateBefore]` / `[UpdateAfter]` attributes remain valid for intra-phase ordering.

### 5.2 Direct Mutation Systems

Systems that call `EntityRepository` methods directly (create entity, add/remove component) must use the downcast:
```csharp
if (view is not EntityRepository repo) return;
```
This is NOT a performance concern for synchronous modules (which are the majority). The downcast is a safety gate that returns a no-op if the system is ever accidentally configured to run on a background snapshot. These systems cannot be moved to background execution without redesign, which is acceptable.

### 5.3 The SimHostApp `_kernelGroup` Bug

The current `_kernelGroup` in `SimHostApp` is a plain `SystemGroup` that contains ALL simulation systems regardless of phase (input, sim, postSim all packed in together). After the migration, all systems declare their own phase via `[UpdateInPhase]` and are registered with the kernel directly (or via togglable wrappers). The `_kernelGroup` field and the `_kernelGroup.Run()` call in `OnUpdate` are both removed entirely. The kernel's `Update()` handles all execution.

### 5.4 The Empty SimulationSystemGroup Bug

The empty `SimulationSystemGroup` instantiated in `SimHostApp.OnLoad` (line near `BuildOrchestration`) serves no purpose and is removed. After Phase 3, a properly-populated `TogglableSimulationGroup` takes its place and is correctly wired to the replay handler.

### 5.5 The CGF `simGroup: null` Bug

The `ReferenceReplayLoadHandler` in `CgfSubsystem` passes `simGroup: null, lifecycleGroup: null`. This is fixed in Phase 3 when real `TogglableSimulationGroup` and `TogglableInputGroup` references are wired from the composition root.

### 5.6 Egress Keeps Running During Replay

`CycloneEgressSystem`, `SmartEgressSystem`, and `OwnershipEgressSystem` are all `[UpdateInPhase(SystemPhase.Export)]` and are registered by the replication module. They are NOT inside any togglable group. The kernel continues to run the Export phase during replay, broadcasting the restored historical ECS state over DDS. IG nodes receive this data and render it normally. This is the intended behavior.

### 5.7 PostSimulation Systems During Replay

`PlaybackTickSystem` (from `ReplayModule`) is `[UpdateInPhase(SystemPhase.PostSimulation)]`. It runs freely during replay because it is what drives the frame-by-frame restore of ECS state. It is NOT placed inside `TogglablePostSimulationGroup`.

`RecorderTickSystem` (from `RecordingModule`) does NOT run during replay. `RecordingModule` is installed only when an exercise is active and live recording is enabled. `ReplayModule` is mutually exclusive with `RecordingModule` — when the orchestrator transitions a node to replay mode, it uninstalls `RecordingModule` before installing `ReplayModule`. Therefore `RecorderTickSystem` is never registered in the kernel during replay.

`BallisticsSystem`, `LinearKinematicsSystem`, and `CarKinematicsSystem` are placed inside `TogglablePostSimulationGroup` and are disabled during replay. See Section 2.5 for details.

---

## 6. Dependency Graph

```
Fdp.Core
  (ComponentSystem, SystemGroup, StandardSystemGroups)
  |
  v
Fdp.ModuleHost.Abstractions
  (IEcsModuleSystem, ISystemRegistry, SystemPhase, UpdateInPhase)
  |
  v
Fdp.ModuleHost
  (ModuleHostKernel, SystemScheduler)
Fdp.ModuleHost.Scheduling
  (NetworkLifecycleSystemGroup, TogglableInputGroup*, TogglableSimulationGroup*)
  |
  v
Fdp.Toolkits
  (CombatModule, GroundKinematicsModule, etc.)
  |
  v
Hrot.Common
  (LegacySystemGroupAdapters, CgfInputGroupAdapter)  <-- DELETED in Phase 4
  |
  v
Hrot.CGF / Hrot.SimHost / Hrot.Editor
  (composition roots, application layer)
```

Items marked `*` are new. Items marked DELETED are removed in Phase 4.

---

## 7. Out of Scope

The following items are explicitly out of scope for this workstream:

- Changing the `AutonomousPerceptionModule` or any toolkit that already uses `IEcsModuleSystem` natively. These are already in the target state.
- Changing the recording format or playback mechanics.
- Adding DDS ingress toggling to the replication module (this would be a separate workstream if needed; current Plan A recording makes it unnecessary since the replaying node does not receive conflicting live data on the same ECS state).
- Moving any synchronous system to background execution (this is a separate performance workstream).
- The IG subsystem (already uses `ListenerRecordReplayController`; no changes needed).
- The ExCon subsystem (uses a similar listener pattern; no changes needed).

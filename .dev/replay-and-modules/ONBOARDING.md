# Onboarding: Replay Isolation and Modern Module System

This document explains the "why" behind this workstream for developers who join after the design was finalized. Read DESIGN.md for the full specification. Read TASK-DETAIL.md for what to implement. Read TASK-TRACKER.md for current progress.

---

## Why This Workstream Exists

There are two problems being solved together because the fix for one is the foundation for the other.

### Problem 1: Replay Does Not Actually Stop Simulation

When a cluster node transitions into `RunningReplay` mode, the intent is:
1. The `PlaybackTickSystem` reads historical ECS state from a recording file and writes it into the entity repository frame-by-frame.
2. All simulation systems (AI, kinematics, combat) should stop running so they do not overwrite the restored historical data.
3. Egress systems keep running so the IG nodes can receive the historical data over DDS and render it.

The current code does not achieve step 2. There are three concrete bugs:

**Bug A (SimHostApp):** `SimHostApp.OnLoad` creates `var simulationSystemGroup = new SimulationSystemGroup()` and immediately passes it to the replay handler — but no simulation systems are ever added to it. The actual simulation systems run in a separate `_kernelGroup` that the replay handler knows nothing about. Disabling the empty group does nothing.

**Bug B (Input phase):** Nothing ever disables the input-phase systems during replay. The `CycloneIngressSystem`, `DoctrineIngressSystem`, `FireProcessingSystem`, `PersonalRouteAuthoringSystem`, and others keep running, injecting live commands and live network data into the world on top of the historical state.

**Bug C (CgfSubsystem):** The `ReferenceReplayLoadHandler` registered in `CgfSubsystem` is passed `simGroup: null, lifecycleGroup: null`. Even if the handler logic were correct, it would toggle nothing on the CGF node.

### Problem 2: Legacy Module System Is Still in Use

The engine has two module system generations:

**Legacy** (`ComponentSystem` + `SystemGroup`): Systems extend `ComponentSystem` and declare `protected override void OnUpdate()`. Groups (`InputSystemGroup`, `SimulationSystemGroup`, etc.) sort systems via topological sort on `[UpdateBefore]`/`[UpdateAfter]` attributes and execute them. This design cannot integrate cleanly with the modern kernel's profiling, scheduling, and RCU execution.

**Modern** (`IEcsModuleSystem` + `ModuleHostKernel`): Systems implement `IEcsModuleSystem` and declare `public void Execute(ISimulationView view, float deltaTime)`. They declare their phase with `[UpdateInPhase(SystemPhase.X)]`. The `ModuleHostKernel` reads these attributes, builds a topological execution graph, and executes everything with full profiling support.

All toolkit systems (CycloneEgressSystem, PlaybackTickSystem, AutonomousPerceptionModule, etc.) are already in the modern style. All game systems (CombatModule, GroundKinematicsModule, MissionControlModule, etc.) are still in the legacy style.

The legacy systems are bridged into the modern kernel via adapter classes in `Hrot.Common.Infrastructure`: `CgfInputGroupAdapter` and `SimulationGroupModule`. These adapters are what cause Bug A, Bug B, and Bug C above — they create a disconnect between how systems are registered and what the replay handler can toggle.

---

## The Architecture After This Workstream

### Togglable Groups Replace Legacy Groups

The existing `NetworkLifecycleSystemGroup` (in `Fdp.ModuleHost.Scheduling`) is already the correct model. It wraps an array of `IEcsModuleSystem` instances and exposes a `bool Enabled` flag. When disabled, none of the inner systems execute.

Three new classes follow this exact pattern:

- **`TogglableInputGroup`** (`[UpdateInPhase(SystemPhase.Input)]`): wraps all input-phase game logic systems.
- **`TogglableSimulationGroup`** (`[UpdateInPhase(SystemPhase.Simulation)]`): wraps all simulation-phase game logic systems.
- **`TogglablePostSimulationGroup`** (`[UpdateInPhase(SystemPhase.PostSimulation)]`): wraps physics-integration systems (`BallisticsSystem`, `LinearKinematicsSystem`, `CarKinematicsSystem`). These must be disabled during replay because they integrate velocity into `SimTransform` position each frame — if they run after `PlaybackTickSystem` has restored historical ECS state they will advance positions beyond the recorded values, corrupting the replay.

All three must implement `ISystemGroup` (not just `IEcsModuleSystem`). `SystemScheduler.ExecuteSystem` (line 92 in `SystemScheduler.cs`) checks `if (system is ISystemGroup group)` and calls `ExecuteGroup` which profiles each inner system individually in the `ArchitectureDiagnosticsWindow`. Implementing only `IEcsModuleSystem` would lose this per-system profiling.

The `ReferenceReplayLoadHandler` acquires references to all three wrappers (plus the existing `NetworkLifecycleSystemGroup`) and flips their `Enabled` flags during state transitions. It does not need to know what systems are inside.

### Systems Convert to IEcsModuleSystem

Every system that currently extends `ComponentSystem` is changed to implement `IEcsModuleSystem`. The mechanical change is straightforward:

```csharp
// Before:
public class CarKinematicsSystem : ComponentSystem
{
    protected override void OnUpdate()
    {
        World.Query().With<SimTransform>().Each((entity, ref SimTransform t) => {
            ...
        });
    }
}

// After:
[UpdateInPhase(SystemPhase.PostSimulation)]
public class CarKinematicsSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        view.Query().With<SimTransform>().Each((entity, ref SimTransform t) => {
            ...
        });
    }
}
```

For systems that need direct world mutation (creating entities, adding/removing components), use the EntityRepository downcast — but **throw**, do not silently return:

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class GenesisMaterializationSystem : IEcsModuleSystem
{
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(GenesisMaterializationSystem)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}). " +
                "Do not schedule this system on a background thread.");
        var entity = repo.CreateEntity();
        ...
    }
}
```

A silent `return` would hide a scheduling misconfiguration indefinitely — the system would silently do nothing without anyone noticing. Throwing immediately surfaces the problem in logs and the `ModuleHostKernel`'s circuit breaker.

### Composition Roots Simplify

Before: `SimHostCoreLogicPack` had to be called with three `SystemGroup` arguments:
```csharp
_simCorePack.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup);
```

After: `SimHostCoreLogicPack` exposes three `IReadOnlyList<IEcsModuleSystem>` properties (`InputSystems`, `SimulationSystems`, `PostSimulationSystems`) instead of registering systems directly. `CgfLogicPack` exposes `InputSystems` and `SimulationSystems` with the same pattern:
```csharp
var toggleInput   = new TogglableInputGroup("SimHostInput",   _simCorePack.InputSystems);
var toggleSim     = new TogglableSimulationGroup("SimHostSim", _simCorePack.SimulationSystems);
var togglePostSim = new TogglablePostSimulationGroup("SimHostPostSim", _simCorePack.PostSimulationSystems);
_kernel.RegisterGlobalSystem(toggleInput);
_kernel.RegisterGlobalSystem(toggleSim);
_kernel.RegisterGlobalSystem(togglePostSim);
_simCorePack.RegisterSystems(kernelRegistry); // always-on systems (perception, diagnostics)
```

**Why arrays and not direct `RegisterSystem` calls?** `SystemScheduler` throws if the same system instance is registered twice. If `SimHostCoreLogicPack` called `registry.RegisterSystem(new BallisticsSystem(...))` AND `SimHostApp` also wrapped `BallisticsSystem` in a `TogglablePostSimulationGroup` (also registered with the kernel), the scheduler would reject the duplicate. The fix: `SimHostCoreLogicPack` does not register toggled systems at all — it returns them as arrays for the application layer (`SimHostApp`) to pack into togglable wrappers.

The `EditorSubsystem` does NOT need togglable groups (no replay in the editor). It reads the same array properties from both packs and registers each system directly:
```csharp
// CgfLogicPack arrays
foreach (var sys in _cgfLogicPack.InputSystems)      kernelRegistry.RegisterSystem(sys);
foreach (var sys in _cgfLogicPack.SimulationSystems) kernelRegistry.RegisterSystem(sys);
// SimHostCoreLogicPack arrays
foreach (var sys in _simCorePack.InputSystems)          kernelRegistry.RegisterSystem(sys);
foreach (var sys in _simCorePack.SimulationSystems)     kernelRegistry.RegisterSystem(sys);
foreach (var sys in _simCorePack.PostSimulationSystems) kernelRegistry.RegisterSystem(sys);
_simCorePack.RegisterSystems(kernelRegistry); // always-on systems
```

### What Keeps Running During Replay

| What | Phase | During Replay |
|------|-------|---------------|
| `TogglableInputGroup` (game logic input) | Input | **Stopped** |
| `TogglableSimulationGroup` (AI, kinematics, combat) | Simulation | **Stopped** |
| `NetworkLifecycleSystemGroup` (ghost lifecycle) | Simulation/Export | **Stopped** |
| `TogglablePostSimulationGroup` (physics: Ballistics, LinearKin, CarKin) | PostSimulation | **Stopped** |
| `PlaybackTickSystem` | PostSimulation | **Running** (drives replay frames) |
| `RecorderTickSystem` | PostSimulation | **Not registered** (`RecordingModule` is mutually exclusive with `ReplayModule` — uninstalled before replay starts) |
| `CycloneEgressSystem` | Export | **Running** (IGs receive historical state over DDS) |
| `SmartEgressSystem` | Export | **Running** (same) |
| `OwnershipEgressSystem` | Export | **Running** (same) |

The IG nodes do not replay anything. They receive everything from the network just as in live mode. Brain/Muscle nodes replay historical ECS state from disk and broadcast it via DDS. IG nodes render it. No changes are needed on the IG side.

---

## Key Files to Know

**New files created by this workstream:**
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableSimulationGroup.cs`
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableInputGroup.cs`
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs`

**Files significantly changed:**
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` — now accepts all three new togglable group types instead of legacy `SimulationSystemGroup`.
- `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs` — updated parameter types.
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` — `_kernelGroup` removed, all three togglable groups wired.
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` — `CgfSimGroupModule` removed, togglable groups wired, `simGroup: null` bug fixed.
- `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` — `RegisterSystems(ISystemRegistry)` is now real; old overloads deleted.
- `Hrot/Subsystems/Hrot.SimHost/Modules/SimHostCoreLogicPack.cs` — exposes `IReadOnlyList<IEcsModuleSystem>` properties (`InputSystems`, `SimulationSystems`, `PostSimulationSystems`); legacy overloads deleted.
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs` — all adapters removed; uses `ISystemRegistry` directly.
- `Hrot/Subsystems/Hrot.Editor/Systems/EditorSystemsModule.cs` — `SystemGroup` removed; registers editor systems via `ISystemRegistry`.
- All sub-module files (`CombatModule.cs`, `GroundKinematicsModule.cs`, `MissionControlModule.cs`, etc.) — now implement `ISystemRegistry`-based registration with phase-split array exposure.

**Files deleted:**
- `FDP/Engine/Fdp.Core/ComponentSystem.cs`
- `FDP/Engine/Fdp.Core/SystemGroup.cs`
- `FDP/Engine/Fdp.Core/StandardSystemGroups.cs`
- `Hrot/Engine/Hrot.Common/Infrastructure/CgfInputGroupAdapter.cs`
- `Hrot/Engine/Hrot.Common/Infrastructure/LegacySystemGroupAdapters.cs`

**Reference model for the new pattern (already exists, study this):**
- `FDP/Engine/Fdp.ModuleHost/Scheduling/NetworkLifecycleSystemGroup.cs` — model for TogglableInputGroup/TogglableSimulationGroup.
- `FDP/Toolkits/Fdp.Toolkits/Perception/Modules/AutonomousPerceptionModule.cs` — example of a native `IEcsModule` with background execution.
- `FDP/Toolkits/Fdp.Toolkits/Replay/RecorderTickSystem.cs` — example of a native `IEcsModuleSystem` with `[UpdateInPhase(SystemPhase.PostSimulation)]`.

---

## The SimHostApp Bug Explained in Detail

`SimHostApp.OnLoad` (around line 360) has this code:

```csharp
// GhostCreationSystem and NetworkLifecycleGroup come from the replication module.
var ghostCreationSystem   = replicationModule.GhostCreationSystem;
var simulationSystemGroup = new SimulationSystemGroup();   // <-- EMPTY, no systems added
var networkLifecycleGroup = replicationModule.NetworkLifecycleGroup;

var bootstrapper = new NodeBootstrapper(_networkFactory);
_clusterSlave = bootstrapper.BuildOrchestration(
    ...
    simGroup: simulationSystemGroup,    // <-- passed to replay handler (but empty!)
    lifecycleGroup: networkLifecycleGroup,
    ...);

_kernelGroup = new SystemGroup();
_kernelGroup.Create(_world);
_simCorePack!.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup); // <-- actual systems here
```

The actual simulation systems go into `_kernelGroup`, which is run via `_kernelGroup.Run()` in `OnUpdate`. The replay handler only knows about `simulationSystemGroup` which is empty. This workstream fixes this by removing `_kernelGroup` entirely and placing all simulation systems into a properly-wired `TogglableSimulationGroup` that the replay handler can toggle.

---

## The Phase Order Contract

Before this workstream, execution order was enforced by the order in which `SystemGroup.AddSystem()` was called in each composition root. This is fragile and hard to verify.

After this workstream, execution order is enforced by `[UpdateInPhase]` attributes on each system class. The `SystemScheduler` inside `ModuleHostKernel` reads these attributes and builds the correct topological execution graph. The order is:

1. `SystemPhase.Input` (value 1) — input-phase game systems inside `TogglableInputGroup`
2. `SystemPhase.BeforeSync` (value 2) — any BeforeSync systems
3. `SystemPhase.Simulation` (value 10) — simulation-phase game systems inside `TogglableSimulationGroup`
4. `SystemPhase.PostSimulation` (value 20) — post-simulation systems (PlaybackTickSystem, RecorderTickSystem, BallisticsSystem, LinearKinematicsSystem, CarKinematicsSystem)
5. `SystemPhase.Export` (value 40) — egress systems (CycloneEgressSystem, SmartEgressSystem, OwnershipEgressSystem)

Intra-phase ordering within a single phase is still controlled by `[UpdateBefore]` and `[UpdateAfter]` attributes on individual systems.

---

## Deep Architectural Risks Fixed in Phase 4

The three-group toggling fix (Phases 1-3) stops most replay corruption. However, four deeper issues remain that must be fixed in Phase 4 (T-RMF-20 through T-RMF-23).

### GhostDestructionSystem and DeferredTakeoverSystem Run During Replay

`NetworkLifecycleSystemGroup` is correctly disabled during replay. But two related systems are registered **outside** the group in `NedReplicationModule.RegisterSystems`:
- `GhostDestructionSystem` (`[UpdateInPhase(SystemPhase.PostSimulation)]`, line 312) — sends DDS `DISPOSE` when ghost entities disappear.
- `DeferredTakeoverSystem` (`[UpdateInPhase(SystemPhase.BeforeSync)]`, line 333) — sends network ownership grants.

During replay, historical entities appear and disappear on every frame. These two systems see the historical churn and send real DDS traffic — corrupting IG state. Fix: move both systems into a new `NetworkIngressSystemGroup` (T-RMF-20) and toggle it with `ReferenceReplayLoadHandler`.

### GlobalTime Singleton Tug-of-War

`ModuleHostKernel.Update()` writes the live `TimeController`'s timestamp as the `GlobalTime` ECS singleton **before any system runs**, at the top of every frame. `PlaybackSystem` (`[UpdateInPhase(SystemPhase.PostSimulation)]`) then overwrites it with the historical time. The result: Input and Simulation phase systems see live time, Export phase systems see historical time. Time-dependent logic (TTL-based caches, velocity integration, heartbeats) is inconsistent within a single frame. Fix: expose `SuspendGlobalTimePush()` on the kernel and call it from `ReferenceReplayLoadHandler` during replay (T-RMF-21).

### SmartEgressSystem 10-Second Lag on Timeline Seek

`SmartEgressSystem` only re-publishes low-frequency declarative data when `SmartEgressUtil.MarkDirty()` is called. During replay, simulation systems are disabled so nothing calls `MarkDirty`. The fallback heartbeat is 600 ticks (~10 seconds). When an operator seeks the timeline, `PlaybackTickSystem` blits raw chunk data into `NativeChunkTable`, bypassing component setters entirely. The IG receives stale data for up to 10 seconds. Fix: after a `SeekToFrame`, force-flag all active entities dirty in `EgressPublicationState` so `SmartEgressSystem` does a full sync on the very next Export frame (T-RMF-22).

### CycloneNetworkCleanupSystem Scrub Flood on Seek

`CycloneNetworkCleanupSystem` tracks all live entities in a `_trackedEntities` dictionary. On each Export frame it disposes entries for entities that are no longer alive. On a timeline seek, the entire ECS world is wiped and rebuilt. Every previously-tracked entity appears dead, triggering a mass `DISPOSE` flood over DDS. The IG tears down all ghost entities simultaneously, then has to rebuild them all on the next frame. Fix: expose `ResetTracking()` and call it from `PlaybackTickSystem` immediately after a seek, before the Export phase runs (T-RMF-23).

---

## Working on a Task: Quick Checklist

When converting a system from `ComponentSystem` to `IEcsModuleSystem`:

1. Change class declaration: `: ComponentSystem` → `: IEcsModuleSystem`
2. Rename method: `protected override void OnUpdate()` → `public void Execute(ISimulationView view, float deltaTime)`
3. Replace `World.` with `view.`
4. Replace `DeltaTime` with `deltaTime`
5. Add `[UpdateInPhase(SystemPhase.X)]` where X matches the group the system previously lived in
6. If the system calls `World.CreateEntity()`, `World.AddComponent()`, `World.RemoveComponent()`, or similar structural mutations: add the downcast at the top and **throw** if it fails (not silent return): `if (view is not EntityRepository repo) throw new InvalidOperationException(...);`
7. Update `using` directives: remove `Fdp.Core` references to `ComponentSystem`; add `using Fdp.ModuleHost.Abstractions;` for the interface
8. Update the module's `RegisterSystems(ISystemRegistry)` to call `registry.RegisterSystem(new ThisSystem(...))` instead of `systemGroup.AddSystem(new ThisSystem(...))`

When done, run the solution build. If `ComponentSystem` or `SystemGroup` references still appear as errors, trace them back to the system class and repeat the conversion.

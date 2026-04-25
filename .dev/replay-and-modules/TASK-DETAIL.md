# Task Detail: Replay Isolation and Modern Module System

All tasks use the prefix `RMF` (Replay / Modules / Foundation).

> **Revision note:** Feedback round incorporated (1) `TogglablePostSimulationGroup` for physics systems, (2) `ISystemGroup` interface on all three togglable groups for per-system profiling, (3) throw-on-downcast instead of silent return, (4) `SimHostCoreLogicPack` exposes arrays rather than registering directly, (5) `EditorSystemsModule` refactoring detail, (6) four deep architectural fixes (GhostDestruction/DeferredTakeover, GlobalTime tug-of-war, SmartEgress seek lag, CycloneNetworkCleanup scrub flood). Tasks renumbered; total is 27.

---

## PHASE 1 — TOGGLABLE GROUP FOUNDATION

### T-RMF-01 — Create `TogglableSimulationGroup`

**Project:** `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableSimulationGroup.cs`

**Goal:** Create the modern togglable wrapper for simulation-phase systems. Must implement `ISystemGroup` (not just `IEcsModuleSystem`) so the `SystemScheduler` can unwrap and profile each inner system individually in the `ArchitectureDiagnosticsWindow`.

**Implementation:**
```csharp
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Togglable wrapper for simulation-phase systems.
    /// Implements <see cref="ISystemGroup"/> so <c>SystemScheduler</c> can profile
    /// each inner system individually.
    /// When <see cref="Enabled"/> is false the inner systems are not executed.
    /// The replay handler flips this flag during PrepareReplay / FinalizeReplay / PrepareLive.
    /// </summary>
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
}
```

**Success Criteria:**
- File exists and compiles.
- Implements `ISystemGroup` (which extends `IEcsModuleSystem`).
- Has `Name` property and `GetSystems()` method.
- `[UpdateInPhase(SystemPhase.Simulation)]` is present.

---

### T-RMF-02 — Create `TogglableInputGroup`

**Project:** `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableInputGroup.cs`

**Goal:** Create the modern togglable wrapper for input-phase systems. Must implement `ISystemGroup` for profiling.

**Implementation:**
```csharp
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Togglable wrapper for input-phase systems.
    /// Implements <see cref="ISystemGroup"/> so <c>SystemScheduler</c> can profile
    /// each inner system individually.
    /// When <see cref="Enabled"/> is false, all inner systems are skipped.
    /// The replay handler disables this group during PrepareReplay to prevent live
    /// operator commands and network ingress from corrupting historical ECS state.
    /// </summary>
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
}
```

**Success Criteria:**
- File exists and compiles.
- Implements `ISystemGroup`.
- `[UpdateInPhase(SystemPhase.Input)]` is present.

---

### T-RMF-03 — Create `TogglablePostSimulationGroup`

**Project:** `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs`

**Goal:** Create the togglable wrapper for PostSimulation-phase physics systems (`BallisticsSystem`, `LinearKinematicsSystem`, `CarKinematicsSystem`). These must be disabled during replay because they integrate velocity into `SimTransform` position each frame; if they run after `PlaybackTickSystem` restores historical ECS state they will advance positions past the recorded values, corrupting the replay.

**Implementation:**
```csharp
using System.Collections.Generic;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.ModuleHost.Scheduling
{
    /// <summary>
    /// Togglable wrapper for post-simulation physics integration systems.
    /// Implements <see cref="ISystemGroup"/> so <c>SystemScheduler</c> can profile
    /// each inner system individually.
    /// Must be disabled during replay to prevent kinematic integration from
    /// overwriting restored historical <c>SimTransform</c> positions.
    /// </summary>
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
}
```

**Note:** `PlaybackTickSystem` is also `[UpdateInPhase(SystemPhase.PostSimulation)]` and must NOT be placed inside this wrapper — it must always run. It is registered directly with the kernel (by `ReplayModule`) outside of this group.

**Success Criteria:**
- File exists and compiles.
- Implements `ISystemGroup`.
- `[UpdateInPhase(SystemPhase.PostSimulation)]` is present.
- XML doc explains why physics integration must be disabled during replay.

---

### T-RMF-04 — Update `ReferenceReplayLoadHandler` for New Group Types

**File:** `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`

**Goal:** Replace `SimulationSystemGroup?` with `TogglableSimulationGroup?`, add `TogglableInputGroup?` and `TogglablePostSimulationGroup?`.

**Current State:**
The constructor accepts `SimulationSystemGroup? simGroup` (legacy type from `Fdp.Core`) and `NetworkLifecycleSystemGroup? lifecycleGroup`. The `SetSystemsEnabled` helper toggles `_simGroup.Enabled` and `_lifecycleGroup.Enabled`.

**Changes:**

1. Replace/add field declarations:
   - Remove: `private readonly SimulationSystemGroup? _simGroup;`
   - Add: `private readonly TogglableInputGroup? _inputGroup;`
   - Add: `private readonly TogglableSimulationGroup? _simGroup;`
   - Add: `private readonly TogglablePostSimulationGroup? _postSimGroup;`

2. Update constructor signature and body to accept and store all three new types.

3. Update `SetSystemsEnabled` to toggle all groups:
   ```csharp
   private void SetSystemsEnabled(bool enabled)
   {
       if (_inputGroup     != null) _inputGroup.Enabled     = enabled;
       if (_simGroup       != null) _simGroup.Enabled       = enabled;
       if (_postSimGroup   != null) _postSimGroup.Enabled   = enabled;
       if (_lifecycleGroup != null) _lifecycleGroup.Enabled = enabled;
   }
   ```

4. Remove `using Fdp.Core;` if `SimulationSystemGroup` was the only use. Add `using Fdp.ModuleHost.Scheduling;`.

5. Update XML doc comments.

**Tests to update:** `ReplayLoadClusterOpHandlerTests.cs`, `LiveFromReplayTests.cs` — replace `new SimulationSystemGroup()` with `new TogglableSimulationGroup("test")`.

**Success Criteria:**
- Handler stores and toggles all four groups.
- `SetSystemsEnabled` covers `_inputGroup`, `_simGroup`, `_postSimGroup`, `_lifecycleGroup`.
- Legacy `SimulationSystemGroup` type removed from field declarations.

---

### T-RMF-05 — Update `NodeBootstrapper.BuildOrchestration` Signature

**File:** `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`

**Goal:** Replace `SimulationSystemGroup?` with `TogglableSimulationGroup?` and add `TogglableInputGroup?` and `TogglablePostSimulationGroup?` parameters.

**Changes:**

1. Replace parameter: `Fdp.Core.SimulationSystemGroup? simGroup = null` → `Fdp.ModuleHost.Scheduling.TogglableSimulationGroup? simGroup = null`
2. Add parameter: `Fdp.ModuleHost.Scheduling.TogglableInputGroup? inputGroup = null`
3. Add parameter: `Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup? postSimGroup = null`

4. Update the replay handler construction:
   ```csharp
   if (controller != null && (inputGroup != null || simGroup != null || postSimGroup != null || lifecycleGroup != null))
   {
       clusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
           controller, inputGroup, simGroup, postSimGroup, lifecycleGroup, bypassToggle, localTempRoot));
   }
   ```

5. Update `using` directives and XML doc comments.

**Tests to update:** `NodeBootstrapperReplayTests.cs` — use `TogglableSimulationGroup` and new types instead of legacy types.

**Success Criteria:**
- `BuildOrchestration` accepts all three new togglable group types.
- Guard condition covers all three new parameters.

---

## PHASE 2 — SYSTEM MIGRATION

### T-RMF-06 — Convert `CombatModule` Systems to `IEcsModuleSystem`

**Project:** `Hrot/Subsystems/Hrot.SimHost/Modules/CombatModule.cs` and system files.

**Goal:** Convert all four systems in `CombatModule` from `ComponentSystem` to `IEcsModuleSystem`. Update the module's `RegisterSystems` to use `ISystemRegistry`.

**Systems to convert:**

| System | Previous Group | New Phase |
|--------|---------------|-----------|
| `FireProcessingSystem` | inputGroup | `SystemPhase.Input` |
| `RaycastSolverSystem` | inputGroup | `SystemPhase.Input` |
| `HitResolutionSystem` | inputGroup | `SystemPhase.Input` |
| `BallisticsSystem` | postSimGroup | `SystemPhase.PostSimulation` |

**Change for each system class:**
- Replace `ComponentSystem` base with `IEcsModuleSystem` interface.
- Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`.
- Replace `World.Query(...)` with `view.Query(...)`.
- Replace `DeltaTime` with `deltaTime` parameter.
- Add `[UpdateInPhase(SystemPhase.X)]` where X is the phase from the table above.
- Add `using Fdp.ModuleHost.Abstractions;` and `using Fdp.Core;` (for ISimulationView).
- If the system needs direct world mutation: throw instead of silently returning (see T-RMF-12 pattern).

**Change for `CombatModule.RegisterSystems`:**
- The existing `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)` method is deleted.
- The module exposes three array properties: `InputSystems`, `SimulationSystems`, `PostSimulationSystems` (used by `SimHostCoreLogicPack` in T-RMF-13 to build togglable wrappers).

**Success Criteria:**
- All four systems implement `IEcsModuleSystem` with correct `[UpdateInPhase]` attributes.
- Module exposes phase-split system arrays.
- No reference to `ComponentSystem` remains in this module or its systems.
- Solution still compiles (callers updated in T-RMF-13).

---

### T-RMF-07 — Convert `GroundKinematicsModule` Systems to `IEcsModuleSystem`

**Project:** `Hrot/Subsystems/Hrot.SimHost/Modules/GroundKinematicsModule.cs` and system files.

**Goal:** Convert all systems in `GroundKinematicsModule` from `ComponentSystem` to `IEcsModuleSystem`.

**Systems to convert:**

| System | New Phase |
|--------|-----------|
| `SpatialHashSystem` | `SystemPhase.Simulation` |
| `FormationTargetSystem` | `SystemPhase.Simulation` |
| `VehicleCommandSystem` | `SystemPhase.Simulation` |
| `NavigationExecutionSystem` | `SystemPhase.Simulation` |
| `CarKinematicsSystem` | `SystemPhase.PostSimulation` |
| `LinearKinematicsSystem` | `SystemPhase.PostSimulation` |

Apply the same conversion rules as T-RMF-06. Expose phase-split system arrays. Delete legacy `RegisterSystems(SystemGroup)` overloads.

**Note:** `CarKinematicsSystem` and `LinearKinematicsSystem` go into `TogglablePostSimulationGroup` (disabled during replay). Verify if they need the `EntityRepository` downcast for position mutation.

**Success Criteria:** Same as T-RMF-06 for this module.

---

### T-RMF-08 — Convert Navigation Bridge Systems to `IEcsModuleSystem`

**Project:** Various toolkit/Hrot projects containing navigation bridge systems.

**Goal:** Convert navigation-related systems that run on input and simulation phases.

**Systems to convert:**

| System | New Phase | Note |
|--------|-----------|------|
| `PersonalRouteAuthoringSystem` | `SystemPhase.Input` | Inputs operator intent into navigation |
| `NavigationIntentBridgeSystem` | `SystemPhase.Simulation` | Bridges intent into navigation state |
| `RouteTrajectorySyncSystem` | `SystemPhase.Simulation` | Syncs route/trajectory |

Apply standard conversion rules. These systems are used in both SimHostCoreLogicPack and possibly CgfLogicPack. Update callers in T-RMF-13 and T-RMF-14.

**Success Criteria:** All three systems implement `IEcsModuleSystem` with correct phase attributes.

---

### T-RMF-09 — Convert `MissionControlModule` Systems to `IEcsModuleSystem`

**Project:** CGF toolkit project containing `MissionControlModule`.

**Goal:** Convert all systems in `MissionControlModule` from `ComponentSystem` to `IEcsModuleSystem`.

**Systems to convert:**

| System | New Phase |
|--------|-----------|
| `DoctrineIngressSystem` | `SystemPhase.Input` |
| `MissionDirectorSystem` | `SystemPhase.Simulation` |
| Any other systems in this module | Per their previous group assignment |

Delete all `RegisterSystems(SystemGroup)` overloads. `RegisterSystems(ISystemRegistry registry)` becomes the real implementation.

**Success Criteria:** Module uses `ISystemRegistry` only. All systems have `[UpdateInPhase]` attributes.

---

### T-RMF-10 — Convert `CognitiveRuntimeModule` and `ActionDispatchModule` Systems

**Project:** CGF toolkit project.

**Goal:** Convert all systems in both modules from `ComponentSystem` to `IEcsModuleSystem`.

**Systems to convert (CognitiveRuntimeModule):**

| System | New Phase |
|--------|-----------|
| `BTreeTickSystem` | `SystemPhase.Simulation` |
| `HsmTickSystem` (multiple instances) | `SystemPhase.Simulation` |
| `ChannelArbitrationSystem` | `SystemPhase.Simulation` |
| `HsmDamageBridgeSystem` | `SystemPhase.Simulation` |

**Systems to convert (ActionDispatchModule):**

| System | New Phase |
|--------|-----------|
| `LocomotionDispatcherSystem` | `SystemPhase.Simulation` |
| `WeaponDispatcherSystem` | `SystemPhase.Simulation` |
| Any others in this module | `SystemPhase.Simulation` |

Delete all `RegisterSystems(SystemGroup)` overloads from both modules. Activate `RegisterSystems(ISystemRegistry)`.

**Success Criteria:** Both modules use `ISystemRegistry` only. All systems have `[UpdateInPhase]` attributes.

---

### T-RMF-11 — Convert Standalone CGF Systems and `DamageAssessmentModule`

**Project:** `Hrot.CGF` and related projects.

**Goal:** Convert standalone systems used in `CgfLogicPack` and `DamageAssessmentModule`.

**Standalone systems in `CgfLogicPack`:**

| System | New Phase | Note |
|--------|-----------|------|
| `MissionControlExecutionSystem` | `SystemPhase.Input` | Previously in inputGroup (two-group overload) |
| `MissionAdapterSystem` | `SystemPhase.Simulation` | Bridges MissionPlanQueue to DoctrineState |
| `HealthApplicationSystem` | `SystemPhase.Simulation` | Applies authoritative damage to Health |
| `CgfThreatEvaluationSystem` | `SystemPhase.Simulation` | Decays/boosts TargetMemory scores |
| `RouteContextSystem` | `SystemPhase.Simulation` | Writes danger level to BrainBlackboard |

**`DamageAssessmentModule` systems:** All go to `SystemPhase.Simulation` (or PostSim if they were in postSimGroup). Apply standard conversion rules. Delete `RegisterSystems(SystemGroup)` overloads.

**Success Criteria:** All standalone CGF systems implement `IEcsModuleSystem`. `DamageAssessmentModule` uses `ISystemRegistry`.

---

### T-RMF-12 — Convert `GenesisMaterializationSystem`

**Project:** `Hrot.SimHost`

**Goal:** Convert `GenesisMaterializationSystem` from `ComponentSystem` to `IEcsModuleSystem`. Use the throw-on-bad-view pattern rather than a silent return.

**This system requires direct world mutation** (it materializes entities). The correct pattern is to throw if the view is not an `EntityRepository` so that accidental background-thread scheduling is surfaced immediately:

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class GenesisMaterializationSystem : IEcsModuleSystem
{
    private readonly NetworkEntityMap _entityMap;

    public GenesisMaterializationSystem(NetworkEntityMap entityMap)
        => _entityMap = entityMap;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"{nameof(GenesisMaterializationSystem)} requires direct EntityRepository access " +
                $"and cannot run on a read-only snapshot ({view.GetType().Name}). " +
                "Do not schedule this system on a background thread.");
        // direct mutation using repo ...
    }
}
```

**Rationale for throw:** A silent `return` would hide a scheduling misconfiguration indefinitely. Throwing allows the `ModuleHostKernel`'s circuit breaker to catch and surface the error immediately in logs and the `ArchitectureDiagnosticsWindow`. Apply this same throw-pattern to every direct-mutation system converted in this workstream.

**Success Criteria:** System implements `IEcsModuleSystem`. Throws `InvalidOperationException` if view is not `EntityRepository`. Has `[UpdateInPhase]` attribute.

---

## PHASE 3 — COMPOSITION ROOTS AND APPLICATION WIRING

### T-RMF-13 — Rework `SimHostCoreLogicPack`: Expose System Arrays

**File:** `Hrot/Subsystems/Hrot.SimHost/Modules/SimHostCoreLogicPack.cs`

**Goal:** Replace the three-group overload with an API that lets `SimHostApp` pack systems into togglable groups WITHOUT causing double-registration. Registering the same system instance into the kernel directly AND also into a `TogglableSimulationGroup` (which is also registered with the kernel) would cause a `SystemScheduler` duplicate-registration exception. The fix is that `SimHostCoreLogicPack` does not call `registry.RegisterSystem` for the game-logic systems that need toggling; instead it exposes them as arrays that `SimHostApp` packs into the three togglable wrappers.

**Change:**

1. Delete `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup)` overload.
2. Add three read-only properties built at construction time:
   ```csharp
   /// <summary>Systems to wrap in TogglableInputGroup.</summary>
   public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

   /// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
   public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

   /// <summary>Systems to wrap in TogglablePostSimulationGroup (BallisticsSystem, LinearKinematicsSystem, CarKinematicsSystem).</summary>
   public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }
   ```
3. Keep `RegisterSystems(ISystemRegistry registry)` for any non-toggled always-on systems (e.g., perception, diagnostics).
4. Each array property is populated in the constructor with the appropriate sub-module systems in declaration order.

**Success Criteria:**
- Three-group legacy overload deleted.
- `InputSystems`, `SimulationSystems`, `PostSimulationSystems` properties exist and return non-null lists.
- `SimHostApp` (T-RMF-15) consumes these arrays.
- No system instance is registered twice.

---

### T-RMF-14 — Rework `CgfLogicPack`: Expose System Arrays

**File:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`

**Goal:** Adopt the same array-property pattern as `SimHostCoreLogicPack` (T-RMF-13) to prevent the double-registration trap. If `CgfLogicPack` called `registry.RegisterSystem()` directly AND `CgfSubsystem` also registered the same instances inside `TogglableInputGroup`/`TogglableSimulationGroup`, `SystemScheduler` would throw a duplicate-registration exception.

**Current State:**
- `RegisterSystems(ISystemRegistry registry)` is a no-op stub.
- `RegisterSystems(SystemGroup simGroup)` — 15 systems all in one group.
- `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` — split by phase.

**Change:**

1. Delete both old `SystemGroup` overloads.
2. Add two read-only properties built at construction time:
   ```csharp
   /// <summary>Systems to wrap in TogglableInputGroup.</summary>
   public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

   /// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
   public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }
   ```
3. Populate both properties in the constructor from the appropriate sub-module systems.
4. Remove `using Fdp.Core;` if `SystemGroup` was the only type used from that namespace.

**Note:** `CgfSubsystem` (T-RMF-16) reads `InputSystems` and `SimulationSystems` and packs them into togglable wrappers for replay isolation. `EditorSubsystem` (T-RMF-18) reads the same properties and calls `registry.RegisterSystem()` directly for each system (no toggling needed in the editor).

**Success Criteria:**
- Only `IReadOnlyList<IEcsModuleSystem> InputSystems` and `IReadOnlyList<IEcsModuleSystem> SimulationSystems` remain as the public surface.
- Both properties return non-null lists.
- Old `SystemGroup` overloads deleted.
- Solution compiles.

---

### T-RMF-15 — Update `SimHostApp`: Remove `_kernelGroup`, Wire Three Togglable Groups

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

**Goal:** Remove the legacy `_kernelGroup` `SystemGroup` and wire `TogglableInputGroup`, `TogglableSimulationGroup`, and `TogglablePostSimulationGroup` for replay isolation.

**Current State (verified):**
- Creates `var simulationSystemGroup = new SimulationSystemGroup()` (empty, passed to replay handler but useless).
- Creates `_kernelGroup = new SystemGroup()`.
- Calls `_simCorePack!.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup)`.
- Calls `_kernelGroup.Run()` in `OnUpdate`.

**Changes:**

1. Remove `private SystemGroup? _kernelGroup;` field.
2. Remove `var simulationSystemGroup = new SimulationSystemGroup()`.
3. Use the new `SimHostCoreLogicPack` API:
   ```csharp
   var toggleInput   = new TogglableInputGroup("SimHostInput",
       _simCorePack!.InputSystems);
   var toggleSim     = new TogglableSimulationGroup("SimHostSimulation",
       _simCorePack!.SimulationSystems);
   var togglePostSim = new TogglablePostSimulationGroup("SimHostPostSimulation",
       _simCorePack!.PostSimulationSystems);

   _kernel.RegisterGlobalSystem(toggleInput);
   _kernel.RegisterGlobalSystem(toggleSim);
   _kernel.RegisterGlobalSystem(togglePostSim);
   _simCorePack!.RegisterSystems(_kernelRegistry); // always-on systems (perception etc.)
   ```
4. Pass all three togglable groups to `BuildOrchestration`:
   ```csharp
   _clusterSlave = bootstrapper.BuildOrchestration(
       ...,
       inputGroup:   toggleInput,
       simGroup:     toggleSim,
       postSimGroup: togglePostSim,
       lifecycleGroup: networkLifecycleGroup,
       ...);
   ```
5. Remove `_kernelGroup?.Run();` from `OnUpdate`. Only `_kernel?.Update()` remains.

**Success Criteria:**
- `_kernelGroup` field removed.
- Three togglable groups correctly populated and passed to replay handler.
- Replay handler can toggle all physics integration and simulation systems.
- Solution compiles.

---

### T-RMF-16 — Update `CgfSubsystem`: Remove Legacy Groups, Fix Replay Bug

**File:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

**Goal:** Remove `_inputGroup`/`_simGroup` legacy `SystemGroup` fields, remove `CgfSimGroupModule` nested class, wire `TogglableInputGroup` and `TogglableSimulationGroup` for replay. Fix the `simGroup: null` bug.

**Current State (verified):**
- Private nested class `CgfSimGroupModule` wraps a `SystemGroup` and ticks it via `Tick`.
- Private fields `SystemGroup? _simGroup` and `SystemGroup? _inputGroup`.
- Passes `simGroup: null, lifecycleGroup: null` to `ReferenceReplayLoadHandler`.

**Changes:**

1. Delete the `CgfSimGroupModule` private nested class.
2. Remove `SystemGroup?` fields.
3. Call `cgfLogicPack.RegisterSystems(registry)` where `registry` is a wrapper that collects systems by phase for building togglable groups.
4. Wire `ReferenceReplayLoadHandler` with real togglable group references.
5. Remove `using Hrot.Common.Infrastructure;` if adapters were the only types used from that namespace.

**Success Criteria:**
- `CgfSimGroupModule` nested class removed.
- `CgfInputGroupAdapter` no longer instantiated.
- Replay handler receives real togglable group references (not null).
- Solution compiles.

---

### T-RMF-17 — Update `CgfApplication`: Remove Legacy Groups, Fix Replay Bug

**File:** `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`

**Goal:** Apply the same changes as T-RMF-16. `CgfApplication` also passes `simGroup: null, lifecycleGroup: null` to `ReferenceReplayLoadHandler`.

**Success Criteria:** Same as T-RMF-16.

---

### T-RMF-18 — Update `EditorSubsystem` and `EditorSystemsModule`: Remove All Adapter Usage

**Files:**
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- `Hrot/Subsystems/Hrot.Editor/Systems/EditorSystemsModule.cs` (or wherever `EditorCargoSystem` etc. live)

**Goal:** Remove all legacy adapter classes (`CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter`) from the Editor composition root. The Editor does not participate in replay, so no togglable groups are needed.

**EditorSubsystem changes:**
1. Remove all `SystemGroup` fields and local variables.
2. Remove `SimGroupModule` and `PostSimGroupModule` nested classes.
3. `EditorSubsystem` directly instantiates both `CgfLogicPack` and `SimHostCoreLogicPack`. After this task, read their array properties and register each system directly into the kernel:
   ```csharp
   // CgfLogicPack: register arrays directly (no toggling needed in the editor)
   foreach (var sys in _cgfLogicPack.InputSystems)      kernelRegistry.RegisterSystem(sys);
   foreach (var sys in _cgfLogicPack.SimulationSystems) kernelRegistry.RegisterSystem(sys);
   // SimHostCoreLogicPack: same pattern for all three phases
   foreach (var sys in _simCorePack.InputSystems)          kernelRegistry.RegisterSystem(sys);
   foreach (var sys in _simCorePack.SimulationSystems)     kernelRegistry.RegisterSystem(sys);
   foreach (var sys in _simCorePack.PostSimulationSystems) kernelRegistry.RegisterSystem(sys);
   _simCorePack.RegisterSystems(kernelRegistry); // always-on systems
   ```
4. Remove `using Hrot.Common.Infrastructure;` from usings.

**EditorSystemsModule changes:**
- `EditorSystemsModule` currently holds a `SystemGroup` and calls `_editorGroup.Run()` in `Tick`.
- After this task, `EditorSystemsModule` calls `registry.RegisterSystem(new EditorCargoSystem(...))` etc. for each editor-specific system. Its `Tick()` method becomes a no-op or the module is replaced by direct system registration.

**Success Criteria:**
- No references to `CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter` in either file.
- No `SystemGroup` fields remain.
- `EditorSystemsModule` does not hold or run a `SystemGroup`.
- Solution compiles.

---

### T-RMF-19 — Update `EditorHarness` and `SimHostInstance` Test Infrastructure

**Files:**
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`
- `Hrot/Subsystems/Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs`

**Goal:** Remove `SystemGroup` usage from test infrastructure harnesses.

**EditorHarness:** Remove local `SimGroupModule` and `PostSimGroupModule` nested classes. Use direct system registration.

**SimHostInstance:** Remove `_inputGroup`, `_simGroup`, `_postSimGroup` `SystemGroup` fields. Register systems directly via the updated `RegisterSystems(ISystemRegistry)` APIs.

**Success Criteria:**
- Neither harness references `SystemGroup`, `ComponentSystem`, or any legacy `StandardSystemGroups`.
- All harness-based tests still pass.

---

## PHASE 4 — DEEP REPLAY ARCHITECTURE FIXES

### T-RMF-20 — Move `GhostDestructionSystem` and `DeferredTakeoverSystem` Inside `NetworkLifecycleSystemGroup`

**Files:**
- `Hrot/Network/Hrot.Network.NED/Replication/NedReplicationModule.cs`
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` (if using explicit disable list instead)

**Goal:** Prevent stray DDS `DISPOSE` packets and network ownership grants from corrupting historical entities during replay.

**Current State (verified):**
- `GhostDestructionSystem` is `[UpdateInPhase(SystemPhase.PostSimulation)]`, registered outside `NetworkLifecycleSystemGroup`.
- `DeferredTakeoverSystem` is `[UpdateInPhase(SystemPhase.BeforeSync)]`, registered outside `NetworkLifecycleSystemGroup`.
- Both run freely during replay even though `NetworkLifecycleSystemGroup` is disabled.

**Fix — Move both systems into the existing `NetworkLifecycleSystemGroup`:**
1. Add `GhostDestructionSystem` and `DeferredTakeoverSystem` to `NetworkLifecycleSystemGroup`'s constructor as additional members of its internal system array.
2. Remove the two standalone `registry.RegisterSystem(new GhostDestructionSystem(...))` and `registry.RegisterSystem(new DeferredTakeoverSystem(...))` calls from `NedReplicationModule.RegisterSystems`.
3. No changes required to `ReferenceReplayLoadHandler` — it already disables `NetworkLifecycleSystemGroup` during replay, which now also disables these two systems.

This is the chosen approach because: (a) `NetworkLifecycleSystemGroup` is already successfully wired to the replay handler; (b) it avoids introducing a new wrapper class (`NetworkIngressSystemGroup`) and a new field in `ReferenceReplayLoadHandler`; (c) it is consistent with what `DESIGN.md` Section 2.1 specifies.

**Success Criteria:**
- Neither `GhostDestructionSystem` nor `DeferredTakeoverSystem` runs during replay.
- Both resume on `FinalizeReplay` or `PrepareLive` commit.
- Solution compiles and existing replication tests pass.

---

### T-RMF-21 — Fix `GlobalTime` Singleton Tug-of-War During Replay

**Files:**
- `FDP/Engine/Fdp.ModuleHost/Kernel/ModuleHostKernel.cs`
- `FDP/Toolkits/Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs`

**Goal:** Prevent the `ModuleHostKernel`'s live `TimeController` from overwriting the historical `GlobalTime` singleton that `PlaybackSystem` has just restored.

**Current State:** `ModuleHostKernel.Update()` writes the live `TimeController`'s current time as the `GlobalTime` singleton in the ECS world at the start of every frame, before any system runs. `PlaybackSystem` (PostSimulation) overwrites it with the historical value. Systems in Input and Simulation phases see live time; systems in Export phase see historical time. This is inconsistent.

**Fix:**
1. Expose a method on `ModuleHostKernel` (or `TimeController`) such as `SuspendGlobalTimePush()` / `ResumeGlobalTimePush()`.
2. `ReferenceReplayLoadHandler` calls `SuspendGlobalTimePush()` in `PrepareReplay` (when `enabled = false`) and `ResumeGlobalTimePush()` in `FinalizeReplay` / `PrepareLive` (when `enabled = true`).
3. When suspended, `ModuleHostKernel.Update()` skips the `SetSingletonUnmanaged(globalTime)` call, leaving `PlaybackSystem` as the sole writer of `GlobalTime`.

**Success Criteria:**
- During replay, all phases see the same historical `GlobalTime` written by `PlaybackSystem`.
- After replay ends, live time resumes on the first live frame.
- Existing time-related tests pass.

---

### T-RMF-22 — Fix `SmartEgressSystem` 10-Second Lag on Timeline Seek

**Files:**
- `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Replication/Systems/SmartEgressSystem.cs`

**Goal:** When an operator seeks the replay timeline, the IG node should snap to the new position within one frame, not after a 10-second heartbeat cycle.

**Root Cause:** `SmartEgressSystem` only publishes low-frequency declarative data when `SmartEgressUtil.MarkDirty()` is called. During replay, simulation systems (which call `MarkDirty`) are disabled. `SmartEgressSystem` falls back to its 600-tick heartbeat. On a timeline seek (`SeekToFrame`), `PlaybackSystem` blits raw ECS chunk data directly into `NativeChunkTable`, bypassing component setters and `MarkDirty` calls entirely.

**Fix:**
1. `PlaybackTickSystem.Execute` detects when it has executed a `SeekToFrame` operation (as opposed to a sequential `AdvanceFrame`).
2. After a seek, it iterates all active entities with `EgressPublicationState` and calls `SmartEgressUtil.ForceMarkAllDirty(repo)` or equivalent to flag every descriptor as needing immediate publication.
3. `SmartEgressSystem` then publishes the full state on the very next Export frame.

**Note:** If `SmartEgressUtil.ForceMarkAllDirty` does not exist, it must be added as a static helper method in `Fdp.Toolkits.Replication`.

**Success Criteria:**
- After a timeline seek during replay, the IG node displays the correct historical state within one frame.
- Sequential frame-advance playback is unaffected (no spurious dirty-flagging).

---

### T-RMF-23 — Fix `CycloneNetworkCleanupSystem` Scrub Flood on Seek

**Files:**
- `FDP/Network/Fdp.Network.Cyclone/Systems/CycloneNetworkCleanupSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Replay/PlaybackTickSystem.cs`

**Goal:** Prevent a mass DDS `DISPOSE` broadcast when the replay timeline is seeked, which would tear down all IG ghost entities and cause a massive network spike.

**Root Cause:** `CycloneNetworkCleanupSystem` maintains a `_trackedEntities` dictionary mapping `NetworkIdentity.Value -> Entity`. On each Export frame it checks if tracked entities are still alive. On a seek, the entire world is wiped and replaced. All previously-tracked entities become dead. `CycloneNetworkCleanupSystem` sends `DISPOSE` for each of them — a mass-destruction spike. It then re-adds all restored entities as "new" on the next frame, triggering IG reconstruction.

**Fix:**
1. Expose a `ResetTracking()` method on `CycloneNetworkCleanupSystem` that clears `_trackedEntities`.
2. `PlaybackTickSystem` calls `ResetTracking()` immediately after executing a `SeekToFrame`, before the Export phase runs.
3. On the next Export frame, `CycloneNetworkCleanupSystem` sees a freshly-built tracking dict from scratch — no stale entries to dispose, no spurious flood.

**Alternative:** Instead of `ResetTracking()`, add a `SeekCompleted` event on `PlaybackTickSystem` that `CycloneNetworkCleanupSystem` subscribes to.

**Success Criteria:**
- After a timeline seek, `CycloneNetworkCleanupSystem` does not send any `DISPOSE` signals for the wiped entities.
- The IG does not experience entity disappearance/reappearance on seek.
- Normal end-of-life `DISPOSE` signals (from entities that actually die during forward playback) still work correctly.

---

## PHASE 5 — LEGACY REMOVAL

### T-RMF-24 — Delete Legacy Classes from `Fdp.Core`

**Goal:** Delete the three legacy source files. Any remaining reference becomes a compile error.

**Files to delete:**
1. `FDP/Engine/Fdp.Core/ComponentSystem.cs`
2. `FDP/Engine/Fdp.Core/SystemGroup.cs`
3. `FDP/Engine/Fdp.Core/StandardSystemGroups.cs`

**Pre-condition:** All Phase 1-3 tasks complete. No game system or composition root references these types.

**Post-deletion:** Full solution build. Fix all remaining compile errors. Common remaining references:
- Test files that instantiate `SimulationSystemGroup()` — replace with `new TogglableSimulationGroup("test")`.
- Any test that calls `TestHook_AddSystem(ComponentSystem)` — update to `IEcsModuleSystem`.

**Success Criteria:** Three files deleted. Solution builds cleanly. All tests pass.

---

### T-RMF-25 — Delete Legacy Adapter Classes from `Hrot.Common.Infrastructure`

**Goal:** Delete the two adapter source files.

**Files to delete:**
1. `Hrot/Engine/Hrot.Common/Infrastructure/CgfInputGroupAdapter.cs`
2. `Hrot/Engine/Hrot.Common/Infrastructure/LegacySystemGroupAdapters.cs`

**Pre-condition:** T-RMF-16, T-RMF-17, T-RMF-18 complete.

**Success Criteria:** Two files deleted. Solution builds cleanly. All tests pass.

---

## PHASE 6 — VERIFICATION AND TESTS

### T-RMF-26 — Write Replay Isolation Tests

**Goal:** Add tests verifying the replay handler disables all four groups (`TogglableInputGroup`, `TogglableSimulationGroup`, `TogglablePostSimulationGroup`, `NetworkLifecycleSystemGroup`) during replay, and re-enables them after `FinalizeReplay` and `PrepareLive`.

**File to extend:** `Hrot/Subsystems/Hrot.SimHost.Tests/ReplayLoadClusterOpHandlerTests.cs`

**New test cases:**

1. `PrepareReplay_DisablesInputGroup`
2. `PrepareReplay_DisablesSimGroup`
3. `PrepareReplay_DisablesPostSimGroup`
4. `FinalizeReplay_ReEnablesInputGroup`
5. `FinalizeReplay_ReEnablesPostSimGroup`
6. `PrepareLive_FromReplay_ReEnablesInputGroup`
7. `PrepareLive_FromReplay_ReEnablesPostSimGroup`

**Success Criteria:** All seven new test cases pass. Existing tests updated for new group types and still pass.

---

### T-RMF-27 — Update Existing Replay Tests for New Types

**Goal:** Update all existing tests that construct `ReferenceReplayLoadHandler` or `NodeBootstrapper` with legacy `SimulationSystemGroup` to use the new types.

**Files to update:**
- `Hrot/Subsystems/Hrot.SimHost.Tests/ReplayLoadClusterOpHandlerTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/LiveFromReplayTests.cs`
- `Hrot/Subsystems/Hrot.SimHost.Tests/NodeBootstrapperReplayTests.cs`

**Changes:**
- Replace `new SimulationSystemGroup()` with `new TogglableSimulationGroup("test")`.
- Add `new TogglableInputGroup("test")` and `new TogglablePostSimulationGroup("test")` where the handler is constructed (empty wrappers are fine for toggle-behavior tests).
- Update `using` directives.

**Success Criteria:** All three test files compile. All existing test cases still pass.

---

## Summary Table

| Task ID | Phase | Description | Key Files |
|---------|-------|-------------|-----------|
| T-RMF-01 | 1 | Create `TogglableSimulationGroup` (implements `ISystemGroup`) | `Fdp.ModuleHost/Scheduling/TogglableSimulationGroup.cs` (new) |
| T-RMF-02 | 1 | Create `TogglableInputGroup` (implements `ISystemGroup`) | `Fdp.ModuleHost/Scheduling/TogglableInputGroup.cs` (new) |
| T-RMF-03 | 1 | Create `TogglablePostSimulationGroup` (implements `ISystemGroup`) | `Fdp.ModuleHost/Scheduling/TogglablePostSimulationGroup.cs` (new) |
| T-RMF-04 | 1 | Update `ReferenceReplayLoadHandler` -- add all three new group types | `Fdp.Toolkits/Orchestration/Handlers/ReferenceReplayLoadHandler.cs` |
| T-RMF-05 | 1 | Update `NodeBootstrapper.BuildOrchestration` -- new parameter types | `Hrot.SimHost/NodeBootstrapper.cs` |
| T-RMF-06 | 2 | Convert `CombatModule` systems | `Hrot.SimHost/Modules/CombatModule.cs` + system files |
| T-RMF-07 | 2 | Convert `GroundKinematicsModule` systems | `Hrot.SimHost/Modules/GroundKinematicsModule.cs` + systems |
| T-RMF-08 | 2 | Convert navigation bridge systems | Various projects |
| T-RMF-09 | 2 | Convert `MissionControlModule` systems | CGF toolkit |
| T-RMF-10 | 2 | Convert `CognitiveRuntimeModule` and `ActionDispatchModule` | CGF toolkit |
| T-RMF-11 | 2 | Convert standalone CGF systems and `DamageAssessmentModule` | `Hrot.CGF` |
| T-RMF-12 | 2 | Convert `GenesisMaterializationSystem` (throw on bad view) | `Hrot.SimHost` |
| T-RMF-13 | 3 | Rework `SimHostCoreLogicPack` -- expose system arrays | `Hrot.SimHost/Modules/SimHostCoreLogicPack.cs` |
| T-RMF-14 | 3 | Rework `CgfLogicPack` -- expose `InputSystems`/`SimulationSystems` arrays | `Hrot.CGF/CgfLogicPack.cs` |
| T-RMF-15 | 3 | Update `SimHostApp` -- wire three togglable groups, fix empty-simGroup bug | `Hrot.SimHost/SimHostApp.cs` |
| T-RMF-16 | 3 | Update `CgfSubsystem` -- remove adapters, fix `simGroup: null` bug | `Hrot.CGF/CgfSubsystem.cs` |
| T-RMF-17 | 3 | Update `CgfApplication` -- same as T-RMF-16 | `Hrot.CGF/CgfApplication.cs` |
| T-RMF-18 | 3 | Update `EditorSubsystem` + `EditorSystemsModule` -- remove adapters, ISystemRegistry | `Hrot.Editor/EditorSubsystem.cs` + EditorSystemsModule |
| T-RMF-19 | 3 | Update test harnesses -- `EditorHarness` and `SimHostInstance` | Test infrastructure |
| T-RMF-20 | 4 | Move `GhostDestructionSystem` + `DeferredTakeoverSystem` inside lifecycle group | `NedReplicationModule.cs`, `ReferenceReplayLoadHandler.cs` |
| T-RMF-21 | 4 | Fix `GlobalTime` singleton tug-of-war (suspend kernel time push during replay) | `ModuleHostKernel.cs`, `ReferenceReplayLoadHandler.cs` |
| T-RMF-22 | 4 | Fix `SmartEgressSystem` 10-second lag on seek (force-dirty after SeekToFrame) | `PlaybackTickSystem.cs`, `SmartEgressSystem.cs` |
| T-RMF-23 | 4 | Fix `CycloneNetworkCleanupSystem` scrub flood on seek (reset tracking on seek) | `CycloneNetworkCleanupSystem.cs`, `PlaybackTickSystem.cs` |
| T-RMF-24 | 5 | Delete legacy classes from `Fdp.Core` | 3 file deletions |
| T-RMF-25 | 5 | Delete legacy adapters from `Hrot.Common.Infrastructure` | 2 file deletions |
| T-RMF-26 | 6 | Write replay isolation tests (all three togglable groups) | `ReplayLoadClusterOpHandlerTests.cs` |
| T-RMF-27 | 6 | Update existing replay tests for new types | 3 test files |

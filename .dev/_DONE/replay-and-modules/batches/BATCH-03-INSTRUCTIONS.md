# BATCH-03 Instructions — Phase 3: Composition Roots

**Tasks:** T-RMF-13, T-RMF-14, T-RMF-15, T-RMF-16, T-RMF-17, T-RMF-18, T-RMF-19
**Git baseline:** HEAD = 0ce69f5 (BATCH-02 commit)

---

## Overview

Phase 3 replaces all `SystemGroup`-based registration patterns in the composition roots
with the new `IReadOnlyList<IEcsModuleSystem>` array properties exposed by the packs and
modules.  Three `TogglableXxxGroup` wrappers replace the ad-hoc `SystemGroup` fields in
`SimHostApp`, `CgfSubsystem`, and `EditorSubsystem`.

Key rules:
- All system instances that currently live inside `RegisterSystems(SystemGroup, ...)` must
  move to the constructor (held in private fields or built as inline arrays).
- Delete every `RegisterSystems(SystemGroup ...)` overload once its content has been
  relocated.
- The no-op `RegisterSystems(ISystemRegistry registry)` and `Tick()` methods stay.
- Do NOT delete `SimulationLogicModule` or `ComponentSystem` — that is Phase 5.
- Follow AGENTS.md: preserve all existing comments exactly unless they are wrong.
- Use only ASCII characters in new comments.

---

## T-RMF-13 — SimHostCoreLogicPack: Expose System Arrays

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs`

### Changes

1. Add three new private fields for the systems that are currently created inside
   `RegisterSystems(SystemGroup, ...)`:

```csharp
private readonly NavigationIntentBridgeSystem _navIntentBridge;
private readonly RouteTrajectorySyncSystem    _routeTrajSync;
private readonly PersonalRouteAuthoringSystem _personalRouteAuthoring;
```

2. In the constructor, after creating `_groundKinematicsModule`, instantiate those systems
   and build the three phase arrays:

```csharp
// Navigation bridge systems
_navIntentBridge         = new NavigationIntentBridgeSystem();
_routeTrajSync           = new RouteTrajectorySyncSystem(_groundKinematicsModule.TrajectoryPool);
_personalRouteAuthoring  = new PersonalRouteAuthoringSystem();

// Phase arrays
var inputList   = new List<IEcsModuleSystem>();
var simList     = new List<IEcsModuleSystem>();
var postSimList = new List<IEcsModuleSystem>();

foreach (var s in _combatModule.InputSystems)  inputList.Add(s);
inputList.Add(_personalRouteAuthoring);

foreach (var s in _damageAssessmentModule.SimulationSystems) simList.Add(s);
simList.Add(_navIntentBridge);
simList.Add(_routeTrajSync);
foreach (var s in _groundKinematicsModule.SimulationSystems) simList.Add(s);

foreach (var s in _combatModule.PostSimulationSystems)             postSimList.Add(s);
foreach (var s in _groundKinematicsModule.PostSimulationSystems)   postSimList.Add(s);

InputSystems           = inputList;
SimulationSystems      = simList;
PostSimulationSystems  = postSimList;
```

3. Add the three public properties (with XML-doc summary comments):

```csharp
/// <summary>Systems to wrap in TogglableInputGroup.</summary>
public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

/// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

/// <summary>Systems to wrap in TogglablePostSimulationGroup.</summary>
public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }
```

4. Delete the entire `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup,
   SystemGroup postSimGroup)` method (approximately lines 115-143 in the original).

5. Add `using System.Collections.Generic;` to the top if not already present.

6. Remove the `using Fdp.Core;` line ONLY if `SystemGroup` is now the only type from that
   namespace remaining. Check carefully — `EntityRepository`, `Entity`, and other core
   types may still be used.

**Success criteria:**
- `InputSystems`, `SimulationSystems`, `PostSimulationSystems` properties exist and
  return non-null lists.
- `RegisterSystems(SystemGroup, SystemGroup, SystemGroup)` overload is deleted.
- `RegisterSystems(ISystemRegistry registry)` no-op and `Tick()` remain unchanged.

---

## T-RMF-14 — CgfLogicPack: Expose System Arrays

**File:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`

### Context

The `RegisterSystems(SystemGroup)` and `RegisterSystems(SystemGroup, SystemGroup)` overloads
currently create three systems inline: `new HealthApplicationSystem()`,
`new CgfThreatEvaluationSystem()`, `new RouteContextSystem()`. These must move to the
constructor.

The split between Input and Simulation phases is:
- **Input:** `_missionExecutionSystem`, then `_missionControlModule.InputSystems` (BehaviorIngress)
- **Simulation:** `_missionAdapterSystem`, `_missionControlModule.SimulationSystems`,
  `_healthApplicationSystem`, `_cgfThreatEvaluationSystem`,
  `_cognitiveRuntimeModule.SimulationSystems`, `_actionDispatchModule.SimulationSystems`,
  `_routeContextSystem`

### Changes

1. Add private fields:

```csharp
private readonly HealthApplicationSystem      _healthApplicationSystem;
private readonly CgfThreatEvaluationSystem    _cgfThreatEvaluationSystem;
private readonly RouteContextSystem           _routeContextSystem;
```

2. In the constructor (after all existing sub-module creation), instantiate those systems
   and build the two phase arrays:

```csharp
_healthApplicationSystem   = new HealthApplicationSystem();
_cgfThreatEvaluationSystem = new CgfThreatEvaluationSystem();
_routeContextSystem        = new RouteContextSystem();

var inputList = new List<IEcsModuleSystem>();
var simList   = new List<IEcsModuleSystem>();

inputList.Add(_missionExecutionSystem);
foreach (var s in _missionControlModule.InputSystems) inputList.Add(s);

simList.Add(_missionAdapterSystem);
foreach (var s in _missionControlModule.SimulationSystems) simList.Add(s);
simList.Add(_healthApplicationSystem);
simList.Add(_cgfThreatEvaluationSystem);
foreach (var s in _cognitiveRuntimeModule.SimulationSystems) simList.Add(s);
foreach (var s in _actionDispatchModule.SimulationSystems)   simList.Add(s);
simList.Add(_routeContextSystem);

InputSystems      = inputList;
SimulationSystems = simList;
```

3. Add the two public properties:

```csharp
/// <summary>Systems to wrap in TogglableInputGroup.</summary>
public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

/// <summary>Systems to wrap in TogglableSimulationGroup.</summary>
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }
```

4. Delete both `RegisterSystems(SystemGroup simGroup)` and
   `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup)` overloads entirely.

5. Remove `using Fdp.Core;` ONLY if no remaining code in the file uses `SystemGroup` or
   any other `Fdp.Core` type. Double-check by searching for `Fdp.Core` type usage.

6. Add `using System.Collections.Generic;` if not present.

**Success criteria:**
- `InputSystems` and `SimulationSystems` properties exist and return non-null lists.
- Both `RegisterSystems(SystemGroup ...)` overloads deleted.
- `RegisterSystems(ISystemRegistry registry)` no-op remains.

---

## T-RMF-15 — SimHostApp: Replace _kernelGroup with Three Togglable Groups

**File:** `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

### Context

Current problematic code (around line 374-393 in `OnLoad`):
```csharp
_kernelGroup = new SystemGroup();
_kernelGroup.Create(_world);
// Add DDS attribute/descriptor update systems from factory ...
if (nodeFactory != null)
{
    foreach (var sys in nodeFactory.CreateSimHostAttributeUpdateSystems())
        _kernelGroup.AddSystem(sys);
}
_simCorePack!.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup);
_kernelGroup.AddSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(entityMap));
```

And in `OnUpdate`:
```csharp
_kernelGroup?.Run();   // process incoming requests first (sets dirty flags)
```

And the field declaration:
```csharp
private SystemGroup? _kernelGroup;
```

And in `Shutdown`:
```csharp
_kernelGroup?.Dispose();
```

### Changes

1. **Delete the field declaration:**
   ```csharp
   private SystemGroup? _kernelGroup;
   ```
   Replace with three togglable group fields:
   ```csharp
   private TogglableInputGroup?          _toggleInput;
   private TogglableSimulationGroup?     _toggleSim;
   private TogglablePostSimulationGroup? _togglePostSim;
   ```

2. **In `OnLoad`, replace the `_kernelGroup` block** with:
   ```csharp
   // Systems from nodeFactory (DDS attribute/descriptor updates) -- Input phase
   var factoryInputSystems = new System.Collections.Generic.List<IEcsModuleSystem>();
   if (nodeFactory != null)
   {
       foreach (var sys in nodeFactory.CreateSimHostAttributeUpdateSystems())
           factoryInputSystems.Add(sys);
   }

   // Combine factory input systems with pack input systems
   var allInputSystems = new System.Collections.Generic.List<IEcsModuleSystem>(factoryInputSystems);
   foreach (var s in _simCorePack!.InputSystems) allInputSystems.Add(s);

   _toggleInput   = new TogglableInputGroup("SimHostInput",          allInputSystems);
   _toggleSim     = new TogglableSimulationGroup("SimHostSimulation", _simCorePack!.SimulationSystems);
   _togglePostSim = new TogglablePostSimulationGroup("SimHostPostSimulation",
       _simCorePack!.PostSimulationSystems);

   _kernel.RegisterGlobalSystem(_toggleInput);
   _kernel.RegisterGlobalSystem(_toggleSim);
   _kernel.RegisterGlobalSystem(_togglePostSim);

   // GenesisMaterializationSystem -- Input phase, registered after the togglable groups
   _kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.GenesisMaterializationSystem(entityMap));
   ```

   NOTE: The `factoryInputSystems` loop replaces the old
   `nodeFactory.CreateSimHostAttributeUpdateSystems()` loop that used `_kernelGroup.AddSystem`.
   GenesisMaterializationSystem is now a global system registered directly on the kernel
   (it has `[UpdateInPhase(SystemPhase.Input)]` attribute), not inside the togglable group.

   Actually, think about whether GenesisMaterializationSystem needs to be toggleable. The task description says it "should be registered directly on the kernel" - so yes, register it as a global system, not inside the togglable groups.

3. **Update the `BuildOrchestration` call** to pass the real togglable group for `simGroup`:
   The current call has `simGroup: null`. Change this to `simGroup: _toggleSim`.
   Also change `inputGroup: null` (if present) to `inputGroup: _toggleInput`.

   Find the `bootstrapper.BuildOrchestration(...)` call and update the named arguments:
   - `simGroup: null` -> `simGroup: _toggleSim`
   (If the method doesn't have `inputGroup:` parameter, leave it; only update parameters
   that exist in the method signature. Check the BuildOrchestration signature first.)

4. **In `OnUpdate`, delete** the line:
   ```csharp
   _kernelGroup?.Run();   // process incoming requests first (sets dirty flags)
   ```
   The kernel now runs everything via `_kernel?.Update()`.

5. **In `Shutdown`, replace:**
   ```csharp
   _kernelGroup?.Dispose();
   ```
   With:
   ```csharp
   _toggleInput?.Dispose();
   _toggleSim?.Dispose();
   _togglePostSim?.Dispose();
   ```
   (Only if TogglableXxxGroup implements IDisposable. If it does not, just remove the
   `_kernelGroup?.Dispose()` call without adding replacements.)

6. Add `using Fdp.ModuleHost.Scheduling;` to the using block if `TogglableInputGroup` etc.
   are in that namespace (check existing using statements).

**Success criteria:**
- `_kernelGroup` field removed.
- Three togglable groups populated and passed to the kernel.
- BuildOrchestration receives real `simGroup: _toggleSim` instead of null.
- No `_kernelGroup?.Run()` in OnUpdate.
- Build succeeds.

---

## T-RMF-16 — CgfSubsystem: Replace Legacy Groups, Delete CgfSimGroupModule

**File:** `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

### Context

Current pattern (around line 277-305 in Initialize):
```csharp
var inputGroup = new SystemGroup();
inputGroup.Create(_context.World);
_inputGroup = inputGroup;

var simGroup = new SystemGroup();
simGroup.Create(_context.World);
_simGroup = simGroup;

cgfLogicPack.RegisterSystems(inputGroup, simGroup);

// Register the Input-phase group via the shared adapter (SystemPhase.Input).
_context.Kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(_inputGroup));
// Register the Simulation-phase group as a synchronous module.
_context.Kernel.RegisterModule(new CgfSimGroupModule(_simGroup));
```

Fields declared earlier:
```csharp
private SystemGroup? _simGroup;
private SystemGroup? _inputGroup;
```

And the `ReferenceReplayLoadHandler` call:
```csharp
newClusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
    rrController, 
    inputGroup:            null,
    simGroup:              null, 
    ...));
```

### Changes

1. **Delete the nested class `CgfSimGroupModule`** (approximately lines 49-64, the entire
   private sealed class including its braces).

2. **Remove the two field declarations:**
   ```csharp
   private SystemGroup? _simGroup;
   private SystemGroup? _inputGroup;
   ```

3. **Add three togglable group fields** in their place:
   ```csharp
   private TogglableInputGroup?      _toggleInput;
   private TogglableSimulationGroup? _toggleSim;
   ```
   (No postSimGroup needed for CGF — it has no PostSimulation systems.)

4. **In `Initialize`, replace the SystemGroup block** with:
   ```csharp
   _toggleInput = new TogglableInputGroup("CgfInput",          cgfLogicPack.InputSystems);
   _toggleSim   = new TogglableSimulationGroup("CgfSimulation", cgfLogicPack.SimulationSystems);

   _context.Kernel.RegisterGlobalSystem(_toggleInput);
   _context.Kernel.RegisterGlobalSystem(_toggleSim);
   ```

5. **Fix the `ReferenceReplayLoadHandler` call** to pass the real togglable groups:
   ```csharp
   newClusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
       rrController, 
       inputGroup:            _toggleInput,
       simGroup:              _toggleSim, 
       postSimGroup:          null,
       lifecycleGroup:        null, 
       bypassLifecycleToggle: null, 
       storageDirectory:      OrchestrationConstants.DefaultStagingDirectory));
   ```

6. **Remove `using Hrot.Common.Infrastructure;`** ONLY if `CgfInputGroupAdapter` (from that
   namespace) was the only type used. Verify by searching for other types from that namespace.
   If `SimulationGroupModule`, `PostSimulationGroupAdapter`, or `CgfInputGroupAdapter` are
   from `Hrot.Common.Infrastructure`, remove the using once those are all gone.

7. Add `using Fdp.ModuleHost.Scheduling;` if not present.

**Success criteria:**
- `CgfSimGroupModule` nested class deleted.
- `_inputGroup` and `_simGroup` fields removed.
- Togglable groups wired to kernel and replay handler.
- ReferenceReplayLoadHandler receives real group references for input and sim.

---

## T-RMF-17 — CgfApplication: Fix Replay Handler Groups

**File:** `Hrot/Subsystems/Hrot.CGF/CgfApplication.cs`

### Context

`CgfApplication` passes `inputGroup: null, simGroup: null, postSimGroup: null` to
`ReferenceReplayLoadHandler`. The fix: accept an optional `CgfLogicPack?` constructor
parameter and, when provided, build togglable groups from its arrays and pass them to the
replay handler.

### Changes

1. **Add a new optional constructor parameter** at the end of the existing constructor
   signature:
   ```csharp
   public CgfApplication(int domainId = 0, int nodeId = DefaultNodeId,
       DdsParticipant? participant = null,
       ScenarioSerializer? scenarioSerializer = null,
       string localTempRoot = OrchestrationConstants.DefaultStagingDirectory,
       INetworkFactory? networkFactory = null,
       CgfLogicPack? logicPack = null)      // new optional parameter
   ```

2. **In the constructor body**, after `_world` and `_kernel` initialization, build the
   togglable groups when `logicPack` is non-null:
   ```csharp
   TogglableInputGroup?      replayInputGroup = null;
   TogglableSimulationGroup? replaySimGroup   = null;
   if (logicPack != null)
   {
       replayInputGroup = new TogglableInputGroup("CgfInput",          logicPack.InputSystems);
       replaySimGroup   = new TogglableSimulationGroup("CgfSimulation", logicPack.SimulationSystems);
       _kernel.RegisterGlobalSystem(replayInputGroup);
       _kernel.RegisterGlobalSystem(replaySimGroup);
   }
   ```

3. **Update the `ReferenceReplayLoadHandler` constructor call** to pass the groups:
   ```csharp
   _clusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
       rrController,
       inputGroup:            replayInputGroup,
       simGroup:              replaySimGroup,
       postSimGroup:          null,
       lifecycleGroup:        null,
       bypassLifecycleToggle: null,
       storageDirectory:      localTempRoot));
   ```

4. Add `using Fdp.ModuleHost.Scheduling;` to the using block if not already present.

**Note:** The `Install(IEcsModule module)` method and `Tick()` are unchanged. The test
`new CgfApplication(domainId: TestDomain, nodeId: 401)` continues to work — logicPack
defaults to null and the groups default to null (same as before).

**Success criteria:**
- Constructor accepts optional `CgfLogicPack? logicPack = null`.
- When logicPack provided, togglable groups are created and passed to replay handler.
- When logicPack is null, behavior is unchanged from before.
- Existing `CgfHandlerRegistrationTests` still compiles and passes.

---

## T-RMF-18 — EditorSubsystem and EditorHarness: Remove All Adapter Usage

### File 1: `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`

#### Context (around lines 219-245 and 391-410)

Current nested classes and registration:
```csharp
private sealed class SimGroupModule : IEcsModule { ... _group.Run() }
private sealed class PostSimGroupModule : IEcsModule { ... _group.Run() }

// Then in Initialize:
var inputGroup   = new SystemGroup();
var cgfSimGroup  = new SystemGroup();
var muscleSimGroup = new SystemGroup();
var postSimGroup = new SystemGroup();

cgfLogicPackInst.RegisterSystems(inputGroup, cgfSimGroup);
simHostCorePack.RegisterSystems(inputGroup, muscleSimGroup, postSimGroup);

_kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(inputGroup));
_kernel.RegisterModule(new SimulationGroupModule(cgfSimGroup, "BrainSimGroup"));
_kernel.RegisterModule(new SimulationGroupModule(muscleSimGroup, "MuscleSimGroup"));
_kernel.RegisterGlobalSystem(new PostSimulationGroupAdapter(postSimGroup));
```

#### Changes

1. **Delete the `SimGroupModule` nested class** entirely (the private sealed class at
   approximately lines 221-230).

2. **Delete the `PostSimGroupModule` nested class** entirely (approximately lines 234-243).

3. **In the Initialize method, replace the 4-SystemGroup block** with foreach loops that
   register systems directly:

   ```csharp
   // CGF Brain systems -- register directly (no toggling needed in the editor)
   foreach (var sys in cgfLogicPackInst.InputSystems)      _kernel.RegisterGlobalSystem(sys);
   foreach (var sys in cgfLogicPackInst.SimulationSystems) _kernel.RegisterGlobalSystem(sys);

   // Muscle systems -- register directly
   foreach (var sys in simHostCorePack.InputSystems)          _kernel.RegisterGlobalSystem(sys);
   foreach (var sys in simHostCorePack.SimulationSystems)     _kernel.RegisterGlobalSystem(sys);
   foreach (var sys in simHostCorePack.PostSimulationSystems) _kernel.RegisterGlobalSystem(sys);
   ```

   NOTE: `RegisterGlobalSystem` may not exist on the kernel. Check the kernel API. If the
   kernel uses `RegisterModule` for IEcsModuleSystem, use that instead. The correct call
   for registering an `IEcsModuleSystem` on the kernel is to wrap it or find the right API.
   Check `ModuleHostKernel` for a method that accepts `IEcsModuleSystem`. If only
   `RegisterModule(IEcsModule)` exists, wrap each system as a single-system module.

   ACTUALLY -- Looking at this more carefully: the systems are `IEcsModuleSystem` instances.
   The kernel's `RegisterGlobalSystem` method accepts `IEcsModuleSystem` directly. Use that.
   The `[UpdateInPhase]` attribute on each system determines the execution phase.

4. **Remove the `using Hrot.Common.Infrastructure;` import** if it was only there for
   `CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter`. Verify by
   searching for other types from that namespace in the file.

5. **The `EditorSystemsModule` registration stays unchanged.** Do NOT touch
   `_kernel.RegisterModule(new EditorSystemsModule(_world))`.

### File 2: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs`

Same pattern as EditorSubsystem. Look for `SimGroupModule`, `PostSimGroupModule` nested
classes and 4-SystemGroup wiring block. Apply the same replacement:

1. Delete `SimGroupModule` and `PostSimGroupModule` nested classes.
2. Replace the `inputGroup/cgfSimGroup/muscleSimGroup/postSimGroup` SystemGroup block with
   foreach loops over pack arrays calling `Kernel.RegisterGlobalSystem(sys)`.
3. Remove `using Hrot.Common.Infrastructure;` if no longer needed.

**Success criteria (both files):**
- No references to `CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter`
  in either file.
- No `SystemGroup` fields or local variables remain in the registration code.
- `SimGroupModule` and `PostSimGroupModule` nested classes deleted.
- Solution compiles.

---

## T-RMF-19 — EditorHarness and SimHostInstance: Remove SystemGroup Usage

### Context: EditorHarness

Already addressed in T-RMF-18.

### File: `Hrot/Subsystems/Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs`

#### Context (lines 215-217, 269-287, 567-569, 609-611)

Current fields:
```csharp
private readonly SystemGroup _inputGroup;
private readonly SystemGroup _simGroup;
private readonly SystemGroup _postSimGroup;
```

Current construction (step 6 in constructor):
```csharp
_inputGroup = new SystemGroup();
_inputGroup.Create(_world);
_simGroup = new SystemGroup();
_simGroup.Create(_world);
_postSimGroup = new SystemGroup();
_postSimGroup.Create(_world);

var simLogicModule = new SimulationLogicModule(..., role: NodeRole.Brain | NodeRole.MuscleGround | NodeRole.Perception);
simLogicModule.RegisterSystems(_inputGroup, _simGroup, _postSimGroup);
// MissionAdapterSystem ...
_simGroup.AddSystem(new MissionAdapterSystem(_behaviorRegistry, _entityMap));
```

Current disposal:
```csharp
_postSimGroup.Dispose();
_inputGroup.Dispose();
_simGroup.Dispose();
```

Current tick (approximately lines 609-611):
```csharp
_inputGroup.Run();
_simGroup.Run();
_postSimGroup.Run();
```

#### Changes

Replace the three `SystemGroup` fields with `IReadOnlyList<IEcsModuleSystem>` lists:

1. **Replace field declarations:**
   ```csharp
   // Old: private readonly SystemGroup _inputGroup/simGroup/postSimGroup
   // New:
   private readonly System.Collections.Generic.IReadOnlyList<IEcsModuleSystem> _inputSystems;
   private readonly System.Collections.Generic.IReadOnlyList<IEcsModuleSystem> _simSystems;
   private readonly System.Collections.Generic.IReadOnlyList<IEcsModuleSystem> _postSimSystems;
   ```

2. **Replace the step 6 construction block:**
   ```csharp
   // 6. Simulation-logic system lists ------------------------------------
   var roadNetwork    = new RoadNetworkBuilder().Build(10f, 100, 100);
   var trajectoryPool = new TrajectoryPoolManager();

   // Use dedicated packs instead of SimulationLogicModule to get IEcsModuleSystem lists.
   var musclePack = new SimHostCoreLogicPack(_entityMap, roadNetwork, trajectoryPool);
   var brainPack  = new CgfLogicPack(_behaviorRegistry, _entityMap,
       new ScenarioEntityCreationRequestSource());

   var inputList   = new System.Collections.Generic.List<IEcsModuleSystem>();
   var simList     = new System.Collections.Generic.List<IEcsModuleSystem>();
   var postSimList = new System.Collections.Generic.List<IEcsModuleSystem>();

   foreach (var s in brainPack.InputSystems)       inputList.Add(s);
   foreach (var s in musclePack.InputSystems)      inputList.Add(s);

   foreach (var s in brainPack.SimulationSystems)  simList.Add(s);
   foreach (var s in musclePack.SimulationSystems) simList.Add(s);
   // MissionAdapterSystem bridges ActiveMissionPlan BehaviorParams into BrainBlackboard,
   // enabling end-to-end mission execution tests without a live CGF node.
   simList.Add(new MissionAdapterSystem(_behaviorRegistry, _entityMap));

   foreach (var s in musclePack.PostSimulationSystems) postSimList.Add(s);

   _inputSystems   = inputList;
   _simSystems     = simList;
   _postSimSystems = postSimList;

   var physicsModule = new PhysicsToolkitModule();
   physicsModule.Initialize(_world);
   ```

3. **Remove the disposal block** (no Dispose needed for IReadOnlyList):
   ```csharp
   // Delete these lines:
   _postSimGroup.Dispose();
   _inputGroup.Dispose();
   _simGroup.Dispose();
   ```

4. **Replace the tick block:**
   ```csharp
   // Old: _inputGroup.Run(); _simGroup.Run(); _postSimGroup.Run();
   // New:
   const float dt = 1f / 60f;
   foreach (var s in _inputSystems)   s.Execute(_world, dt);
   foreach (var s in _simSystems)     s.Execute(_world, dt);
   foreach (var s in _postSimSystems) s.Execute(_world, dt);
   ```

   IMPORTANT: The dt value should match the existing GlobalTime singleton seeded in step 7
   of the constructor (1f / 60f). If the tick method already receives a `dt` parameter,
   use that instead of the constant.

5. Add required using directives:
   - `using Hrot.CGF;` (for CgfLogicPack)
   - `using Fdp.Toolkit.Scenario;` (for ScenarioEntityCreationRequestSource) -- only if not
     already present
   - `using Fdp.ModuleHost.Abstractions;` (for IEcsModuleSystem) -- check if already present

**Note:** The existing `_elmSystems`, `_geoSystems`, and their `.Run()` calls are unrelated
to this change -- leave them completely intact.

**Note on removed TrajectoryPool:** The old `SimulationLogicModule` had
`TrajectoryPool => _groundKinematicsModule?.TrajectoryPool`. If `SimHostInstance` uses
`TrajectoryPool` anywhere in tests, use `musclePack.TrajectoryPool` instead. Check and
update if needed.

**Success criteria:**
- `_inputGroup`, `_simGroup`, `_postSimGroup` SystemGroup fields removed.
- `SimulationLogicModule` no longer instantiated in SimHostInstance.
- Systems executed directly via `IEcsModuleSystem.Execute(_world, dt)`.
- All existing Hrot.SimHost.Integration.Tests still pass.

---

## Build and Test Verification

After all changes, run the following in order:

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | grep -E "error CS|Build succeeded|FAILED"
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj --no-build
dotnet test Hrot/Subsystems/Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj --no-build
```

On Windows PowerShell, replace `grep` with `Select-String`:
```powershell
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/... --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/... --no-build
dotnet test Hrot/Subsystems/Hrot.SimHost.Integration.Tests/... --no-build
```

Expected results:
- Build: 0 errors
- Hrot.SimHost.Tests: 460/463 (same as BATCH-02)
- Hrot.ClusterRunner.Tests: all pass
- Hrot.SimHost.Integration.Tests: same pass count as before

---

## Report Template

Create `.dev/replay-and-modules/reports/BATCH-03-REPORT.md` with:
- Summary of each task (T-RMF-13 through T-RMF-19): done / partial / skipped
- Exact build output (error count)
- Test results (pass/fail counts) for all affected test projects
- Any deviations from instructions (explain why)
- List all files modified or created

---

## Important Notes

1. **Do not** delete `SimulationLogicModule` or `ComponentSystem` — these stay until Phase 5.
2. **Do not** change `RegisterSystems(ISystemRegistry)` no-op methods.
3. **Do not** change `Tick()` no-op methods in the packs.
4. **Preserve all existing comments exactly** (per AGENTS.md).
5. The `CgfInputGroupAdapter`, `SimulationGroupModule`, `PostSimulationGroupAdapter` classes
   themselves are NOT deleted in this batch -- only their usages are removed. Phase 5 will
   delete those adapter classes.
6. In EditorSubsystem and EditorHarness: `RegisterGlobalSystem(IEcsModuleSystem)` on the
   kernel directly respects the `[UpdateInPhase]` attribute on each system, so each system
   runs in its correct phase automatically. No manual phase management needed.

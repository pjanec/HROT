# BATCH-02 Instructions: Phase 2 — System Migration (T-RMF-06..T-RMF-12)

**Prerequisites:** BATCH-01 merged. Solution builds with 0 errors.

---

## Overview

Convert all remaining `ComponentSystem` subclasses to `IEcsModuleSystem`, and replace legacy
`RegisterSystems(SystemGroup)` module methods with phase-split array properties.

After this batch:
- No production system class extends `ComponentSystem` (tests are allowed to keep it if they have their own test stubs, but new test helpers should use `IEcsModuleSystem`).
- Every module exposes `IReadOnlyList<IEcsModuleSystem>` properties instead of `RegisterSystems(SystemGroup)` overloads.
- The solution compiles with 0 errors and all tests pass (or stay in their pre-existing skip state).

---

## Universal Conversion Rules

Apply these to every `ComponentSystem` being converted:

| Before | After |
|--------|-------|
| `public class Foo : ComponentSystem` | `public class Foo : IEcsModuleSystem` |
| `protected override void OnUpdate()` | `public void Execute(ISimulationView view, float deltaTime)` |
| `World.Query(...)` | `view.Query(...)` |
| `World.GetComponent<T>(e)` | `view.GetComponent<T>(e)` |
| `World.Bus.Read<T>()` | `view.Bus.Read<T>()` |
| `World.IsAlive(e)` | `view.IsAlive(e)` |
| `DeltaTime` | `deltaTime` |
| `[UpdateInGroup(typeof(SimulationSystemGroup))]` | `[UpdateInPhase(SystemPhase.Simulation)]` |
| `[UpdateInGroup(typeof(InputSystemGroup))]` | `[UpdateInPhase(SystemPhase.Input)]` |
| `[UpdateAfter(...)]` | Delete — ordering is now controlled by array position |
| `using Fdp.Core;` (for `ComponentSystem`) | Keep only if still needed for other types |
| Add `using Fdp.ModuleHost.Abstractions;` | Always required for `IEcsModuleSystem` and `[UpdateInPhase]` |

**Direct-mutation throw pattern** (for systems that need `EntityRepository` access):
If a system currently has `if (World is not EntityRepository repo) return;` or casts `World` to
`EntityRepository` to call `SetComponent`/`AddComponent`/`RemoveComponent`, replace the silent
return or implicit cast with an explicit throw:
```csharp
public void Execute(ISimulationView view, float deltaTime)
{
    if (view is not EntityRepository repo)
        throw new InvalidOperationException(
            $"{nameof(FooSystem)} requires direct EntityRepository access " +
            $"and cannot run on a read-only snapshot ({view.GetType().Name}).");
    // ... rest of the body using repo
}
```

**Module array property pattern** (replaces `RegisterSystems(SystemGroup)` overloads):
```csharp
// Old:
public void RegisterSystems(SystemGroup simGroup)
{
    simGroup.AddSystem(new FooSystem(_dep));
    simGroup.AddSystem(new BarSystem());
}

// New: expose a lazily-built readonly property:
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

// Built in the constructor:
public FooModule(IDep dep)
{
    SimulationSystems = new IEcsModuleSystem[]
    {
        new FooSystem(dep),
        new BarSystem(),
    };
}
```

Rules for which properties to expose per module:
- Only expose properties for phases that have at least one system.
- Use `IReadOnlyList<IEcsModuleSystem>` as the property type (not `IEcsModuleSystem[]`).
- Populate the backing arrays in the constructor (or a lazy getter if construction of systems
  requires lazy dependencies — see `GroundKinematicsModule`).
- Delete ALL `RegisterSystems(SystemGroup ...)` overloads from each module.
- If a module had a `RegisterSystems(ISystemRegistry registry)` overload, delete it too
  (callers will be updated in BATCH-03).

---

## T-RMF-06 — Convert `CombatModule` Systems

### System files to modify

| File | Phase | World-mutation? |
|------|-------|----------------|
| `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/FireProcessingSystem.cs` | `Input` | Read-only |
| `FDP/Toolkits/Fdp.Toolkits/Physics/Systems/RaycastSolverSystem.cs` | `Input` | Read-only |
| `FDP/Toolkits/Fdp.Toolkits/Physics/Systems/HitResolutionSystem.cs` | `Input` | Read-only |
| `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/BallisticsSystem.cs` | `PostSimulation` | Read-only |

Apply the universal conversion rules. The search paths above are approximate — find the actual
file location with a grep if needed. Add `[UpdateInPhase(SystemPhase.X)]` per the table.

### Module file to modify

`Hrot/Subsystems/Hrot.SimHost/Modules/CombatModule.cs`

Replace `RegisterSystems(SystemGroup inputGroup, SystemGroup simGroup, SystemGroup postSimGroup, NetworkEntityMap entityMap)` with:

```csharp
/// <summary>Systems that run in the Input phase.</summary>
public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

/// <summary>Systems that run in the PostSimulation phase.</summary>
public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }
```

Populate both in the constructor. The constructor currently takes no parameters — no parameters
needed now either (the systems have no constructor arguments per the current implementation,
except `FireProcessingSystem` may or may not — check the actual file):

```csharp
public CombatModule()
{
    InputSystems = new IEcsModuleSystem[]
    {
        new FireProcessingSystem(),
        new RaycastSolverSystem(),
        new HitResolutionSystem(),
    };
    PostSimulationSystems = new IEcsModuleSystem[]
    {
        new BallisticsSystem(),
    };
}
```

**IMPORTANT:** If `FireProcessingSystem` takes `NetworkEntityMap` in its constructor, `CombatModule`
must accept it as a constructor parameter too and pass it through:
```csharp
public CombatModule(NetworkEntityMap entityMap)
{
    InputSystems = new IEcsModuleSystem[]
    {
        new FireProcessingSystem(entityMap),
        ...
    };
    ...
}
```

Remove the `NetworkEntityMap` parameter from `RegisterSystems` (it no longer exists).
Check `FireProcessingSystem.cs` to confirm whether it takes `NetworkEntityMap`.

`SimulationSystems` is intentionally absent — `CombatModule` has no simulation-phase systems.

### Tests to update

`FDP/Toolkits/Fdp.Toolkits.Tests/Combat/FireProcessingSystemTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Combat/BallisticsSystemTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Physics/RaycastSolverSystemTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Physics/HitResolutionSystemTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Physics/HitResolutionSystemDetonationTests.cs`

These test files currently create systems via `world.AddSystem()` and tick them with `world.Update()`.
After conversion, the pattern becomes:
```csharp
using var world = new EntityRepository();
var system = new FireProcessingSystem(...);
// setup entities
system.Execute(world, deltaTime: 0.016f);
// assert
```

Update each test file to use `system.Execute(world, 0.016f)` instead of `world.Update()`.
Remove `world.AddSystem(...)` calls.

---

## T-RMF-07 — Convert `GroundKinematicsModule` Systems

### System files to modify

| File | Phase |
|------|-------|
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/SpatialHashSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationTargetSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Commands/VehicleCommandSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/CarKinematicsSystem.cs` | `PostSimulation` |
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/NavigationExecutionSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/LinearKinematicsSystem.cs` | `PostSimulation` |

The actual file locations may differ from the paths above — search if needed.

**CarKinematicsSystem world-mutation check:** If `CarKinematicsSystem.OnUpdate()` uses
`World.SetComponent(...)` to write physics results, check if it does so via `EntityRepository`
downcast or via `ISimulationView`. If it uses `World` directly (it extends `ComponentSystem`,
so `World` is always the repository) and calls `SetComponent`, keep the throw pattern.
Read the file first and decide. Most likely it reads positions and writes velocities via
`World.SetComponent` — if so, apply throw pattern.

`LinearKinematicsSystem` likely also calls `World.SetComponent` — check and apply throw pattern if so.

### Module file to modify

`FDP/Toolkits/Fdp.Toolkits/CarKinem/Modules/GroundKinematicsModule.cs`

The module currently uses lazy pool/template allocation. Keep that pattern. Replace
`RegisterSystems(SystemGroup group)` with:

```csharp
/// <summary>Systems that run in the Simulation phase.</summary>
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }

/// <summary>Systems that run in the PostSimulation phase.</summary>
public IReadOnlyList<IEcsModuleSystem> PostSimulationSystems { get; }
```

Because the systems need `TrajectoryPool` and `FormationTemplates` (which are lazily allocated
in the properties), build the arrays in the constructor AFTER triggering the lazy properties:

```csharp
public GroundKinematicsModule(
    RoadNetworkBlob roadNetwork = default,
    TrajectoryPoolManager? trajectoryPool = null,
    FormationTemplateManager? formationTemplates = null)
{
    _roadNetwork        = roadNetwork;
    _trajectoryPool     = trajectoryPool;
    _formationTemplates = formationTemplates;

    SimulationSystems = new IEcsModuleSystem[]
    {
        new SpatialHashSystem(),
        new FormationTargetSystem(FormationTemplates, TrajectoryPool),
        new VehicleCommandSystem(),
        new NavigationExecutionSystem(),
    };
    PostSimulationSystems = new IEcsModuleSystem[]
    {
        new CarKinematicsSystem(TrajectoryPool),
        new LinearKinematicsSystem(),
    };
}
```

**Note:** Accessing `FormationTemplates` and `TrajectoryPool` in the constructor is fine —
it just forces eager allocation rather than lazy. This is acceptable since the module is only
constructed when actually needed.

Delete the old `RegisterSystems(SystemGroup group)` method.

### Tests to update

`FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Modules/GroundKinematicsModuleTests.cs`

Update assertions from "RegisterSystems puts N systems in the group" to:
```csharp
var module = new GroundKinematicsModule();
Assert.Equal(4, module.SimulationSystems.Count);
Assert.Equal(2, module.PostSimulationSystems.Count);
```

Individual system tests (`SpatialHashSystemTests`, `CarKinematicsSystemTests`, etc.): update
from `world.AddSystem` + `world.Update()` to `system.Execute(world, dt)`.

---

## T-RMF-08 — Convert Navigation Bridge Systems

### PersonalRouteAuthoringSystem

`Hrot/Subsystems/Hrot.SimHost/Systems/Routing/PersonalRouteAuthoringSystem.cs`

This file already has `[UpdateInPhase(SystemPhase.Input)]`. Just change:
- `public sealed class PersonalRouteAuthoringSystem : ComponentSystem` → `: IEcsModuleSystem`
- `protected override void OnUpdate()` → `public void Execute(ISimulationView view, float deltaTime)`
- `var view = (ISimulationView)World;` — delete this line (parameter is already named `view`)
- All remaining `World.` references → `view.`
- If any `World.SetComponent` or similar direct-mutation call exists, apply throw pattern.

### RouteTrajectorySyncSystem

`Hrot/Subsystems/Hrot.SimHost/Systems/Routing/RouteTrajectorySyncSystem.cs`

This file already has `[UpdateInPhase(SystemPhase.BeforeSync)]`.
**Keep `BeforeSync`** — the code comment says it must run after ingress translators but before
`CarKinematicsSystem`. Do NOT change it to `Simulation`.

Apply only:
- `: ComponentSystem` → `: IEcsModuleSystem`
- `protected override void OnUpdate()` → `public void Execute(ISimulationView view, float deltaTime)`
- `World.` → `view.`

### NavigationIntentBridgeSystem

`FDP/Toolkits/Fdp.Toolkits/Navigation/Systems/NavigationIntentBridgeSystem.cs`

This file has `[UpdateInGroup(typeof(SimulationSystemGroup))]` — change to
`[UpdateInPhase(SystemPhase.Simulation)]`.

Also has `if (World is not EntityRepository repo) return;`. Read the file body:
- If the code calls `repo.SetComponent(...)` (direct mutation) → apply throw pattern.
- If `repo` is only used for `repo.Query()` or `repo.QueryDelta()` (read-only) → can use
  `view` directly without downcast; remove the `if` guard entirely and use `view.Query()`.

Check: `QueryDelta` is a method on `EntityRepository` not on `ISimulationView`. If the system
uses `QueryDelta`, it does need the EntityRepository downcast → apply throw pattern.

Apply:
- `[UpdateInGroup(typeof(SimulationSystemGroup))]` → `[UpdateInPhase(SystemPhase.Simulation)]`
- `[UpdateAfter(...)]` → delete
- `: ComponentSystem` → `: IEcsModuleSystem`
- `protected override void OnUpdate()` → `public void Execute(ISimulationView view, float deltaTime)`
- Replace `if (World is not EntityRepository repo) return;` with throw or remove per the check above.

### Tests to update

`FDP/Toolkits/Fdp.Toolkits.Tests/Navigation/NavigationIntentBridgeSystemTests.cs` (if it exists)
`Hrot/Subsystems/Hrot.SimHost.Tests/Systems/` (routing tests if any)

Update from `world.AddSystem` pattern to `system.Execute(world, dt)`.

---

## T-RMF-09 — Convert `MissionControlModule` Systems

### System files to modify

| File | Phase |
|------|-------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/DoctrineIngressSystem.cs` | `Input` |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/MissionDirectorSystem.cs` | `Simulation` |

Apply universal conversion rules.

### Module file to modify

`FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/MissionControlModule.cs`

Replace both `RegisterSystems` overloads (the single-group and the two-group ones) with:

```csharp
/// <summary>Systems that run in the Input phase.</summary>
public IReadOnlyList<IEcsModuleSystem> InputSystems { get; }

/// <summary>Systems that run in the Simulation phase.</summary>
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }
```

Populate in the constructor:
```csharp
public MissionControlModule(DoctrineRegistry registry)
{
    _registry = registry;
    InputSystems = new IEcsModuleSystem[]
    {
        new DoctrineIngressSystem(_registry),
    };
    SimulationSystems = new IEcsModuleSystem[]
    {
        new MissionDirectorSystem(),
    };
}
```

Delete both `RegisterSystems` overloads. Delete `ArgumentNullException` guards on `inputGroup`/`simGroup`
(they were for the old parameters).

### Tests to update

`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Modules/MissionControlModuleTests.cs`

Update from "RegisterSystems adds N systems to group" to:
```csharp
var module = new MissionControlModule(registry);
Assert.Single(module.InputSystems);
Assert.Single(module.SimulationSystems);
Assert.IsType<DoctrineIngressSystem>(module.InputSystems[0]);
Assert.IsType<MissionDirectorSystem>(module.SimulationSystems[0]);
```

Individual system tests (`DoctrineIngressSystemTests.cs`, `MissionDirectorSystemTests.cs`): update
from `world.AddSystem` pattern to `system.Execute(world, dt)`.

---

## T-RMF-10 — Convert `CognitiveRuntimeModule` and `ActionDispatchModule` Systems

### DispatcherSystemBase (shared base class)

`FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/DispatcherSystemBase.cs`

`DispatcherSystemBase<TChannel>` currently:
- Extends `ComponentSystem`
- Uses `OnCreate()` to initialize `_previousAction`

After conversion:
```csharp
public abstract class DispatcherSystemBase<TChannel> : IEcsModuleSystem
    where TChannel : struct
{
    // ... same fields ...

    protected DispatcherSystemBase()
    {
        _previousAction = new ushort[InitialPreviousActionCapacity];
    }

    // No OnCreate override — initialization moved to constructor above.

    public void RegisterExecutor(ushort actionId, IActionExecutor<TChannel> executor)
    {
        _executors[actionId] = executor;
    }

    protected void EnsurePreviousActionCapacity(int requiredMinSize) { ... }

    // Abstract Execute — each subclass implements this.
    public abstract void Execute(ISimulationView view, float deltaTime);
}
```

**IMPORTANT:** Remove `OnCreate()` entirely. Move its body (`_previousAction = new ushort[...]`)
to the constructor. If the constructor was implicit (no explicit constructor), add one.

### Concrete dispatcher systems

`FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/LocomotionDispatcherSystem.cs`
`FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/WeaponDispatcherSystem.cs`
`FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/InteractionDispatcherSystem.cs`

All extend `DispatcherSystemBase<TChannel>`. Add `[UpdateInPhase(SystemPhase.Simulation)]` to each.
Change `protected override void OnUpdate()` to `public override void Execute(ISimulationView view, float deltaTime)`.
Replace `World.` with `view.`.

### CognitiveRuntimeModule system files

| File | Phase |
|------|-------|
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/ChannelArbitrationSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmDamageBridgeSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BTreeTickSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs` | `Simulation` |

`BTreeTickSystem` and `HsmTickSystem` have both `[UpdateInGroup(...)]` and `[UpdateAfter(...)]` attributes.
Replace `[UpdateInGroup(typeof(SimulationSystemGroup))]` with `[UpdateInPhase(SystemPhase.Simulation)]`.
Delete `[UpdateAfter(...)]` — ordering within simulation phase is enforced by array position in the module.

### Module file: CognitiveRuntimeModule

`FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/CognitiveRuntimeModule.cs`

Replace `RegisterSystems(SystemGroup group)` with:
```csharp
/// <summary>Systems that run in the Simulation phase.</summary>
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }
```

Populate in constructor:
```csharp
public CognitiveRuntimeModule(DoctrineRegistry registry)
{
    _registry = registry;
    SimulationSystems = new IEcsModuleSystem[]
    {
        new ChannelArbitrationSystem(),
        new HsmDamageBridgeSystem(),
        new BTreeTickSystem(_registry),
        new HsmTickSystem<BrainHsm128>(_registry),
        new HsmTickSystem<BrainHsm64>(_registry),
    };
}
```

### Module file: ActionDispatchModule

`FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/ActionDispatchModule.cs`

The module builds dispatcher systems by registering executors. Keep the executor registration logic
but expose the result as an array property.

Replace `RegisterSystems(SystemGroup group)` with:
```csharp
/// <summary>Systems that run in the Simulation phase.</summary>
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }
```

Build the dispatchers in the constructor (move the executor registration loop there):
```csharp
public ActionDispatchModule(
    (ushort, IActionExecutor<LocomotionChannel>)[] locoExecutors,
    (ushort, IActionExecutor<WeaponChannel>)[]? weaponExecutors = null,
    (ushort, IActionExecutor<InteractionChannel>)[]? interactionExecutors = null)
{
    _locoExecutors        = locoExecutors ?? throw new ArgumentNullException(nameof(locoExecutors));
    _weaponExecutors      = weaponExecutors ?? Array.Empty<(ushort, IActionExecutor<WeaponChannel>)>();
    _interactionExecutors = interactionExecutors ?? Array.Empty<(ushort, IActionExecutor<InteractionChannel>)>();

    var locoDispatcher = new LocomotionDispatcherSystem();
    foreach (var (id, exec) in _locoExecutors)
        locoDispatcher.RegisterExecutor(id, exec);

    var weaponDispatcher = new WeaponDispatcherSystem();
    foreach (var (id, exec) in _weaponExecutors)
        weaponDispatcher.RegisterExecutor(id, exec);

    var interactionDispatcher = new InteractionDispatcherSystem();
    foreach (var (id, exec) in _interactionExecutors)
        interactionDispatcher.RegisterExecutor(id, exec);

    SimulationSystems = new IEcsModuleSystem[]
    {
        locoDispatcher,
        weaponDispatcher,
        interactionDispatcher,
    };
}
```

Keep the private `_locoExecutors`, `_weaponExecutors`, `_interactionExecutors` fields only if still
needed (e.g. if some other code reads them). If they were only used in `RegisterSystems`, remove them.

Delete `RegisterSystems(SystemGroup group)`.

### Tests to update

`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Modules/CognitiveRuntimeModuleTests.cs`
`Hrot/Subsystems/Hrot.SimHost.Tests/ActionDispatchModuleTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeTickSystemTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/HsmDamageBridgeSystemTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/HsmTickSystemTests.cs` (if exists)

For module tests: change assertions to verify `SimulationSystems.Count` and element types.
For system tests: change from `world.AddSystem` + `world.Update()` to `system.Execute(world, dt)`.

`ActionDispatchModuleTests` verifies that the dispatchers have registered executors. After the
refactor, test via `module.SimulationSystems` — the dispatchers are built in the constructor,
so you can cast them and check executor counts:
```csharp
var module = new ActionDispatchModule(locoExecutors: ...);
var locoSys = (LocomotionDispatcherSystem)module.SimulationSystems[0];
// verify executor is registered by calling Execute and observing behavior
```

---

## T-RMF-11 — Convert Standalone CGF Systems and `DamageAssessmentModule`

### Standalone CGF system files

| File | Phase |
|------|-------|
| `Hrot/Engine/Hrot.Common/Systems/MissionControlExecutionSystem.cs` | `Input` |
| `Hrot/Subsystems/Hrot.CGF/Systems/MissionAdapterSystem.cs` | `Simulation` |
| `FDP/Toolkits/Fdp.Toolkits/Combat/Systems/HealthApplicationSystem.cs` | `Simulation` |
| `Hrot/Subsystems/Hrot.CGF/Systems/CgfThreatEvaluationSystem.cs` | `Simulation` |
| `Hrot/Subsystems/Hrot.CGF/Systems/Routing/RouteContextSystem.cs` | `Simulation` |

Apply universal conversion rules to each:
- Change `[UpdateInGroup(typeof(SimulationSystemGroup))]` → `[UpdateInPhase(SystemPhase.Simulation)]` (or `Input`).
- Change `[UpdateInGroup(typeof(InputSystemGroup))]` → `[UpdateInPhase(SystemPhase.Input)]`.
- Delete `[UpdateAfter(...)]` if present.
- `: ComponentSystem` → `: IEcsModuleSystem`
- `OnUpdate()` → `Execute(ISimulationView view, float deltaTime)`
- `World.` → `view.`

**World-mutation check:** Read each file before converting. If any system calls `World.SetComponent`,
`World.AddComponent`, `World.RemoveComponent` (direct mutation on the EntityRepository), apply the throw pattern.

`HealthApplicationSystem` currently has `[UpdateInGroup(typeof(SimulationSystemGroup))]`.

### DamageAssessmentModule

`FDP/Toolkits/Fdp.Toolkits/Combat/Modules/DamageAssessmentModule.cs`

Find and convert `DamageCalculationSystem.cs` first (it is the only system in this module):
- Phase: `Simulation`
- Apply universal rules

Then update `DamageAssessmentModule`:
- Replace `RegisterSystems(SystemGroup simGroup)` with:
```csharp
public IReadOnlyList<IEcsModuleSystem> SimulationSystems { get; }
```
- Populate in constructor:
```csharp
public DamageAssessmentModule()
{
    SimulationSystems = new IEcsModuleSystem[]
    {
        new DamageCalculationSystem(),
    };
}
```

If `DamageAssessmentModule` had no explicit constructor before, add a default one.

### Tests to update

`FDP/Toolkits/Fdp.Toolkits.Tests/Combat/DamageCalculationSystemTests.cs`
`FDP/Toolkits/Fdp.Toolkits.Tests/Combat/HealthApplicationSystemTests.cs`
`Hrot/Subsystems/Hrot.SimHost.Tests/Systems/MissionControlExecutionSystemTests.cs` (if it exists)

Update from `world.AddSystem` + `world.Update()` to `system.Execute(world, dt)`.

---

## T-RMF-12 — Convert `GenesisMaterializationSystem`

`Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs`

This is the canonical example of the throw-not-return pattern.

```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public sealed class GenesisMaterializationSystem : IEcsModuleSystem
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

        var cmd = new EntityCommandBuffer();
        try
        {
            MaterializePassengers(view, cmd);
            MaterializeVehicle(view, cmd);
            MaterializeHierarchy(view, cmd);
            MaterializeRoute(view, cmd);
            MaterializeTargets(view, cmd);
            cmd.Playback(repo);
        }
        finally
        {
            cmd.Dispose();
        }
    }

    // private helpers: MaterializePassengers, MaterializeVehicle, MaterializeHierarchy,
    //                  MaterializeRoute, MaterializeTargets
    // Replace all `World.IsAlive(...)` with `view.IsAlive(...)`
    // Replace all `World.SetComponent(...)` with `repo.SetComponent(...)`
    // The `World` field is gone — use `view` for reads and `repo` for mutations.
}
```

**Note:** `cmd.Playback(World)` at the end must become `cmd.Playback(repo)` since `cmd.Playback`
takes an `EntityRepository` directly. All private `Materialize*` helpers that currently use
`World.SetComponent` should be updated to use `repo.SetComponent`.

All private helpers that use `view.Query(...)` / `view.GetManagedComponentRO(...)` stay as-is
(they use the read-only view parameter).

No test file for `GenesisMaterializationSystem` needs updating — if there is one, update it.

---

## Final Verification

After all changes:

1. Run: `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`
   - Expected: **Build succeeded. 0 Error(s).**

2. Run the test suites that are most likely affected:
   ```
   dotnet test FDP/Engine/Fdp.ModuleHost.Tests/Fdp.ModuleHost.Tests.csproj --no-build
   dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
   dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
   ```
   - Expected: 0 new failures (pre-existing 3 skips in SimHost.Tests are acceptable).

3. Run the full solution test if time allows:
   ```
   dotnet test IOS-IG-SimHost.sln --no-build
   ```

---

## Report Format

Write `BATCH-02-REPORT.md` in `.dev/replay-and-modules/reports/` with:
- Task status table (T-RMF-06..12)
- Build result
- Test result summary
- Files created / modified
- Any issues encountered and how they were resolved
- Any deviations from these instructions and the rationale

# BATCH-05 Implementation Instructions
**Tasks**: T-RMF-24, T-RMF-25  
**Phase**: Phase 5 — Legacy Removal  
**Agent**: Claude Sonnet 4.6

---

## Overview

Delete the five legacy source files and fix all resulting compile errors.

**Files to DELETE:**
1. `FDP/Engine/Fdp.Core/ComponentSystem.cs`
2. `FDP/Engine/Fdp.Core/SystemGroup.cs`
3. `FDP/Engine/Fdp.Core/StandardSystemGroups.cs`
4. `FDP/Engine/Fdp.ModuleHost/SystemGroupExtensions.cs` — references ComponentSystem/SystemGroup; also delete
5. `Hrot/Engine/Hrot.Common/Infrastructure/CgfInputGroupAdapter.cs`
6. `Hrot/Engine/Hrot.Common/Infrastructure/LegacySystemGroupAdapters.cs`

**Additional dead code to DELETE (no longer called from production):**
7. `Hrot/Subsystems/Hrot.SimHost/Modules/SimulationLogicModule.cs` — superseded by `SimHostCoreLogicPack`
8. `Hrot/Subsystems/Hrot.SimHost.Tests/SimulationLogicModuleTests.cs` — tests of the above

**Fdp.Core.Tests — DELETE these test files (they test deleted types):**
9. `FDP/Engine/Fdp.Core.Tests/SystemTests.cs`
10. `FDP/Engine/Fdp.Core.Tests/ComponentTests.cs`

---

## Required code changes after deletions

### 1. `FDP/Engine/Fdp.Core/SystemAttributes.cs`

Read the file. The `UpdateInGroupAttribute` constructor currently validates that the `groupType` derives from `SystemGroup`. After `SystemGroup` is deleted, this constraint must be removed. Change the constructor to only check for null:

```csharp
public UpdateInGroupAttribute(Type groupType)
{
    GroupType = groupType ?? throw new ArgumentNullException(nameof(groupType));
}
```

Remove any reference to `SystemGroup` from the file.

---

### 2. Convert `UpdateEntityDescriptorRequestSystem` to `IEcsModuleSystem`

**File**: `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/UpdateEntityDescriptorRequestSystem.cs`

Read the full file. This class extends `ComponentSystem`. Convert it to `IEcsModuleSystem`:

1. Remove `using Fdp.Core;` import (or keep it only if needed for non-ComponentSystem types)
2. Add `using Fdp.ModuleHost.Abstractions;` if not present  
3. Add `using Fdp.ModuleHost;` if not present
4. Change `public sealed class UpdateEntityDescriptorRequestSystem : ComponentSystem` to `public sealed class UpdateEntityDescriptorRequestSystem : IEcsModuleSystem, IDisposable`
5. Add `[UpdateInPhase(SystemPhase.Input)]` attribute (these attribute-update systems run in Input phase since they were added to an InputGroup in SimHostApp)
6. Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`. The body of `OnUpdate` remains the same (it reads from `_reader.Take()` and calls `ProcessRequest`).
7. Replace `protected override void OnDestroy()` with `public void Dispose()` — same body.
8. The `World` property is used inside `ProcessGeoSpatialUpdate`:
   ```csharp
   var view = (ISimulationView)World;
   ```
   After conversion, `Execute(ISimulationView view, ...)` receives `view` as a parameter. You need to pass `view` into `ProcessRequest` and then into `ProcessGeoSpatialUpdate`. One approach:
   - Store `view` in a field temporarily: add `private ISimulationView? _currentView;`
   - Set `_currentView = view;` at the start of `Execute`
   - Replace `var view = (ISimulationView)World;` in `ProcessGeoSpatialUpdate` with `var view = _currentView!;`
   - Or better: change method signatures so that `ProcessRequest(UpdateEntityDescriptorRequest req, ISimulationView view)` and `ProcessGeoSpatialUpdate(UpdateEntityDescriptorRequest req, Entity entity, ISimulationView view)` etc.
   
   Use the second approach (pass `view` through method parameters) for thread-safety:
   - `Execute(ISimulationView view, float deltaTime)` calls `ProcessRequest(sample.Data, view)`
   - `ProcessRequest(UpdateEntityDescriptorRequest req, ISimulationView view)` calls `ProcessGeoSpatialUpdate(req, entity, view)` or `ProcessMapVisualOverlayUpdate(req, entity, view)`
   - Replace `var view = (ISimulationView)World;` in `ProcessGeoSpatialUpdate` by using the passed `view` parameter
   - In `ProcessMapVisualOverlayUpdate`, use the passed `view` to get `GetComponentRW` — likely needs `(EntityRepository)view`

9. The `World.SetComponentRW<T>` and similar calls after `ProcessGeoSpatialUpdate` — check if those are used and replace `World` with `(EntityRepository)view` appropriately.

---

### 3. Convert `UpdateEntityAttributeRequestSystem` to `IEcsModuleSystem`

**File**: `Hrot/Network/Hrot.Network.NED/Systems/UpdateEntityAttributeRequestSystem.cs`

Read the full file. This class extends `ComponentSystem`. Convert it:

1. Change inheritance: `public sealed class UpdateEntityAttributeRequestSystem : IEcsModuleSystem, IDisposable`
2. Add `[UpdateInPhase(SystemPhase.Input)]`
3. Replace `protected override void OnUpdate()` with:
   ```csharp
   public void Execute(ISimulationView view, float deltaTime)
   {
       _requestSource.ProcessRequests(req => ProcessRequest(req, (EntityRepository)view));
   }
   ```
4. Replace `protected override void OnDestroy()` with:
   ```csharp
   public void Dispose()
   {
       (_requestSource as IDisposable)?.Dispose();
       (_ackSink       as IDisposable)?.Dispose();
   }
   ```
5. Update `ProcessRequest` signature to accept `EntityRepository repo` as a second parameter: `private void ProcessRequest(UpdateEntityAttributeRequest req, EntityRepository repo)`
6. Inside `ProcessRequest`, replace `World` with `repo`: `var ecsPatchCtx = EcsPatchContext.Create(repo, entity);`
7. Remove `using Fdp.Core;` if no longer needed

---

### 4. Update `INetworkFactory.CreateSimHostAttributeUpdateSystems`

**File**: `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs`

Read the file. Change:
```csharp
IReadOnlyList<ComponentSystem> CreateSimHostAttributeUpdateSystems();
```
to:
```csharp
IReadOnlyList<IEcsModuleSystem> CreateSimHostAttributeUpdateSystems();
```

Remove `using Fdp.Core;` if no other things in that file use it (check first). Add `using Fdp.ModuleHost.Abstractions;` if not present.

---

### 5. Update `NedNetworkFactory.CreateSimHostAttributeUpdateSystems`

**File**: `Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs`

Read the file. Change:
```csharp
public IReadOnlyList<Fdp.Core.ComponentSystem> CreateSimHostAttributeUpdateSystems()
{
    if (_participant == null) return System.Array.Empty<Fdp.Core.ComponentSystem>();
    ...
    return new Fdp.Core.ComponentSystem[] { ... };
}
```
to:
```csharp
public IReadOnlyList<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> CreateSimHostAttributeUpdateSystems()
{
    if (_participant == null) return System.Array.Empty<Fdp.ModuleHost.Abstractions.IEcsModuleSystem>();
    ...
    return new Fdp.ModuleHost.Abstractions.IEcsModuleSystem[] { ... };
}
```

Or use a proper `using` at the top and just use `IEcsModuleSystem`.

---

### 6. Update `BdcNetworkFactory.CreateSimHostAttributeUpdateSystems`

**File**: `Hrot/Network/Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`

Same pattern — change `ComponentSystem` to `IEcsModuleSystem` in return type.

---

### 7. Update `OfflineNetworkFactory.CreateSimHostAttributeUpdateSystems`

**File**: `Hrot/Subsystems/Hrot.Editor/OfflineNetworkFactory.cs`

Read the file. Change the `CreateSimHostAttributeUpdateSystems` implementation return type from `ComponentSystem` to `IEcsModuleSystem`.

---

### 8. Update `MockNetworkFactory.CreateSimHostAttributeUpdateSystems` (test file)

**File**: `Hrot/Runner/Hrot.ClusterRunner.Tests/HexagonalBoundaryTests.cs`

Read the file. Find the `CreateSimHostAttributeUpdateSystems` method (appears twice, at lines ~288 and ~342). Change return type from `IReadOnlyList<Fdp.Core.ComponentSystem>` to `IReadOnlyList<Fdp.ModuleHost.Abstractions.IEcsModuleSystem>`.

---

### 9. Update `MockNetworkFactory.CreateSimHostAttributeUpdateSystems` (integration test)

**File**: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/MockNetworkFactory.cs`

Same pattern as above.

---

### 10. Update `SimHostApp.cs` — remove SystemGroup + CgfInputGroupAdapter usage

**File**: `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

Read the current state of the file around line 344-355. The current code is:
```csharp
if (nodeFactory != null)
{
    var factoryGroup = new SystemGroup();
    factoryGroup.Create(_world);
    foreach (var sys in nodeFactory.CreateSimHostAttributeUpdateSystems())
        factoryGroup.AddSystem(sys);
    _kernel.RegisterGlobalSystem(new CgfInputGroupAdapter(factoryGroup));
}
```

Since `CreateSimHostAttributeUpdateSystems` now returns `IReadOnlyList<IEcsModuleSystem>`, and the systems have `[UpdateInPhase(SystemPhase.Input)]`, they can be registered directly via `_kernel.RegisterGlobalSystem(sys)` or added to `allInputSystems`. Replace the block with:
```csharp
if (nodeFactory != null)
{
    foreach (var sys in nodeFactory.CreateSimHostAttributeUpdateSystems())
        allInputSystems.Add(sys);
}
```

> Note: `allInputSystems` is the `List<IEcsModuleSystem>` that gets passed to `TogglableInputGroup`. The factory systems will now run in the Input phase alongside other input systems. This is correct behavior.

Remove the `using` for `CgfInputGroupAdapter` or `LegacySystemGroupAdapters` if present at the top of the file.

---

### 11. Convert `SimMapRenderSystem` to `IEcsModuleSystem`

**File**: `Hrot/Subsystems/Hrot.SimHost/Systems/SimMapRenderSystem.cs`

Read the file. This class extends `ComponentSystem` and uses `[UpdateInGroup(typeof(PresentationSystemGroup))]`.

1. Remove `[UpdateInGroup(typeof(PresentationSystemGroup))]`
2. Add `[UpdateInPhase(SystemPhase.Export)]` (export phase is the last main-thread phase)
3. Change `public sealed class SimMapRenderSystem : ComponentSystem` to `public sealed class SimMapRenderSystem : IEcsModuleSystem`
4. Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`
5. Inside `Execute`, replace `World.HasSingletonManaged<T>()` with `view.HasSingletonManaged<T>()` (or cast to `EntityRepository`)  
   - `World.HasSingletonManaged<Hrot.Common.ActivePerspective>()` → `((EntityRepository)view).HasSingletonManaged<Hrot.Common.ActivePerspective>()`
   - Or check if `ISimulationView` has `HasSingletonManaged` — if not, cast to `EntityRepository`
6. Similarly `World.GetSingletonManaged<Hrot.Common.ActivePerspective>()` → cast to `EntityRepository`
7. Remove `using Fdp.Core;` if no longer needed; add `using Fdp.ModuleHost.Abstractions;`

---

### 12. Update `SimPresentationModule.RegisterSystems`

**File**: `Hrot/Subsystems/Hrot.SimHost/Modules/SimPresentationModule.cs`

Read the file. Change:
```csharp
public void RegisterSystems(SystemGroup group) =>
    group.AddSystem(_renderSystem);
```
to:
```csharp
public void RegisterSystems(ISystemRegistry registry) =>
    registry.RegisterSystem(_renderSystem);
```

Add `using Fdp.ModuleHost.Abstractions;` if not present. Remove `using Fdp.Core;` if no longer needed.

---

### 13. Update `PresentationModuleTests.cs`

**File**: `Hrot/Subsystems/Hrot.SimHost.Tests/PresentationModuleTests.cs`

Read the file. The tests currently create `SystemGroup`, call `module.RegisterSystems(group)`, then call `group.Run()`.

After the changes, `SystemGroup` is gone. Update the tests as follows:

1. Remove `using Fdp.Core;`
2. Remove the `CreateGroup(EntityRepository world)` helper method
3. Update `SimPresentationModule_DrawsCalled_WhenSimPerspectiveActive` test:
   - Remove lines creating group and calling RegisterSystems
   - Instead call `module.RenderSystem.Execute(_world, 0f)` directly
   
4. Update `SimPresentationModule_DoesNotDraw_WhenOtherPerspectiveActive` test: same pattern — call `module.RenderSystem.Execute(_world, 0f)` directly

5. Update `SimPresentationModule_RegistersOneSystem_InPresentationGroup` test:
   - Remove SystemGroup creation
   - The test assertion that `s is SimMapRenderSystem` becomes: `Assert.IsType<SimMapRenderSystem>(simModule.RenderSystem)`

6. `SimPresentationModule_ProductionCanvas_IsSameAsProvided` test — no SystemGroup in it, no change needed.

---

### 14. Convert `EditorCargoSystem` to `IEcsModuleSystem`

**File**: `Hrot/Subsystems/Hrot.Editor/Systems/EditorCargoSystem.cs`

Read the file. Convert:

1. Remove `using Fdp.Core;` 
2. Add `using Fdp.ModuleHost.Abstractions;`
3. Change `public sealed class EditorCargoSystem : ComponentSystem` to `public sealed class EditorCargoSystem : IEcsModuleSystem`
4. Add `[UpdateInPhase(SystemPhase.Simulation)]`
5. Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`
6. Replace ALL uses of `World` with appropriate calls using `view`:
   - `World.Bus.Read<EmbarkEntityCommand>()` → `view.ReadEvents<EmbarkEntityCommand>()`  
   - `World.Bus.Read<DisembarkEntityCommand>()` → `view.ReadEvents<DisembarkEntityCommand>()`
   - `World.IsAlive(entity)` → `view.IsAlive(entity)`
   - `World.HasComponent<T>(entity)` → `view.HasComponent<T>(entity)`
   - `World.GetComponentRW<T>(entity)` → `((EntityRepository)view).GetComponentRW<T>(entity)`
   - `World.AddComponent(entity, component)` → `((EntityRepository)view).AddComponent(entity, component)`
   - `World.HasComponent<IsEmbarkedTag>(...)` → check if IsEmbarkedTag is unmanaged; if so, `view.HasComponent<IsEmbarkedTag>(entity)`
   - `World.GetComponent<IsEmbarkedTag>(entity)` → use view or cast

> Tip: Look at how other converted systems (like `FireProcessingSystem`) handle the conversion pattern in `Hrot/Subsystems/Hrot.SimHost/Modules/CombatModule.cs` or related files.

---

### 15. Convert `EditorPerceptionSetupSystem` to `IEcsModuleSystem`

**File**: `Hrot/Subsystems/Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs`

Read the file. Same pattern as EditorCargoSystem:

1. Remove `using Fdp.Core;`
2. Add `using Fdp.ModuleHost.Abstractions;` and `using Fdp.Core;` (for GlobalTime, SimTransform)
3. Change base class to `IEcsModuleSystem`
4. Add `[UpdateInPhase(SystemPhase.Simulation)]`
5. Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`
6. Replace `World.Bus.Read<SeedTargetCommand>()` with `view.ReadEvents<SeedTargetCommand>()`
7. Replace `World.IsAlive(entity)` → `view.IsAlive(entity)`
8. Replace `World.HasComponent<TargetMemory>(entity)` → `view.HasComponent<TargetMemory>(entity)` (if TargetMemory is unmanaged) or use managed variant
9. Replace `World.HasComponent<SimTransform>(entity)` → `view.HasComponent<SimTransform>(entity)`
10. Replace `World.GetComponentRW<TargetMemory>(entity)` → `((EntityRepository)view).GetComponentRW<TargetMemory>(entity)`
11. Replace `World.GetComponent<SimTransform>(entity)` → `view.GetComponentRO<SimTransform>(entity)`
12. Replace `World.HasSingleton<GlobalTime>()` → `((EntityRepository)view).HasSingleton<GlobalTime>()`
13. Replace `World.GetSingletonUnmanaged<GlobalTime>()` → `view.GetSingleton...` or cast to EntityRepository

---

### 16. Convert `EditorZoneAuthoringSystem` to `IEcsModuleSystem`

**File**: `Hrot/Subsystems/Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs`

Read the full file. Same conversion pattern:

1. Remove `using Fdp.Core;`
2. Change base class to `IEcsModuleSystem`
3. Add `[UpdateInPhase(SystemPhase.Simulation)]`
4. Replace `protected override void OnUpdate()` with `public void Execute(ISimulationView view, float deltaTime)`
5. Replace `World.Bus.ReadManaged<SpawnZoneObstacleCommand>()` with `view.ReadManagedEvents<SpawnZoneObstacleCommand>()`
6. Replace `World.Bus.ReadManaged<UpdateZoneConfigCommand>()` with `view.ReadManagedEvents<UpdateZoneConfigCommand>()`
7. Replace `World.CreateEntity()` → `((EntityRepository)view).CreateEntity()`
8. Replace `World.AddComponent(entity, ...)` → `((EntityRepository)view).AddComponent(entity, ...)`
9. Replace `World.SetSingleton(...)` → `((EntityRepository)view).SetSingleton(...)`
10. Add `using Fdp.ModuleHost.Abstractions;`

---

### 17. Update `EditorSystemsModule` to use `IEcsModuleSystem` directly

**File**: `Hrot/Subsystems/Hrot.Editor/Modules/EditorSystemsModule.cs`

Read the file. It currently creates systems with `Create(world)` and ticks with `Run()`. After conversion:

1. Remove `using Fdp.Core;`
2. Remove the `EntityRepository world` constructor parameter (no longer needed since IEcsModuleSystem doesn't need Create)
3. Remove `_cargo.Create(world); _perception.Create(world); _zone.Create(world);` lines
4. The constructor becomes:
   ```csharp
   public EditorSystemsModule(ZoneManagerService? zoneService = null)
   {
       _cargo      = new EditorCargoSystem();
       _perception = new EditorPerceptionSetupSystem();
       _zone       = new EditorZoneAuthoringSystem(zoneService);
   }
   ```
5. Change `Tick(ISimulationView view, float deltaTime)` to:
   ```csharp
   public void Tick(ISimulationView view, float deltaTime)
   {
       _cargo.Execute(view, deltaTime);
       _perception.Execute(view, deltaTime);
       _zone.Execute(view, deltaTime);
   }
   ```

> **Important**: Check all callers of `new EditorSystemsModule(world, ...)` — they need to change to `new EditorSystemsModule(zoneService)` or `new EditorSystemsModule()`.

---

### 18. Update callers of `EditorSystemsModule` constructor

Find all usages of `new EditorSystemsModule(world` or `new EditorSystemsModule(entityRepository`:

Run `grep_search` for `new EditorSystemsModule`. Update each caller to remove the `world` parameter. If there's only one parameter (world) and no zoneService, the call becomes `new EditorSystemsModule()`. If there's a zoneService: `new EditorSystemsModule(zoneService)`.

---

## Files NOT to change

- `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs` — already uses `IEcsModuleSystem` arrays
- `FDP/Engine/Fdp.ModuleHost/Scheduling/TogglableSimulationGroup.cs` etc. — already correct
- Any file that uses `ISystemGroup` (the interface) — that's fine, it's not being deleted

---

## Build and Test

After all changes, run:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2

# 1. Build
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"

# 2. SimHost tests  
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# 3. ClusterRunner tests
dotnet test Hrot/Runner/Hrot.ClusterRunner.Tests/Hrot.ClusterRunner.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# 4. NED tests
dotnet test Hrot/Network/Hrot.Network.NED.Tests/Hrot.Network.NED.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2

# 5. Fdp.Core.Tests (to confirm deleted test files don't cause issues)
dotnet test FDP/Engine/Fdp.Core.Tests/Fdp.Core.Tests.csproj --no-build 2>&1 | Select-String "Passed!|Failed!" | Select-Object -Last 2
```

**Expected**: Build 0 errors. All remaining tests pass. Test count will be lower than before due to deleted dead-code test files.

> The pre-existing `EntityMission_MovesEntity` failure (in SimHost.Tests) is expected. It was present before this batch.

---

## FDP Submodule Commit

After build succeeds:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2\FDP
git add -A
git commit -m "T-RMF-24: Delete ComponentSystem, SystemGroup, StandardSystemGroups, SystemGroupExtensions"
```

Then the outer repo:
```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
git add -A
git commit -m "T-RMF-24/25: Phase 5 - delete legacy ComponentSystem/SystemGroup/adapters; convert remaining systems"
```

---

## Summary of files to change / delete

**FDP submodule — DELETE (4 files):**
1. `FDP/Engine/Fdp.Core/ComponentSystem.cs`
2. `FDP/Engine/Fdp.Core/SystemGroup.cs`
3. `FDP/Engine/Fdp.Core/StandardSystemGroups.cs`
4. `FDP/Engine/Fdp.ModuleHost/SystemGroupExtensions.cs`

**FDP submodule — Fdp.Core.Tests — DELETE (2 files):**
5. `FDP/Engine/Fdp.Core.Tests/SystemTests.cs`
6. `FDP/Engine/Fdp.Core.Tests/ComponentTests.cs`

**FDP submodule — UPDATE (1 file):**
7. `FDP/Engine/Fdp.Core/SystemAttributes.cs` — remove SystemGroup constraint

**Hrot — DELETE (4 files):**
8. `Hrot/Engine/Hrot.Common/Infrastructure/CgfInputGroupAdapter.cs`
9. `Hrot/Engine/Hrot.Common/Infrastructure/LegacySystemGroupAdapters.cs`
10. `Hrot/Subsystems/Hrot.SimHost/Modules/SimulationLogicModule.cs`
11. `Hrot/Subsystems/Hrot.SimHost.Tests/SimulationLogicModuleTests.cs`

**Hrot — UPDATE (13+ files):**
12. `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs`
13. `Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs`
14. `Hrot/Network/Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`
15. `Hrot/Subsystems/Hrot.Editor/OfflineNetworkFactory.cs`
16. `Hrot/Runner/Hrot.ClusterRunner.Tests/HexagonalBoundaryTests.cs`
17. `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/MockNetworkFactory.cs`
18. `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/UpdateEntityDescriptorRequestSystem.cs`
19. `Hrot/Network/Hrot.Network.NED/Systems/UpdateEntityAttributeRequestSystem.cs`
20. `Hrot/Subsystems/Hrot.SimHost/Systems/SimMapRenderSystem.cs`
21. `Hrot/Subsystems/Hrot.SimHost/Modules/SimPresentationModule.cs`
22. `Hrot/Subsystems/Hrot.SimHost.Tests/PresentationModuleTests.cs`
23. `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
24. `Hrot/Subsystems/Hrot.Editor/Systems/EditorCargoSystem.cs`
25. `Hrot/Subsystems/Hrot.Editor/Systems/EditorPerceptionSetupSystem.cs`
26. `Hrot/Subsystems/Hrot.Editor/Systems/EditorZoneAuthoringSystem.cs`
27. `Hrot/Subsystems/Hrot.Editor/Modules/EditorSystemsModule.cs`
28. Any callers of `new EditorSystemsModule(world, ...)` — fix constructor call

# BATCH-04 Instructions — Serialization, Load Guards & ORBAT UI Stubs

## Context

**Repository:** `d:\Work\IOS-IG-SimHost-FDP-2`
**Build command:** `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`
**Test command:** `dotnet test IOS-IG-SimHost.sln --no-build --nologo`

**Design reference:** `.dev/commander-subordinates/DESIGN.md`
**Task specifications:** `.dev/commander-subordinates/TASK-DETAIL.md`

## Current State

All Phase 1–3 tasks and Phase 5 runtime infrastructure are complete.
The `InitialUnitSubordinateIntent` component (`ComponentId = 184`) exists in
`Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` (namespace `Hrot.Common.Serializers`).
The `UnitSubordinate` component exists in `FDP/Engine/Fdp.Core/CommandHierarchy/UnitSubordinate.cs`
(namespace `Fdp.Core.CommandHierarchy`, `ComponentId = 183`).
The `UnitRoster` component exists in `FDP/Engine/Fdp.Core/CommandHierarchy/UnitRoster.cs`
(namespace `Fdp.Core.CommandHierarchy`, `ComponentId = 182`).

**IMPORTANT — Bus.Read<T>() is non-draining.** Every system that calls `Read<T>()` in the same
tick sees the same events. Do NOT double-register any system.

## Tasks

---

### TASK-CS013 — UnitSubordinateTranslator (IEntityScenarioTranslator)

**Reference:** TASK-DETAIL.md § TASK-CS013

**Create** `Hrot/Subsystems/Hrot.SimHost/Serializers/UnitSubordinateTranslator.cs`.
**Modify** `Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs`.

**Translator contract:**
- Pattern: identical to `PassengerBufferTranslator` (same file for reference).
- `GetConsumedComponentsMask()`: set the bit for `UnitSubordinate`
  (`ComponentTypeRegistry.GetId(typeof(UnitSubordinate))`).
- `IsExtractionSafe`: not overridden (default `false` is correct).
- `CanTranslate`: return `true` only when `repo.HasComponent<UnitSubordinate>(entity)`
  AND `!repo.GetComponent<UnitSubordinate>(entity).Commander.IsNull`.
- `Extract`:
  - Get `UnitSubordinate sub = repo.GetComponent<UnitSubordinate>(entity)`.
  - If `sub.Commander.IsNull` → return `new Dictionary<string, object>()`.
  - Otherwise: `commanderGuid = resolver.Resolve(sub.Commander)`;
    `designation = (int)sub.Designation`.
  - Return dict with key `"UnitSubordinate"` containing a `JsonObject` with
    `"commanderGuid"` and `"designation"`.
- `Inject`:
  - Read `"UnitSubordinate"` from dict; if missing or wrong type → return.
  - Extract `commanderGuidStr` (string) and `designation` (int).
  - `Entity resolved = resolver.Resolve(commanderGuidStr)`.
  - If `resolved.IsNull || !repo.IsAlive(resolved)` → log warning with
    `FdpLog<UnitSubordinateTranslator>.Warn(...)`, attach intent with
    `CommanderNetworkId = 0` (materialization will skip it gracefully).
  - Else if `!repo.HasComponent<NetworkIdentity>(resolved)` → same warning path.
  - Else: `long networkId = repo.GetComponent<NetworkIdentity>(resolved).Value`.
  - Attach:
    ```csharp
    repo.SetManagedComponent(entity, new InitialUnitSubordinateIntent
    {
        CommanderNetworkId = networkId,
        Designation = (TacticalDesignation)designation,
    });
    ```
- `GetOutputDomKeys()`: return `Array.Empty<string>()`.

**Required usings** (in `UnitSubordinateTranslator.cs`):
```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Core.Logging;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Hrot.Common.Serializers;
```

**Register** in `HrotScenarioSerializerFactory.Build()`:
Add `.RegisterTranslator(new UnitSubordinateTranslator())` after the existing `PersonalRouteRefTranslator` line.

**Tests** — create `Hrot/Subsystems/Hrot.SimHost.Tests/UnitSubordinateTranslatorTests.cs`:
Verify TASK-CS013 success conditions 1–4 from TASK-DETAIL.md:
1. Extract with non-null commander → dict has `commanderGuid` and `designation`.
2. Extract with null commander → empty dict.
3. Inject → entity has `InitialUnitSubordinateIntent` with correct `CommanderNetworkId = 77`
   and `Designation = Wingman`.
4. `HrotScenarioSerializerFactory.Build()` result has `UnitSubordinate` in consumed mask.

---

### TASK-CS014 — GenesisMaterializationSystem: MaterializeUnitSubordinate

**Reference:** TASK-DETAIL.md § TASK-CS014

**Modify** `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs`.

Add `using Fdp.Core.CommandHierarchy;` and `using Fdp.Core.Logging;` at the top.

Add the following private method after `MaterializeTargets`:

```csharp
private unsafe void MaterializeUnitSubordinate(ISimulationView view, EntityCommandBuffer cmd, EntityRepository repo)
{
    foreach (var entity in view.Query().WithManaged<InitialUnitSubordinateIntent>().Build())
    {
        var intent = view.GetManagedComponentRO<InitialUnitSubordinateIntent>(entity);

        if (intent.CommanderNetworkId == 0)
        {
            cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
            continue;
        }

        if (!_entityMap.TryGetEntity(intent.CommanderNetworkId, out var commander) || !view.IsAlive(commander))
        {
            // Escape hatch: if the entity is already Active, the commander will never arrive.
            if (repo.GetLifecycleState(entity) == EntityLifecycle.Active)
            {
                FdpLog<GenesisMaterializationSystem>.Warn(
                    $"[GenesisMaterializationSystem] Commander network ID {intent.CommanderNetworkId} " +
                    $"not found for entity {entity.Index}; dropping intent.");
                cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
            }
            // Otherwise retry next tick.
            continue;
        }

        // Capacity check — do not set UnitSubordinate if roster is full.
        var roster = view.HasComponent<UnitRoster>(commander)
            ? view.GetComponent<UnitRoster>(commander)
            : new UnitRoster();

        if (roster.Count >= UnitRoster.Capacity)
        {
            FdpLog<GenesisMaterializationSystem>.Warn(
                $"[GenesisMaterializationSystem] Commander {commander.Index} roster is full; " +
                $"cannot add subordinate {entity.Index}.");
            cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
            continue;
        }

        // Atomic write: subordinate component + roster append.
        repo.SetComponent(entity, new UnitSubordinate
        {
            Commander   = commander,
            Designation = intent.Designation,
        });

        roster.SubordinateEntities[roster.Count]  = (long)entity.PackedValue;
        roster.TacticalDesignations[roster.Count] = (ushort)intent.Designation;
        roster.Count++;
        repo.SetComponent(commander, roster);

        cmd.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity);
    }
}
```

**NOTE on `UnitRoster` fields:**
`UnitRoster` is an unsafe struct with `fixed long SubordinateEntities[16]` and
`fixed ushort TacticalDesignations[16]`. Use `(long)entity.PackedValue` to store an entity
and `(ushort)designation` for the tactical designation slot. This is the identical pattern
used in `UnitHierarchySystem.cs` lines 137–138.

Call it from `Execute` alongside the other materialization helpers, **after** `MaterializeTargets`:
```csharp
MaterializeUnitSubordinate(view, cmd, repo);
```

**Tests** — create `Hrot/Subsystems/Hrot.SimHost.Tests/GenesisMaterializationSystemTests.cs`:
Verify TASK-CS014 success conditions 1–4 from TASK-DETAIL.md:
1. Normal resolution — after one tick, subordinate has `UnitSubordinate`, commander `UnitRoster.Count == 1`, intent removed.
2. Retry-until-resolved — intent persists after tick 1 (commander not in map), resolves after tick 2.
3. Capacity exceeded — `UnitRoster.Count == 16` (capacity), no `UnitSubordinate` set, intent removed.
4. Lifecycle escape hatch — entity with `EntityLifecycle.Active` and unresolvable `CommanderNetworkId`
   eventually has intent removed.

**Test helper:** After spawning entities, manually transition them from `Constructing` to `Active`
with `repo.SetLifecycleState(entity, EntityLifecycle.Active)` when needed. The `GenesisMaterializationSystem`
is constructed with `new GenesisMaterializationSystem(networkEntityMap)`. Use `EntityRepository`
directly (`view` parameter) — the system asserts `view is EntityRepository`.

---

### TASK-CS026 — Cluster Load Handlers: InitialUnitSubordinateIntent Drain Check

**Reference:** TASK-DETAIL.md § TASK-CS026

**Modify two files:**

**File 1:** `Hrot/Subsystems/Hrot.SimHost/Orchestration/Handlers/HrotScenarioLoadHandler.cs`

Add `using Hrot.Common.Serializers;` if not already present.

In `DrainDeferredAcks()`, after the existing `InitialRouteIntent` guard line, add:
```csharp
foreach (var _ in _world.Query().WithManaged<InitialUnitSubordinateIntent>().Build()) return;
```

**File 2:** `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfScenarioLoadHandler.cs`

Add `using Hrot.Common.Serializers;` if not already present.

In `DrainDeferredAcks()`, after the existing `InitialRouteIntent` guard line, add:
```csharp
foreach (var _ in _world.Query().WithManaged<InitialUnitSubordinateIntent>().Build()) return;
```

**Tests:**

In `Hrot/Subsystems/Hrot.SimHost.Tests/HrotScenarioLoadHandlerTests.cs`, add two tests:

1. `DrainDeferredAcks_WithPendingSubordinateIntent_DoesNotComplete`:
   Spawn an entity in `_repo`; attach `InitialUnitSubordinateIntent` via
   `_repo.SetManagedComponent(entity, new InitialUnitSubordinateIntent { CommanderNetworkId = 1 })`.
   Call `DrainDeferredAcks()`. Assert the task returned from `PrepareAsync(PrepareState/OperatingLive)`
   is NOT completed yet.

2. `DrainDeferredAcks_AfterSubordinateIntentRemoved_Completes`:
   Same setup as above, then remove the intent via `_repo.RemoveManagedComponent<InitialUnitSubordinateIntent>(entity)`.
   Call `DrainDeferredAcks()`. Assert the task completes.

In `Hrot/Subsystems/Hrot.SimHost.Tests/CgfScenarioLoadHandlerTests.cs`, add two analogous tests.

---

### TASK-CS027 — StagingEntityExtractor: Remap CommanderNetworkId on Load

**Reference:** TASK-DETAIL.md § TASK-CS027

**Modify** `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs`.

Add `using Hrot.Common.Serializers;` to the top.

In `RemapComponentNetworkIds`, after the last existing `else if` block (for `InitialTargetsIntent`),
add:
```csharp
else if (comps[ci] is InitialUnitSubordinateIntent subIntent)
{
    comps[ci] = new InitialUnitSubordinateIntent
    {
        CommanderNetworkId = oldToNewMap.TryGetValue(subIntent.CommanderNetworkId, out long newId)
            ? newId
            : subIntent.CommanderNetworkId,
        Designation = subIntent.Designation,
    };
}
```

**Tests** — add to `Hrot/Subsystems/Hrot.SimHost.Tests/StagingEntityExtractorTests.cs`:

1. `RemapComponentNetworkIds_RemapsSubordinateIntent`:
   Build a `List<object>` containing an `InitialUnitSubordinateIntent { CommanderNetworkId = 10, Designation = Wingman }`;
   supply `oldToNewMap = { [10L] = 99L }`.
   Assert remapped intent has `CommanderNetworkId == 99`.

2. `RemapComponentNetworkIds_PreservesUnknownCommanderNetworkId`:
   Supply `InitialUnitSubordinateIntent { CommanderNetworkId = 55 }` with an empty `oldToNewMap`.
   Assert `CommanderNetworkId` remains `55`.

`RemapComponentNetworkIds` is `private static` — call it via reflection or make it `internal` for test access.
Examine how the existing `StagingEntityExtractorTests` call this method to follow the same pattern.

---

### TASK-CS017 — OrbatNodeViewModel: CanAcceptSubordinates Flag

**Reference:** TASK-DETAIL.md § TASK-CS017

**Both OrbatNodeViewModel files are identical and share the same namespace.**
Modify **both**:
- `Hrot/Engine/Hrot.UI.Common/Models/OrbatNodeViewModel.cs`
- `Hrot/Engine/Hrot.Presentation/Models/OrbatNodeViewModel.cs`

Add `bool CanAcceptSubordinates` as a new positional record parameter after `IsPendingDelete`:

```csharp
public sealed record OrbatNodeViewModel(
    int EntityId,
    string Name,
    int Depth,
    bool HasChildren,
    bool IsPendingDelete,
    bool CanAcceptSubordinates);
```

Update the XML doc with a `<param name="CanAcceptSubordinates">` entry.

**Update all construction sites:**

In `Hrot/Subsystems/Hrot.Editor/Adapters/EditorOrbatAdapter.cs`:
- Add `CanAcceptSubordinates: false` as a named argument (temporary `false`; full value
  comes in TASK-CS020).

In `Hrot/Subsystems/Hrot.ExCon/Adapters/ExConOrbatAdapter.cs`:
- Add `CanAcceptSubordinates: false` as a named argument (safe default; full value
  comes in TASK-CS021).

**Update existing test constructions:**

In `Hrot/Subsystems/Hrot.ExCon.Tests/SharedOrbatPanelTests.cs`:
- All `new OrbatNodeViewModel(...)` calls — add `false` (or `CanAcceptSubordinates: false`)
  as the 6th argument.

Scan for any other `new OrbatNodeViewModel` calls in the test suite and update them too.

**Do NOT add a new test file for CS017.** The success condition is that all existing constructions
compile without error. Add a single inline assertion in the compile-time test:
`Assert.True(new OrbatNodeViewModel(1, "A", 0, false, false, true).CanAcceptSubordinates)`.
Add this as a `[Fact]` to `SharedOrbatPanelTests.cs`.

---

### TASK-CS018 — IOrbatController: Subordination Methods

**Reference:** TASK-DETAIL.md § TASK-CS018

**Modify** `Hrot/Engine/Hrot.UI.Common/Facades/IOrbatController.cs`.

Add two method declarations after `RequestDisembark`:

```csharp
/// <summary>
/// Requests that the subordinate entity be assigned under the commander entity.
/// </summary>
/// <param name="subordinateEntityId">The network entity ID of the subordinate.</param>
/// <param name="commanderEntityId">The network entity ID of the commander.</param>
void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId);

/// <summary>
/// Requests that the subordinate entity be removed from its current commander.
/// </summary>
/// <param name="subordinateEntityId">The network entity ID of the subordinate to remove.</param>
void RequestRemoveSubordinate(int subordinateEntityId);
```

**Add stubs to both adapters:**

In `Hrot/Subsystems/Hrot.Editor/Adapters/EditorOrbatAdapter.cs`, after `RequestDisembark`:
```csharp
/// <inheritdoc/>
public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)
{
    FdpLog<EditorOrbatAdapter>.Warn(
        "[EditorOrbatAdapter] RequestAssignSubordinate not yet implemented " +
        "(subordinate={0}, commander={1}).", subordinateEntityId, commanderEntityId);
}

/// <inheritdoc/>
public void RequestRemoveSubordinate(int subordinateEntityId)
{
    FdpLog<EditorOrbatAdapter>.Warn(
        "[EditorOrbatAdapter] RequestRemoveSubordinate not yet implemented " +
        "(subordinate={0}).", subordinateEntityId);
}
```

Add `using Fdp.Core.Logging;` to `EditorOrbatAdapter.cs` if not already present.

In `Hrot/Subsystems/Hrot.ExCon/Adapters/ExConOrbatAdapter.cs`, after `RequestDisembark`:
```csharp
/// <inheritdoc/>
public void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)
{
    FdpLog<ExConOrbatAdapter>.Warn(
        "[ExConOrbatAdapter] RequestAssignSubordinate not yet implemented over DDS " +
        "(subordinate={0}, commander={1}).", subordinateEntityId, commanderEntityId);
}

/// <inheritdoc/>
public void RequestRemoveSubordinate(int subordinateEntityId)
{
    FdpLog<ExConOrbatAdapter>.Warn(
        "[ExConOrbatAdapter] RequestRemoveSubordinate not yet implemented over DDS " +
        "(subordinate={0}).", subordinateEntityId);
}
```

**Tests:** No new test file for CS018. Both adapters must compile without errors.

---

### TASK-CS019 — SharedOrbatPanel: Subordination Drag-Drop

**Reference:** TASK-DETAIL.md § TASK-CS019

**Modify** `Hrot/Engine/Hrot.UI.Common/Panels/SharedOrbatPanel.cs`.

**Step 1: Add `HandleHierarchyDropPayload` internal method.**

This method encodes the routing logic:
- Self-drop → no-op.
- `targetNode.CanAcceptSubordinates == true` → `ctrl.RequestAssignSubordinate(subId, targetNode.EntityId)`.
- `targetNode.CanAcceptSubordinates == false` → `ctrl.RequestEmbark(subId, targetNode.EntityId)`.

```csharp
/// <summary>
/// Routes a drop onto a specific ORBAT node: assign-as-subordinate when the target
/// node accepts subordinates, embark otherwise. Self-drop is always a no-op.
/// </summary>
internal void HandleHierarchyDropPayload(int subId, OrbatNodeViewModel targetNode, IOrbatController ctrl)
{
    if (subId == targetNode.EntityId) return;

    if (targetNode.CanAcceptSubordinates)
        ctrl.RequestAssignSubordinate(subId, targetNode.EntityId);
    else
        ctrl.RequestEmbark(subId, targetNode.EntityId);
}
```

**Step 2: Update the drop target inside `DrawContent`.**

Replace the existing `HandleDropPayload(passengerId, vehicleId, ctrl)` call in the drop target
block with `HandleHierarchyDropPayload(passengerId, node, ctrl)`. The existing `HandleDropPayload`
method can remain unchanged (its existing tests use it directly).

**Step 3: Add a background drop target after the nodes loop.**

After the closing `}` of the `for (int i ...)` loop in `DrawContent`, append:

```csharp
// Background drop target: dropping onto empty space removes the subordinate from its commander.
ImGui.Dummy(new System.Numerics.Vector2(ImGui.GetContentRegionAvail().X, Math.Max(ImGui.GetContentRegionAvail().Y, 20f)));
if (ImGui.BeginDragDropTarget())
{
    unsafe
    {
        var bgPayload = ImGui.AcceptDragDropPayload("ORBAT_ENTITY");
        if (bgPayload.NativePtr != null)
        {
            int subordinateId = *(int*)bgPayload.Data;
            ctrl.RequestRemoveSubordinate(subordinateId);
        }
    }
    ImGui.EndDragDropTarget();
}
```

Add `using System.Numerics;` if not already present in the file.

**Tests** — add to `Hrot/Subsystems/Hrot.ExCon.Tests/SharedOrbatPanelTests.cs`:

Test 1 — `HandleHierarchyDropPayload_ToCommanderNode_CallsRequestAssignSubordinate`:
```
var targetNode = new OrbatNodeViewModel(12, "Alpha Company", 0, true, false, CanAcceptSubordinates: true);
panel.HandleHierarchyDropPayload(5, targetNode, ctrl.Object);
ctrl.Verify(c => c.RequestAssignSubordinate(5, 12), Times.Once);
ctrl.Verify(c => c.RequestEmbark(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
```

Test 2 — `HandleHierarchyDropPayload_ToNonCommanderNode_CallsRequestEmbark`:
```
var targetNode = new OrbatNodeViewModel(12, "Vehicle", 0, false, false, CanAcceptSubordinates: false);
panel.HandleHierarchyDropPayload(5, targetNode, ctrl.Object);
ctrl.Verify(c => c.RequestEmbark(5, 12), Times.Once);
ctrl.Verify(c => c.RequestAssignSubordinate(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
```

Test 3 — `HandleHierarchyDropPayload_SelfDrop_NoOp`:
```
var targetNode = new OrbatNodeViewModel(7, "Self", 0, false, false, CanAcceptSubordinates: true);
var lenientCtrl = new Mock<IOrbatController>();
panel.HandleHierarchyDropPayload(7, targetNode, lenientCtrl.Object);
lenientCtrl.Verify(c => c.RequestAssignSubordinate(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
lenientCtrl.Verify(c => c.RequestEmbark(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
```

Use `MockBehavior.Strict` for tests 1 and 2 (controller mock). For test 3 use `MockBehavior.Loose`
(or `new Mock<IOrbatController>()` without strict) since no calls are expected.

---

## Build Order

Implement in this order to avoid compile errors at each step:
1. CS013 (new file — no dependencies)
2. CS014 (depends on `InitialUnitSubordinateIntent` and `UnitSubordinate` — both exist)
3. CS026 (depends on `InitialUnitSubordinateIntent` — exists)
4. CS027 (depends on `InitialUnitSubordinateIntent` — exists)
5. CS017 (adds record parameter — update all construction sites before building)
6. CS018 (adds interface methods — adapters must also be updated before building)
7. CS019 (depends on CS017 `CanAcceptSubordinates` and CS018 `RequestAssignSubordinate`)

## Report

After completing all tasks, create `.dev/commander-subordinates/reports/BATCH-04-REPORT.md`
following the format in previous batch reports (table of tasks, files modified, tests added).

Run `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` and confirm 0 errors.
Run `dotnet test IOS-IG-SimHost.sln --no-build --nologo` and report any new failures
(pre-existing failures documented in BATCH-03-REVIEW are expected and acceptable).

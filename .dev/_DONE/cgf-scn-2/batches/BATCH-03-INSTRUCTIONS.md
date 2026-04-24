# BATCH-03: Intent DTO Components for Cross-Entity Reference Safety

**Batch Number:** BATCH-03
**Tasks:** TASK-S401, TASK-S402, TASK-S403, TASK-S404, TASK-S405, TASK-S406
**Phase:** Phase 4 — Intent Components for Cross-Entity Reference Safety
**Estimated Effort:** 10-15 hours
**Priority:** HIGH
**Dependencies:** BATCH-01 (DataPolicy on components) and BATCH-02 (FdpAutoSerializer InlineArray) must be complete.

---

## Onboarding & Workflow

### Developer Instructions

This batch implements Phase 4 of the cgf-scn-2 workstream: the "Intent DTO" pattern for
all translators that write cross-entity references during scenario load.

**The Core Problem:** When a scenario is loaded from JSON on a CGF node, entities are
created one at a time by `StagingEntityExtractor`. When a translator calls
`resolver.Resolve(guidStr)` to get an `Entity` handle during `Inject`, the referenced
entity may not yet be alive. Writing a raw `Entity` handle that was just created in this
session is also unsafe for distributed scenarios, where the same scenario file is loaded
on multiple nodes with different entity allocations.

**The Solution — Two Steps:**
1. **Translators** write `long NetworkId` values into managed Intent DTO components
   (not `Entity` handles into structural components). The Network ID is a stable
   cross-node identity.
2. **`GenesisMaterializationSystem`** runs in the `InitializationSystemGroup` each tick
   and resolves Intent components by looking up Network IDs in `NetworkEntityMap` to
   obtain live `Entity` handles — only once those entities are alive.

### Required Reading (IN ORDER)

1. **BATCH-02 Review:** `.dev/cgf-scn-2/reviews/BATCH-02-REVIEW.md` — Clean handoff.
2. **Design Doc Phase 4:** `.dev/cgf-scn-2/DESIGN.md` — Full section "Phase 4: Intent
   Components for Cross-Entity Reference Safety" (and the architecture explanation).
3. **Task Definitions:** `.dev/cgf-scn-2/TASK-DETAIL.md` — Tasks TASK-S401 through TASK-S406.
4. **Design Talk:** `.dev/cgf-scn-2/design-talk.md` — Any reference code for Phase 4.

### Source Code to Study Before Starting

| File | Why |
|---|---|
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PassengerBufferTranslator.cs` | Current `Inject` pattern to be replaced by Intent write |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/TargetMemoryTranslator.cs` | Current `Inject` pattern to be replaced by Intent write |
| `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` | ID block — allocate new IDs 172–176 here |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | TASK-S405 patch site |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | How systems are added (`_kernelGroup.AddSystem(...)`) |
| `FDP/Toolkits/Fdp.Toolkits/Replication/Services/NetworkEntityMap.cs` | `TryGetEntity(long networkId, out Entity entity)` signature |
| `FDP/Engine/Fdp.Core/Components/EntityInfo.cs` | Example of simple managed component in Fdp.Core |
| `Hrot/Engine/Hrot.Core/Components/Map/PersonalRouteRef.cs` | Cross-entity `Entity` reference component |
| `FDP/Engine/Fdp.Presentation/Vis2D/Components/HierarchyComponents.cs` | `VisHierarchyNode` struct (Parent, FirstChild, NextSibling) |
| `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/InteractionComponents.cs` | `IsEmbarkedTag` and `PassengerBuffer` |

---

## Component ID Allocation

New component IDs must be added to `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs`.
The current highest used ID is 171 (`ZoneMembership`). IDs 172-199 are available.

Allocate:
- `172` → `InitialPassengersIntent`
- `173` → `InitialVehicleIntent`
- `174` → `InitialHierarchyIntent`
- `175` → `InitialRouteIntent`
- `176` → `InitialTargetsIntent`

---

## New Files

| File | Change |
|---|---|
| `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` | NEW — 5 Intent DTO managed classes (TASK-S401) |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/VisHierarchyNodeTranslator.cs` | NEW (TASK-S402) |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/IsEmbarkedTagTranslator.cs` | NEW (TASK-S402) |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PersonalRouteRefTranslator.cs` | NEW (TASK-S402) |
| `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs` | NEW (TASK-S404) |

## Modified Files

| File | Change |
|---|---|
| `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` | Add 5 new IDs 172-176 |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/PassengerBufferTranslator.cs` | Inject writes InitialPassengersIntent (TASK-S403) |
| `Hrot/Subsystems/Hrot.SimHost/Serializers/TargetMemoryTranslator.cs` | Inject writes InitialTargetsIntent (TASK-S406) |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | Register GenesisMaterializationSystem; register new translators (TASK-S404, S402) |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Register new translators (TASK-S402) |
| `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs` | Register new translators (TASK-S402) |
| `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` | Remap Intent NetworkIds (TASK-S405) |

---

## Tasks

### Task 1: Define Intent DTO Components (TASK-S401)

**File:** `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` (NEW FILE)
**Namespace:** `Hrot.Common.Serializers`

Create 5 managed component classes. They are `class` (not struct) so they can hold `List<T>`.
All must carry `[DataPolicy(DataPolicy.Transient)]` and `[ComponentId(...)]`.
All are **managed** components — register with `repo.RegisterManagedComponent<T>()`.

**Class 1: `InitialPassengersIntent`**
```csharp
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialPassengersIntent)]  // 172
public sealed class InitialPassengersIntent
{
    public List<long> PassengerNetworkIds { get; set; } = new();
}
```

**Class 2: `InitialVehicleIntent`**
```csharp
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialVehicleIntent)]  // 173
public sealed class InitialVehicleIntent
{
    public long VehicleNetworkId { get; set; }
}
```

**Class 3: `InitialHierarchyIntent`**
```csharp
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialHierarchyIntent)]  // 174
public sealed class InitialHierarchyIntent
{
    public long ParentNetworkId { get; set; }
    public long FirstChildNetworkId { get; set; }
    public long NextSiblingNetworkId { get; set; }
}
```

**Class 4: `InitialRouteIntent`**
```csharp
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialRouteIntent)]  // 175
public sealed class InitialRouteIntent
{
    public long RouteNetworkId { get; set; }
}
```

**Class 5: `InitialTargetsIntent`**
```csharp
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialTargetsIntent)]  // 176
public sealed class InitialTargetsIntent
{
    public List<TargetEntry> Entries { get; set; } = new();
}

public struct TargetEntry
{
    public long  NetworkId;
    public float PosX;
    public float PosY;
    public float Score;
    public uint  LastSeenTick;
    public byte  Modality;
}
```

**Also update `HrotComponentIds.cs`** to add the 5 new constants (IDs 172-176) with
XML doc comments.

**Required Tests (add to `Hrot.SimHost.Tests` — new file `GenesisIntentComponentsTests.cs`):**
1. Each of the 5 classes can be `RegisterManagedComponent<T>()` without error.
2. `ComponentTypeRegistry.GetType(HrotComponentIds.InitialPassengersIntent)` returns
   `typeof(InitialPassengersIntent)` after registration.
3. `DataPolicyAttribute` on each class reports `DataPolicy.Transient`.

---

### Task 2: New Translators for VisHierarchyNode, IsEmbarkedTag, PersonalRouteRef (TASK-S402)

Create 3 new `IEntityScenarioTranslator` files in `Hrot/Subsystems/Hrot.SimHost/Serializers/`.
Also register them at all 3 `ScenarioSerializerBuilder` sites.

**Translator 1: `VisHierarchyNodeTranslator`**
- Consumes: `VisHierarchyNode` (GlobalComponentIds.VisHierarchyNode)
- Key: `"VisHierarchyNode"`
- Extract: For each of Parent, FirstChild, NextSibling — if the Entity is not null/alive,
  call `resolver.Resolve(entity)` to get a GUID string; store it in a JsonObject.
  Null/dead entities → store `null` or omit the key.
- Inject: Read 3 optional GUID strings; resolve each to a `long` NetworkId via
  `resolver.ResolveNetworkId(guidStr)` (or by resolving `Entity` then reading `NetworkIdentity.NetworkId`);
  write `InitialHierarchyIntent` with those 3 NetworkIds (0 if absent).

**Translator 2: `IsEmbarkedTagTranslator`**
- Consumes: `IsEmbarkedTag` (GlobalComponentIds.IsEmbarkedTag)
- Key: `"IsEmbarkedTag"`
- Extract: Resolve `VehicleEntity` → GUID string.
- Inject: Resolve GUID → NetworkId; write `InitialVehicleIntent { VehicleNetworkId }`.

**Translator 3: `PersonalRouteRefTranslator`**
- Consumes: `PersonalRouteRef` (HrotComponentIds.PersonalRouteRef)
- Key: `"PersonalRouteRef"`
- Extract: Resolve `RouteEntity` → GUID string.
- Inject: Resolve GUID → NetworkId; write `InitialRouteIntent { RouteNetworkId }`.

**Important:** On the `Inject` path, do NOT write the structural component
(`VisHierarchyNode`, `IsEmbarkedTag`, `PersonalRouteRef`) — write only the Intent
DTO. `GenesisMaterializationSystem` writes the structural component later.

**Registration:** Add all 3 translators to `ScenarioSerializerBuilder` in:
- `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`
- `Hrot/Subsystems/Hrot.Editor/EditorBootstrap.cs`

**IGuidResolver.ResolveNetworkId:** Check the `IGuidResolver` interface to confirm whether
`ResolveNetworkId(string guidStr)` exists. If not, resolve to `Entity` via the existing
`resolver.Resolve(guidStr)` and then read `repo.GetComponent<NetworkIdentity>(entity).NetworkId`.

**Required Tests (add to `Hrot.SimHost.Tests` — new file `IntentTranslatorTests.cs`):**
1. `VisHierarchyNodeTranslator.Extract`: entity with Parent=e1; assert DOM has `"VisHierarchyNode"` key
   with a GUID string for `"Parent"`.
2. `VisHierarchyNodeTranslator.Inject`: DOM with a Parent GUID; assert entity has
   `InitialHierarchyIntent.ParentNetworkId` matching the NetworkId of the parent entity.
3. Same for `IsEmbarkedTagTranslator` and `PersonalRouteRefTranslator`.
4. `CanTranslate` returns `false` when the component is absent.

---

### Task 3: Update PassengerBufferTranslator to Emit Intent (TASK-S403)

**File:** `Hrot/Subsystems/Hrot.SimHost/Serializers/PassengerBufferTranslator.cs` (UPDATE)

**Change only the `Inject` method.** The `Extract` side is unchanged.

Current `Inject` writes a `PassengerBuffer` directly:
```csharp
repo.SetComponent(entity, buffer);
```

New `Inject` should instead write `InitialPassengersIntent`:
```csharp
var intent = new InitialPassengersIntent();
foreach (var item in entries)
{
    // ... resolve GUID string → long NetworkId
    intent.PassengerNetworkIds.Add(networkId);
}
repo.SetManagedComponent(entity, intent);
```

Do NOT call `repo.SetComponent(entity, buffer)` anymore.

**How to get NetworkId from a GUID string:**
The `IGuidResolver` resolves a GUID string to an `Entity`. Once you have the Entity,
read `repo.GetComponent<NetworkIdentity>(entity).NetworkId`. Store that `long` in the intent.
Handle missing/null GUID strings gracefully (skip).

**Required Tests (update existing `PassengerBufferTranslatorTests.cs` or create new):**
1. After `Inject`, entity has `InitialPassengersIntent.PassengerNetworkIds.Count` equal to
   the number of valid GUIDs in the DOM.
2. After `Inject`, entity does NOT have `PassengerBuffer`.
3. The `Extract` side is unchanged — still produces GUID strings (not NetworkIds).

---

### Task 4: Implement GenesisMaterializationSystem (TASK-S404)

**File:** `Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs` (NEW FILE)
**Namespace:** `Hrot.SimHost.Systems`

This system runs in the `_kernelGroup` (kernel `SystemGroup`) in `SimHostApp`.
It resolves all 5 Intent component types each tick. For each Intent component, it tries to
resolve all referenced Network IDs using `NetworkEntityMap`. Only if ALL are resolved does
it write the structural component and remove the Intent.

**Constructor:** Takes `NetworkEntityMap entityMap`.

**OnUpdate logic (pseudocode):**
```
foreach entity with InitialPassengersIntent:
    intent = repo.GetManagedComponent<InitialPassengersIntent>(entity)
    allResolved = true
    for each networkId in intent.PassengerNetworkIds:
        if !entityMap.TryGetEntity(networkId, out entity_ref): allResolved = false; break
        if !repo.IsAlive(entity_ref): allResolved = false; break
    if allResolved:
        build PassengerBuffer, set it, remove InitialPassengersIntent

foreach entity with InitialVehicleIntent:
    intent = repo.GetManagedComponent<InitialVehicleIntent>(entity)
    if entityMap.TryGetEntity(intent.VehicleNetworkId, out vehicleEntity) && repo.IsAlive(vehicleEntity):
        repo.SetComponent(entity, new IsEmbarkedTag { VehicleEntity = vehicleEntity })
        repo.RemoveManagedComponent<InitialVehicleIntent>(entity)

foreach entity with InitialHierarchyIntent:
    (same pattern for Parent, FirstChild, NextSibling; null NetworkId=0 maps to Entity.Null)
    if all non-zero NetworkIds resolve:
        build VisHierarchyNode { Parent=..., FirstChild=..., NextSibling=... }
        repo.SetComponent(entity, node); remove intent

foreach entity with InitialRouteIntent:
    (same pattern for RouteEntity)

foreach entity with InitialTargetsIntent:
    intent = ...
    var mem = new TargetMemory();
    int count = 0;
    foreach entry in intent.Entries:
        if !entityMap.TryGetEntity(entry.NetworkId, out targetEntity): continue  // skip unknown
        if !repo.IsAlive(targetEntity): continue
        // fill mem at [count++]
    mem.Count = count;
    repo.SetComponent(entity, mem)
    remove InitialTargetsIntent  // Note: partial materialization is OK here (missing targets dropped)
```

**Key details:**
- `NetworkEntityMap` is at `FDP/Toolkits/Fdp.Toolkits/Replication/Services/NetworkEntityMap.cs` —
  read it to confirm `TryGetEntity(long networkId, out Entity entity)` signature.
- For `InitialHierarchyIntent`: a NetworkId of `0` means the entity reference was null in the
  scenario; produce `Entity.Null` for those.
- For `InitialTargetsIntent`: **partial** materialization is allowed (missing targets are dropped
  silently). Remove the intent even if some entries were unresolved.
- For the other 4 intent types: skip the entity if any Network ID cannot be resolved.
- Registration: add `_kernelGroup.AddSystem(new GenesisMaterializationSystem(entityMap))` in
  `SimHostApp.cs`. The `NetworkEntityMap` instance is available in the context where
  `_kernelGroup` is built — check the constructor parameters of `BuildOrchestration` or
  look for where `entityMap` is created in `SimHostApp.cs`.

**For reading managed components in OnUpdate:**
Use `World.Query().With<InitialPassengersIntent>().Build()` then iterate, and use
`World.GetManagedComponent<T>` / `World.RemoveManagedComponent<T>`. Check existing systems
(e.g., `Hrot.SimHost/Systems/`) for the correct API pattern.

**Required Tests (add to `Hrot.SimHost.Tests` — new file `GenesisMaterializationSystemTests.cs`):**
1. Spawn entity A (passenger) and entity B (vehicle); give A `InitialPassengersIntent` with B's
   NetworkId; tick the system once; assert A now has `PassengerBuffer` with `Count==1` and
   `Passengers[0]==entityB`; assert `InitialPassengersIntent` is removed.
2. B is not yet alive; tick once; assert A still has `InitialPassengersIntent` (deferred).
   Make B alive; tick again; assert materialization.
3. Same pattern for `InitialVehicleIntent` → `IsEmbarkedTag`.
4. Same for `InitialHierarchyIntent` → `VisHierarchyNode`.
5. Same for `InitialRouteIntent` → `PersonalRouteRef`.
6. `InitialTargetsIntent` with 2 entries: one resolves, one doesn't; assert `TargetMemory.Count==1`
   (partial) and intent is removed.

---

### Task 5: Patch StagingEntityExtractor for Intent NetworkId Remapping (TASK-S405)

**File:** `Hrot/Subsystems/Hrot.CGF/Orchestration/StagingEntityExtractor.cs` (UPDATE)

**Context:** When entities are created during genesis, each entity is assigned a new Network ID.
The `oldToNewMap` (which already exists in this file) maps old → new Network IDs. Intent
components still carry the old IDs from the scenario file; they must be remapped.

**Change:** After each entity's component list is assembled (at the end of the entity-
creation loop), check if the entity has any Intent managed component. If yes, create a
remapped copy and replace it.

Read the file carefully to understand the existing `oldToNewMap` structure and where
entity creation happens. The remapping must mirror what `ScenarioBehaviorRemapper` does
for `ActiveMissionPlan` strings.

```csharp
// Pseudocode for remapping after component assembly:
if (request.ManagedComponents.TryGet<InitialPassengersIntent>(out var pIntent))
{
    var remapped = new InitialPassengersIntent();
    foreach (var id in pIntent.PassengerNetworkIds)
        remapped.PassengerNetworkIds.Add(oldToNewMap.TryGetValue(id, out long newId) ? newId : id);
    request.ManagedComponents.Replace(remapped);
}
// ... same for InitialVehicleIntent, InitialHierarchyIntent (3 fields), InitialRouteIntent, InitialTargetsIntent
```

**Important:** Do not change the Pass 1 ID allocation logic. Only add the remapping
pass at the end.

**Required Tests (extend `Hrot.SimHost.Tests/StagingEntityExtractorTests.cs` or add new):**
1. Load a scenario with 2 entities (A references B); assert that after extraction,
   `InitialPassengersIntent` on A's `EntityCreationRequest` carries B's **new** Network ID.
2. A references an ID not in the oldToNewMap; assert the original ID is preserved.
3. Existing `StagingEntityExtractorTests` still pass.

---

### Task 6: Refactor TargetMemoryTranslator to Emit InitialTargetsIntent (TASK-S406)

**File:** `Hrot/Subsystems/Hrot.SimHost/Serializers/TargetMemoryTranslator.cs` (UPDATE)

**Change only the `Inject` method.** The `Extract` side is unchanged (GUID strings + data).
Note the current `Inject` is `unsafe` due to pointer arithmetic on `TargetMemory`.

New `Inject`:
1. Read each entry from the DOM (same loop as before).
2. Instead of resolving GUID → `Entity` handle and writing to `TargetMemory*`,
   resolve GUID → NetworkId and build a `TargetEntry`.
3. After building all entries, write `InitialTargetsIntent` onto the entity.
4. Do NOT call `repo.SetComponent(entity, mem)` for `TargetMemory`.

The `unsafe` keyword and pointer arithmetic should be removed from the method since
we no longer write to a `TargetMemory*`.

**How to resolve GUID → NetworkId:**
Same approach as TASK-S402: `resolver.Resolve(guidStr)` → Entity, then
`repo.GetComponent<NetworkIdentity>(resolvedEntity).NetworkId`.

**Required Tests (update or add to `Hrot.SimHost.Tests`):**
1. After `Inject`, entity has `InitialTargetsIntent` with 2 entries (correct NetworkIds,
   positions, scores, modalities).
2. After `Inject`, entity does NOT have `TargetMemory`.
3. End-to-end round-trip via `GenesisMaterializationSystem`: save entity with
   `TargetMemory.Count==2`; load on fresh repo; tick `GenesisMaterializationSystem`;
   assert `TargetMemory` restored with correct values.

---

## Mandatory Workflow: Task Progression

Complete tasks in this exact order (each has a hard dependency):

1. **Task 1 (S401):** Intent DTOs + ID registration — must be done before everything else.
2. **Task 2 (S402):** New translators — depends on S401.
3. **Task 3 (S403):** PassengerBuffer → Intent — depends on S401.
4. **Task 4 (S404):** GenesisMaterializationSystem — depends on S401+S402+S403.
5. **Task 5 (S405):** StagingEntityExtractor patch — depends on S401.
6. **Task 6 (S406):** TargetMemoryTranslator → Intent — depends on S401+S404 (run concurrently with S405 is OK).

After each task:
- Run `dotnet build IOS-IG-SimHost.sln --no-restore 2>&1 | Select-String "error CS"` to verify no compile errors.
- Run affected test projects with `--no-build`.
- Fix any failures before moving on.

**DO NOT** stop to ask for permission. Work autonomously until ALL 6 tasks are done and ALL
tests pass, then write the report.

---

## Testing

Run tests with:
```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj --no-build
```

Expected baselines: 407 pass in SimHost.Tests (0 failures), 753 pass in Fdp.Toolkits.Tests (7 pre-existing failures OK).

Minimum **18 new unit tests** across all 6 tasks.

---

## Success Criteria

- [ ] 5 Intent DTO classes created with correct `[ComponentId]` and `[DataPolicy(Transient)]` (S401)
- [ ] 5 new IDs (172-176) added to `HrotComponentIds.cs` (S401)
- [ ] `VisHierarchyNodeTranslator`, `IsEmbarkedTagTranslator`, `PersonalRouteRefTranslator` created (S402)
- [ ] All 3 new translators registered at 3 sites each (S402)
- [ ] `PassengerBufferTranslator.Inject` writes `InitialPassengersIntent` (S403)
- [ ] `GenesisMaterializationSystem` resolves all 5 Intent types (S404)
- [ ] `GenesisMaterializationSystem` registered in `SimHostApp._kernelGroup` (S404)
- [ ] `StagingEntityExtractor` remaps Intent NetworkIds using `oldToNewMap` (S405)
- [ ] `TargetMemoryTranslator.Inject` writes `InitialTargetsIntent` (S406)
- [ ] All new tests pass; no regressions

---

## Common Pitfalls

- `DataPolicy.Transient` is a combined flag (`NoSave | NoRecord`). Do not use `NoSave`
  only for Intent DTOs — they must also be excluded from checkpoints.
- `NetworkEntityMap.TryGetEntity(long, out Entity)` may have a different parameter name
  than expected. Read the actual source before writing code.
- Managed components are registered with `repo.RegisterManagedComponent<T>()`, not
  `repo.RegisterComponent<T>()`. Check existing managed component registrations in
  SimHostApp.cs for the correct call.
- `World.GetManagedComponent<T>()` vs `((ISimulationView)repo).GetManagedComponentRO<T>()`:
  use whichever is appropriate from the system's `World` reference.
- `InitialTargetsIntent` partial materialization: the intent is ALWAYS removed after the
  first successful tick (even if some entries were dropped). Do not defer on partial resolution.
- `InitialHierarchyIntent` with a `0` NetworkId means null entity reference in the original
  scenario. Map 0 → `Entity.Null` explicitly; do not call `TryGetEntity(0)`.

---

## Developer Insights

**Q1:** Did `IGuidResolver` expose a `ResolveNetworkId` method or did you have to resolve
to `Entity` first and then read `NetworkIdentity`? Which approach did you use and why?

**Q2:** How did you get the `NetworkEntityMap` instance into `GenesisMaterializationSystem`
from `SimHostApp.cs`? Was it already visible in the method where `_kernelGroup` is built?

**Q3:** Did the `StagingEntityExtractor` `oldToNewMap` already exist as a `Dictionary<long, long>`
or a different type? What changes were needed to iterate Intent managed components in
`EntityCreationRequest`?

**Q4:** Were there any existing tests for `PassengerBufferTranslator` or `TargetMemoryTranslator`
that broke because of the Intent change? How did you update them?

**Q5:** Suggest a git commit message for this batch.

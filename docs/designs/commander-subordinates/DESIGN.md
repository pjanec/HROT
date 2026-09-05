# Commander-Subordinate Infrastructure — Design

## Background

The FDP engine currently conflates two distinct concerns inside `FormationRoster` and
`FormationMember`:

1. **Generic command hierarchy** — who is the commander of this entity; which entities does
   a commander control. Used by the cognitive tier (BTree/HSM) for issuing tactical intents.
2. **Kinematic formation steering** — formation type, template, slot assignments, and per-member
   steering state. Used by the high-rate `FormationTargetSystem` / `CarKinematicsSystem`.

Because the two concerns share the same component, every entity that participates in a formation
also drags along the full cognitive hierarchy data and vice versa. More critically, `FormationMember.LeaderEntityId`
is a raw `int` (entity index only) — a generation-unsafe handle that risks accessing recycled
entities in long-running simulations.

The refactor separates the concerns, introduces generation-safe entity references, and extends the
architecture to support full CRUD of command relationships at runtime, over DDS, and across
scenario save/load cycles.

---

## Target Architecture

### Component Separation

| Purpose | Commander Component | Subordinate Component | Project |
|---------|--------------------|-----------------------|---------|
| Generic command hierarchy (AI tier) | `UnitRoster` | `UnitSubordinate` | `Hrot.Core` |
| Kinematic formation (muscle tier) | `FormationController` | `FormationFollower` | `Fdp.Toolkits` |

**Invariant:** `UnitSubordinate.Commander` (an 8-byte generation-safe `Entity` struct) is the
single source of truth for the command relationship in the local ECS. `UnitRoster` is a
locally-derived top-down cache. The network layer (`EntityInfo` DDS descriptor) carries the
integer network ID and is bridged to the ECS world by the ACL translators.

### Data-Flow Diagram

```
Network (DDS)                  ACL Layer                        ECS (local)
───────────────                ─────────────────                ──────────────────────
EntityInfo                     EntityInfoIngressTranslator      UnitSubordinate
  CommanderId (int)   ──────>    resolve via NetworkEntityMap     Commander (Entity 8B)
  TacticalDesignation           write CmdAssignSubordinate >─>    Designation (ushort)
                                                                 UnitRoster (NoSave cache)
EntityInfo              <─────  EntityInfoEgressTranslator        Count, SubordinateEntities[]
  CommanderId (int)              read UnitSubordinate.Commander
  TacticalDesignation            map to network ID
```

---

## Phase 1 — Core Component Definitions

### 1.1 Tactical Designation Enums

Two enum definitions with identical integer values, one on each side of the network boundary,
keeping them in sync by comment (established dual-enum pattern used by `EClampingMode`,
`ENavigationMode`).

**ECS-side (`Hrot.Core`):**
```csharp
/// IMPORTANT: Must be kept in sync with Hrot.NED.Descriptors.eTacticalDesignation
public enum TacticalDesignation : ushort
{
    Undefined = 0,
    Commander = 1,
    SquadLeader = 2,
    Wingman = 3,
    Support = 4
}
```

**DDS-side (`Hrot.Network.NED`):**
```csharp
/// IMPORTANT: Must be kept in sync with Hrot.Core.CommandHierarchy.TacticalDesignation
public enum eTacticalDesignation : ushort { Undefined=0, Commander=1, SquadLeader=2, Wingman=3, Support=4 }
```

A `TacticalDesignationMapper` static class in `Hrot.Network.NED` provides the ACL conversion
(simple casts — both enums share the same underlying `ushort` values).

### 1.2 UnitSubordinate Component

Blittable struct placed on **subordinate entities**. Carries the generation-safe handle to the
commander and the logical role within that unit.

```csharp
[StructLayout(LayoutKind.Sequential)]
[ComponentId(HrotComponentIds.UnitSubordinate)]   // ID 183
public struct UnitSubordinate
{
    public Entity Commander;                  // Generation-safe 8-byte handle (Index + Generation)
    public TacticalDesignation Designation;   // Logical role; 0 = Undefined/default
}
```

`Entity.Null` is the valid "no commander" sentinel (matches FDP null-entity convention).

### 1.3 UnitRoster Component

Unsafe struct placed on **commander entities**. Marked `NoSave` because it is entirely
derived from the bottom-up `UnitSubordinate` records and is rebuilt on scenario load.

```csharp
[DataPolicy(DataPolicy.NoSave)]
[StructLayout(LayoutKind.Sequential)]
[ComponentId(HrotComponentIds.UnitRoster)]        // ID 182
public unsafe struct UnitRoster
{
    public const int Capacity = 16;

    public int Count;
    public fixed long   SubordinateEntities[Capacity];   // Entity packed as long (Index|Generation)
    public fixed ushort TacticalDesignations[Capacity];  // mirrors UnitSubordinate.Designation
}
```

**Design decisions:**
- Capacity = 16 enforced as a hard limit; overflow is rejected with a `FdpLog.Warn` and the
  assignment is aborted atomically (neither component is written).
- `long` packing matches the existing `FormationRoster.MemberEntities` convention and enables
  zero-allocation reads without boxing.
- Insertion order is preserved (O(N) left-shift on removal, N ≤ 16).

### 1.4 Component ID Assignments

New entries in `HrotComponentIds` (Hrot.Core):

| Constant | Value | Component |
|----------|-------|-----------|
| `UnitRoster` | 182 | `UnitRoster` struct |
| `UnitSubordinate` | 183 | `UnitSubordinate` struct |
| `InitialUnitSubordinateIntent` | 184 | Genesis intent DTO (Phase 4) |

All new components must be registered in the `SimHostCoreLogicPack` component registry (and in
all other EcsWorld setups that use the command hierarchy: CGF, Editor integration tests).

---

## Phase 2 — Formation Component Refactor

The existing `FormationRoster` and `FormationMember` are split so that each component holds
only the data relevant to its concern.

### 2.1 FormationRoster → FormationController

`FormationRoster` (ID 33) is **renamed** to `FormationController` and its member-array fields
(`MemberEntities`, `SlotIndices`) are **removed**. The member list moves to `UnitRoster`.

**Before:**
```
FormationRoster { Count, TemplateId, Type, Params, MemberEntities[16], SlotIndices[16] }
```

**After — FormationController (ID 33, same number, new name):**
```csharp
public struct FormationController
{
    public int TemplateId;
    public FormationType Type;
    public FormationParams Params;
}
```

`GlobalComponentIds.FormationRoster` is renamed to `GlobalComponentIds.FormationController`.

### 2.2 FormationMember → FormationFollower

`FormationMember` (ID 45) is **renamed** to `FormationFollower` and its `LeaderEntityId` field
(raw `int`, generation-unsafe) is **removed**. The commander reference moves to `UnitSubordinate`.

**Before:**
```
FormationMember { LeaderEntityId(int), SlotIndex, State, IsInFormation, SlotDistFiltered, RejoinTimer }
```

**After — FormationFollower (ID 45, same number, new name):**
```csharp
public struct FormationFollower
{
    public ushort SlotIndex;
    public FormationMemberState State;
    public byte IsInFormation;
    public float SlotDistFiltered;
    public float RejoinTimer;
}
```

`GlobalComponentIds.FormationMember` is renamed to `GlobalComponentIds.FormationFollower`.

### 2.3 VehicleCommandSystem Update

`VehicleCommandSystem.ProcessJoinFormationCommands` (processes `CmdJoinFormation`) currently
writes to `FormationRoster.MemberEntities`. After the refactor it must:

1. Publish `CmdAssignSubordinate { Subordinate = follower, Commander = leader, Designation = ...,
   HasFormationSlot = 1, SlotIndex = <from CmdJoinFormation> }` to the event bus. **Never**
   directly write `UnitSubordinate`, `UnitRoster`, or `FormationFollower` from
   `VehicleCommandSystem` — `UnitHierarchySystem` is the sole mutation authority for all three.
   The kinematic `FormationFollower` is written atomically by `UnitHierarchySystem` only when
   the hierarchy transaction succeeds. Writing it early would leave a "zombie follower" (a
   vehicle steering into a formation it was never admitted to) if the capacity check later rejects
   the 17th applicant in the same tick.
2. Maintain `FormationController` on the leader (unchanged, no member list).

`CmdLeaveFormation` handling: publish `CmdRemoveSubordinate` only. **Do not** call
`repo.RemoveComponent<FormationFollower>` directly — `UnitHierarchySystem.RemoveFromHierarchy`
removes it atomically together with `UnitSubordinate`.

### 2.4 Update scope

All callers of `FormationRoster.MemberEntities`, `FormationMember.LeaderEntityId`, and the
old `FormationRoster.Count` (used for member list management) must be updated. Key places:

- `VehicleCommandSystem` (Fdp.Toolkits)
- `FormationTargetSystem` / `CarKinematicsSystem` (Fdp.Toolkits) — formation steering reads
  `FormationFollower.SlotIndex` and must now get the leader from `UnitSubordinate.Commander`
  rather than `FormationMember.LeaderEntityId`
- Any tests directly constructing `FormationRoster` with `MemberEntities`

---

## Phase 3 — Network Anti-Corruption Layer

### 3.1 Extend the DDS EntityInfo Descriptor

Add `TacticalDesignation` field to `Hrot.NED.Descriptors.EntityInfo`:

```csharp
[DdsTopic("EntityInfo")]
public partial struct EntityInfo
{
    [DdsKey] public int EntityId;
    public string Name;
    public eForceIdentifier ForceIdentifier;
    public int CommanderId;
    public eTacticalDesignation TacticalDesignation;   // NEW; 0 = Undefined
}
```

### 3.2 Remove CommanderId from Fdp.Core.EntityInfo

`Fdp.Core.EntityInfo` (the internal ECS component) is the presentation / network tier cache.
Its `CommanderId` field is removed. The single source of truth for the command relationship in
the local ECS is `UnitSubordinate.Commander`.

```csharp
[ComponentId(GlobalComponentIds.EntityInfo)]
public struct EntityInfo
{
    public FixedString64 Name;
    public ForceId ForceId;
    // CommanderId removed — use UnitSubordinate.Commander (Entity) for AI,
    // EntityInfoIngressTranslator for network ingress.
}
```

**Impact:** All local code that previously read `EntityInfo.CommanderId` must migrate:
- `EditorOrbatAdapter.GetVisibleNodes` → read `UnitSubordinate.Commander` instead.
- `EntityInfoEgressTranslator.ScanAndPublish` → read `UnitSubordinate` instead (see 3.3).
- `CreateEntityRequestSystem` line that sets `CommanderId` is removed; the
  `EntityInfoIngressTranslator` sets `UnitSubordinate` instead.

### 3.3 EntityInfoEgressTranslator Update

`ScanAndPublish` iterates all entities that have `NetworkIdentity` **and** `Fdp.Core.EntityInfo`
(the existing base query is unchanged). Inside the loop `UnitSubordinate` is checked optionally
via `view.HasComponent`; entities without it emit `CommanderId = 0` and
`TacticalDesignation = Undefined`.

**Important:** Do NOT add `UnitSubordinate` to the query filter. Doing so would silently exclude
commanders, standalone vehicles, and civilians from broadcasting their `Name` and `ForceIdentifier`.

```csharp
// Read UnitSubordinate if present; otherwise commander = 0
int commanderNetId = 0;
if (view.HasComponent<UnitSubordinate>(entity))
{
    ref readonly var sub = ref view.GetComponentRO<UnitSubordinate>(entity);
    if (!sub.Commander.IsNull)
        _entityMap.TryGetNetworkId(sub.Commander, out var cid);
        commanderNetId = (int)cid;
}
_writer.Write(new Hrot.NED.Descriptors.EntityInfo
{
    EntityId           = (int)netId.Value,
    Name               = data.Name.ToString(),
    ForceIdentifier    = MapForceId(data.ForceId),
    CommanderId        = commanderNetId,
    TacticalDesignation = view.HasComponent<UnitSubordinate>(entity)
        ? TacticalDesignationMapper.ToDds(view.GetComponentRO<UnitSubordinate>(entity).Designation)
        : eTacticalDesignation.Undefined
});
```

### 3.4 EntityInfoIngressTranslator Update (with deferred queue)

`ProcessSample` now writes `UnitSubordinate` via `CmdAssignSubordinate` event instead of
setting `EntityInfo.CommanderId`. Because DDS does not guarantee packet arrival order, the
translator implements the same deferred-queue pattern used by `MapRouteIngressTranslator`:

- **Pending queue:** `Dictionary<long, List<Entity>>` — key = commander's network ID; value =
  subordinate entities waiting for that commander to arrive.
- **Event subscription:** `_entityMap.EntityRegistered += OnEntityRegistered` sets a
  `_recentlyRegistered` flag (HashSet) so the retry loop runs only when relevant IDs arrive.
- **Per-sample logic:** If the commander's entity handle is resolved immediately, publish
  `CmdAssignSubordinate` directly. Otherwise add subordinate to pending list.
- **Drain loop:** At the top of `PollIngress`, process `_recentlyRegistered` and apply
  deferred assignments.
- **Memory safety:** Before adding a subordinate to a new commander's queue, scrub it from any
  existing queues (handles `CommanderId` update to a different value). `Dispose(long)` removes
  both the commander's queue entry and the entity from all pending lists.
- **Fallback for not-yet-spawned subordinates:** When `ProcessSample` receives an `EntityInfo`
  for a subordinate entity that does not yet exist in the ECS world, do **NOT** use
  `UpdateEntityCommand` — `NetworkSpawningSystem` silently drops it for unknown entities. Instead
  the translator maintains a second queue `_pendingUnspawnedSubordinates` keyed by the
  **subordinate**'s network ID. When the subordinate's `EntityRegistered` event fires, the entry
  is promoted: if the commander is already alive, `CmdAssignSubordinate` is published immediately;
  if the commander is also missing, the entry is moved into the existing
  `_pendingSubordinates` queue keyed by the commander's network ID.

---

## Phase 4 — Scenario Serialization

### 4.1 InitialUnitSubordinateIntent (new transient DTO)

Added to `Hrot.Common.Serializers.GenesisIntentComponents` alongside the existing intent DTOs:

```csharp
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialUnitSubordinateIntent)]  // ID 184
public sealed class InitialUnitSubordinateIntent
{
    public long CommanderNetworkId { get; set; }
    public TacticalDesignation Designation { get; set; }
}
```

### 4.2 UnitSubordinateTranslator (new scenario translator)

Implements `IEntityScenarioTranslator` for `UnitSubordinate`. New file in `Hrot.SimHost/Serializers/`.

- **Extract (Save):** reads `UnitSubordinate.Commander`, converts it to a stable GUID string via
  `IGuidResolver.Resolve(commander)`, writes `{ "commanderGuid": "...", "designation": N }`.
- **Inject (Load):** reads the GUID string, resolves via `IGuidResolver` to a network ID, attaches
  `InitialUnitSubordinateIntent` to the entity.
- `GetConsumedComponentsMask()` returns the bit for `UnitSubordinate` so the auto-serializer
  skips it.
- `IsExtractionSafe = false` (contains volatile entity handle).

**Network ID Remap on Load:** When a scenario is loaded into a live cluster,
`StagingEntityExtractor.RemapComponentNetworkIds` (Pass 2) patches offline network IDs inside
all transient intent DTOs. `InitialUnitSubordinateIntent.CommanderNetworkId` must be remapped in
this pass; otherwise the intent arrives with the dead staging ID and `GenesisMaterializationSystem`
never resolves it (see TASK-CS027).

### 4.3 HrotScenarioSerializerFactory Registration

`UnitSubordinateTranslator` is added to the factory's translator list (Hrot.SimHost):

```csharp
new UnitSubordinateTranslator()
```

### 4.4 GenesisMaterializationSystem — MaterializeUnitSubordinate

A new `MaterializeUnitSubordinate(ISimulationView, EntityCommandBuffer, EntityRepository)` method
is added, following the same retry-until-resolved pattern as `MaterializePassengers`:

- Queries entities with `InitialUnitSubordinateIntent`.
- If `_entityMap.TryGetEntity(intent.CommanderNetworkId, out var commander)` fails or the
  commander is not yet alive → `continue` (intent is preserved; retried next tick).
- On success: performs the two-way assignment atomically (see Phase 5 helper), then removes
  the intent via `cmd.RemoveManagedComponent`.

**Why retry works:** `GenesisMaterializationSystem` runs in `SystemPhase.Input` every tick while
genesis intents are present. Once all entities in a scenario are alive, every remaining
unresolved intent can resolve. This matches the existing `MaterializeHierarchy` pattern.

**Escape hatch (infinite-retry prevention):** If the entity's `EntityLifecycle` has reached
`EntityLifecycle.Active` (the per-entity validation timeout has elapsed and the entity is fully
spawned) but the commander still cannot be resolved, the intent must be dropped via
`cmd.RemoveManagedComponent` and a `FdpLog.Warn` emitted. This prevents a permanent per-frame
lookup penalty for deliberately unconnected or corrupt scenario references.

**Cluster load gating:** Because `DrainDeferredAcks` in `HrotScenarioLoadHandler` and
`CgfScenarioLoadHandler` holds the cluster in `LoadingLive` until all transient intents are
resolved, `InitialUnitSubordinateIntent` must be included in those checks. Without the guard the
cluster starts physical simulation before all hierarchies are linked (see TASK-CS026).

---

## Phase 5 — Runtime Hierarchy Management

### 5.1 Command Events

Two new unmanaged event structs (location: `Hrot.Core/CommandHierarchy/`):

```csharp
[EventId(/* assign new ID */)]
public struct CmdAssignSubordinate
{
    public Entity Subordinate;
    public Entity Commander;
    public TacticalDesignation Designation;
    public ushort SlotIndex;         // Formation slot; read only when HasFormationSlot == 1
    public byte HasFormationSlot;    // 1 = UnitHierarchySystem also writes FormationFollower atomically
}

[EventId(/* assign new ID */)]
public struct CmdRemoveSubordinate
{
    public Entity Subordinate;
}

[EventId(/* assign new ID */)]
public struct CmdAssignSubordinateRejected
{
    public Entity Subordinate;   // The entity whose CmdAssignSubordinate was rejected
}
```

`CmdAssignSubordinateRejected` is published by `UnitHierarchySystem` when a
`CmdAssignSubordinate` event is rejected due to roster capacity being full.
`VehicleCommandSystem` consumes it to fail the waiting `JoinFormationExecutor`
BTree node so the AI can recalculate a new plan.

### 5.2 UnitHierarchySystem

New system in `Hrot.Common/Systems/`, phase `SystemPhase.Simulation`. Must be registered in
**every** node-level logic pack — `SimHostCoreLogicPack`, `CgfLogicPack`, `IgLogicPack`, and
`EditorSubsystem` — so that presentation nodes (IG) and development nodes (Editor) also maintain
the local ECS hierarchy when they receive `CmdAssignSubordinate` events from the ingress
translator. Placing the file in `Hrot.Common` (which all of those packs already depend on)
avoids circular dependencies.

**Network dirty marking:** Whenever the system successfully mutates an entity's hierarchy
membership it must call `SmartEgressUtil.MarkDirty(repo, entity, EntityInfoDescriptorOrdinal)`
for every affected entity. Specifically:
- On `CmdAssignSubordinate` success: mark the **subordinate** dirty.
- On `CmdRemoveSubordinate` success: mark the **subordinate** dirty.
- In the destruction cascade: mark each **orphaned follower** dirty after removing its
  `UnitSubordinate`. Without these marks `EntityInfoEgressTranslator` will never broadcast the
  updated `CommanderId` to remote nodes.

**Responsibilities (per-Execute tick):**

1. **Destruction cascade:** Read `DestructionOrder` events (EventId 9003). For any entity that
   has `UnitRoster`, iterate its subordinates and `RemoveComponent<UnitSubordinate>` from each
   live subordinate. Also call `RemoveFromHierarchy` if the destroyed entity is itself a
   subordinate.

2. **Removals:** Read `CmdRemoveSubordinate` events. Call `RemoveFromHierarchy`.

3. **Assignments / Reassignments:** Read `CmdAssignSubordinate` events.
   - If the subordinate already has `UnitSubordinate` pointing to a **different** commander,
     call `RemoveFromHierarchy` first.
   - Capacity check: if `UnitRoster.Count >= UnitRoster.Capacity` → log warning, publish
     `CmdAssignSubordinateRejected { Subordinate = event.Subordinate }`, and abort the entire
     transaction (do not write `UnitSubordinate` or `FormationFollower`).
   - Atomic writes: set `UnitSubordinate` on subordinate; mutate and set back `UnitRoster` on
     commander; if `CmdAssignSubordinate.HasFormationSlot == 1`, also write
     `FormationFollower { SlotIndex = event.SlotIndex }` on the subordinate. All three writes
     succeed together or not at all.

**`RemoveFromHierarchy` helper (private):**
- Reads `UnitSubordinate.Commander` from the subordinate.
- Finds the subordinate's index in `UnitRoster.SubordinateEntities` (linear scan, N ≤ 16).
- Removes with an order-preserving left-shift using a manual `for` loop or a pointer-derived
  `Span<T>.CopyTo()`. **Do not use `System.Array.Copy`** — the `fixed` buffer is an unmanaged
  pointer, not a managed heap array; passing it to `Array.Copy` will either fail to compile or
  corrupt memory.
- Zeros the vacated last slot.
- Writes the mutated `UnitRoster` back to the commander.
- Calls `repo.RemoveComponent<UnitSubordinate>(subordinate)`.
- Calls `repo.RemoveComponent<FormationFollower>(subordinate)` if the component is present.
  This ensures total physical and cognitive decoupling and prevents zombie-kinematic behaviour
  where a vehicle steers into a formation position after losing its `UnitSubordinate`.

### 5.3 Event Sources

`CmdAssignSubordinate` is published by:
- `EntityInfoIngressTranslator` (live DDS traffic, Phase 3)
- `GenesisMaterializationSystem.MaterializeUnitSubordinate` (scenario load, Phase 4)
- `EditorOrbatAdapter.RequestAssignSubordinate` (UI drag-drop, Phase 6)

`CmdRemoveSubordinate` is published by:
- `EntityInfoIngressTranslator` (incoming `CommanderId = 0`)
- `EditorOrbatAdapter.RequestRemoveSubordinate`

`CmdAssignSubordinateRejected` is published by:
- `UnitHierarchySystem` (capacity exceeded — see Phase 5.2)

`CmdAssignSubordinateRejected` is consumed by:
- `VehicleCommandSystem` — sets `LocomotionChannel.Status = NodeStatus.Failure` for the
  rejected subordinate so the waiting `JoinFormationExecutor` BTree node can abort and
  recalculate a new plan. Without this, the BTree hangs indefinitely in `Running` state
  waiting for a `FormationFollower` that will never be written.

---

## Phase 6 — ORBAT UI Drag-Drop Subordination

### 6.1 OrbatNodeViewModel Extension

Add `CanAcceptSubordinates` flag (in `Hrot.UI.Common`):

```csharp
public sealed record OrbatNodeViewModel(
    int EntityId,
    string Name,
    int Depth,
    bool HasChildren,
    bool IsPendingDelete,
    bool CanAcceptSubordinates);   // true only for entities carrying UnitRoster
```

### 6.2 IOrbatController Extension

Add two new methods:

```csharp
void RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId);
void RequestRemoveSubordinate(int subordinateEntityId);
```

### 6.3 SharedOrbatPanel Update

The drop-target logic in `DrawContent` is extended to support subordination operations:

- **Drop onto a `CanAcceptSubordinates = true` node:** calls `RequestAssignSubordinate` instead
  of `RequestEmbark`.
- **Drop onto a `CanAcceptSubordinates = false` node:** calls `RequestEmbark` (existing embark
  behavior is unchanged).
- **Drop onto empty space (new "background" drop target below the tree):** calls
  `RequestRemoveSubordinate` to detach the entity from its commander.

`HandleDropPayload` is updated or a new `HandleHierarchyDropPayload` helper is extracted for
testability.

### 6.4 EditorOrbatAdapter Update

- `GetVisibleNodes`: builds the hierarchy from `UnitSubordinate.Commander` (ECS component) instead
  of `EntityInfo.CommanderId`. Sets `CanAcceptSubordinates = repo.HasComponent<UnitRoster>(entity)`.
- `RequestAssignSubordinate`: looks up both entity handles via `_indexCache`, publishes
  `CmdAssignSubordinate` on `_bus`. Changes apply immediately on the next ECS tick (even when
  SimTime is paused, the Editor ticks `UnitHierarchySystem`).
- `RequestRemoveSubordinate`: looks up the subordinate handle, publishes `CmdRemoveSubordinate`.

### 6.5 ExConOrbatAdapter Update

The ExCon has no local ECS. It operates on the DER (`IDerRepo`) which mirrors DDS data.

- `GetVisibleNodes`: populates `CanAcceptSubordinates` by checking whether the `EntityMasterDescriptor.TkbType`
  is a known commander type (composite units that carry `UnitRoster` on SimHost).
- `RequestAssignSubordinate` / `RequestRemoveSubordinate`: send an `UpdateEntityAttributeCommand`
  via the new `ICommandGateway.SendUpdateAttributeAsync` method. The command carries
  `AttributePatchJson = "{ \"CommanderId\": commanderId }"` (or `0` to remove). This reaches
  `UpdateEntityAttributeRequestSystem.ProcessRequest` on the SimHost, which intercepts the key
  and publishes the appropriate hierarchy event (see Phase 3 / CS024).
  **Do not** use `SendUpdateDescriptorAsync` (`UpdateEntityDescriptorRequest`): that topic carries
  a binary `EntityDescriptorUnion Payload` and has no JSON field; it cannot route the command.
- `ICommandGateway` must be extended with:
  ```csharp
  Task SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct = default);
  ```
  The concrete gateway implementation writes a `Hrot.NED.Messages.UpdateEntityAttributeRequest`
  DDS sample.

---

## Phase 7 — TKB Composite Definition Update

### 7.1 TkbChildSlot — Replace RoleTag with Designation

`TkbChildSlot` (in `Hrot.Core`) currently has `string RoleTag`. Replace with the ECS enum:

```csharp
public struct TkbChildSlot
{
    public long TkbType { get; set; }
    public int Count { get; set; }
    public TacticalDesignation Designation { get; set; }  // replaces string RoleTag
}
```

This eliminates string allocations in the TKB setup code and allows the composite-spawning
system to write `Designation` directly into `UnitSubordinate` when it creates child entities.

### 7.2 Composite-Spawning Update

The system that processes `TkbCompositionDef` to spawn child entities must be updated to:
1. Attach `InitialUnitSubordinateIntent { CommanderNetworkId = commanderNetworkId,
   Designation = slot.Designation }` to each spawned child (when
   `Designation != TacticalDesignation.Undefined`).

`GenesisMaterializationSystem` then resolves the intent once both the child and its commander are
fully alive and registered in `NetworkEntityMap`. This avoids a race where `UnitHierarchySystem`
rejects the assignment because the newly-spawned entities are not yet fully registered.
Direct attachment of `UnitSubordinate` or publishing `CmdAssignSubordinate` from the spawner is
**prohibited** — entities are not fully alive at spawn time.

---

## Architectural Decisions (summary)

| Decision | Rationale |
|----------|-----------|
| `UnitRoster` is `NoSave` | It is fully derived; saving fixed buffers with entity handles would corrupt on reload |
| `UnitSubordinate.Commander` is `Entity` (8 bytes) | Prevents zombie references from entity index recycling |
| Bottom-up (`UnitSubordinate`) is the truth; top-down (`UnitRoster`) is a cache | Network sends 1 field per subordinate (not an array); matches DDS EntityInfo protocol |
| CommanderId removed from `Fdp.Core.EntityInfo` | Eliminates duplicate of the same relationship across two components |
| ACL translators handle ID ↔ Entity mapping | No cross-layer leakage; presentation nodes don't run the AI tier |
| Capacity = 16, overflow aborts the transaction atomically | Both components are written together or not at all to prevent desync |
| Order-preserving removal (left-shift) | Guarantees deterministic `UnitRoster` order for UI and AI iteration |
| `UnitHierarchySystem` is the sole mutation authority | All sources publish events; system validates and atomically mutates both components |
| `FormationFollower` written/removed only by `UnitHierarchySystem` | Prevents zombie-kinematic state; guarantees kinematic and cognitive coupling are always in sync |
| Deferred queue in ingress translator | DDS packet arrival order is not guaranteed; pattern from `MapRouteIngressTranslator` |
| `_pendingUnspawnedSubordinates` second queue in ingress translator | `NetworkSpawningSystem` drops `UpdateEntityCommand` for unknown entities; the second queue handles late-spawning subordinates without losing hierarchy | Promoted to `_pendingSubordinates` on subordinate spawn if commander is still absent |
| `GenesisMaterializationSystem` retries unresolved intents every tick | ECS chunk processing order is arbitrary; retry-until-resolved avoids once-off missed initializations |

---

## Phase 8 — Architectural Validation: Success Conditions

### 8.1 Architectural Decoupling

**Kinematic vs. Cognitive:** `FormationFollower` carries a `SlotIndex` only; the leader entity is
obtained by reading `UnitSubordinate.Commander`. `UnitHierarchySystem` writes and removes both
`UnitSubordinate` and `FormationFollower` atomically so they are never out of sync. Breaking
formation (publish `CmdRemoveSubordinate`) removes both. `VehicleCommandSystem` never touches
`FormationFollower` directly.

**Network vs. Local:** `Fdp.Core.EntityInfo` does not contain `CommanderId`. `UnitSubordinate` is
the absolute ECS truth for the AI tier. The DDS `EntityInfo` descriptor carries the integer
`CommanderId`, bridged via `EntityInfoEgressTranslator` (ECS to DDS) and
`EntityInfoIngressTranslator` (DDS to ECS events).

### 8.2 Memory and Structural Safety

- `UnitRoster` uses `fixed long SubordinateEntities[16]` and `fixed ushort TacticalDesignations[16]`;
  `Capacity = 16` is a compile-time constant.
- On removal: order-preserving left-shift via manual `for` loop; `System.Array.Copy` is never
  used on the unmanaged fixed buffer.
- All mutations of `UnitSubordinate`, `UnitRoster`, and `FormationFollower` go through
  `UnitHierarchySystem` exclusively. Capacity violations abort the transaction completely
  (no partial state).

### 8.3 Networking and Distributed State

- `SmartEgressUtil.MarkDirty` is called on every affected entity for assign, remove, and
  destruction cascade, so `EntityInfoEgressTranslator` broadcasts the updated `CommanderId`.
- `EntityInfoIngressTranslator` defers hierarchy commands via the pending queue (commander not
  yet in `_entityMap`) and maintains a second `_pendingUnspawnedSubordinates` queue for
  subordinates whose ECS entity has not yet spawned. On subordinate `EntityRegistered`, the
  entry is promoted: publish `CmdAssignSubordinate` directly if the commander is alive, or
  migrate to `_pendingSubordinates` if it is not. `UpdateEntityCommand` is never used for
  this path because `NetworkSpawningSystem` silently drops it for unknown entities.
- `UpdateEntityAttributeRequestSystem.ProcessRequest` intercepts `"CommanderId"` in the JSON
  before the reflection compiler. It resolves the network ID, checks `view.HasAuthority`,
  publishes the appropriate event, sanitizes the JSON (removes `"CommanderId"`), and then
  passes the clean JSON to the reflection compiler. This is the correct interception point
  because it has direct access to `Entity`, `EntityRepository`, `NetworkEntityMap`, and
  `repo.Bus` — dependencies that `IEntityPatchContext` / `ValueAttributeSetter<T>` do not
  expose. The ACK is sent by the system when the interception flag is set or the reflection
  compiler reports any mutation.

### 8.4 Scenario Serialization (Genesis Pipeline)

- `UnitRoster` carries `[DataPolicy(DataPolicy.NoSave)]` and is dynamically reconstructed from
  `UnitSubordinate` records after load.
- `UnitSubordinateTranslator` converts `Entity` handles to GUID strings on save and attaches
  `InitialUnitSubordinateIntent` on load.
- `GenesisMaterializationSystem` retries unresolved intents each tick; drops with `FdpLog.Warn`
  if the entity reaches `EntityLifecycle.Active` before the commander is found (escape hatch).

---

## Phase 9 — Integration Testing Requirements

Integration tests must cover the distributed boundary cases below. Use `HrotRunnerHarness` for
multi-node DDS tests and `EditorHarness` for offline serialization tests.

### 9.1 Out-of-Order Ingress (`HrotRunnerHarness`, nodes: `"simhost,cgf"`)

Simulate DDS traffic where the subordinate's `EntityInfo` packet (with `CommanderId = N`) arrives
before the commander's creation packet.

Assert: the subordinate sits in the ingress pending queue initially; once the commander's
`EntityRegistered` fires, the queue is drained and `CmdAssignSubordinate` is published;
`UnitSubordinate` and `UnitRoster` are correctly linked on the next tick.

### 9.2 Atomic Capacity Validation (`EditorHarness`)

Spawn a commander entity. Publish 17 `CmdAssignSubordinate` events (all targeting the same
commander) on the same `FdpEventBus`. Pump simulation frames.

Assert: `UnitRoster.Count == 16`; the 17th entity has no `UnitSubordinate` and no
`FormationFollower` (partial state is ruled out).

### 9.3 Destruction Cascade and Dirty Egress (`HrotRunnerHarness`, node: `"simhost"`)

Spawn a platoon. Publish `DestructionOrder` for the platoon commander.

Assert: `UnitSubordinate` removed from all children; for each child,
`SmartEgressUtil.ShouldPublish(view, child, EntityInfoDescriptorOrdinal)` returns `true`,
proving dirty marks were set so remote nodes (IG, ExCon) receive the update.

### 9.4 ExCon Drag-and-Drop Patching (`HrotRunnerHarness`, nodes: `"simhost,ig,excon"`)

Emulate drag-and-drop by sending a JSON `UpdateEntityDescriptorCommand` patch
`{ "CommanderId": N }` via the ExCon mock client.

Assert: the ExCon mock client receives `UpdateEntityDescriptorAck` without a 5-second timeout
(patch context was marked as mutated); `UnitRoster` on both the old and new commander reflects
an order-preserving shift.

### 9.5 Kinematic / Tactical Decoupling (`HrotRunnerHarness`)

Spawn an `InfantrySquad` using `TkbCompositionDef` (with `Designation` enum, no `RoleTag`).
Issue a BTree tactical intent to followers to break formation.

Assert: followers drop out of `KinematicsMode.Formation` (kinematic decoupling) while retaining
`UnitSubordinate` and remaining in `UnitRoster` (cognitive coupling preserved); the leader's
`UnitRoster` reflects the correct member count throughout.

### 9.6 Genesis Scenario Serialization (`EditorHarness`)

Save a scenario containing a commander and a subordinate via `EditorHarness.Editor.SaveScenario`.
Clear the world (`NewScenario`) and load the JSON file.

Assert: `GenesisMaterializationSystem` reconstructs `UnitSubordinate` and `UnitRoster` correctly
regardless of ECS chunk processing order; `UnitRoster` is dynamically rebuilt (not loaded from a
corrupted fixed-buffer dump); the subordinate's `FormationFollower` is re-applied if applicable.

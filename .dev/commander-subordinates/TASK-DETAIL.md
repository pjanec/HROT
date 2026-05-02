# Commander-Subordinate Infrastructure — Task Details

All task IDs use the prefix **CS** (Commander-Subordinate).

---

## Phase 1 — Core Component Definitions

---

### TASK-CS001 — TacticalDesignation Dual-Enum Definitions

**Design Reference:** DESIGN.md § Phase 1.1

**Scope:**
- Create `TacticalDesignation` ECS enum in `Hrot.Core/CommandHierarchy/TacticalDesignation.cs`
- Create `eTacticalDesignation` DDS enum in `Hrot.Network.NED/GenericDescriptors.cs` (add to existing file)
- Create `TacticalDesignationMapper` static class in `Hrot.Network.NED` (new file: `Replication/Map/TacticalDesignationMapper.cs`)

**NOT included:** Applying the enums to any component or descriptor — that happens in CS002, CS003, CS007.

**Constraints:**
- Both enums must derive from `ushort` for 2-byte memory footprint.
- Values must be identical across both enums (Undefined=0, Commander=1, SquadLeader=2, Wingman=3, Support=4).
- A synchronization comment (`/// IMPORTANT: Must be kept in sync with ...`) must appear on both enum definitions.
- `TacticalDesignationMapper` methods must be simple casts — no lookup tables.

**Success Conditions:**

1. *Enum values match:*
   ```
   (ushort)TacticalDesignation.SquadLeader == (ushort)eTacticalDesignation.SquadLeader  // true
   (ushort)TacticalDesignation.Undefined == 0  // true
   ```

2. *Mapper round-trips:*
   Setup: call `TacticalDesignationMapper.ToDds(TacticalDesignation.Wingman)`.
   Assert: returns `eTacticalDesignation.Wingman`.
   Setup: call `TacticalDesignationMapper.ToEcs(eTacticalDesignation.Support)`.
   Assert: returns `TacticalDesignation.Support`.

3. *Default zero is Undefined on both sides:*
   `default(TacticalDesignation) == TacticalDesignation.Undefined` is true.
   `default(eTacticalDesignation) == eTacticalDesignation.Undefined` is true.

---

### TASK-CS002 — UnitSubordinate Component

**Design Reference:** DESIGN.md § Phase 1.2

**Scope:**
- Create `UnitSubordinate` struct in `Hrot.Core/CommandHierarchy/UnitSubordinate.cs`
- Add `HrotComponentIds.UnitSubordinate = 183` to `Hrot.Core/MapDefinitions/HrotComponentIds.cs`
- Register `world.RegisterComponent<UnitSubordinate>()` in SimHostCoreLogicPack component registry

**NOT included:** Populating the component (translator / materialization changes are separate tasks).

**Constraints:**
- Must use `[StructLayout(LayoutKind.Sequential)]` and `[ComponentId(HrotComponentIds.UnitSubordinate)]`.
- `Commander` field must be `Fdp.Core.Entity` (8 bytes, generation-safe) — **not** `int`.
- `Designation` must use the ECS `TacticalDesignation` enum.
- `Entity.Null` (default zero) is the valid "no commander" sentinel; no special null check needed.

**Success Conditions:**

1. *Component is blittable:*
   `System.Runtime.InteropServices.Marshal.SizeOf<UnitSubordinate>()` equals **16 bytes**.
   The struct contains one `Entity` (8-byte aligned `ulong`) and one `ushort`; C# struct
   alignment rules pad to the largest field's alignment (8 bytes), adding 6 implicit bytes
   after the `ushort`. A test asserting 10 bytes will fail immediately.

2. *ComponentId attribute is correct:*
   `typeof(UnitSubordinate).GetCustomAttribute<ComponentIdAttribute>().Id == HrotComponentIds.UnitSubordinate`.

3. *ECS world registers without exception:*
   Setup: fresh `EntityRepository`; call `world.RegisterComponent<UnitSubordinate>()`.
   Assert: no exception; `world.GetComponentTable<UnitSubordinate>()` returns non-null.

4. *Default value is safe null commander:*
   `new UnitSubordinate().Commander == Entity.Null` is true.
   `new UnitSubordinate().Designation == TacticalDesignation.Undefined` is true.

---

### TASK-CS003 — UnitRoster Component

**Design Reference:** DESIGN.md § Phase 1.3

**Scope:**
- Create `UnitRoster` unsafe struct in `Hrot.Core/CommandHierarchy/UnitRoster.cs`
- Add `HrotComponentIds.UnitRoster = 182` to `HrotComponentIds.cs`
- Register in SimHostCoreLogicPack component registry

**NOT included:** Populating the component.

**Constraints:**
- Must carry `[DataPolicy(DataPolicy.NoSave)]` — the serializer must not save it.
- Must carry `[StructLayout(LayoutKind.Sequential)]` and `[ComponentId(HrotComponentIds.UnitRoster)]`.
- `public const int Capacity = 16` must appear inside the struct definition.
- Fixed buffers must use the named constant: `fixed long SubordinateEntities[Capacity]` and
  `fixed ushort TacticalDesignations[Capacity]` (not hard-coded `16`).
- The struct must be declared `unsafe`.

**Success Conditions:**

1. *Correct memory size (no silent overflow):*
   `sizeof(UnitRoster)` == `4 + 16*8 + 16*2` == `4 + 128 + 32` == 164 bytes.
   (Verify with `Unsafe.SizeOf<UnitRoster>()` in a unit test.)

2. *DataPolicy is NoSave:*
   `typeof(UnitRoster).GetCustomAttribute<DataPolicyAttribute>().Value & DataPolicy.NoSave != 0`.

3. *Capacity constant is 16:*
   `UnitRoster.Capacity == 16`.

4. *Writing to index 15 does not corrupt adjacent memory:*
   Setup: allocate `UnitRoster` in a fixed byte buffer; write a sentinel long to index 15 of
   `SubordinateEntities`; verify the byte immediately after the struct boundary is unchanged.

---

### TASK-CS004 — Component ID Registration

**Design Reference:** DESIGN.md § Phase 1.4

**Scope:**
- Add `UnitRoster = 182`, `UnitSubordinate = 183`, `InitialUnitSubordinateIntent = 184` to
  `HrotComponentIds.cs` (Hrot.Core) — doc comments required.
- Register `UnitRoster` and `UnitSubordinate` in `SimHostCoreLogicPack` component registry, and
  in the `EditorSubsystem` / Editor integration-test harness component setup.
- Update `ComponentRegistryTests` to cover the two new components.

**NOT included:** `InitialUnitSubordinateIntent` registration (done in TASK-CS009 when the class is created).

**Constraints:**
- IDs 182–184 must not conflict with any existing `HrotComponentIds` entry.
- Comment format must match existing entries: `/// <summary><c>UnitRoster</c> — ...</summary>`.

**Success Conditions:**

1. *No duplicate IDs:*
   A test iterates all `public const byte` fields on `HrotComponentIds` and asserts all values are unique.

2. *Components are available in SimHostCoreLogicPack world:*
   Integration test: create a SimHost world via `SimHostCoreLogicPack`; assert
   `world.GetComponentTable<UnitRoster>() != null` and `world.GetComponentTable<UnitSubordinate>() != null`.

---

## Phase 2 — Formation Component Refactor

---

### TASK-CS005 — Rename FormationRoster to FormationController

**Design Reference:** DESIGN.md § Phase 2.1

**Scope:**
- In `Fdp.Toolkits/CarKinem/Formation/FormationRoster.cs`: rename struct to `FormationController`,
  remove `MemberEntities` and `SlotIndices` fixed arrays, remove `Count` field.
- In `Fdp.Core/GlobalComponentIds.cs`: rename `FormationRoster = 33` to `FormationController = 33`.
- Update all usages in Fdp.Toolkits tests.

**NOT included:** Updating `VehicleCommandSystem` or other consumers of the old member arrays (TASK-CS007).

**Constraints:**
- The component ID value must remain 33 (backwards compatibility for already-registered worlds).
- The remaining fields (`TemplateId`, `Type`, `Params`) must be preserved unchanged.
- All existing formation steering tests must pass after the rename.

**Success Conditions:**

1. *`FormationRoster` no longer exists in codebase:*
   `grep_search("FormationRoster", "**/*.cs")` returns zero results (except this task list).

2. *`FormationController` compiles cleanly:*
   `sizeof(FormationController)` matches `TemplateId(4) + Type(4 if int) + Params(size)`.

3. *ComponentId is unchanged:*
   `typeof(FormationController).GetCustomAttribute<ComponentIdAttribute>().Id == 33`.

---

### TASK-CS006 — Rename FormationMember to FormationFollower

**Design Reference:** DESIGN.md § Phase 2.2

**Scope:**
- In `Fdp.Toolkits/CarKinem/Formation/FormationMember.cs`: rename struct to `FormationFollower`,
  remove `LeaderEntityId` field (int).
- In `Fdp.Core/GlobalComponentIds.cs`: rename `FormationMember = 45` to `FormationFollower = 45`.
- Update all usages in Fdp.Toolkits tests.

**NOT included:** Updating `VehicleCommandSystem` to publish `CmdAssignSubordinate` (TASK-CS007).

**Constraints:**
- Component ID value must remain 45.
- Fields `SlotIndex`, `State`, `IsInFormation`, `SlotDistFiltered`, `RejoinTimer` must be preserved.
- Existing tests that set up `FormationMember` must be updated to `FormationFollower`.
  The test `VehicleCommandSystem.JoinFormation_SetsFormationMemberAndMode` must be updated
  to check `FormationFollower` rather than `FormationMember`.

**Success Conditions:**

1. `FormationMember` no longer exists in codebase.
2. `typeof(FormationFollower).GetCustomAttribute<ComponentIdAttribute>().Id == 45`.
3. Renamed test `JoinFormation_SetsFormationFollowerAndMode` passes.

---

### TASK-CS007 — Update VehicleCommandSystem for New Component Names

**Design Reference:** DESIGN.md § Phase 2.3

**Scope:**
- Update `VehicleCommandSystem.ProcessJoinFormationCommands` to use `FormationController` and
  `FormationFollower`; publish `CmdAssignSubordinate` (with `HasFormationSlot = 1` and the slot
  index from `CmdJoinFormation`) to the event bus for hierarchy and kinematic changes.
- Update `ProcessLeaveFormationCommands` (or equivalent) to publish `CmdRemoveSubordinate` only.
- Update `FormationTargetSystem` / `CarKinematicsSystem` to read the formation leader via
  `UnitSubordinate.Commander` (replacing the old `FormationMember.LeaderEntityId` lookup).

**NOT included:** Creating `CmdAssignSubordinate` / `CmdRemoveSubordinate` events (TASK-CS015).

**Constraints:**
- The `CmdJoinFormation` (EventId 2104) event struct is unchanged — it still uses `Entity Entity`,
  `Entity LeaderEntity`, `int SlotIndex`.
- `VehicleCommandSystem` must publish
  `CmdAssignSubordinate { Subordinate = follower, Commander = leader, Designation = ...,
  HasFormationSlot = 1, SlotIndex = <from event> }`. **Never** directly write `UnitSubordinate`,
  `UnitRoster`, or `FormationFollower` from `VehicleCommandSystem`. Writing `FormationFollower`
  before the hierarchy transaction is confirmed creates a "zombie follower" (a vehicle steering
  into a formation it was never admitted to if the slot was already full by the time
  `UnitHierarchySystem` processes the event in the same tick).
- `VehicleCommandSystem` must not read `UnitRoster.Count` as a capacity pre-check. That check
  belongs exclusively to `UnitHierarchySystem`, which processes the event atomically.
- `CmdLeaveFormation` handling: publish `CmdRemoveSubordinate` only. **Do not** remove
  `FormationFollower` directly — `UnitHierarchySystem.RemoveFromHierarchy` removes it atomically.
- `FormationTargetSystem` must obtain the leader entity from `UnitSubordinate.Commander`, not from
  `FormationFollower` (which no longer has a `LeaderEntityId`).
- **Rejection handling (prevents zombie AI):** `VehicleCommandSystem` must also consume
  `CmdAssignSubordinateRejected` events (published by `UnitHierarchySystem` on capacity failure).
  For each rejected subordinate entity, set `LocomotionChannel.Status = NodeStatus.Failure`.
  Without this, the `JoinFormationExecutor` BTree node remains `Running` indefinitely, waiting
  for a `FormationFollower` that will never be attached, and the unit's AI is permanently frozen.

**Success Conditions:**

1. *Join publishes hierarchy+formation event; does NOT write FormationFollower directly:*
   Test: create leader and follower; publish `CmdJoinFormation`; tick `VehicleCommandSystem` only
   (do not tick `UnitHierarchySystem` yet).
   Assert: follower does NOT yet have `FormationFollower` (direct write is prohibited); a
   `CmdAssignSubordinate { Subordinate = follower, Commander = leader, HasFormationSlot = 1,
   SlotIndex = <expected> }` event is on the bus.
   (Then tick `UnitHierarchySystem` and assert `UnitSubordinate`, `UnitRoster`, AND
   `FormationFollower { SlotIndex }` are all written atomically.)

2. *Roster overflow rejects join atomically — no FormationFollower written:*
   Setup: leader with `UnitRoster.Count == UnitRoster.Capacity`; publish `CmdJoinFormation`.
   Tick both `VehicleCommandSystem` and `UnitHierarchySystem`.
   Assert: follower has neither `FormationFollower` nor `UnitSubordinate`;
   `UnitRoster.Count` remains at capacity; no `CmdAssignSubordinate` published.

3. *Leave publishes removal event only; does NOT directly remove FormationFollower:*
   Setup: follower with both `FormationFollower` and `UnitSubordinate`; publish `CmdLeaveFormation`.
   Tick `VehicleCommandSystem` only.
   Assert: follower still has `FormationFollower` (not yet removed); a `CmdRemoveSubordinate`
   event is on the bus.
   (Tick `UnitHierarchySystem` and assert both `UnitSubordinate` and `FormationFollower` removed.)

4. *Formation steering reads commander from UnitSubordinate:*
   `FormationTargetSystem` test: setup follower with `UnitSubordinate.Commander = leader`, no
   `LeaderEntityId` field; assert the steering target is computed relative to the leader's
   transform.

5. *Rejection received \u2014 LocomotionChannel set to Failure:*
   Setup: publish `CmdAssignSubordinateRejected { Subordinate = follower }` to the event bus.
   Tick `VehicleCommandSystem`.
   Assert: `LocomotionChannel.Status == NodeStatus.Failure` on the follower entity.
   (This allows the `JoinFormationExecutor` BTree node to abort and the AI to recalculate.)

---

## Phase 3 — Network Anti-Corruption Layer

---

### TASK-CS008 — Extend EntityInfo DDS Descriptor

**Design Reference:** DESIGN.md § Phase 3.1

**Scope:**
- Add `public eTacticalDesignation TacticalDesignation;` field to `Hrot.NED.Descriptors.EntityInfo`
  partial struct in `Hrot.Network.NED/GenericDescriptors.cs`.

**Constraints:**
- Field ordering must not change existing fields (DDS wire format is sensitive to field order in
  some bindings). Add at the end.
- `eTacticalDesignation` must be the DDS-side enum defined in TASK-CS001.
- Default (zero) is `eTacticalDesignation.Undefined`.

**Success Conditions:**

1. `EntityInfo` descriptor struct compiles and contains `TacticalDesignation` field.
2. Serialization round-trip test: write `TacticalDesignation = eTacticalDesignation.SquadLeader`;
   deserialize; assert value is preserved.

---

### TASK-CS009 — Remove CommanderId from Fdp.Core.EntityInfo

**Design Reference:** DESIGN.md § Phase 3.2

**Scope:**
- Remove `public int CommanderId;` from `Fdp.Core/Components/EntityInfo.cs`.
- Fix every compilation error caused by removing the field across the entire solution.
  Key files to update:
  - `EntityInfoEgressTranslator.ScanAndPublish` (reads `data.CommanderId`) — handled in TASK-CS010.
  - `EntityInfoIngressTranslator.ProcessSample` and `ApplyToEntity` (writes `CommanderId`) — handled in TASK-CS011.
  - `EditorOrbatAdapter.GetVisibleNodes` (reads `EntityInfo.CommanderId` for tree building) — update
    to use `UnitSubordinate.Commander.Index` instead. Entities without `UnitSubordinate` are treated
    as roots (`commanderId = 0`).
  - `CreateEntityRequestSystem` line that sets `CommanderId = (int)pending.NetworkId` — remove.
  - Any tests that set `CommanderId = 42` on `EntityInfo`.

**Constraints:**
- No field named `CommanderId` may remain on `Fdp.Core.EntityInfo` after this task.
- The ORBAT tree in the Editor must still build correctly using `UnitSubordinate.Commander.Index`.
- All existing tests that assert the ORBAT tree structure must be updated; no tests may be deleted.

**Success Conditions:**

1. `typeof(Fdp.Core.EntityInfo).GetField("CommanderId")` returns null.
2. Solution builds without errors (`dotnet build` returns 0 errors).
3. `EditorOrbatAdapter` ORBAT tree test: create two entities where entity B has
   `UnitSubordinate.Commander` pointing to entity A; assert `GetVisibleNodes` places B as a
   child of A at depth 1.

---

### TASK-CS010 — Update EntityInfoEgressTranslator

**Design Reference:** DESIGN.md § Phase 3.3

**Scope:**
- Rewrite `ScanAndPublish` in `EntityInfoEgressTranslator` to read `UnitSubordinate` instead of
  `EntityInfo.CommanderId`.
- Populate the new `TacticalDesignation` field in the outgoing DDS packet.

**Constraints:**
- The main entity scan must iterate all entities with `NetworkIdentity` + `Fdp.Core.EntityInfo`
  (the existing base query — unchanged). Inside the loop, check for `UnitSubordinate` using
  `view.HasComponent<UnitSubordinate>(entity)`; if absent, emit `CommanderId = 0` and
  `TacticalDesignation = eTacticalDesignation.Undefined`. **Do not** add `UnitSubordinate` to
  the query filter — doing so would silently exclude commanders, standalone vehicles, and
  civilians from broadcasting their `Name` and `ForceIdentifier`.
- Must use `_entityMap.TryGetNetworkId(sub.Commander, out long cid)` for the ID conversion.
- If `TryGetNetworkId` fails (e.g. commander was just destroyed), use `commanderNetId = 0` and
  log a debug-level message.
- **Authority guard (mandatory):** The loop must preserve the existing
  `view.HasAuthority(entity, packedKey)` guard. Only publish `EntityInfo` descriptors for
  entities this node has network authority over. Dropping this guard would cause every cluster
  node to broadcast ORBAT updates for every entity simultaneously, triggering a DDS broadcast
  storm.
- **Dirty-state gate (mandatory to prevent broadcast storm):** Wrap every `_writer.Write()` call
  with `SmartEgressUtil.ShouldPublishDescriptor(view, entity, EntityInfoDescriptorOrdinal)` (or
  the equivalent `ShouldPublish` overload used by existing egress translators for this topic).
  Only emit a DDS sample when the entity is dirty (changed since last publish) or due for a
  salted heartbeat. Without this gate the translator will serialize and publish a full
  `EntityInfo` packet for every locally-owned entity on every 60 Hz frame, saturating the DDS
  network and choking all connected nodes.

**Success Conditions:**

1. *UnitSubordinate present — ID and designation published:*
   Setup: entity with `NetworkIdentity`, `EntityInfo`, `UnitSubordinate { Commander = cmdEntity,
   Designation = TacticalDesignation.SquadLeader }`; `NetworkEntityMap` maps cmdEntity to net ID 99.
   Call `ScanAndPublish`.
   Assert: written `Hrot.NED.Descriptors.EntityInfo.CommanderId == 99` and
   `TacticalDesignation == eTacticalDesignation.SquadLeader`.

2. *No UnitSubordinate — CommanderId zero:*
   Setup: entity with `NetworkIdentity` and `EntityInfo` but no `UnitSubordinate`.
   Assert: written descriptor has `CommanderId == 0` and `TacticalDesignation == Undefined`.

3. *Commander entity destroyed — CommanderId zero, no exception:*
   Setup: entity with `UnitSubordinate.Commander` pointing to a dead entity (not in `NetworkEntityMap`).
   Assert: no exception; `CommanderId == 0`.

---

### TASK-CS011 — Update EntityInfoIngressTranslator (with deferred queue)

**Design Reference:** DESIGN.md § Phase 3.4

**Scope:**
- Rewrite `ProcessSample` to publish `CmdAssignSubordinate` (or `CmdRemoveSubordinate` when
  `CommanderId == 0`) instead of writing `EntityInfo.CommanderId`.
- Implement the `_pendingSubordinates` deferred queue and `EntityRegistered` event subscription
  (pattern from `MapRouteIngressTranslator`).
- Update `Dispose(long)` for memory-safe cleanup.
- The `Fdp.Core.EntityInfo` component (Name, ForceId) must still be applied as before.

**NOT included:** Changes to `EntityInfoEgressTranslator`.

**Constraints:**
- A subordinate entity must be scrubbed from all existing pending queues before being added to a
  new one (prevents multi-queue membership when `CommanderId` changes).
- `_recentlyRegistered.Clear()` must be called at the end of every `PollIngress` call.
- `Dispose(long networkEntityId)` must handle both: (a) the disposed entity was a commander →
  remove its entire queue entry; (b) the disposed entity was a pending subordinate → remove it
  from whichever list it occupies.
- **Not-yet-spawned subordinate path (second pending queue):** When `ProcessSample` receives
  an `EntityInfo` for a subordinate that does not yet exist as an ECS entity (net ID not in
  `_entityMap`), do **NOT** use `UpdateEntityCommand`. `NetworkSpawningSystem` silently drops
  `UpdateEntityCommand` for unknown entities (logs: "[NS] UpdateEntityCommand for unknown
  network entity... ignored."), so the subordinate would permanently lose its hierarchy.
  Instead, record the pending data in a second queue:
  `_pendingUnspawnedSubordinates: Dictionary<long, (long CommanderNetId, TacticalDesignation Designation)>`
  keyed by the **subordinate**'s network ID.
  Subscribe to `_entityMap.EntityRegistered`. When the subordinate's net ID fires:
  - If the commander is already alive in `_entityMap` — publish `CmdAssignSubordinate` immediately.
  - If the commander is not yet alive — move the entry into the existing `_pendingSubordinates`
    queue keyed by the commander's net ID (same deferred-by-commander path as spawned entities).
  Remove the entry from `_pendingUnspawnedSubordinates` in both cases.
  `Dispose(long)` must also clean up `_pendingUnspawnedSubordinates`.
- Before publishing `CmdRemoveSubordinate`, verify the entity currently carries a `UnitSubordinate`
  component. This prevents event bus flooding from periodic DDS state refreshes for unassigned,
  civilian, or neutral entities that continuously broadcast `CommanderId = 0`.

**Success Conditions:**

1. *Commander present — immediate assignment:*
   Setup: populate `NetworkEntityMap` with commander net ID 10 → commander entity; call
   `ProcessSample` with `info.CommanderId = 10, info.TacticalDesignation = SquadLeader`.
   Assert: `CmdAssignSubordinate` with the resolved commander entity and `SquadLeader` designation
   is published to `_eventBus`.

2. *Commander absent — deferred:*
   Setup: `NetworkEntityMap` has no entry for commander net ID 20; call `ProcessSample` with
   `info.CommanderId = 20`.
   Assert: no `CmdAssignSubordinate` published immediately; entity is in `_pendingSubordinates[20]`.

3. *Deferred resolved on EntityRegistered:*
   Continuing test 2: register commander net ID 20 in `NetworkEntityMap` (triggers `EntityRegistered`);
   call `PollIngress`.
   Assert: `CmdAssignSubordinate` now published for the deferred subordinate.

4. *Commander update scrubs old queue:*
   Setup: entity deferred under commander A (net ID 30); new DDS sample arrives with
   `CommanderId = 40` (different commander).
   Assert: entity removed from queue for 30; entity added to queue for 40.

5. *Dispose cleans up pending subordinate:*
   Setup: entity deferred under commander net ID 50; call `Dispose(entityNetId)`.
   Assert: entity is no longer in any pending list.

6. *CommanderId == 0 with existing UnitSubordinate — publishes CmdRemoveSubordinate:*
   Setup: entity with existing `UnitSubordinate`; call `ProcessSample` with `CommanderId = 0`.
   Assert: `CmdRemoveSubordinate` published for that entity.

8. *Subordinate arrives before entity spawns — queued in _pendingUnspawnedSubordinates:*
   Setup: `_entityMap` does NOT contain subordinate net ID 99; call `ProcessSample` with
   subordinate net ID 99, `CommanderId = 10`.
   Assert: no event published; entry exists in `_pendingUnspawnedSubordinates[99]`.

9. *Entity spawns while commander is already alive — immediate assignment:*
   Continuing test 8: register commander net ID 10 in `_entityMap`; then register subordinate
   net ID 99 (triggers `EntityRegistered`).
   Assert: `CmdAssignSubordinate` is published; `_pendingUnspawnedSubordinates` no longer
   contains key 99.

10. *Entity spawns while commander is also missing — moves to _pendingSubordinates:*
    Setup: subordinate net ID 99 in `_pendingUnspawnedSubordinates` with commander net ID 10;
    register subordinate net ID 99 before commander appears.
    Assert: entry moves from `_pendingUnspawnedSubordinates` to `_pendingSubordinates[10]`;
    no event yet published.

11. *Dispose cleans _pendingUnspawnedSubordinates:*
    Setup: subordinate net ID 99 in `_pendingUnspawnedSubordinates`; call `Dispose(99)`.
    Assert: key 99 removed from `_pendingUnspawnedSubordinates`.

---

## Phase 4 — Scenario Serialization

---

### TASK-CS012 — InitialUnitSubordinateIntent Component

**Design Reference:** DESIGN.md § Phase 4.1

**Scope:**
- Add `InitialUnitSubordinateIntent` class to `Hrot.Common/Serializers/GenesisIntentComponents.cs`
  (same file as existing intent DTOs).
- Add `HrotComponentIds.InitialUnitSubordinateIntent = 184`.
- Register the component in `SimHostCoreLogicPack`.

**Constraints:**
- Must carry `[DataPolicy(DataPolicy.Transient)]` and `[ComponentId(HrotComponentIds.InitialUnitSubordinateIntent)]`.
- Must be a `sealed class` (managed), not a struct — matches the existing intent DTO pattern.
- `Designation` property must be `TacticalDesignation` (ECS enum, not the DDS enum).

**Success Conditions:**

1. `typeof(InitialUnitSubordinateIntent).GetCustomAttribute<DataPolicyAttribute>().Value == DataPolicy.Transient`.
2. `new InitialUnitSubordinateIntent { CommanderNetworkId = 99, Designation = TacticalDesignation.Wingman }` round-trips through JSON serialization preserving both values.

---

### TASK-CS013 — UnitSubordinateTranslator (IEntityScenarioTranslator)

**Design Reference:** DESIGN.md § Phase 4.2, 4.3

**Scope:**
- Create `UnitSubordinateTranslator.cs` in `Hrot.SimHost/Serializers/`.
- Register it in `HrotScenarioSerializerFactory.Build()`.

**Constraints:**
- `GetConsumedComponentsMask()` must include the bit for `UnitSubordinate` (so auto-serializer skips it).
- `IsExtractionSafe` must return `false`.
- `Extract`: if entity has no `UnitSubordinate` or `Commander.IsNull`, emit nothing (return empty dict).
- `Inject`: read `commanderGuid` string; call `resolver.Resolve(commanderGuidStr)` to obtain
  a staging `Entity` handle; then query `stagingRepo.GetComponent<NetworkIdentity>(resolvedCmd).Value`
  to extract the `long` network ID; attach `InitialUnitSubordinateIntent`. If the GUID cannot be
  resolved or the staging entity lacks `NetworkIdentity`, log a warning and attach with
  `CommanderNetworkId = 0` so materialization skips it gracefully.
  `IGuidResolver.Resolve` returns an `Entity` handle, **not** a `long` — passing the result
  directly as a network ID will silently produce a garbage value.

**Success Conditions:**

1. *Extract with commander:*
   Setup: entity with `UnitSubordinate { Commander = entityA, Designation = SquadLeader }`;
   `IGuidResolver.Resolve(entityA)` returns `"guid-A"`.
   Assert: extracted dict contains `"commanderGuid": "guid-A"` and `"designation": 2`.

2. *Extract with null commander:*
   Entity has `UnitSubordinate` with `Commander = Entity.Null`.
   Assert: returned dict is empty (nothing to save).

3. *Inject attaches intent:*
   Setup: staging repository contains an entity with GUID `"guid-A"` and
   `NetworkIdentity { Value = 77 }` attached.
   Call `Inject` with `{ "commanderGuid": "guid-A", "designation": 3 }`.
   Assert: entity has `InitialUnitSubordinateIntent { CommanderNetworkId = 77, Designation = Wingman }`.

4. *Serializer factory includes the translator:*
   `HrotScenarioSerializerFactory.Build()` returns an instance that reports `UnitSubordinate`
   in its consumed component mask.

---

### TASK-CS014 — GenesisMaterializationSystem: MaterializeUnitSubordinate

**Design Reference:** DESIGN.md § Phase 4.4

**Scope:**
- Add `MaterializeUnitSubordinate` private method to `GenesisMaterializationSystem`.
- Call it from `Execute` alongside the existing materialization methods.

**Constraints:**
- If commander is not yet in `NetworkEntityMap` or not alive → `continue` (do not remove intent).
- On success: perform the atomic two-write (subordinate's `UnitSubordinate` + commander's
  `UnitRoster` append).
- If `UnitRoster.Count >= UnitRoster.Capacity` at materialization time: log a warning, do NOT
  set `UnitSubordinate`, remove the intent so it doesn't retry forever.
- Remove `InitialUnitSubordinateIntent` via `cmd.RemoveManagedComponent` only on success or
  permanent-failure (capacity exceeded).
- **Escape hatch:** If the entity's `EntityLifecycle` has reached `EntityLifecycle.Active`
  (fully validated, timeout elapsed) but the commander is still not resolvable, drop the intent
  via `cmd.RemoveManagedComponent` and emit `FdpLog.Warn`. This prevents an infinite per-frame
  lookup penalty for corrupt or deliberately disconnected scenario references.

**Success Conditions:**

1. *Normal resolution:*
   Setup: commander entity + subordinate entity both alive; subordinate has `InitialUnitSubordinateIntent
   { CommanderNetworkId = cmd.NetworkId, Designation = Wingman }`.
   Tick `GenesisMaterializationSystem`.
   Assert: subordinate has `UnitSubordinate { Commander = cmdEntity, Designation = Wingman }`;
   commander `UnitRoster.Count == 1`; intent is removed.

2. *Retry-until-resolved:*
   Setup: commander not in `NetworkEntityMap` on tick 1; register commander on tick 2.
   Assert: intent persists after tick 1; `UnitSubordinate` is applied after tick 2.

3. *Capacity-exceeded aborts and removes intent:*
   Setup: commander `UnitRoster.Count == 16`; tick materializer.
   Assert: subordinate has no `UnitSubordinate`; intent is removed (no infinite retry).

4. *Lifecycle escape hatch drops unresolvable intent:*
   Setup: entity with `EntityLifecycle.Active`; `CommanderNetworkId` never appears in
   `NetworkEntityMap`.
   Tick `GenesisMaterializationSystem` multiple times.
   Assert: intent is eventually removed; `FdpLog.Warn` was emitted.

---

## Phase 5 — Runtime Hierarchy Management

---

### TASK-CS015 — CmdAssignSubordinate and CmdRemoveSubordinate Events

**Design Reference:** DESIGN.md § Phase 5.1

**Scope:**
- Create `CommandHierarchyEvents.cs` in `Hrot.Core/CommandHierarchy/` with both event structs.
- Assign unique EventIds (add new constants or use the inline `[EventId(N)]` pattern).

**Constraints:**
- Both events must be unmanaged value-type structs.
- Event IDs must not conflict with existing IDs. Check `CmdJoinFormation = 2104`,
  `DestructionOrder = 9003`.  Suggest range 2200–2299 for command hierarchy events.
- `CmdAssignSubordinate` must include `Subordinate`, `Commander`, `Designation`,
  `HasFormationSlot` (byte, 1 = UnitHierarchySystem also writes `FormationFollower`), and
  `SlotIndex` (ushort, valid when `HasFormationSlot == 1`). All four fields together are
  required for the atomic kinematic linkage in Phase 5.2.
- `CmdRemoveSubordinate` must include only `Subordinate`.
- `CmdAssignSubordinateRejected` must include only `Subordinate`. It is published by
  `UnitHierarchySystem` when a `CmdAssignSubordinate` event is rejected because
  `UnitRoster.Count >= UnitRoster.Capacity`, and consumed by `VehicleCommandSystem` to fail the
  waiting `JoinFormationExecutor` BTree node. Without this signal the rejected unit's AI will
  hang indefinitely in `Running` state on the `LocomotionChannel`.

**Success Conditions:**

1. Both event structs are `unmanaged` (verified by `where T : unmanaged` constraint in a unit test helper).
2. `EventType<CmdAssignSubordinate>.Id != EventType<CmdRemoveSubordinate>.Id != EventType<CmdAssignSubordinateRejected>.Id`.
3. IDs are distinct from all existing event IDs in the codebase.
4. `CmdAssignSubordinateRejected` is `unmanaged` and contains only `Entity Subordinate`.

---

### TASK-CS016 — UnitHierarchySystem

**Design Reference:** DESIGN.md § Phase 5.2, 5.3

**Scope:**
- Create `UnitHierarchySystem.cs` in `Hrot.Common/Systems/`.
- Register in **every** node-level logic pack at `SystemPhase.Simulation`:
  `SimHostCoreLogicPack`, `CgfLogicPack`, `IgLogicPack`, and `EditorSubsystem`.
  Placing the file in `Hrot.Common` (which all those packs already depend on) avoids circular
  dependencies and ensures the IG node also maintains the local ECS hierarchy.

**Constraints:**
- All three operations (destruction cascade, removal, assignment) must be handled in a single
  `Execute` call in the specified order: destruction → removal → assignment.
- `RemoveFromHierarchy` must use an order-preserving left-shift implemented as a manual `for`
  loop or via a pointer-derived `Span<T>.CopyTo()`. **Do not use `System.Array.Copy`** — the
  `fixed` buffer is an unmanaged pointer, not a managed heap array.
- `RemoveFromHierarchy` must also call `repo.RemoveComponent<FormationFollower>(subordinate)` if
  the component is present. This ensures total physical and cognitive decoupling. Without this,
  `FormationTargetSystem` will attempt to read `UnitSubordinate.Commander` from an entity that no
  longer has the component, causing null-reference exceptions or erratic vehicle steering.
- The assignment transaction is atomic: capacity check before writing either component; on failure
  neither `UnitSubordinate` nor `UnitRoster` is modified. **On capacity failure** (i.e.,
  `UnitRoster.Count >= UnitRoster.Capacity`), publish
  `CmdAssignSubordinateRejected { Subordinate = event.Subordinate }` to `repo.Bus` before
  returning. `VehicleCommandSystem` (CS007) consumes this event to unblock the waiting
  `JoinFormationExecutor` BTree node. If `CmdAssignSubordinate.HasFormationSlot
  == 1`, `FormationFollower { SlotIndex = event.SlotIndex }` is also written atomically; if the
  capacity check fails, `FormationFollower` is also not written.
- All entity liveness checks (`repo.IsAlive`) must precede component reads.
- `if (view is not EntityRepository repo) return;` guard at the top.
- **Network dirty marking (required for DDS replication):**
  - After every successful `CmdAssignSubordinate`: call
    `SmartEgressUtil.MarkDirty(repo, subordinate, EntityInfoDescriptorOrdinal)`.
  - After every successful `CmdRemoveSubordinate`: call
    `SmartEgressUtil.MarkDirty(repo, subordinate, EntityInfoDescriptorOrdinal)`.
  - In the destruction cascade: after `repo.RemoveComponent<UnitSubordinate>(sub)` for each
    orphaned follower, call `SmartEgressUtil.MarkDirty(repo, sub, EntityInfoDescriptorOrdinal)`.
  Without these marks `EntityInfoEgressTranslator` will never broadcast the updated `CommanderId`
  to remote nodes.

**Success Conditions:**

1. *Assign — atomic two-write:*
   Setup: commander with `UnitRoster.Count=0`; publish `CmdAssignSubordinate`.
   Tick system. Assert: `UnitSubordinate` set on subordinate; `UnitRoster.Count==1`;
   `SubordinateEntities[0]` packs the subordinate.

2. *Assign — roster order preserved:*
   Assign A, B, C. Assert `SubordinateEntities` order is [A, B, C].

3. *Reassign to different commander:*
   Setup: entity assigned to commander X. Publish `CmdAssignSubordinate` for commander Y.
   Tick. Assert: entity has `UnitSubordinate.Commander == Y`; Y's roster contains entity;
   X's roster no longer contains entity.

4. *Remove — order-preserving:*
   Assign A, B, C. Remove B. Assert roster is [A, C] (not [A, C, garbage] — last slot is zeroed).

5. *Assign — capacity exceeded, no partial write:*
   Setup: 16 subordinates in roster. Publish 17th `CmdAssignSubordinate`.
   Tick. Assert: 17th entity has no `UnitSubordinate`; `UnitRoster.Count` still 16.

6. *Commander destroyed — subordinates cleaned up and marked dirty:*
   Setup: commander with UnitRoster containing 3 subordinates. Publish `DestructionOrder` for
   the commander.
   Tick. Assert: all 3 subordinates have `UnitSubordinate` removed;
   `SmartEgressUtil.IsDirty(repo, sub, EntityInfoDescriptorOrdinal)` is true for each.

7. *Subordinate destroyed — removed from commander roster:*
   Setup: subordinate assigned to commander. Publish `DestructionOrder` for the subordinate.
   Tick. Assert: commander `UnitRoster.Count` decremented; freed slot is zeroed.

8. *MarkDirty called on successful assign:*
   Setup: new assignment processed successfully.
   Assert: `SmartEgressUtil.IsDirty(repo, subordinate, EntityInfoDescriptorOrdinal)` is true.

9. *RemoveFromHierarchy removes FormationFollower if present:*
   Setup: subordinate with both `UnitSubordinate` and `FormationFollower`; publish
   `CmdRemoveSubordinate`.
   Tick. Assert: `UnitSubordinate` removed AND `FormationFollower` removed.

10. *FormationFollower written atomically when HasFormationSlot is set:*
    Setup: publish `CmdAssignSubordinate { HasFormationSlot = 1, SlotIndex = 3 }`.
    Tick. Assert: subordinate has `FormationFollower.SlotIndex == 3` in addition to `UnitSubordinate`
    and commander has `UnitRoster` entry — all three written atomically in a single tick.

11. *Capacity exceeded with HasFormationSlot — FormationFollower also not written:*
    Setup: 16 subordinates in roster. Publish `CmdAssignSubordinate { HasFormationSlot = 1,
    SlotIndex = 5 }` for a 17th entity.
    Tick. Assert: 17th entity has no `UnitSubordinate` AND no `FormationFollower`.

12. *Capacity exceeded — CmdAssignSubordinateRejected published:*
    Setup: 16 subordinates in roster. Publish `CmdAssignSubordinate` for a 17th entity.
    Tick. Assert: `CmdAssignSubordinateRejected { Subordinate = 17th }` is on the event bus.

---

## Phase 6 — ORBAT UI Drag-Drop Subordination

---

### TASK-CS017 — OrbatNodeViewModel: CanAcceptSubordinates Flag

**Design Reference:** DESIGN.md § Phase 6.1

**Scope:**
- Add `bool CanAcceptSubordinates` parameter to `OrbatNodeViewModel` record in
  `Hrot.UI.Common/Models/OrbatNodeViewModel.cs`.
- Update all construction sites of `OrbatNodeViewModel` in `EditorOrbatAdapter` and
  `ExConOrbatAdapter` to provide the new field (with `false` as a safe default where unknown).

**Constraints:**
- Must be a positional record parameter (maintain the existing `sealed record` pattern).
- Existing tests that construct `OrbatNodeViewModel` must be updated (add the new argument).

**Success Conditions:**

1. `new OrbatNodeViewModel(1, "A", 0, false, false, true).CanAcceptSubordinates == true`.
2. All existing tests that construct `OrbatNodeViewModel` compile without error.
3. `ExConOrbatAdapter` sets `CanAcceptSubordinates = false` for all nodes as a temporary
   safe default (full implementation in TASK-CS019).

---

### TASK-CS018 — IOrbatController: Subordination Methods

**Design Reference:** DESIGN.md § Phase 6.2

**Scope:**
- Add `RequestAssignSubordinate(int subordinateEntityId, int commanderEntityId)` and
  `RequestRemoveSubordinate(int subordinateEntityId)` to `IOrbatController`.
- Add stub implementations to `EditorOrbatAdapter` (not-implemented log for now) and
  `ExConOrbatAdapter`.

**NOT included:** Full implementations (TASK-CS019 and TASK-CS020).

**Constraints:**
- Parameter type is `int` (network entity ID), matching existing `RequestEmbark/RequestDisembark`.

**Success Conditions:**

1. `IOrbatController` has the two new method declarations.
2. Both adapters compile without errors.

---

### TASK-CS019 — SharedOrbatPanel: Subordination Drag-Drop

**Design Reference:** DESIGN.md § Phase 6.3

**Scope:**
- Update `SharedOrbatPanel.DrawContent` (Hrot.UI.Common) to route drops differently:
  - Drop on node where `CanAcceptSubordinates == true` → call `RequestAssignSubordinate`.
  - Drop on node where `CanAcceptSubordinates == false` → call `RequestEmbark` (unchanged).
  - Drop on empty background → call `RequestRemoveSubordinate`.
- Extract `HandleHierarchyDropPayload` (internal, for testing) that encodes this routing logic.

**Constraints:**
- The existing `HandleDropPayload` method and its tests must not be broken; it may be renamed to
  `HandleEmbarkDropPayload` if needed.
- The background drop target must be a `ImGui.Dummy` filling remaining vertical space.
- Self-drop (same entity ID) is always a no-op.

**Success Conditions:**

1. *Hierarchy drop to commander node:*
   Call `HandleHierarchyDropPayload(subId, cmdId, ctrl)` where target `CanAcceptSubordinates=true`.
   Assert: `ctrl.RequestAssignSubordinate(subId, cmdId)` was called; `RequestEmbark` was NOT called.

2. *Embark drop to non-commander node:*
   Call with `CanAcceptSubordinates=false`.
   Assert: `ctrl.RequestEmbark(passengerId, vehicleId)` was called.

3. *Self-drop no-op:*
   Call with `passengerId == vehicleId`.
   Assert: neither assign nor embark was called.

---

### TASK-CS020 — EditorOrbatAdapter Full Implementation

**Design Reference:** DESIGN.md § Phase 6.4

**Scope:**
- Update `EditorOrbatAdapter.GetVisibleNodes` to build the hierarchy from `UnitSubordinate.Commander`
  (ECS component) instead of `EntityInfo.CommanderId`.
- Set `CanAcceptSubordinates = _world.HasComponent<UnitRoster>(entity)`.
- Implement `RequestAssignSubordinate` and `RequestRemoveSubordinate`.

**Constraints:**
- Entities without `UnitSubordinate` are treated as roots (depth 0).
- `RequestAssignSubordinate`: if either entity is not in `_indexCache`, log a warning and return;
  otherwise publish `CmdAssignSubordinate` on `_bus`.
- `RequestRemoveSubordinate`: publish `CmdRemoveSubordinate` on `_bus`.
- `_indexCache` is rebuilt on every `GetVisibleNodes` call (existing pattern — unchanged).
- Changes must work when `SimTime` is paused (the Editor still ticks `UnitHierarchySystem`).

**Success Conditions:**

1. *Tree built from UnitSubordinate:*
   Setup: entity A (has `UnitRoster`); entity B with `UnitSubordinate.Commander = A`.
   Assert: `GetVisibleNodes` places B at depth 1 under A.

2. *CanAcceptSubordinates reflects UnitRoster:*
   Entity with `UnitRoster` → node `CanAcceptSubordinates = true`.
   Entity without `UnitRoster` → node `CanAcceptSubordinates = false`.

3. *RequestAssignSubordinate publishes event:*
   Call; assert `CmdAssignSubordinate` with correct `Subordinate` and `Commander` entities is
   on the bus.

4. *RequestRemoveSubordinate publishes event:*
   Call; assert `CmdRemoveSubordinate` is on the bus.

---

### TASK-CS021 — ExConOrbatAdapter Full Implementation

**Design Reference:** DESIGN.md § Phase 6.5

**Scope:**
- Update `ExConOrbatAdapter.GetVisibleNodes` to set `CanAcceptSubordinates` based on TkbType
  (composite commander unit types).
- Implement `RequestAssignSubordinate` and `RequestRemoveSubordinate` using the new
  `ICommandGateway.SendUpdateAttributeAsync` method added as part of this task (see Constraints).

**Constraints:**
- ExCon has **no ECS** — it operates on `IDerRepo` (DDS-derived data); never import `Fdp.Core`.
- **Extend `ICommandGateway`:** Add the following method to the `ICommandGateway` interface
  (in the ExCon application layer):
  ```csharp
  Task SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct = default);
  ```
  Implement it in the concrete gateway class (e.g. `ExConCommandGateway`) to write a
  `Hrot.NED.Messages.UpdateEntityAttributeRequest` DDS sample. `ICommandGateway` currently only
  exposes `SendUpdateDescriptorAsync`, which maps to the binary `UpdateEntityDescriptorRequest`
  topic; that binary topic cannot carry a JSON string. Without this extension there is no
  compile-time-safe path for the ExCon to send the `AttributePatchJson` string over DDS.
- `CanAcceptSubordinates`: check if the entity has a `TkbType` that is a known composite commander
  type. A helper method `IsCompositeType(long tkbType)` may check against a defined list.
- `RequestAssignSubordinate`: call `SendUpdateAttributeAsync` with an `UpdateEntityAttributeCommand`
  containing the subordinate entity ID and `AttributePatchJson = "{ \"CommanderId\": commanderId }"`. 
  `commanderEntityId` is the integer network ID. **Do NOT use `SendUpdateDescriptorAsync`** —
  that channel carries a binary `EntityDescriptorUnion Payload` with no JSON field.
- `RequestRemoveSubordinate`: call `SendUpdateAttributeAsync` with
  `AttributePatchJson = "{ \"CommanderId\": 0 }"`.

**Constraints on the SimHost side:**
- The JSON attribute patch path requires a server-side fix: since `CommanderId` was removed from
  `Fdp.Core.EntityInfo` in CS009, the generic JSON-to-ECS reflection compiler in
  `UpdateEntityAttributeRequestSystem` will silently drop the key. **TASK-CS024** fixes this by
  intercepting the `"CommanderId"` key directly in `UpdateEntityAttributeRequestSystem.ProcessRequest`
  (before the reflection compiler) and routing it to `CmdAssignSubordinate` /
  `CmdRemoveSubordinate` events. CS021 and CS024 must be implemented together for the ExCon
  drag-drop path to work end-to-end.

**Success Conditions:**

1. *ICommandGateway extended:*
   Assert: `ICommandGateway` declares `SendUpdateAttributeAsync`; the concrete gateway
   implementation writes a `UpdateEntityAttributeRequest` DDS sample.

2. *RequestAssignSubordinate calls SendUpdateAttributeAsync:*
   Assert: `SendUpdateAttributeAsync` called with the subordinate's entity ID and
   `AttributePatchJson` containing `"CommanderId": commanderEntityId`.
   Assert: `SendUpdateDescriptorAsync` is NOT called.

3. *RequestRemoveSubordinate sends CommanderId=0 via attribute channel:*
   Assert: `SendUpdateAttributeAsync` called with `AttributePatchJson` containing
   `"CommanderId": 0`.

3. *CanAcceptSubordinates set for known composite types:*
   Entity with known composite `TkbType` → `CanAcceptSubordinates = true`.
   Entity with non-composite type → `CanAcceptSubordinates = false`.

---

## Phase 7 — TKB Composite Definition Update

---

### TASK-CS022 — TkbChildSlot: Replace RoleTag with Designation

**Design Reference:** DESIGN.md § Phase 7.1, 7.2

**Scope:**
- In `Hrot.Core/MapDefinitions/Tkb/TkbCompositionDef.cs`: replace `public string RoleTag` with
  `public TacticalDesignation Designation` on `TkbChildSlot`.
- Update all `TkbChildSlot` construction sites in the TKB catalog / builder code.
- Update the composite-spawning system to attach `InitialUnitSubordinateIntent
  { CommanderNetworkId = commanderNetworkId, Designation = slot.Designation }` to each spawned
  child. `GenesisMaterializationSystem` will wire the hierarchy once entities are fully alive.

**Constraints:**
- `string RoleTag` must not remain anywhere in `TkbChildSlot` or its usages.
- The spawning system must only attach `InitialUnitSubordinateIntent` when
  `Designation != TacticalDesignation.Undefined`.
- **Do not** directly attach `UnitSubordinate` or publish `CmdAssignSubordinate` from the
  spawner — entities are not fully alive at spawn time and `UnitHierarchySystem` may reject the
  assignment, leaving a permanently broken state.
- All existing TKB builder tests that set `RoleTag` must be updated to use `Designation`.

**Success Conditions:**

1. `TkbChildSlot` has no `RoleTag` field.
2. `new TkbChildSlot { Designation = TacticalDesignation.SquadLeader }` compiles.
3. Composite spawn test: spawn commander + 1 child slot with `Designation = Wingman`;
   after spawn tick, assert the child entity has `InitialUnitSubordinateIntent
   { Designation = Wingman }` attached;
   after `GenesisMaterializationSystem` tick (with both entities alive and mapped),
   assert the child has `UnitSubordinate.Designation == TacticalDesignation.Wingman` and is
   in the commander's `UnitRoster`.

---

### TASK-CS024 — UpdateEntityAttributeRequestSystem: CommanderId Pre-Intercept

**Design Reference:** DESIGN.md § Phase 3 (cross-cut with CS021)

**Scope:**
- In `UpdateEntityAttributeRequestSystem.ProcessRequest` (Hrot.SimHost), pre-parse
  `req.AttributePatchJson` to detect a `"CommanderId"` property **before** the JSON is
  forwarded to the generic reflection compiler.
- If `"CommanderId"` is present and non-zero: resolve the integer via the system's injected
  `NetworkEntityMap`, verify authority, and publish
  `CmdAssignSubordinate { Subordinate = target, Commander = resolved, Designation = Undefined }`
  to `repo.Bus`.
- If `"CommanderId"` is present and zero, and the target entity has `UnitSubordinate`: publish
  `CmdRemoveSubordinate { Subordinate = target }` to `repo.Bus`.
- Sanitize the JSON (rebuild without `"CommanderId"`) and pass the clean string to
  `_jsonCompiler.Compile()` so the reflection compiler never sees the removed field.
- Track `bool commanderIntercepted` and ensure `UpdateEntityDescriptorAck` is sent even when
  the sanitized JSON has no remaining keys (i.e., when `_jsonCompiler.Compile()` would otherwise
  return `HasAppliedAny = false`).

**Background:** `UpdateEntityAttributeRequestSystem.ProcessRequest` has direct access to the
`Entity` handle, `EntityRepository`, `NetworkEntityMap`, and `repo.Bus` — the exact dependencies
required to resolve the commander network ID and publish the hierarchy event. The generic
JSON-to-ECS reflection compiler (`ValueAttributeSetter<T>`) does NOT have these: it operates
exclusively through `IEntityPatchContext`, which provides only an opaque `ref T component` and
a JSON reader with no knowledge of other entities. Attempting to intercept inside
`EntityDataAttributeInstaller` is therefore physically impossible. Without this task the key
is silently dropped because `CommanderId` was removed from `Fdp.Core.EntityInfo` in CS009.

**Constraints:**
- Must NOT pass `"CommanderId"` to the generic reflection compiler under any circumstances.
  Use `System.Text.Json.JsonDocument.Parse` to extract the value; rebuild the JSON without
  the property before forwarding to `_jsonCompiler.Compile()`.
- **Authority guard (required before publishing any event):** Call
  `view.HasAuthority(entity, packedKey)` (the same guard used by all egress translators).
  If `false`, drop the request silently. This prevents any remote node from spoofing hierarchy
  changes for entities it does not own.
- **ACK contract:** The ACK (`UpdateEntityDescriptorAck`) must be sent whenever
  `commanderIntercepted == true`, regardless of whether `_jsonCompiler.Compile()` reports any
  mutation. Without the ACK, ExCon drag-and-drop silently times out after 5 seconds.
- If the commander network ID cannot be resolved immediately (entity not yet in `_entityMap`),
  reuse the deferred queue path already implemented in `EntityInfoIngressTranslator` (CS011).
- The interception must be transparent to other patch keys: `{ "Name": "Alpha" }` must still
  be compiled by reflection as before.
- ExCon sends integer network IDs (not GUIDs); no GUID resolution required.

**Success Conditions:**

1. *Assign patch routes to event:*
   Setup: target entity; commander with net ID 42 in `_entityMap`; authority check passes.
   Call `ProcessRequest` with `AttributePatchJson = "{ \"CommanderId\": 42 }"`.
   Assert: `CmdAssignSubordinate { Subordinate = target, Commander = resolvedCmd }` published;
   `"CommanderId"` key does NOT reach the reflection compiler.

2. *Remove patch routes to event:*
   Setup: target entity with existing `UnitSubordinate`; authority check passes.
   Call `ProcessRequest` with `AttributePatchJson = "{ \"CommanderId\": 0 }"`.
   Assert: `CmdRemoveSubordinate { Subordinate = target }` published.

3. *Remove patch on entity without UnitSubordinate — no event:*
   Setup: target entity without `UnitSubordinate`.
   Call `ProcessRequest` with `AttributePatchJson = "{ \"CommanderId\": 0 }"`.
   Assert: no event published.

4. *Other keys unaffected:*
   Call with `AttributePatchJson = "{ \"Name\": \"Bravo\", \"CommanderId\": 0 }"`.
   Assert: `"Name"` is still written to the ECS component; `"CommanderId"` is intercepted and
   not forwarded to the reflection compiler.

5. *ACK sent when only CommanderId was in the patch:*
   Setup: `AttributePatchJson = "{ \"CommanderId\": 42 }"` (no other keys).
   Assert: `UpdateEntityDescriptorAck` is published (ExCon does not time out), even though
   `_jsonCompiler.Compile()` processes an empty sanitized JSON and would otherwise not set
   `HasAppliedAny = true`.

6. *Authority check blocks unauthorized write:*
   Setup: target entity for which `view.HasAuthority(entity, packedKey)` returns `false`.
   Apply `AttributePatchJson = "{ \"CommanderId\": 42 }"`.
   Assert: no `CmdAssignSubordinate` or `CmdRemoveSubordinate` published; no exception thrown.

---

### TASK-CS025 — Integration Tests: Distributed Boundary Validation

**Design Reference:** DESIGN.md § Phase 9

**Scope:**
- Add 6 integration tests covering the multi-node and serialization boundary cases defined in
  DESIGN.md Phase 9. Tests live in `Hrot.ClusterRunner.Integration.Tests` (harness-based tests)
  and `Hrot.SimHost.Integration.Tests` (EditorHarness-based tests).

**Constraints:**
- `HrotRunnerHarness` tests must not depend on wall-clock time; use deterministic tick counts.
- `EditorHarness` tests must tear down and reload the harness between sub-scenarios to avoid
  state bleed.
- All tests must pass with `--no-build` after CS016 and CS024 are implemented.

**Success Conditions:**

1. *Out-of-order ingress (`HrotRunnerHarness`, `"simhost,cgf"`)* (see DESIGN.md § 9.1):
   Subordinate `EntityInfo` arrives before commander creation; after commander `EntityRegistered`,
   `UnitSubordinate` and `UnitRoster` are linked within the same drain cycle.

2. *Atomic capacity validation (`EditorHarness`)* (see DESIGN.md § 9.2):
   17 `CmdAssignSubordinate` events; `UnitRoster.Count == 16`; 17th entity has no `UnitSubordinate`
   and no `FormationFollower`.

3. *Destruction cascade dirty egress (`HrotRunnerHarness`, `"simhost"`)* (see DESIGN.md § 9.3):
   Commander destroyed; all children lose `UnitSubordinate`;
   `SmartEgressUtil.ShouldPublish` returns `true` for each child.

4. *ExCon drag-and-drop ACK (`HrotRunnerHarness`, `"simhost,ig,excon"`)* (see DESIGN.md § 9.4):
   JSON `{ "CommanderId": N }` patch; ExCon mock receives `UpdateEntityDescriptorAck` without
   timeout; both rosters updated with order-preserving shift.

5. *Kinematic/tactical decoupling (`HrotRunnerHarness`)* (see DESIGN.md § 9.5):
   Spawn `InfantrySquad`; tactical intent breaks formation; followers drop `KinematicsMode.Formation`
   while retaining `UnitSubordinate` in commander `UnitRoster`.

6. *Genesis scenario serialization (`EditorHarness`)* (see DESIGN.md § 9.6):
   Save then reload; `GenesisMaterializationSystem` reconstructs hierarchy regardless of chunk
   order; `UnitRoster` is rebuilt dynamically.

---

### TASK-CS026 — Cluster Load Handlers: InitialUnitSubordinateIntent Drain Check

**Design Reference:** DESIGN.md § Phase 4.4 (cross-cut with CS012, CS014)

**Scope:**
- Update `DrainDeferredAcks` in `HrotScenarioLoadHandler` (`Hrot.ScenarioEditor.Handlers`) and
  in `CgfScenarioLoadHandler` (`Hrot.CGF.Orchestration.Handlers`) to hold the cluster in the
  `LoadingLive` state until all `InitialUnitSubordinateIntent` components have been resolved.
- Add the following guard in each `DrainDeferredAcks` method alongside the existing intent
  checks:
  ```csharp
  foreach (var _ in _world.Query().WithManaged<InitialUnitSubordinateIntent>().Build()) return;
  ```

**Background:** `DrainDeferredAcks` polls until all unresolved transient intents are gone before
releasing the `LoadingLive` lock. Without this check the cluster proceeds to physical simulation
while `GenesisMaterializationSystem` is still linking command hierarchies, causing AI BTree
nodes to execute with missing `UnitSubordinate.Commander` references.

**Constraints:**
- Follow the exact pattern used for other managed intent components already guarded in
  `DrainDeferredAcks` (e.g., `InitialPassengersIntent`). Do not invent a new polling mechanism.
- Both handlers must be updated; omitting one leaves CGF or the Editor cluster susceptible to
  the same race.
- This task must be implemented after `TASK-CS012` (component exists).

**Success Conditions:**

1. *Cluster waits while intent is pending:*
   Setup: spawn a subordinate entity; attach `InitialUnitSubordinateIntent`; call `DrainDeferredAcks`.
   Assert: method returns without releasing the lock (returns early).

2. *Cluster proceeds after all intents resolved:*
   Setup: remove all `InitialUnitSubordinateIntent` components (as `GenesisMaterializationSystem`
   would); call `DrainDeferredAcks`.
   Assert: method does NOT return early from the intent check; execution continues to the
   next lock-release step.

3. *Both handlers covered:*
   Test exists for `HrotScenarioLoadHandler` and separately for `CgfScenarioLoadHandler`.

---

### TASK-CS027 — StagingEntityExtractor: Remap CommanderNetworkId on Load

**Design Reference:** DESIGN.md § Phase 4.2 (cross-cut with CS013)

**Scope:**
- Update `StagingEntityExtractor.RemapComponentNetworkIds` (Pass 2 of the extract pipeline in
  `Hrot.SimHost`) to intercept `InitialUnitSubordinateIntent` and remap `CommanderNetworkId`
  from the offline staging ID to the live cluster ID using the `oldToNewMap` dictionary.

**Background:** `RemapComponentNetworkIds` walks the component list of each extracted entity and
replaces offline network IDs with fresh live IDs. Without this patch, `InitialUnitSubordinateIntent`
carries the dead staging `CommanderNetworkId` into the live cluster. `GenesisMaterializationSystem`
never matches it, the escape hatch drops the intent, and every platoon loaded from scenario loses
its hierarchy permanently.

**Implementation:**
```csharp
else if (comps[ci] is InitialUnitSubordinateIntent subIntent)
{
    comps[ci] = new InitialUnitSubordinateIntent
    {
        CommanderNetworkId = oldToNewMap.TryGetValue(subIntent.CommanderNetworkId, out long newId)
            ? newId
            : subIntent.CommanderNetworkId,
        Designation = subIntent.Designation
    };
}
```

**Constraints:**
- Follow the existing `else if` chain pattern for other intent types (e.g., `InitialPassengersIntent`).
- `InitialUnitSubordinateIntent` is a managed class (`sealed class`); use `is` pattern-matching,
  not struct-equality.
- If `CommanderNetworkId` is 0 (no commander), the mapping lookup will not find a match; the
  intent is preserved unchanged (0 is the sentinel for "skip materialization").
- This remap applies to both root-entity and child-entity component lists (the extractor calls
  `RemapComponentNetworkIds` for both).

**Success Conditions:**

1. *CommanderNetworkId remapped:*
   Setup: `oldToNewMap = { 100 -> 200 }`;
   `InitialUnitSubordinateIntent { CommanderNetworkId = 100, Designation = Wingman }`.
   Call `RemapComponentNetworkIds` with the map.
   Assert: intent in the output list has `CommanderNetworkId == 200` and `Designation == Wingman`.

2. *Unknown ID preserved unchanged:*
   Setup: `CommanderNetworkId = 999` not in `oldToNewMap`.
   Assert: output intent has `CommanderNetworkId == 999`.

3. *Zero CommanderNetworkId unchanged:*
   Setup: `CommanderNetworkId = 0`.
   Assert: output intent has `CommanderNetworkId == 0`.

4. *Other components in the list unaffected:*
   Mix `InitialUnitSubordinateIntent` with an `InitialPassengersIntent` in the same list.
   Assert: both are remapped correctly; neither overwrites the other.

---

## Cross-Cutting — Tests

---

### TASK-CS023 — Component Registry Integration Test Update

**Scope:**
- Update `ComponentRegistryTests` in `Hrot.SimHost.Tests` to cover `UnitRoster` and
  `UnitSubordinate`.
- Verify all new components are registered without ID collision.

**Success Conditions:**

1. `world.GetComponentTable<UnitRoster>()` returns non-null.
2. `world.GetComponentTable<UnitSubordinate>()` returns non-null.
3. All registered component IDs are unique (existing global-uniqueness assertion extended).

---

## Dependency and Project Map

| Task | Project(s) Changed |
|------|--------------------|
| CS001 | Hrot.Core, Hrot.Network.NED |
| CS002 | Hrot.Core, Hrot.SimHost |
| CS003 | Hrot.Core, Hrot.SimHost |
| CS004 | Hrot.Core, Hrot.SimHost |
| CS005 | Fdp.Core (GlobalComponentIds), Fdp.Toolkits |
| CS006 | Fdp.Core (GlobalComponentIds), Fdp.Toolkits |
| CS007 | Fdp.Toolkits |
| CS008 | Hrot.Network.NED |
| CS009 | Fdp.Core, Hrot.Editor, Hrot.SimHost, Hrot.CGF |
| CS010 | Hrot.Network.NED |
| CS011 | Hrot.Network.NED |
| CS012 | Hrot.Core (HrotComponentIds), Hrot.Common, Hrot.SimHost |
| CS013 | Hrot.SimHost |
| CS014 | Hrot.SimHost |
| CS015 | Hrot.Core |
| CS016 | Hrot.Common (file), Hrot.SimHost + Hrot.CGF + Hrot.IG + Hrot.Editor (registration) |
| CS017 | Hrot.UI.Common, Hrot.Editor, Hrot.ExCon |
| CS018 | Hrot.UI.Common, Hrot.Editor, Hrot.ExCon |
| CS019 | Hrot.UI.Common |
| CS020 | Hrot.Editor |
| CS021 | Hrot.ExCon |
| CS022 | Hrot.Core, (composite-spawning system location) |
| CS023 | Hrot.SimHost.Tests |
| CS024 | Hrot.SimHost (EntityDataAttributeInstaller) |
| CS025 | Hrot.ClusterRunner.Integration.Tests, Hrot.SimHost.Integration.Tests |
| CS026 | Hrot.SimHost (HrotScenarioLoadHandler), Hrot.CGF (CgfScenarioLoadHandler) |
| CS027 | Hrot.SimHost (StagingEntityExtractor) |

### Project Dependency Notes

- **Hrot.Core → Fdp.Core + Fdp.Toolkits** (already established): Safe to add `UnitRoster`,
  `UnitSubordinate`, `TacticalDesignation`, `CmdAssignSubordinate` here.
- **Hrot.Common → Hrot.Core** (already established): Safe to add `InitialUnitSubordinateIntent`
  here, and to place `UnitHierarchySystem` here for shared node access.
- **Hrot.SimHost → Hrot.Common** (already established): Safe to add `UnitSubordinateTranslator`,
  `GenesisMaterializationSystem` changes, and register `UnitHierarchySystem` here.
- **Hrot.CGF / Hrot.IG / Hrot.Editor → Hrot.Common** (must be verified): All node packs need
  `UnitHierarchySystem` registered. Confirm each project's dependency graph before adding the
  registration call.
- **Hrot.Network.NED → Hrot.Core** (already established): Safe to reference `UnitSubordinate`
  from the translators.
- **Hrot.UI.Common → Hrot.Core** (already established): Safe to reference `UnitRoster` for
  `CanAcceptSubordinates` check.
- **Fdp.Core** is a leaf dependency with no circular risks. Renaming `FormationRoster/Member`
  constants there is safe.
- **WARNING — Fdp.Core.EntityInfo field removal (CS009):** Removing `CommanderId` is a breaking
  change that cascades across `Hrot.CGF`, `Hrot.Editor`, `Hrot.ExCon`, `Hrot.SimHost`, and
  `Hrot.Network.NED`. All must be fixed in the same compilation unit to avoid a broken-build
  intermediate state. Recommended: do CS009 as a single commit that touches all affected files.
- **WARNING — CS021 + CS024 must be done together:** The ExCon drag-drop path only works
  end-to-end when both the ExConOrbatAdapter patch (CS021) and the
  EntityDataAttributeInstaller interception (CS024) are implemented.
- **CS025 depends on CS016, CS021, CS024:** Integration tests must not be run before those
  tasks are complete (authority guard, ACK flag, and atomic `FormationFollower` writes must all
  be in place).
- **CS026 depends on CS012:** `InitialUnitSubordinateIntent` must exist before the drain guard
  can compile.
- **CS027 depends on CS012:** `InitialUnitSubordinateIntent` must be a `sealed class` before
  the `is`-pattern remap can compile. CS027 and CS013 (UnitSubordinateTranslator) should be
  implemented together — both deal with the intent's lifetime across the staging boundary.

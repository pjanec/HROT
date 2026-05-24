# Tactical Intent Distribution System - Task Detail

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture overview and rationale.

---

## Phase 1: Core Contracts

### TASK-TI001 - Add AssignTacticalIntentEvent

**Design Reference:** DESIGN.md §1.1

**Scope:**
- Add class `AssignTacticalIntentEvent` to
  `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs`
- Register the new managed event type with the `FdpEventBus` where other managed events
  are registered (same file or initializer that registers `AssignBehaviorEvent`).

**Not in scope:** Any system that reads or publishes this event.

**Constraints:**
- Must be a `sealed class` (not a struct) because it carries managed string fields,
  exactly like `AssignBehaviorEvent`.
- File location must match the existing `AssignBehaviorEvent.cs` neighbor pattern.
- Fields: `Entity Entity`, `string IntentId = string.Empty`, `string JsonParams = string.Empty`.
- No `IsRemote` flag. Authority-based gates in `TacticalIntentResolutionSystem` and
  `TacticalIntentEgressTranslator` (both keyed on `HasAuthority<BehaviorState>`) are
  sufficient to prevent echo loops in a distributed topology. Adding a flag would be
  redundant and would re-introduce sender-side network knowledge.
- No dependency on Hrot-specific types.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | FdpEventBus initialized | Publish `new AssignTacticalIntentEvent { IntentId="X", JsonParams="{}" }` then `SwapBuffers()` | `bus.ReadManaged<AssignTacticalIntentEvent>()` returns one event with `IntentId == "X"` |
| SC-2 | - | Create default instance | `IntentId` and `JsonParams` are both empty strings (not null) |

---

### TASK-TI002 - Add ITacticalOrderMapper Interface and TacticalIntentMapperRegistry

**Design Reference:** DESIGN.md §1.2, §1.3

**Scope:**
- Add `ITacticalOrderMapper` interface in
  `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs`
- Add `TacticalIntentMapperRegistry` class in
  `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/TacticalIntentMapperRegistry.cs`

**Not in scope:** Any concrete mapper implementation; that is Phase 6.

**Constraints:**
- `ITacticalOrderMapper.TryMap` signature:
  ```csharp
  bool TryMap(Entity self, EntityRepository repo, string jsonParams,
              out AssignBehaviorEvent assignment);
  ```
- `TacticalIntentMapperRegistry`:
  - `void Register(ITacticalOrderMapper mapper)` — throws `InvalidOperationException` if
    the same `TargetIntentId` is registered twice.
  - `bool TryGetMapper(string intentId, out ITacticalOrderMapper mapper)`.
- No dependency on Hrot-specific types.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Two mappers with distinct `TargetIntentId` | `Register` both | `TryGetMapper` returns the correct mapper for each ID |
| SC-2 | One mapper registered | `Register` same mapper again | `InvalidOperationException` thrown |
| SC-3 | Empty registry | `TryGetMapper("Unknown")` | returns `false`, `out` param is `null` |

---

## Phase 2: Receiver-Side Resolution

### TASK-TI003 - Implement TacticalIntentResolutionSystem

**Design Reference:** DESIGN.md §2.1, §2.2

**Scope:**
- Add `TacticalIntentResolutionSystem` in
  `Hrot/Subsystems/Hrot.CGF/Systems/TacticalIntentResolutionSystem.cs`
- Register it in `CgfLogicPack.SimulationSystems` immediately after `MissionAdapterSystem`.
- Constructor: `TacticalIntentResolutionSystem(TacticalIntentMapperRegistry mapperRegistry)`
- Add `TacticalIntentMapperRegistry` as a constructor parameter of `CgfLogicPack`.

**Not in scope:** Defining the concrete mappers; registering them on the registry (done
by composition roots, see Phase 6).

**Constraints:**
- Must implement `IEcsModuleSystem` and be decorated `[UpdateInPhase(SystemPhase.Simulation)]`.
- Read all `AssignTacticalIntentEvent` from `repo.Bus.ReadManaged<AssignTacticalIntentEvent>()`.
- **Authority gate (CQRS boundary):** For each event, evaluate
  `repo.HasAuthority<BehaviorState>(evt.Entity)`. If `false`, the cognitive state is
  owned by a remote node — skip silently. Do NOT attempt resolution or publish anything.
- Fallback path: when no mapper is found (or `TryMap` returns false), publish
  `new AssignBehaviorEvent { Entity = evt.Entity, BehaviorName = evt.IntentId, JsonParams = evt.JsonParams }`.
  The `new` allocation is required because `AssignBehaviorEvent` is a managed class.
- Mapper path: publish the `AssignBehaviorEvent` instance returned by `TryMap`.
- Must not mutate `BehaviorState`, `BrainBTreeState`, or `BrainBlackboard` directly.
- If `evt.Entity` does not exist in `repo` (entity was deleted), skip silently.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Registry has mapper for "DefendArea"; entity with relevant capability component; local node has authority over `BehaviorState` | Publish `AssignTacticalIntentEvent { IntentId="DefendArea", ... }` + tick | `AssignBehaviorEvent` published with the mapper-translated behavior name |
| SC-2 | Empty registry; local node has authority over `BehaviorState` | Publish `AssignTacticalIntentEvent { IntentId="ConvoyEscort", ... }` + tick | `AssignBehaviorEvent` published with `BehaviorName == "ConvoyEscort"` (pass-through) |
| SC-3 | Any registry state | Publish event for entity that does not exist | No exception; no `AssignBehaviorEvent` published |
| SC-4 | Registry mapper returns `false` from `TryMap`; local authority | Publish matching intent event | Fallback: `new AssignBehaviorEvent` published with `BehaviorName == evt.IntentId` |
| SC-5 | Local node does NOT have authority over `BehaviorState` for the target entity | Publish `AssignTacticalIntentEvent` + tick | No `AssignBehaviorEvent` published; no exception |

---

## Phase 3: MissionAdapterSystem Modification

### TASK-TI004 - Change MissionAdapterSystem to Emit AssignTacticalIntentEvent

**Design Reference:** DESIGN.md §3

**Scope:**
- Modify `Hrot/Subsystems/Hrot.CGF/Systems/MissionAdapterSystem.cs`.
- Remove `_behaviorRegistry` field and constructor parameter.
- Replace `AssignBehaviorEvent` publication with `AssignTacticalIntentEvent` publication.
- Update `CgfLogicPack` construction site to no longer pass `BehaviorRegistry` to
  `MissionAdapterSystem`.
- Update any tests that construct `MissionAdapterSystem` directly.

**Not in scope:** Changes to `DomainMissionTask`, `MissionTask` DDS struct,
`MissionPlanQueue`, or `MissionControlExecutionSystem`.

**Constraints:**
- Use `task.BehaviorId` (from `DomainMissionTask`) as the `IntentId` in the event.
  If `task` is null or `BehaviorId` is empty/whitespace, skip publishing (same guard as
  existing null-check on `jsonParams`).
- `MissionAdapterState` change-detection logic (comparing `LastPhase` and
  `LastPlanVersion`) must remain unchanged.
- The existing `_entityMap` field and constructor parameter remain — it is still used
  for network ID resolution in other parts of the method.
  - Note: If `_entityMap` is not actually used after removal of `_behaviorRegistry`,
    verify in code before removing. Only remove dependencies that are verifiably unused.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Entity with `MissionPlanQueue` at phase 0, `DomainMissionTask.BehaviorId = "WanderMilitary"` | Run `MissionAdapterSystem` | `AssignTacticalIntentEvent { IntentId="WanderMilitary" }` published; no `AssignBehaviorEvent` published |
| SC-2 | Same mission re-committed from phase 0 (re-commit detection case) | Re-run system | Event published again (change detector fires) |
| SC-3 | `DomainMissionTask.BehaviorId` is empty | Run system | No event published |
| SC-4 | `MissionAdapterSystem` construction site | - | Builds without passing `BehaviorRegistry` |

---

## Phase 4: UI Discovery for Intent DTOs

### TASK-TI005 - Add Commander Flag to BehaviorCategory

**Design Reference:** DESIGN.md §4.1

**Scope:**
- Modify `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorCategory.cs`.
- Add `Commander = 1 << 4` to the `BehaviorCategory` flags enum.

**Not in scope:** Defining `TkbEntityTypes.Commander` or wiring the Commander TKB type
into `BehaviorCatalog` (that requires a TKB template and is out of scope for this
workstream).

**Constraints:**
- Must remain a `[Flags]` enum.
- `AllMilitary` value must not change.
- No existing BehaviorContractAttribute usages broken.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Compile | Read `BehaviorCategory.Commander` | Compiles; value is `1 << 4 = 16` |
| SC-2 | `BehaviorCategory.AllMilitary.HasFlag(Commander)` | Evaluate | `false` — Commander is NOT part of AllMilitary |

---

### TASK-TI006 - Add Example Intent DTOs to Hrot.Core

**Design Reference:** DESIGN.md §4.2

**Scope:**
- Create `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/Intents/` folder.
- Add at least one intent DTO (`DefendAreaIntentDto.cs`) demonstrating the pattern.
- Add a unique integer ID for each intent DTO in `BehaviorIds.cs` (reserved intent range,
  e.g. 1000–1099).

**Not in scope:** Mapper implementations for these intents (Phase 6).

**Constraints:**
- The DTO class must be decorated with `[BehaviorContract(id, "IntentName", BehaviorCategory.AllMilitary)]`.
- The `behaviorId` integer must not collide with any existing `BehaviorIds` constant.
- The `BehaviorId` string must match what the corresponding mapper's `TargetIntentId`
  will return (documented in comments).
- The class must be a plain POCO with only JSON-serializable fields (no `Entity` handles,
  no ECS types).
- Fields should represent universal data (Lat/Lon, network IDs, floats) not local ECS
  entity IDs.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | `BehaviorSchemaDiscovery.AutoRegister(uiRegistry, remapper)` called | Check `uiRegistry.TryGet("DefendArea", ...)` | Returns `true` — the intent DTO is auto-discovered |
| SC-2 | `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.MilitaryApc)` | Inspect result | Contains `"DefendArea"` because DTO is `AllMilitary` which includes `MilitaryApc` |
| SC-3 | `BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianCar)` | Inspect result | Does NOT contain `"DefendArea"` (civilian not in AllMilitary) |

---

## Phase 5: Network Transport

### TASK-TI007 - Define TacticalIntentRequest DDS Message and EDescriptorType

**Design Reference:** DESIGN.md §5.1

**Scope:**
- Add `TacticalIntentRequest` struct to
  `Hrot/Network/Hrot.Network.NED/TacticalIntentMessages.cs` (new file).
- Add `dtTacticalIntentRequest = 92` to `EDescriptorType` in
  `Hrot/Network/Hrot.Network.NED/AllDescriptors.cs`.

**Not in scope:** Any translator; that is TASK-TI008 and TASK-TI009.

**Constraints:**
- `TacticalIntentRequest` must be annotated with `[DdsStruct]`, `[DdsIdlFile("hrot-tactical-intent")]`,
  and `[DdsManaged]`.
- Fields: `long TargetEntityId`, `string IntentId`, `string JsonParams`.
- `dtTacticalIntentRequest = 92` — verify 91 (`dtMissionControlAck`) is the current
  highest value and 92 is free.
- No existing `EDescriptorType` values may be renumbered.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Compile | Access `EDescriptorType.dtTacticalIntentRequest` | Value equals `92` |
| SC-2 | Create `TacticalIntentRequest` | Set fields and serialize | Struct compiles; all three string/long fields accessible |

---

### TASK-TI008 - Implement TacticalIntentEgressTranslator

**Design Reference:** DESIGN.md §5.2

**Scope:**
- Add `TacticalIntentEgressTranslator` to
  `Hrot/Network/Hrot.Network.NED/SimHost/TacticalIntentEgressTranslator.cs`.
- Register it in `SimHostAuxiliaryTranslatorPack` egress list.

**Constraints:**
- Implements `IDescriptorTranslator` with `Direction = TranslatorDirection.Egress` and
  `DescriptorOrdinal = (long)EDescriptorType.dtTacticalIntentRequest`.
- `PollEgress` reads all `AssignTacticalIntentEvent` from `repo.Bus.ReadManaged<AssignTacticalIntentEvent>()`.
- **Authority gate (CQRS boundary):** For each event, evaluate
  `!repo.HasAuthority<BehaviorState>(evt.Entity)`. Only write to DDS if `true` (i.e. the
  local node does NOT own the cognitive state of the target entity). If the local node
  owns the cognitive state, `TacticalIntentResolutionSystem` will handle it locally and
  no DDS write is needed.
- Must look up the local ECS `entity` to get its `NetworkEntityId` for `TargetEntityId`
  in the DDS struct (use `NetworkEntityMap`).
- If entity not found in map, skip the sample (log warning).
- Follow the exact same pattern as `MissionControlAckEgressTranslator`.
- Constructor accepts `(DdsParticipant participant, NetworkEntityMap entityMap)`.
- Internal test constructor accepts `(DdsWriter<TacticalIntentRequest> writer, NetworkEntityMap entityMap)`.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Stub DDS writer; entity registered in map; local node does NOT have authority over `BehaviorState` | Publish `AssignTacticalIntentEvent`; call `PollEgress` | One `TacticalIntentRequest` written with matching `TargetEntityId`, `IntentId`, `JsonParams` |
| SC-2 | Entity NOT in `NetworkEntityMap` | Publish event; call `PollEgress` | No DDS write; `SentSampleCount` unchanged |
| SC-3 | Two events published; authority check passes for both | Call `PollEgress` | Two DDS writes; `SentSampleCount == 2` |
| SC-4 | Local node HAS authority over `BehaviorState` for the target entity | Publish event; call `PollEgress` | No DDS write (resolution handled locally by `TacticalIntentResolutionSystem`) |

---

### TASK-TI009 - Implement TacticalIntentIngressTranslator

**Design Reference:** DESIGN.md §5.2

**Scope:**
- Add `TacticalIntentIngressTranslator` to
  `Hrot/Network/Hrot.Network.NED/SimHost/TacticalIntentIngressTranslator.cs`.
- Register it in `SimHostAuxiliaryTranslatorPack` ingress list.

**Constraints:**
- Implements `IDescriptorTranslator` with `Direction = TranslatorDirection.Ingress` and
  `DescriptorOrdinal = (long)EDescriptorType.dtTacticalIntentRequest`.
- `PollIngress` reads `TacticalIntentRequest` from DDS, resolves `TargetEntityId` to
  local ECS `Entity` via `NetworkEntityMap`.
- If entity not found in map, skip (log warning).
- Publishes `AssignTacticalIntentEvent` on `repo.Bus.PublishManaged(...)`.
- Constructor accepts `(DdsParticipant participant, NetworkEntityMap entityMap)`.
- Internal test constructor accepts `(DdsReader<TacticalIntentRequest> reader, NetworkEntityMap entityMap)`.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Stub DDS reader returning one sample; entity in map | Call `PollIngress` | One `AssignTacticalIntentEvent` published with matching fields |
| SC-2 | `TargetEntityId` NOT in `NetworkEntityMap` | Call `PollIngress` | No event published; `ReceivedSampleCount` incremented (sample read but dropped) |
| SC-3 | DDS sample with `IsValid = false` | Call `PollIngress` | No event published |

---

## Phase 6: Commander BTree Integration and Example Mapper

### TASK-TI010 - Reference Commander BTree Action

**Design Reference:** DESIGN.md §Phase 6

**Scope:**
- Add a reference BTree action node in `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/`
  (e.g., as a static method on an existing or new `CommanderNodes.cs`).
- The action reads `IntentId` and `JsonParams` from its blackboard params DTO and
  publishes `AssignTacticalIntentEvent` for each subordinate entity.

**Not in scope:** A full Commander behavior tree; subordinate enumeration logic.

**Constraints:**
- Must use `repo.Bus.PublishManaged(new AssignTacticalIntentEvent { ... })`.
- Subordinate entity enumeration is stubbed (accepts list via blackboard params or
  leaves a TODO comment).
- Must compile without errors.

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | World with two entities; action called | Execute BTree node | One `AssignTacticalIntentEvent` published per entity; `NodeStatus.Success` returned |

---

### TASK-TI011 - Implement DefendAreaMapper (First Concrete Mapper)

**Design Reference:** DESIGN.md §1.2, §4.2; codebase: `Hrot.AI.Behaviors`, `TkbEntityTypes`

**Scope:**
- Add `DefendAreaMapper` in `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/DefendAreaMapper.cs`.
- Register it on the `TacticalIntentMapperRegistry` instance in at least one composition
  root (e.g. `CgfBehaviorSetup` or the test harness).

**Constraints:**
- `TargetIntentId` must equal `"DefendArea"` (matching `DefendAreaIntentDto.BehaviorId`).
- `TryMap` must first check `repo.HasComponent<TkbIdentity>(entity)`. If the entity has
  no `TkbIdentity` component, return `false` immediately (no exception).
- `TryMap` then queries `TkbIdentity.TkbType` and branches on:
  - `TkbEntityTypes.MilitaryApc` → `BehaviorName = "ConvoyEscort"` (or appropriate APC defend behavior)
  - `TkbEntityTypes.InfantrySoldier` → `BehaviorName = "InfantryCombat"` (or appropriate infantry defend behavior)
  - Unknown type → return `false` (fall back to pass-through)
- JSON params from `jsonParams` are forwarded as-is to `AssignBehaviorEvent.JsonParams`.
- Must not perform any network resolution or DDS calls.
- Must be a stateless class (no instance fields other than injected read-only services).

**Success Conditions:**

| # | Setup | Action | Assertion |
|---|---|---|---|
| SC-1 | Entity with `TkbIdentity { TkbType = TkbEntityTypes.MilitaryApc }` | `TryMap(entity, repo, jsonParams, out evt)` | Returns `true`; `evt.BehaviorName == "ConvoyEscort"`; `evt.JsonParams == jsonParams` |
| SC-2 | Entity with `TkbIdentity { TkbType = TkbEntityTypes.InfantrySoldier }` | `TryMap(...)` | Returns `true`; `evt.BehaviorName == "InfantryCombat"` |
| SC-3 | Entity with unknown TkbType | `TryMap(...)` | Returns `false` |
| SC-4 | Entity has no `TkbIdentity` component | `TryMap(...)` | Returns `false` (no exception) |

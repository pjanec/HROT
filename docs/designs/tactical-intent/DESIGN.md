# Tactical Intent Distribution System - Design

## Background and Motivation

A Platoon Commander AI that directly assigns concrete behavior IDs to its subordinates
creates severe anti-patterns:

- **Tight coupling:** The Commander must know behavior IDs and JSON schemas for every
  possible unit type (APC, Infantry, Drone, etc.).
- **Fragility:** Adding a new unit type forces changes to the Commander's behavior tree.
- **Network inflexibility:** Hard-coded ECS entity handles fail across Brain-node boundaries.

The existing Mission Control pipeline already supports generic `BehaviorId` strings
(`DomainMissionTask.BehaviorId`) for human-authored mission plans, but has no mechanism
for a Commander AI to issue the same kind of generic behavioral order, nor any automatic
translation layer that maps a generic intent to the specific behavior matching a
subordinate's capabilities.

This workstream introduces the **Tactical Intent Distribution System**, which:

1. Unifies AI-issued tactical orders and human-authored mission tasks into one polymorphic
   event pipeline.
2. Keeps senders (Commander AI, `MissionAdapterSystem`) fully agnostic of the recipient's
   unit type and capabilities.
3. Leaves `BehaviorIngressSystem` and `AssignBehaviorEvent` completely unchanged.
4. Reuses the existing `[BehaviorContract]` attribute and `BehaviorUiRegistry` for UI
   discovery of generic intent DTOs with no new rendering code.

---

## Architecture Overview

```
Commander BTree action
MissionAdapterSystem          --[AssignTacticalIntentEvent]-->
                                      |
                               FdpEventBus.SwapBuffers()
                                      |
                               TacticalIntentResolutionSystem
                                |- look up ITacticalOrderMapper
                                |- if found: translate → AssignBehaviorEvent
                                |- if not found: pass-through as behavior name
                                      |
                               FdpEventBus.SwapBuffers()
                                      |
                               BehaviorIngressSystem
                                |- write BrainBTreeState / BrainHsm128
                                |- parse JSON into BrainBlackboard.Memory
```

Two-frame end-to-end latency is accepted and is physically realistic for tactical
command-and-control.

---

## Phase 1: Core Contracts

### 1.1 AssignTacticalIntentEvent

A new managed event placed in `Fdp.Toolkits.Behavior.Events`, alongside the existing
`AssignBehaviorEvent`.

```csharp
public sealed class AssignTacticalIntentEvent
{
    public Entity Entity;
    public string IntentId    = string.Empty;   // e.g. "DefendArea" or "ConvoyEscort"
    public string JsonParams  = string.Empty;   // JSON payload (Lat/Lon, NetIds, etc.)
}
```

`IntentId` carries either:
- A **generic tactical intent** identifier (e.g. `"DefendArea"`) resolved by a mapper, or
- A **concrete behavior name** (e.g. `"ConvoyEscort"`) passed directly to
  `BehaviorIngressSystem` as a fallback.

The sender never needs to know which interpretation applies.

### 1.2 ITacticalOrderMapper

A stateless translation rule placed in `Fdp.Toolkits.Behavior`, alongside the event.

```csharp
public interface ITacticalOrderMapper
{
    string TargetIntentId { get; }

    bool TryMap(
        Entity self,
        EntityRepository repo,
        string jsonParams,
        out AssignBehaviorEvent assignment);
}
```

`TryMap` receives the full `EntityRepository` so the mapper can query `TkbIdentity`,
capability components, or any other ECS state needed to select the correct behavior and
format its DTO. Stateful dependencies (e.g. `NetworkEntityMap`) may be injected via the
mapper's constructor.

### 1.3 TacticalIntentMapperRegistry

A registry class (similar to the existing `BehaviorRegistry`) that holds the mapper
dictionary and is injected into `TacticalIntentResolutionSystem`.

```
TacticalIntentMapperRegistry
  + Register(ITacticalOrderMapper mapper)
  + bool TryGetMapper(string intentId, out ITacticalOrderMapper mapper)
```

---

## Phase 2: Receiver-Side Resolution

### 2.1 TacticalIntentResolutionSystem

A new `IEcsModuleSystem` in `Hrot.CGF.Systems`, registered in the **Simulation** phase
immediately after `MissionAdapterSystem` in `CgfLogicPack`.

**Behaviour per frame:**

1. Read all `AssignTacticalIntentEvent` from the managed bus read buffer.
2. For each event, evaluate `repo.HasAuthority<BehaviorState>(evt.Entity)`. If `false`,
   the cognitive state is owned by another node — skip silently.
3. Look up `event.IntentId` in `TacticalIntentMapperRegistry`.
4. **Mapper found:** Call `mapper.TryMap(entity, repo, jsonParams, out behaviorEvent)`.
   If `TryMap` returns `true`, publish the resulting `AssignBehaviorEvent`.
5. **No mapper / TryMap returns false:** Treat `IntentId` as a concrete behavior name.
   Publish `new AssignBehaviorEvent { Entity, BehaviorName = event.IntentId, JsonParams }`.

`BehaviorIngressSystem` (in the Input phase) then consumes the `AssignBehaviorEvent`
on the next frame, exactly as today.

### 2.2 Registration in CgfLogicPack

`TacticalIntentResolutionSystem` is instantiated and registered in `CgfLogicPack`'s
Simulation system list. A `TacticalIntentMapperRegistry` instance is passed in at
construction time, populated by the composition root with the project-specific mapper
implementations.

`AssignTacticalIntentEvent` must be registered on the `FdpEventBus` (managed stream)
alongside the existing managed events.

---

## Phase 3: MissionAdapterSystem Modification

`MissionAdapterSystem` is changed to emit `AssignTacticalIntentEvent` **instead of**
`AssignBehaviorEvent`.

**Before:**
```
_behaviorRegistry.TryGetDefinition(phase.BehaviorId, out var def)
→ emit AssignBehaviorEvent { BehaviorName = def.Name, JsonParams }
```

**After:**
```
task.BehaviorId (from DomainMissionTask)
→ emit AssignTacticalIntentEvent { IntentId = task.BehaviorId, JsonParams }
```

Key consequences:
- `_behaviorRegistry` dependency is no longer needed in `MissionAdapterSystem` and can
  be removed from its constructor.
- No change to `DomainMissionTask`, `MissionTask` DDS struct, or `MissionPlanQueue`.
- No `IsTacticalIntent` flag anywhere in the data model.

The resolution and fallback logic is now entirely handled by
`TacticalIntentResolutionSystem`, keeping `MissionAdapterSystem` as a thin reactive
change-detector.

---

## Phase 4: UI Discovery for Intent DTOs

### 4.1 BehaviorCategory Extension

`BehaviorCategory` (in `Hrot.Core.MapDefinitions.Behavior`) receives a new `Commander`
flag to represent entities whose role is to direct other units. The existing
`AllMilitary` flag (`MilitaryApc | Infantry | Insurgent`) continues to apply to all
subordinate unit types.

```csharp
public enum BehaviorCategory
{
    None        = 0,
    Civilian    = 1 << 0,
    MilitaryApc = 1 << 1,
    Infantry    = 1 << 2,
    Insurgent   = 1 << 3,
    Commander   = 1 << 4,
    AllMilitary = MilitaryApc | Infantry | Insurgent
}
```

### 4.2 Intent Parameter DTOs

Each generic intent has a plain DTO class decorated with `[BehaviorContract]` in
`Hrot.Core.MapDefinitions.Behavior`. The `BehaviorContractAttribute` carries:
- A **unique** `behaviorId` (reusing the same integer registry as behaviors - intent IDs
  occupy a reserved range).
- The `BehaviorId` string matching the mapper's `TargetIntentId`.
- `BehaviorCategory.AllMilitary` (or `| Commander` if the intent is specifically for
  commander entities).

Example:
```csharp
[BehaviorContract(BehaviorIds.DefendArea_Intent, "DefendArea", BehaviorCategory.AllMilitary)]
public sealed class DefendAreaIntentDto
{
    public double Lat;
    public double Lon;
    public float  Radius;
    public long   TargetNetId;
}
```

`BehaviorSchemaDiscovery.AutoRegister` picks these up automatically via reflection
because it scans all types in the `Hrot.Core` assembly decorated with
`[BehaviorContractAttribute]`. No changes to `BehaviorSchemaDiscovery` are needed.

### 4.3 BehaviorCatalog Update

`BehaviorCatalog.GetValidBehaviors` is extended to handle the new `Commander` TKB type.
The method's switch already handles all known TKB types; a new arm is added for
`TkbEntityTypes.Commander` (once that constant is defined). The `s_commanderIntents`
list is built from `BehaviorContractAttribute` entries that have `Commander` in their
`ValidCategories` flags, following the same reflection pattern as the existing military
and insurgent lists.

---

## Phase 5: Network Transport (Cross-Brain-Node)

When the Commander AI and subordinates reside on different Brain nodes, the
`AssignTacticalIntentEvent` must traverse DDS. The existing `AssignBehaviorEvent`
is and remains a strictly local-bus event that never crosses the network.

### 5.1 DDS Message Type

A new DDS struct `TacticalIntentRequest` is added to `Hrot.Network.NED`
(`MissionMessages.cs` or a new `TacticalIntentMessages.cs`):

```csharp
[DdsStruct]
[DdsIdlFile("hrot-tactical-intent")]
[DdsManaged]
public partial struct TacticalIntentRequest
{
    public long   TargetEntityId;
    public string IntentId;
    public string JsonParams;
}
```

A new ordinal is added to `EDescriptorType` in `AllDescriptors.cs`:

```csharp
dtTacticalIntentRequest = 92,
```

### 5.2 Translator Pair

`TacticalIntentEgressTranslator` (on the Commander's node) reads
`AssignTacticalIntentEvent` from the local bus and writes `TacticalIntentRequest` to DDS.
Before serialising, it checks `!repo.HasAuthority<BehaviorState>(evt.Entity)`: only events
where the local node does **not** own the cognitive state are forwarded over DDS. Events for
locally-owned entities are ignored (they will be resolved by the local
`TacticalIntentResolutionSystem` on the same frame).

`TacticalIntentIngressTranslator` (on the subordinate's node) polls DDS and
re-publishes `AssignTacticalIntentEvent` on the local bus. `TacticalIntentResolutionSystem`
then picks it up exactly as if it came from a local Commander, and its
`HasAuthority<BehaviorState>` gate passes because this node owns the cognitive state.

This authority-based gate also eliminates any need for an `IsRemote` flag on
`AssignTacticalIntentEvent`: Node A lacks authority over Node B's subordinate, so it
publishes the event and egress forwards it to DDS. Node B receives it via ingress, and
egress ignores it because `HasAuthority<BehaviorState>` is `true` on Node B. No echo loop
can form.

Both translators follow the exact same pattern as the existing
`MissionControlIngressTranslator` / `MissionControlAckEgressTranslator` pair in
`Hrot.Network.NED/SimHost/`.

Both translators are registered in `SimHostAuxiliaryTranslatorPack`.

---

## Phase 6: Commander BTree Integration

A reference BTree action node demonstrates how Commander AI publishes the event.
The Commander is completely decoupled from the subordinate's unit type:

```csharp
[SharedAiAction(typeof(CommanderBlackboard), "Params")]
public static NodeStatus Act_IssueTacticalIntent(ref Params dto, Entity self, EntityRepository repo)
{
    foreach (var sub in /* subordinates */)
    {
        repo.Bus.PublishManaged(new AssignTacticalIntentEvent
        {
            Entity     = sub,
            IntentId   = dto.IntentId,   // e.g. "DefendArea"
            JsonParams = dto.JsonParams  // pre-serialized by the BTree action
        });
    }
    return NodeStatus.Success;
}
```

---

## Architectural Decisions

| Decision | Rationale |
|---|---|
| `AssignBehaviorEvent` unchanged | Keeps `BehaviorIngressSystem` as a single-responsibility low-level memory mutator. |
| No `IsTacticalIntent` flag in data model | `BehaviorId` is already a string; fallback handles both concepts without branching. |
| Mapper interface in `Fdp.Toolkits` | Allows mapper implementations in `Hrot.AI.Behaviors` without creating a circular dependency. |
| `TacticalIntentResolutionSystem` in `Hrot.CGF` | Hrot-specific; depends on Hrot behavior registry and mapper registry. Not general FDP engine infrastructure. |
| 2-frame latency accepted | Commander tactical order latency of ~33 ms is physically realistic and not perceptible in gameplay. |
| Formation coordination excluded | Continuous spatial group movement remains with `FormationTargetSystem` (pull pattern). The intent pipeline is for high-level behavioral phase shifts only. |
| `MissionAdapterSystem` emits `AssignTacticalIntentEvent` | Unifies the command path so both AI and human-authored orders go through the same resolution. Removes `_behaviorRegistry` dependency from the adapter. |
| No `IsRemote` flag on `AssignTacticalIntentEvent` | Authority is granular per component, not per entity. The `HasAuthority<BehaviorState>` gate in egress prevents forwarding local events to DDS; the same gate in the resolution system prevents acting on remote events. The two gates together make an `IsRemote` flag redundant. |
| Authority checked via `HasAuthority<BehaviorState>` | FDP ownership is component-level. A Brain node owns the cognitive state (`BehaviorState`, `BrainBlackboard`) independently from kinematic state. Checking `BehaviorState` authority ensures the intent is resolved on the exact node that will later write `BrainBTreeState` via `BehaviorIngressSystem`. |

# Design: DDS-to-ECS Architectural Cleanup

**Source:** [`design-talk.md`](./design-talk.md)  
**Status:** Ready for implementation  
**Date:** 2026-02-27

---

## 1. Architectural Principle (The Golden Rule)

The FDP engine enforces a strict three-layer separation between the network and the simulation:

```
[ DDS (Wire) ] ──ingress──► [ Translator ] ──► [ ECS (Internal State) ]
[ ECS (Internal State) ] ──► [ Translator ] ──egress──► [ DDS (Wire) ]
```

**Rule 1 — DDS types are DTOs.**  
A DDS descriptor struct (`EntityMaster`, `WorldPos`, `EntityInfo`, `EntityDamage`, etc.) is a
*Data Transfer Object* for the network wire. It is designed around what remote nodes need to
communicate, not around what the simulation engine needs internally.

**Rule 2 — ECS types are internal state.**  
ECS components (`SimTransform`, `GeoTransform`, `NetworkIdentity`, `NetworkSpawnRequest`, etc.) are
the engine's internal simulation state. They must only carry data that the simulation systems
actually use. They must never be shaped by network wire formats.

**Rule 3 — Translators are the strict bridge.**  
A translator converts between DDS types and ECS types. It reads one, produces the other. Raw DDS
structs must never appear in `SpawnEntityCommand.InitialComponents`, `UpdateEntityCommand
.ComponentsToUpdate`, or in `cmd.SetComponent` calls.

**Rule 4 — `AutoCycloneTranslator` is only valid for structurally-pure ECS types.**  
Its only legitimate use is when the DDS type happens to be a perfect ECS component (i.e. it carries
only internal simulation data and has a `[ComponentId]`). DDS DTOs like `EntityMaster` and
`WorldPos` are **not** structurally-pure ECS types and must never be used with it.

### The Gold Standard: NetworkDemo

`Fdp.Examples.NetworkDemo.Translators.FastGeodeticTranslator` demonstrates the pattern:

- **Ingress:** Receives `GeoStateDescriptor` (DDS), converts lat/lon/alt → Cartesian, writes
  only `SimTransform` to the command buffer. `GeoStateDescriptor` is never registered in the ECS.
- **Egress:** Queries `SimTransform` from ECS, converts to Geodetic, publishes `GeoStateDescriptor`
  to DDS.

The FDP internal `EntityMasterTranslator` (in `ModuleHost.Core`) also demonstrates: it translates
the DDS `EntityMaster` DTO into `NetworkIdentity`, `NetworkOwnership`, and `NetworkSpawnRequest`
ECS components — none of which are DDS types.

---

## 2. Current Violations (What Is Wrong and Where)

### 2.1 Violations in `Hrot.NED`

| File | Violation |
|------|-----------|
| `GenericDescriptors.cs` | `EntityMaster` carries `[ComponentId(GlobalComponentIds.EntityMaster)]` — a network DTO dual-purposed as an ECS component |
| `SimDescriptors.cs` | `EntityDamage` carries `[ComponentId(GlobalComponentIds.EntityDamage)]` — same anti-pattern |

### 2.2 Violations in `Hrot.SimHost`

| File | Violation |
|------|-----------|
| `Util/DescriptorMapper.cs` | `dtEntityMaster` case: `result.Add(d.EntityMaster)` — raw DDS DTO stuffed into `InitialComponents` |
| `Util/DescriptorMapper.cs` | `dtEntityInfo` case: `result.Add(d.EntityInfo)` — raw DDS DTO stuffed into `InitialComponents` |
| `Util/DescriptorMapper.cs` | `dtWorldPos` case: `result.Add(d.WorldPos)` raw add (alongside the correct `SimTransform`) — partial violation, raw DDS DTO still leaks in |
| `Util/DescriptorMapper.cs` | `dtWorldPos` case: `result.Add(d.WorldPos)` — raw DDS DTO, never translated to internal kinematics |
| `SimHostApp.cs` | `world.RegisterComponent<EntityMaster>()` — DDS DTO registered as ECS component |
| `SimHostApp.cs` | `new AutoCycloneTranslator<EntityMaster>(...)` — magic shortcut relying on `[ComponentId]` on a DDS type |
| `SimHostApp.cs` | `onEntitySpawned` callback: `world.HasComponent<EntityMaster>(entity)` — queries a DDS DTO from the ECS |

### 2.3 Violations in `Hrot.IG`

| File | Violation |
|------|-----------|
| `IgApplication.cs` | `_world.RegisterComponent<EntityMaster>()` — DDS DTO registered as ECS component |
| `IgApplication.cs` | `_world.Query().With<EntityMaster>().With<SimTransform>().Build()` — entity render query is based on a DDS DTO presence |
| `IgApplication.cs` | `DisTypeExtractor` lambda: checks `EntityMaster` struct from ECS to extract `DisType` |
| `Translators/EntityMasterTranslator.cs` | `cmd.SetComponent(existing, master)` — updates EntityMaster DDS struct in ECS when entity already known |
| `Translators/EntityMasterTranslator.cs` | `InitialComponents = new List<object> { master }` — injects the raw DDS DTO into the spawn pipeline |
| `Translators/EntityInfoTranslator.cs` | `ComponentsToUpdate = new List<object> { info }` — passes the raw `EntityInfo` DDS DTO as an ECS UpdateEntityCommand component |
| `Translators/EntityInfoTranslator.cs` | `ApplyToEntity`: `repo.SetComponent(entity, info)` — stores the DDS DTO directly into the ECS |
| (missing) | No `EntityDamageTranslator` — relied on `[ComponentId]` magic on `EntityDamage` |
| (missing) | No `MapEntitySymbolTranslator` — relied on `[ComponentId]` magic |

---

## 3. Clean Architecture (Target State)

### 3.1 DDS Data Model — No ECS Attributes

DDS descriptor types are plain C# structs with only DDS-specific attributes (`[DdsTopic]`,
`[DdsKey]`, `[DdsQos]`). They carry zero FDP kernel attributes:

```csharp
// CORRECT
[DdsTopic("EntityMaster")]
[DdsQos(...)]
public partial struct EntityMaster { ... }

// WRONG
[ComponentId(GlobalComponentIds.EntityMaster)]  // ← must not exist
public partial struct EntityMaster { ... }
```

### 3.2 SimHost — DescriptorMapper

`DescriptorMapper.MapToComponents()` must produce only pure ECS components:

| Incoming Descriptor | Correct ECS Output |
|--------------------|--------------------|
| `dtEntityMaster` | *(nothing — TkbType already flows via `SpawnEntityCommand.TkbType`)* |
| `dtEntityInfo` | *(nothing — EntityInfo is not needed at spawn time for SimHost)* |
| `dtWorldPos` | `SimTransform` (Cartesian position from WGS84) + `GeoTransform` (geodetic coords) |
| `dtWorldPos` | `GeoVelocity` (speed/heading from DR polar coords) |

### 3.3 SimHost — EntityMaster Egress

An `EntityMasterEgressTranslator` (new class) replaces `AutoCycloneTranslator<EntityMaster>`.  
It queries FDP-internal components (`NetworkIdentity`, `NetworkOwnership`, `NetworkSpawnRequest`)
and constructs the DDS `EntityMaster` DTO purely from them for the wire.

### 3.4 IG — EntityMasterTranslator

IG's `EntityMasterTranslator` fires `SpawnEntityCommand` with `InitialComponents = []` (empty).  
The `TkbType` and `OwnerNodeId` are sufficient for `NetworkSpawningSystem` to do its job.  
No `EntityMaster` DDS struct is ever written to the ECS.  
The "update known entity" path (`cmd.SetComponent(existing, master)`) is removed; there is no
ECS EntityMaster component to update.

### 3.5 IG — IgEntityData (New Internal Component)

A new ECS class component `IgEntityData` holds the data the IG actually needs from `EntityInfo`:

```csharp
public class IgEntityData
{
    public string Name        { get; set; } = string.Empty;
    public ForceId ForceId    { get; set; } = ForceId.Unknown;
    public int CommanderId    { get; set; } = 0;
}
```

`EntityInfoTranslator` maps `EntityInfo.Name`, `EntityInfo.ForceIdentifier`, and
`EntityInfo.CommanderId` into an `IgEntityData` instance and issues `UpdateEntityCommand`.

### 3.6 IG — IgHealthState (New Internal Component)

A new ECS value component `IgHealthState` holds damage data the IG rendering needs:

```csharp
public struct IgHealthState
{
    public float Damage;  // 0 = healthy, 100 = fully destroyed
}
```

`EntityDamageTranslator` maps `EntityDamage.Damage` → `IgHealthState`.

### 3.7 IG — MapEntitySymbolTranslator

The existing `IgSymbolOverride` class component is already correct.  
A new `MapEntitySymbolTranslator` (using `CycloneTranslator<MapEntitySymbol, MapEntitySymbol>`)
maps `MapEntitySymbol` → `IgSymbolOverride` via `cmd.SetManagedComponent`.

### 3.8 IG — IgApplication Queries

The entity render query currently uses `EntityMaster` as a presence filter:
```csharp
// WRONG
_world.Query().With<EntityMaster>().With<SimTransform>().Build();
```
After the cleanup it uses the FDP-internal presence marker:
```csharp
// CORRECT
_world.Query().With<NetworkIdentity>().With<SimTransform>().Build();
```

The `DisTypeExtractor` lambda currently extracts `DisType` from the DDS `EntityMaster` struct.
After cleanup it reads from `NetworkSpawnRequest`:
```csharp
DisTypeExtractor disExtractor = (object c, out ulong dis) =>
{
    if (c is NetworkSpawnRequest req) { dis = req.DisType; return true; }
    dis = 0; return false;
};
```
*(Note: `NetworkSpawnRequest` must carry `DisType`. Confirm the existing FDP type has this field,
or adjust extraction accordingly.)*

---

## 4. Implementation Phases

### Phase 1 — Purify DDS Data Model
**Goal:** Remove all ECS kernel attributes from DDS descriptor types.  
**Files touched:** `Hrot.NED/GenericDescriptors.cs`, `SimDescriptors.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S1T1 | Remove `[ComponentId]` from `EntityMaster` in `GenericDescriptors.cs` |
| DDS2ECS-S1T2 | Remove `[ComponentId]` from `EntityDamage` in `SimDescriptors.cs` |

**Gate:** Project compiles with zero errors. Tests for `AutoCycloneTranslator<EntityMaster>` now
fail to compile → confirms correct phase sequencing (Phase 3 fixes SimHost before compilation can
be restored).

---

### Phase 2 — SimHost: Fix DescriptorMapper
**Goal:** `DescriptorMapper.MapToComponents` produces only pure ECS components.  
**Files touched:** `Hrot.SimHost/Util/DescriptorMapper.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S2T1 | `dtEntityMaster` case: produce nothing (remove `result.Add(d.EntityMaster)`) |
| DDS2ECS-S2T2 | `dtEntityInfo` case: produce nothing (remove `result.Add(d.EntityInfo)`) |
| DDS2ECS-S2T3 | `dtWorldPos` case: remove `result.Add(d.WorldPos)` raw add; add `GeoTransform` alongside the existing `SimTransform` generation |
| DDS2ECS-S2T4 | `dtWorldPos` case: replace `result.Add(d.WorldPos)` with `GeoVelocity` translation from the DR polar vector |

---

### Phase 3 — SimHost: Replace `AutoCycloneTranslator<EntityMaster>`
**Goal:** SimHost publishes `EntityMaster` to DDS via a proper egress translator — never via
auto-magic.  
**Files touched:** `Hrot.SimHost/SimHostApp.cs`; new file `Hrot.SimHost/Translators/EntityMasterEgressTranslator.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S3T1 | Create `EntityMasterEgressTranslator.cs`: queries `NetworkIdentity` + `NetworkOwnership` + `NetworkSpawnRequest`, builds `EntityMaster` DTO, publishes to DDS |
| DDS2ECS-S3T2 | In `SimHostApp.cs`: replace `AutoCycloneTranslator<EntityMaster>` registration with `EntityMasterEgressTranslator` |
| DDS2ECS-S3T3 | In `SimHostApp.cs → RegisterSimComponents`: remove `world.RegisterComponent<EntityMaster>()` |
| DDS2ECS-S3T4 | In `SimHostApp.cs → onEntitySpawned` callback: remove `world.HasComponent<EntityMaster>` guard; use `NetworkAuthority` or `NetworkOwnership` to determine local-authority status instead |

---

### Phase 4 — IG: Fix `EntityMasterTranslator`
**Goal:** IG's translator no longer injects the raw `EntityMaster` DDS struct into the ECS.  
**Files touched:** `Hrot.IG/Translators/EntityMasterTranslator.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S4T1 | `ProcessSample` — spawn path: change `InitialComponents = new List<object> { master }` to `InitialComponents = new List<object>()` |
| DDS2ECS-S4T2 | `ProcessSample` — update path: remove the `cmd.SetComponent(existing, master)` call entirely (there is no ECS EntityMaster component to update) |
| DDS2ECS-S4T3 | `ApplyToEntity`: remove the `repo.SetComponent(entity, master)` body (now a no-op) |

---

### Phase 5 — IG: Create `IgEntityData` and Fix `EntityInfoTranslator`
**Goal:** `EntityInfo` DDS data is translated into an IG-internal ECS component.  
**Files touched:** new `Hrot.IG/Components/IgEntityData.cs`; `Hrot.IG/Translators/EntityInfoTranslator.cs`; `Hrot.IG/IgApplication.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S5T1 | Create `IgEntityData.cs`: managed class component with `Name`, `ForceId`, `CommanderId` |
| DDS2ECS-S5T2 | `EntityInfoTranslator.PollIngress`: translate `EntityInfo` → `IgEntityData`; pass `IgEntityData` (not the raw struct) in `UpdateEntityCommand.ComponentsToUpdate` |
| DDS2ECS-S5T3 | `EntityInfoTranslator.ApplyToEntity`: translate and store `IgEntityData` (not `EntityInfo`) |
| DDS2ECS-S5T4 | `IgApplication.InitializeEcs`: add `_world.RegisterManagedComponent<IgEntityData>()` |

---

### Phase 6 — IG: Create `IgHealthState` and `EntityDamageTranslator`
**Goal:** `EntityDamage` DDS data is translated into an IG-internal ECS component.  
**Files touched:** new `Hrot.IG/Components/IgHealthState.cs`; new `Hrot.IG/Translators/EntityDamageTranslator.cs`; `Hrot.IG/IgApplication.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S6T1 | Create `IgHealthState.cs`: unmanaged struct component `{ float Damage; }` |
| DDS2ECS-S6T2 | Create `EntityDamageTranslator.cs`: `CycloneTranslator<EntityDamage, EntityDamage>` that decodes to `cmd.SetComponent(entity, new IgHealthState { Damage = data.Damage })` |
| DDS2ECS-S6T3 | `IgApplication.InitializeNetwork`: add `new EntityDamageTranslator(participant, _entityMap)` to `customTranslators` |
| DDS2ECS-S6T4 | `IgApplication.InitializeEcs`: add `_world.RegisterComponent<IgHealthState>()` |

---

### Phase 7 — IG: Create `MapEntitySymbolTranslator`
**Goal:** `MapEntitySymbol` DDS data is translated into the existing `IgSymbolOverride` ECS component.  
**Files touched:** new `Hrot.IG/Translators/MapEntitySymbolTranslator.cs`; `Hrot.IG/IgApplication.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S7T1 | Create `MapEntitySymbolTranslator.cs`: `CycloneTranslator<MapEntitySymbol, MapEntitySymbol>` that decodes to `cmd.SetManagedComponent(entity, new IgSymbolOverride { StyleSetId = data.StyleSetId, TextureOverride = ... })` |
| DDS2ECS-S7T2 | `IgApplication.InitializeNetwork`: add `new MapEntitySymbolTranslator(participant, _entityMap)` to `customTranslators` |

---

### Phase 8 — IG: Fix `IgApplication` Registrations and Queries
**Goal:** Remove all `EntityMaster` DDS type usage from the IG application shell.  
**Files touched:** `Hrot.IG/IgApplication.cs`

| Task | Description |
|------|-------------|
| DDS2ECS-S8T1 | `InitializeEcs`: remove `_world.RegisterComponent<EntityMaster>()` |
| DDS2ECS-S8T2 | `InitializeNetwork`: replace entity render query `.With<EntityMaster>()` with `.With<NetworkIdentity>()` |
| DDS2ECS-S8T3 | `InitializeNetwork`: fix `DisTypeExtractor` lambda — extract `DisType` from `NetworkSpawnRequest` instead of `EntityMaster` |

---

## 5. Invariants Preserved

These behaviours must remain identical after the cleanup:

- SimHost spawns entities via `CreateEntityRequest` DDS message → entities appear in the world.
- IG receives `EntityMaster` DDS samples → ghost entities are spawned and rendered.
- SimHost publishes `WorldPos` / `WorldPos` → IG's `WorldPosTranslator` updates `SimTransform`.
- IG renders entities using `ResolvedStyle` driven by `IgVisualDef`, `IgSymbolOverride`, `ResolvedStyle`.
- SimHost `WorldPosEgressTranslator` reads `GeoTransform` + `GeoVelocity` and publishes DDS (unchanged).
- All existing unit tests pass; new tests added per task specs in `TASK-DETAIL.md`.

---

## 6. Four Additional Architectural Deviations

Beyond the DDS-in-ECS anti-patterns above, four more major deviations from the `NetworkDemo`
gold standard must be fixed. Left as-is, these cause zombie entities, invisible combat, stuttering
movement, and clock drift between nodes.

---

### 6.1 Deviation 1 — No Network Cleanup (Zombie Entities)

**Root cause:** Neither `SimHostApp.cs` nor `SimHostSubsystem` registers
`CycloneNetworkCleanupSystem`. When a local entity is destroyed the entity disappears from SimHost
memory but no DDS `NOT_ALIVE_DISPOSED` sample is sent on the `EntityMaster` topic.

**Effect:** IG ghost entities for destroyed SimHost vehicles freeze on the map forever ("zombies").

**Fix:** In `SimHostApp.cs` (and `SimHostSubsystem.cs`), after constructing
`EntityMasterEgressTranslator`, pass it to `CycloneNetworkCleanupSystem` and register that system
as a global system:
```csharp
_kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(entityMasterEgressTranslator));
```

---

### 6.2 Deviation 2 — Missing Transient Event Translators (Invisible Combat)

**Root cause:** `Hrot.IG` registers `EventEffectModule` which listens for
`FireInteractionEvent` to draw explosions and laser tracers. But neither SimHost nor IG registers
a `CycloneNativeEventTranslator` for the event — no DDS topic bridges this ECS event across nodes.

**Effect:** SimHost vehicles fire at each other; IG map shows nothing.

**Fix:** Create `FireInteractionEventTranslator` (inheriting `CycloneNativeEventTranslator`).
Register it in:
- **SimHostApp** / **SimHostSubsystem** — Egress: captures the ECS event and publishes it to DDS.
- **IgApplication** / **IgSubsystem** — Ingress: reads the DDS topic and publishes the event on
  the local `FdpEventBus` so `EventEffectModule` receives it.

---

### 6.3 Deviation 3 — Hard-Snapping vs. Dead Reckoning (Stuttering Movement)

**Root cause:** `Hrot.IG/Translators/WorldPosTranslator.cs` overwrites `SimTransform`
directly from the DDS position update. Because network packets arrive at irregular intervals, the
entity's visual position teleports (hard-snaps) every time a packet arrives.

**Effect:** Vehicles visibly stutter and teleport on the IG map. The `WorldPos` topic is sent
by SimHost but never used for dead reckoning.

**Fix — Two-part:**

**Part A — Fix ingress path:**  
`WorldPosTranslator.Decode` must write to `NetworkPosition` (not `SimTransform`) after
converting geodetic → Cartesian.  
Create `WorldPosTranslator` in IG that converts the DR packet to both `NetworkPosition` and
`NetworkVelocity`.  
`TransformSyncSystem` (already registered in IG) then Lerps `SimTransform` toward `NetworkPosition`
smoothly.

**Part B — Dead Reckoning System (replace naive Lerp):**  
The current `TransformSyncSystem` only Lerps; it does not project the anchor forward using
velocity, so entities slip backward toward a static `NetworkPosition` between packets.

Create `DeadReckoningSyncSystem` in `Hrot.IG/Systems/` with a **"Project and Blend"** algorithm:

1. **Project:** Advance `NetworkPosition` by `NetworkVelocity * deltaTime` each frame so the
   anchor continues moving even when no new packet has arrived.
2. **Blend:** Lerp `SimTransform` toward the projected `NetworkPosition` at a configurable rate.
3. Keep `SimVelocity` in sync so trail/effect systems see correct speed.

Register `DeadReckoningSyncSystem` via `_kernel.RegisterGlobalSystem(...)` and deregister or
disable `TransformSyncSystem` for ghost entities (or replace `TransformSyncSystem` registration).

---

### 6.4 Deviation 4 — Broken Distributed Time Synchronisation

**Root cause:** In `IgApplication.cs` the `TimePulseTranslator` line was commented out by the
original drafter with the note *"causes network init to fail (the pulse event not registered as
dds topic)"*. The translator itself exists and is correct; only the registration was disabled.

**Effect:** SimHost and IG run completely decoupled clocks. If SimHost lags slightly, IG dead
reckoning drifts out of phase and prediction errors accumulate.

**Fix — Three-part:**
1. Ensure `TimePulseDescriptor` is registered as a DDS topic (check that `[DdsTopic("TimePulse")]`
   is present on the type).
2. Uncomment `new TimePulseTranslator(participant, _eventBus)` in `IgApplication.InitializeNetwork`.
3. Add a `TimePulseTranslator` (or equivalent egress translator) to `SimHostApp` / `SimHostSubsystem`
   so SimHost broadcasts time pulses as the master clock.

---

## 7. GeoTransform vs NetworkPosition — Egress/Ingress Architecture

This section clarifies when to use each component and why the egress path is deliberately split
into two steps.

### 7.1 `GeoTransform` is an Egress Buffer (SimHost side)

SimHost physics systems update `SimTransform` + `SimVelocity` in local Cartesian space.
`SimTransformBridgeSystem` (geographic toolkit) runs in the `PostSimulation` phase, converts
Cartesian → WGS84, and writes `GeoTransform` + `GeoVelocity`.  
`WorldPosEgressTranslator` then copies `GeoTransform` → `WorldPos` DDS struct for the wire.

```
SimTransform ──► SimTransformBridgeSystem ──► GeoTransform ──► WorldPosEgressTranslator ──► DDS
```

`GeoTransform` is never used for smoothing/interpolation. It is a staging area between the physics
engine and the network layer. Other local systems (AI, UI) can also read `GeoTransform` without
touching the network layer.

### 7.2 Why Two Steps on Egress (Not One)?

Three reasons make combining them into a single translator harmful:

| Reason | Detail |
|--------|--------|
| **Separation of concerns** | `Fdp.Toolkit.Geographic` has no DDS dependency. `ModuleHost.Network.Cyclone` has no geography dependency. Each toolkit stays clean. |
| **Dirty tracking / bandwidth** | `SimTransformBridgeSystem` only updates `GeoTransform` when the change exceeds a configurable threshold (e.g., `> 1e-6 °`). The ECS chunk-version flag is only bumped when the component actually changes. The egress translator checks the chunk version — if unchanged, it skips the entity entirely (zero CPU, zero bandwidth). If the trig math lived inside the translator, it would run every frame for every entity with no way to skip. |
| **Execution phase** | Heavy WGS84 math runs multithreaded in `PostSimulation`. The Export phase runs single-threaded as a fast `memcpy` from RAM to the network socket. |

### 7.3 `NetworkPosition` is an Ingress Anchor (IG side)

On the IG side, incoming WGS84 coordinates are converted to Cartesian **once** in the translator
and stored in `NetworkPosition`. Lerping/dead reckoning happens in cheap Cartesian space — not in
expensive Geodetic space (which would require great-circle formulas every frame).

```
DDS ──► WorldPosTranslator ──► NetworkPosition + NetworkVelocity
                                         │
                                   DeadReckoningSyncSystem
                                    (Project + Blend)
                                         │
                                    SimTransform (visual)
```

---

## 8. IOS / IG Interaction — Completeness Audit

### 8.1 Map Click Flow ✓ (Implemented)

Complete round-trip:
- IOS activates placement tool → broadcasts `MapInteractionConfig` with `ContextId` to IG.
- IG user clicks map → `OnCanvasClicked` sends `MapClickEvent` (WGS84 position + `ContextId`) to DDS.
- IOS receives click, verifies `ContextId`, sends `CreateEntityRequest` to SimHost.
- SimHost spawns entity; `EntityMaster` and `WorldPos` propagate back to IOS and IG.

### 8.2 Context Menu Push ✓ (Logic complete; IG rendering missing)

Architecture (Zero-Latency Push Model):
- IOS `ContextMenuLogic.cs` listens for `SelectionChangedEvent` from IG.
- Maps active `MenuStrategy` to a list of `ContextMenuItem`s (Standard / Admin / Logistics etc.).
- Pushes `ContextActionsUpdate` DDS message to IG.
- IG `ContextMenuSystem` stores result in `ContextMenuState` ECS component.

**Gap:** No ImGui rendering in `IgApplication.DrawUI` calls `BeginPopupContextWindow` to actually
draw the menu. The ECS state is populated but never shown.

### 8.3 ORBAT Hierarchy — Partial

- **IOS** (`OrbatPanel.cs`): Fully implemented. Reads `EntityInfo.CommanderId`, builds
  `CommanderId → children` dictionary, renders collapsible tree with ImGui.
- **IG**: Uses `EntityInfo.Name` for labels only. `VisHierarchyNode` ECS support exists in the
  FDP engine but `CommanderId` from `EntityInfo` is not wired to it.

### 8.4 Mission Plans

- **IOS `MissionEditorService`**: Sends `MissionControlRequest` messages (`CMD_REPLACE_MISSION`,
  `CMD_JUMP_TO_TASK`, `CMD_ABORT_ALL`) and awaits `MissionControlAck`.
- **SimHost `MissionAdapterSystem`**: Executes missions from `EntityMissionHolder` by mapping
  task `BehaviorId` → `DoctrineHash` and pushing `BehaviorParams` JSON to `BrainBlackboard`.
- **🚨 Critical gap:** SimHost has **no translator or system that reads `MissionControlRequest`
  or writes `MissionControlAck`**. IOS commands go into the DDS void — SimHost never applies them.
  A `MissionControlRequestSystem` must be implemented in SimHost.

### 8.5 IOS Mission Editor UI — Incomplete

`Hrot.ExCon/Panels/MissionPanel.cs` is a viewer/controller only:
- ✓ Displays task list with play/stop icons.
- ✓ JUMP and ABORT buttons.
- ✗ Cannot add, insert, or delete tasks.
- ✗ Cannot edit `BehaviorId` or `BehaviorParams` JSON.
- ✗ Cannot edit `Triggers`.
- ✗ No "Commit/Save" button wired to `MissionEditorService.CommitMissionAsync()`.

---

## 9. Additional Implementation Phases

### Phase 9 — Network Cleanup System

**Goal:** Destroyed SimHost entities send DDS dispose messages; IG ghost cleanup is automatic.

| Task | Description |
|------|-------------|
| DDS2ECS-S9T1 | `SimHostApp.cs`: register `CycloneNetworkCleanupSystem(entityMasterEgressTranslator)` as a global system |
| DDS2ECS-S9T2 | `SimHostSubsystem.cs`: same registration (Runner path mirrors standalone app) |

---

### Phase 10 — Dead Reckoning

**Goal:** IG ghost movement is smooth and predictive; no hard-snapping on packet arrival.

| Task | Description |
|------|-------------|
| DDS2ECS-S10T1 | Fix `WorldPosTranslator.Decode` (IG): write `NetworkPosition` instead of `SimTransform` |
| DDS2ECS-S10T2 | Create `WorldPosTranslator` (IG): converts `WorldPos` DDS → `NetworkPosition` + `NetworkVelocity` |
| DDS2ECS-S10T3 | Create `DeadReckoningSyncSystem` (IG): Project-and-Blend algorithm in `PostSimulation` phase |
| DDS2ECS-S10T4 | `IgApplication.InitializeNetwork`: add `WorldPosTranslator`; replace/supplement `TransformSyncSystem` registration with `DeadReckoningSyncSystem` |

---

### Phase 11 — Time Synchronisation Fix

**Goal:** SimHost broadcasts master clock pulses; IG PLL tracks them for deterministic simulation.

| Task | Description |
|------|-------------|
| DDS2ECS-S11T1 | Verify `TimePulseDescriptor` has `[DdsTopic("TimePulse")]`; fix if missing |
| DDS2ECS-S11T2 | `IgApplication.InitializeNetwork`: uncomment `new TimePulseTranslator(participant, _eventBus)` |
| DDS2ECS-S11T3 | `SimHostApp.cs` / `SimHostSubsystem.cs`: register egress `TimePulseTranslator` so SimHost broadcasts pulses |

---

### Phase 12 — Transient Event Translators

**Goal:** `FireInteractionEvent` is distributed over DDS so IG renders combat effects.

| Task | Description |
|------|-------------|
| DDS2ECS-S12T1 | Create `FireInteractionEventTranslator` (`CycloneNativeEventTranslator`) |
| DDS2ECS-S12T2 | `SimHostApp.cs` / `SimHostSubsystem.cs`: register as Egress translator |
| DDS2ECS-S12T3 | `IgApplication.InitializeNetwork`: register as Ingress translator |

---

### Phase 13 — SimHost Mission Control Reception

**Goal:** SimHost listens for `MissionControlRequest` from IOS and responds with `MissionControlAck`.

| Task | Description |
|------|-------------|
| DDS2ECS-S13T1 | Create `MissionControlRequestSystem` in `Hrot.SimHost/Systems/`: reads DDS `MissionControlRequest`, applies commands to `EntityMissionHolder`, writes `MissionControlAck` |
| DDS2ECS-S13T2 | `SimHostApp.cs` / `SimHostSubsystem.cs`: register `MissionControlRequestSystem` |

---

### Phase 14 — IOS Mission Editor UI

**Goal:** `MissionPanel.cs` becomes a full editor: add/delete/reorder tasks and edit parameters.

| Task | Description |
|------|-------------|
| DDS2ECS-S14T1 | Add task-list editing to `MissionPanel`: Add / Insert / Delete task buttons |
| DDS2ECS-S14T2 | Add `BehaviorId` dropdown and `BehaviorParams` JSON text-field editing |
| DDS2ECS-S14T3 | Add "Commit" button wired to `MissionEditorService.CommitMissionAsync()` |

---

### Phase 15 — Integration Test Harness

**Goal:** Automated xUnit integration tests for end-to-end flows using the real DDS stack.

**Strategy:**
- Each test class gets a unique DDS domain ID (domain isolation — tests cannot cross-talk).
- `HrotRunnerHarness` wraps `SubsystemOrchestrator` and provides a `PumpUntil(condition, timeoutFrames)` helper.
- Subsystems expose `internal` test hooks so the harness can inspect ECS state and inject inputs
  without production-code changes (only visibility modifiers).

**Required production-code tweaks (minor):**
- `IgSubsystem`: expose `internal IgApplication App => _app!;`
- `SimHostSubsystem`: expose `internal EntityRepository World => _world!;`
- `IosSubsystem`: expose `internal IosLogic Logic => _mock!.Logic;`
- `IgApplication`: add `internal void TestHook_SimulateMapClick(Vector2 worldPos)` that calls `OnCanvasClicked`.

| Task | Description |
|------|-------------|
| DDS2ECS-S15T1 | Add `internal` test-hook properties/methods to `IgSubsystem`, `SimHostSubsystem`, `IosSubsystem`, `IgApplication` |
| DDS2ECS-S15T2 | Create `HrotRunnerHarness.cs` in `Hrot.ClusterRunner.Integration.Tests`: domain-isolated orchestrator wrapper with `PumpUntil` |
| DDS2ECS-S15T3 | Create `MapPlacementIntegrationTests.cs`: end-to-end placement flow test (IOS activates tool → IG click → SimHost spawns → IG and IOS receive entity) |
| DDS2ECS-S15T4 | Create `ContextMenuIntegrationTests.cs`: selection → IOS pushes menu → IG `ContextMenuState` populated |
| DDS2ECS-S15T5 | Create `EntityDestroyIntegrationTests.cs`: SimHost destroys entity → IG ghost is removed (validates Phase 9) |
| DDS2ECS-S15T6 | Create `MissionControlIntegrationTests.cs`: IOS sends `CMD_JUMP_TO_TASK` → SimHost applies it → `MissionControlAck` returned (validates Phase 13) |

---

### Phase 16 — SimHost Mission Pipeline (UrbanCombat Alignment)

**Goal:** Align SimHost's mission execution pipeline with the `UrbanCombat` golden standard:
replace the managed-DTO holder with `MissionPlanQueue`, compile real BTree interpreters for all
doctrines, and replace the custom `MissionAdapterSystem` with the toolkit-standard
`MissionDirectorSystem`.

**Source of truth:** `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` — specifically
`RegisterDoctrines()` and `RegisterSystems()`. See also §10 below for the full deviation analysis.

**Prerequisite:** Phase 13 (MissionControlRequestSystem) uses `EntityMissionHolder` today;
update that reference to `MissionPlanQueue` as part of S16T1.

| Task | Description |
|------|-------------|
| DDS2ECS-S16T1 | Delete `EntityMissionHolder.cs`; replace `RegisterManagedComponent<EntityMissionHolder>()` with `RegisterComponent<MissionPlanQueue>()` in `SimHostApp.cs` |
| DDS2ECS-S16T2 | Rewrite `EntityMissionTranslator` to write `MissionPlanQueue` (resolve `BehaviorId` → `doctrineId` via `DoctrineRegistry.TryGetId`; map trigger strings → `MissionTrigger` enum); update `EntityMissionTranslatorTests.cs` |
| DDS2ECS-S16T3 | Delete `MissionAdapterSystem.cs`; in `SimulationLogicModule.RegisterSystems()` replace `new MissionAdapterSystem(...)` with `new MissionDirectorSystem()` |
| DDS2ECS-S16T4 | Compile BTree JSON blobs for `MoveTo_BT`, `FollowRoute_BT`, `JoinFormation_BT` and register real `Interpreter<BrainBlackboard,BTreeContext>` in each `DoctrineDefinition` in `SimHostApp.cs`; create `Hrot.SimHost/Brains/SimHostNodes.cs` |
| DDS2ECS-S16T5 | Wire `ParseParams` delegates for `MoveTo_BT` and `FollowRoute_BT` so `BrainBlackboard.Memory` is hydrated with target coordinates on phase activation |

---

### Phase 17 — SimHost Combat Readiness (UrbanCombat Alignment)

**Goal:** Elevate SimHost from a "driving-only" shell to a full FDP simulation node capable of
perception, combat, and damage — matching the `UrbanCombat` golden standard.

**Source of truth:**
- `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs` — `RegisterComponents()`,
  `RegisterSystems()` (Input/Sim/PostSim groups).
- `FDP/Examples/Fdp.Examples.UrbanCombat/Setup/DemoTkbSetup.cs` — canonical pattern for
  attaching real ECS components (`PerceptionReceptor`, `WeaponState`, `PhysicsCollider`, `Health`,
  `Faction`) to TKB templates via `t.AddComponent(...)`.

**Prerequisite:** Phase 16 must be complete (real BTree interpreters registered).

| Task | Description |
|------|-------------|
| DDS2ECS-S17T1 | Add `FDP.Toolkit.Perception` and `FDP.Toolkit.Combat` / `FDP.Toolkit.Combat.Contracts` `<ProjectReference>` entries to `Hrot.SimHost/Hrot.SimHost.csproj` |
| DDS2ECS-S17T2 | Add Perception, Combat, Physics, and HSM component registrations to `SimHostApp.RegisterSimComponents()` (mirror `HeadlessDemoApp.RegisterComponents()`) |
| DDS2ECS-S17T3 | Initialize `PhysicsToolkitModule` in `SimHostApp.OnLoad()` before `_kernel.Initialize()` to allocate `RaycastBatchData` singleton |
| DDS2ECS-S17T4 | Expand `SimulationLogicModule.RegisterSystems()` with Input-phase systems (`FireProcessingSystem`, `RaycastSolverSystem`, `HitResolutionSystem`), Sim-phase systems (`WeaponDispatcherSystem` + `AimAndFireExecutor`, `VisionBroadphaseSystem`, `LosRequestBatchingSystem`, `ThreatEvaluationSystem`, `DamageSystem`, `HsmDamageBridgeSystem`, `HsmTickSystem<BrainHsm128>`), and PostSim-phase system (`BallisticsSystem`) |
| DDS2ECS-S17T5 | Rewrite `BdcTkbBuilder.WithCombat()` to call `template.AddComponent()` for real FDP ECS components (`WeaponState`, `PerceptionReceptor`, `TargetMemory`, `PhysicsCollider`, `Health`, `Faction`) translated from `SimCombatDef` fields; retain `SimCombatDef` managed component for IG UI use |

---

## §10 — UrbanCombat Architecture Deviations: Mission Pipeline

*Identified Feb 28 2026 via design review against `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`.  
Three compounding deviations make it impossible for SimHost to execute any mission: a vehicle that
receives an `EntityMission` will never move.*

### 10.1 Data Model Deviation — Managed DTO Held in ECS

**Golden standard (`UrbanCombat`):** Missions are stored as `MissionPlanQueue`
(`GlobalComponentIds.MissionPlanQueue = 39`), an unmanaged `[InlineArray]` holding up to 8
`MissionPhase` structs in contiguous chunk memory. Zero-allocation and fully compatible with
`MissionDirectorSystem`.

**SimHost deviation:** `EntityMissionHolder` holds the raw DDS `EntityMission` struct. Because
`EntityMission.Plan.Tasks` is a `List<MissionTask>` (managed), the wrapper forces all mission
logic through the GC every frame — the exact same anti-pattern as `WorldPos` (Phase 2).
`MissionDirectorSystem` cannot operate on this type.

**Fix:** S16T1 + S16T2 — delete `EntityMissionHolder`, rewrite `EntityMissionTranslator` to write
`MissionPlanQueue`.

### 10.2 Brain Deviation — Null `BTreeInterpreter`

**Golden standard (`UrbanCombat`):** Every BTree doctrine is compiled from JSON and registered
with a real `Interpreter<BrainBlackboard,BTreeContext>`:

```csharp
var blob = TreeCompiler.CompileFromJson(InfantryCombatJson);
var reg  = new ActionRegistry<BrainBlackboard, BTreeContext>();
reg.Register("HoldPosition", InsurgentNodes.Action_HoldPosition);
_doctrineRegistry.Register(DoctrineIds.InfantryCombat, "InfantryCombat",
    new DoctrineDefinition {
        Name             = "InfantryCombat",
        BrainTier        = BehaviorConstants.BrainTierBTree,
        BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, reg),
    });
```

**SimHost deviation (`SimHostApp.cs` lines 135–142):** All BTree doctrines are registered with
only `Name` and `BrainTier`; `BTreeInterpreter` is implicitly `null`. `BTreeTickSystem` silently
skips any entity whose doctrine has a null interpreter. `LocomotionChannel` is never written.
The vehicle never moves.

**Fix:** S16T4 + S16T5 — compile blobs, build `ActionRegistry` instances with action delegates,
register real interpreters, wire `ParseParams` delegates.

### 10.3 Mission Progression Deviation — Custom String-Parser Instead of `MissionDirectorSystem`

**Golden standard (`UrbanCombat`):** `MissionDirectorSystem` (from `FDP.Toolkit.Behavior`)
evaluates typed `MissionTrigger` fields on each `MissionPhase` in `MissionPlanQueue`
(`ReachedDestination`, `TimerElapsed`, `HealthCritical`, etc.) and automatically advances
`CurrentPhase` when a trigger fires.

**SimHost deviation (`MissionAdapterSystem.cs`):** The system monitors
`LocomotionChannel.Status == NodeStatus.Success` and ignores the DDS `List<MissionTrigger>`
strings entirely. Because the BTree brain is null (§10.2), `Status` never becomes `Success`, so
tasks never advance regardless.

**Fix:** S16T3 — delete `MissionAdapterSystem.cs`; register `MissionDirectorSystem()` in
`SimulationLogicModule.RegisterSystems()` in its place.

### 10.4 Compound Effect and Ordering

All three deviations are mutually reinforcing: the null brain (§10.2) prevents
`LocomotionChannel.Status` from ever changing, so even the custom monitor (§10.3) would not
advance the mission. The managed data model (§10.1) blocks `MissionDirectorSystem` regardless.
**All Phase 16 tasks must be applied together** before the pipeline becomes functional.

---

## §11 — UrbanCombat Architecture Deviations: Combat Readiness

*Identified Feb 28 2026 via design review against `FDP/Examples/Fdp.Examples.UrbanCombat/HeadlessDemoApp.cs`
and `Setup/DemoTkbSetup.cs`.  
Four compounding deviations make SimHost a "driving-only" shell: it cannot see enemies, shoot,
take damage, or apply entity-specific combat parameters from the TKB.*

### 11.1 Missing Project References

`Hrot.SimHost.csproj` references `FDP.Toolkit.Physics`, `FDP.Toolkit.Behavior`,
`FDP.Toolkit.Navigation`, and `FDP.Toolkit.CarKinem`, but is **missing**:

```xml
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Perception\FDP.Toolkit.Perception.csproj" />
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Combat\FDP.Toolkit.Combat.csproj" />
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Combat.Contracts\FDP.Toolkit.Combat.Contracts.csproj" />
```

Without these, the types `PerceptionReceptor`, `TargetMemory`, `WeaponState`, `Health`,
`BallisticProjectile`, `Faction` are unavailable at compile time in the SimHost assembly.
**Fix:** S17T1.

### 11.2 Missing ECS Component Registrations

`SimHostApp.RegisterSimComponents()` registers locomotion, BTree state, and vehicle kinematics.
It is missing every Perception and Combat component listed in `HeadlessDemoApp.RegisterComponents()`:

```csharp
// Currently ABSENT from SimHostApp.RegisterSimComponents():
world.RegisterComponent<Faction>();
world.RegisterComponent<PerceptionReceptor>();
world.RegisterComponent<TargetMemory>();
world.RegisterComponent<PhysicsCollider>();
world.RegisterComponent<WeaponState>();
world.RegisterComponent<Health>();
world.RegisterComponent<BallisticProjectile>();
world.RegisterComponent<BrainHsm64>();
world.RegisterComponent<BrainHsm128>();
```

Any entity that has these components in its TKB template will panic the ECS kernel with
"unregistered component type" at spawn time once S17T5 attaches them to templates.
**Fix:** S17T2 — apply all registrations before S17T5.

### 11.3 Missing `RaycastBatchData` Singleton

`RaycastSolverSystem` and `LosRequestBatchingSystem` require a pre-allocated
`RaycastBatchData` singleton (two `NativeArray`s). `UrbanCombat` allocates it via:

```csharp
// HeadlessDemoApp.Initialize():
using var physics = new PhysicsToolkitModule();
physics.Initialize(World);   // allocates RaycastBatchData singleton
```

`SimHostApp.OnLoad()` never calls `PhysicsToolkitModule.Initialize()`. If `RaycastSolverSystem`
is registered (S17T4) without this call, it will throw a `SingletonNotFoundException` on its
first `OnUpdate` tick.
**Fix:** S17T3.

### 11.4 TKB Templates Are Hollow — `SimCombatDef` Is a UI Proxy

`BdcTkbBuilder.WithCombat()` stores a **managed class** (`SimCombatDef`) on the template. This
class carries armour thickness, weapon names, and sensor range as properties for IG display.
However, it **does not call `template.AddComponent()`** for any unmanaged FDP toolkit struct.
When `template.ApplyTo(world, entity)` is called at spawn time, the entity receives no
`WeaponState`, no `PerceptionReceptor`, no `Health`, and no `Faction`. The AI sees nothing,
shoots nothing, and cannot take damage.

**Golden standard (`DemoTkbSetup.cs`):**
```csharp
// InfantrySoldier template (excerpt from DemoTkbSetup.RegisterInfantrySoldier)
t.AddComponent(new WeaponState {
    Ammo                   = UrbanCombatConstants.RifleAmmo,
    MuzzleVelocity         = UrbanCombatConstants.RifleMuzzleVelocity,
    CooldownTicksRemaining = 0
});
t.AddComponent(new PerceptionReceptor {
    VisionRange    = UrbanCombatConstants.SoldierVisionRange,
    HearingRange   = UrbanCombatConstants.SoldierHearingRange,
    FieldOfViewCos = 0f
});
t.AddComponent(new TargetMemory());
t.AddComponent(new PhysicsCollider {
    Radius         = UrbanCombatConstants.HumanoidColliderRadius,
    CollisionLayer = PhysicsConstants.EntityCollisionLayer
});
t.AddComponent(new Health { Current = 100, Max = 100 });
t.AddComponent(new Faction { FactionId = UrbanCombatConstants.FactionBlue });
```

**Fix (S17T5):** Rewrite `BdcTkbBuilder.WithCombat()` to translate `SimCombatDef` fields into
real ECS components via `template.AddComponent()`. The `SimCombatDef` managed component is
retained on the template so IG can still query it for ORBAT/inspector display.

### 11.5 Missing Systems in the Pipeline

`SimulationLogicModule` currently runs:
1. `MissionAdapterSystem` / `MissionDirectorSystem` (Post Phase 16)
2. `ChannelArbitrationSystem` → `BTreeTickSystem` → `LocomotionDispatcherSystem`
3. `SpatialHashSystem` → `FormationTargetSystem` → `VehicleCommandSystem`
4. `CarKinematicsSystem` → `LinearKinematicsSystem`

`UrbanCombat` (`HeadlessDemoApp.RegisterSystems()`) additionally runs:

| System | Phase | Purpose |
|--------|-------|---------|
| `FireProcessingSystem` | Input | Spawns `BallisticProjectile` entities when `WeaponChannel` fires |
| `RaycastSolverSystem` | Input | Batch-resolves LOS / bullet hit raycasts |
| `HitResolutionSystem` | Input | Converts raw hit results to `TargetVisibleEvent` / `HitEvent` |
| `WeaponDispatcherSystem` | Sim | Routes `AimAndFire` `WeaponChannel` actions to `AimAndFireExecutor` |
| `VisionBroadphaseSystem` | Sim | Checks FOV cones; emits `TargetVisibleEvent` |
| `LosRequestBatchingSystem` | Sim | Batches LOS checks into `RaycastBatchData` |
| `ThreatEvaluationSystem` | Sim | Updates `TargetMemory` scores from events |
| `DamageSystem` | Sim | Subtracts from `Health` on `HitEvent` |
| `HsmDamageBridgeSystem` | Sim | Propagates health changes to HSM capability state |
| `HsmTickSystem<BrainHsm128>` | Sim | Ticks HSM brains (needed for APC-type doctrines) |
| `BallisticsSystem` | PostSim | Moves `BallisticProjectile` entities each frame |

Without these, combat BTree actions (`Action_AimAndFire`) do nothing observable in the simulation.
**Fix:** S17T4.

After Phase 17, the full mission execution chain is:

```
DDS EntityMission  ──►  EntityMissionTranslator  ──►  MissionPlanQueue (ECS)
                                                              │
                                                   MissionDirectorSystem
                                                  (evaluates MissionTrigger)
                                                              │
                                                   DoctrineState.ActiveDoctrineHash
                                                              │
                                       ChannelArbitrationSystem ──► BTreeTickSystem
                                                              │
                                                   LocomotionChannel.ActiveAction
                                                              │
                                       LocomotionDispatcherSystem ──► MoveToExecutor
                                                              │
                                                   SimTransform (vehicle moves)

                    ┌──────────────────────────────────────────────────────────┐
                    │        Phase 17: Full combat loop (post-S17T4)           │
                    │                                                          │
                    │  PerceptionReceptor/TargetMemory                         │
                    │        │                                                 │
                    │  VisionBroadphaseSystem → ThreatEvaluationSystem         │
                    │        │                                                 │
                    │  WeaponChannel.ActiveAction = AimAndFire                 │
                    │        │                                                 │
                    │  WeaponDispatcherSystem → AimAndFireExecutor             │
                    │        │                                                 │
                    │  FireProcessingSystem → BallisticProjectile entity       │
                    │        │                                                 │
                    │  RaycastSolverSystem → HitResolutionSystem → HitEvent    │
                    │        │                                                 │
                    │  DamageSystem → Health decremented                       │
                    └──────────────────────────────────────────────────────────┘
```

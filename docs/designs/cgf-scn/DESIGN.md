# Design: CGF Scenario Loading via Genesis Pipeline

## Context

Hrot.Editor saves scenario files in a subsystem-typed JSON format.  Currently
the CGF node participates in the `PrepareLive` handshake as a header-peek-only
observer (`ReferenceScenarioLoadHandler(world: null)`), while the SimHost loads
entities directly via `HrotScenarioLoadHandler` → `ScenarioSerializer.Deserialize`.

This design changes CGF from a passive observer into the *authoritative entity
genesis source* for scenario loading.  Entity state from the scenario file is
routed through the existing `CreateEntityRequestSystem`→`NetworkSpawningSystem`
pipeline, giving every spawned entity a fresh network identity, correct split
authority, and the full ELM reliable-init handshake — without any special-casing
in the spawning code.

The same pipeline is applied to episode loading (micro-scenarios injected into
a live exercise), fixing the existing architectural defects in
`ReferenceEpisodeLoadHandler`.

A related, self-contained outcome of this workstream is a generic, DTO-driven
mission editor UI that replaces the hardcoded `DrawXxx` methods in `MissionPanel`
using the same strongly-typed DTOs introduced for JSON parameter remapping.

---

## Architectural Decisions

### Decision 1 — CGF is the single source of entity genesis

Loading a scenario in parallel on every node (CGF + SimHost each deserializing
the same file) violates the Single Source of Truth for entity genesis.  It
bypasses `DdsIdAllocator`-managed network ID allocation, breaks the ELM reliable
init handshake, and would require brittle post-load hacking of ECS authority
masks.

CGF is the designated default-processor node (`isDefaultProcessor = true`).
All entity creation in the distributed cluster flows through CGF's
`CreateEntityRequestSystem`.  Scenario loading follows the same path.

### Decision 2 — In-memory request source, not DDS loopback

Generating a DDS `CreateEntityRequest` from CGF just so CGF reads it back is an
anti-pattern.  Instead, an in-memory `IEntityCreationRequestSource` backed by a
thread-safe queue is multiplexed with the live `NedEntityCreationRequestSource`
so `CreateEntityRequestSystem` processes scenario requests identically to network
requests.

### Decision 3 — Staging EntityRepository for component extraction

Deserializing the JSON DOM directly into a `List<object>` would require a
parallel, reflection-heavy pipeline and risks schema drift.  The existing
`FdpAutoSerializer` and `IEntityScenarioTranslator` implementations strictly
require an `EntityRepository`.  A transient staging repository is hydrated, all
component data is extracted from it, and it is then disposed.

### Decision 4 — BitMask256 exclusion mask, not attributes

Components such as `NetworkIdentity`, `NetworkAuthority`, and
`LifecycleDescriptor` must be excluded from `InitialComponents` because the
`NetworkSpawningSystem` synthesizes them from scratch.  However, these components
must NOT be marked globally as non-saveable; they are required by the Checkpoint
pipeline.  The exclusion mask is a context-specific `BitMask256` owned by the
scenario extraction service.

`DescriptorOwnership` (ID 59) must also be in the exclusion mask.
`CreateEntityRequestSystem` determines split-authority routing dynamically and
publishes it via `DeferredTakeOwnershipCommand`.  A stale `DescriptorOwnership`
extracted from the staging repo would be applied by `NetworkSpawningSystem`,
silently overwriting the cluster's live authority layout and breaking Muscle-node
physics takeover.

### Decision 5 — Root-entity-only extraction

The scenario DOM is a flat snapshot that includes both root entities and their
TKB-structural child entities (e.g., tank hull + turret).  `CreateEntityRequestSystem`
already reads `parentTemplate.ChildBlueprints` and spawns children automatically
when a root entity is spawned.  Injecting child entities from the staging
repository would double-spawn them.  Only root entities are extracted.

TKB structural child entities are identified by the presence of the `PartMetadata`
component (`GlobalComponentIds.PartMetadata`, ID 55).  This component is attached
to sub-part entities by `NetworkSpawningSystem` and is persisted in the scenario
DOM.  An entity that has `PartMetadata` is a blueprint child; it must be skipped.

`EntityInfo.CommanderId` must NOT be used as the child-detection criterion.
`CommanderId` tracks the tactical Order of Battle chain of command (e.g., a
subordinate vehicle assigned to a platoon leader).  Filtering on `CommanderId == 0`
would silently drop every subordinate military unit from the scenario, instantiating
only the highest-echelon commanders.

Filtering child entities prevents double-spawning, but it silently discards any
operator-authored overrides on those children (e.g., a modified ammo count in a
turret's `WeaponState`, an adjusted rotation offset).  These authored values live on
the child entity in the staging DOM; dropping the entity means the live cluster spawns
children from TKB template factory defaults only.

To preserve authored child state, `StagingEntityExtractor` harvests non-excluded
components from each child entity and packs them into the parent's
`EntityCreationRequest.ChildComponentOverrides` dictionary, keyed by
`PartMetadata.InstanceId`.  `CreateEntityRequestSystem`, which already iterates
`childDef.InstanceId` when generating child `SpawnEntityCommand` events, merges
these overrides into each child's `InitialComponents` before publishing the child's
command.  No change to `NetworkSpawningSystem` is required.

`PartMetadata` itself is excluded from child overrides: its `ParentEntity` field is
a volatile ECS handle valid only within the staging repository.  `NetworkSpawningSystem`
sets the correct live `ParentEntity` when it spawns the child.

### Decision 6 — Two-pass network ID remapping

Behavior mission tasks store entity references as `long`/`int` network IDs
embedded in opaque `BehaviorParams` JSON strings (e.g., `targetNetworkId` in
`FireAtTarget`, `routeEntityId` in `FollowRoute`).  Because these IDs are
reallocated during genesis, a two-pass process is required:

- **Pass 1 (Allocation & Mapping):** Pre-allocate new network IDs for every
  entity that has a `NetworkIdentity` in the staging repository.  Build a
  `Dictionary<long, long> oldToNewNetworkId` translation map.
- **Pass 2 (Extraction & Patching):** Extract components, apply the exclusion
  mask, and remap any network IDs embedded in behavior JSON strings using the
  translation map.

The Pass 1 ID pre-allocated for each entity is the definitive network ID: the
remapped behavior JSON patches reference it, and the live cluster must materialise
the entity with exactly this ID.  Each `EntityCreationRequest` therefore carries a
`PreAllocatedNetworkId` field (default `0`).  `CreateEntityRequestSystem` checks
this field during request ingress: if non-zero, it uses the pre-allocated value
directly and bypasses its internal `idAllocator.AllocateId()` call.  Normal
(NED/BDC-originated) requests leave `PreAllocatedNetworkId` at zero, so the
existing allocation path is preserved unchanged.

### Decision 7 — DTO-based JSON remapping with compiled delegates

Manual `JsonDocument` parsing with magic property-name strings is brittle and
prone to schema drift.  Strongly-typed DTO classes decorated with
`[RemapNetworkId]` attributes, combined with expression-tree-compiled remapper
delegates registered per `BehaviorId`, make the remapping contract explicit and
schema-safe.

The same DTO types serve as the basis for the generic mission editor UI,
providing a single schema definition that drives both the loading pipeline and
the ImGui rendering layer.

### Decision 8 — Episode loading uses the same staging pipeline

`ReferenceEpisodeLoadHandler.CommitStartEpisode` currently calls
`_serializer.Deserialize(targetRepo, json, asEpisode: true, episodeId: ...)` into
the live `EntityRepository`.  This bypasses the genesis pipeline, generates
network ID collisions in a running exercise, and injects stale network state.

A new `CgfEpisodeLoadHandler` in `Hrot.CGF` replaces the existing handler on
the CGF node.  It uses the same staging extraction pipeline as scenario loading
but additionally appends an `EpisodeTag` component to each entity's
`InitialComponents`.  The `CgfEpisodeLoadHandler` handles `StopEpisode` by
retaining the `EpisodeTag`-based cleanup logic from `ReferenceEpisodeLoadHandler`.

### Decision 9 — Project placement and dependency boundaries

`IEntityCreationRequestSource` and `EntityCreationRequest` currently live in
`Hrot.Core.Network`.  The new in-memory and composite source implementations
also live in `Hrot.Core.Network`.  The scenario extraction service and CGF
handlers live in `Hrot.CGF` (which already references `Hrot.Core` via the
`Hrot.SimHost` dependency chain).

`Fdp.Toolkit.Orchestration` (which contains `ReferenceEpisodeLoadHandler`) must
NOT be modified to reference `Hrot.Core.Network` — that would invert the
dependency hierarchy.  The Hrot-specific episode handler is therefore a new type
in `Hrot.CGF`, not a modification to the toolkit handler.  `ReferenceEpisodeLoadHandler`
remains unchanged as the episode handler for non-distributed (editor) scenarios;
the CGF registration is swapped to `CgfEpisodeLoadHandler`.

### Decision 10 — Translator-handled components excluded to prevent volatile ECS handle leakage

Components such as `TargetMemory` and `PassengerBuffer` store raw `Entity` handles
(volatile ECS index + generation) that were resolved by `IEntityScenarioTranslator`
implementations via `IGuidResolver` during `ScenarioSerializer.Deserialize`.
These handles are valid only within the transient staging `EntityRepository`.
When the staging repo is disposed, those handles become dangling references.

Extracting them via `IComponentTable.GetRawObject` and injecting them into
`InitialComponents` would cause `NetworkSpawningSystem` to apply stale Entity
handles into the live world — pointing to garbage memory or random live entities,
causing catastrophic state corruption.

Mitigation: the union of all `IEntityScenarioTranslator.GetConsumedComponentsMask()`
bitmasks registered with the `ScenarioSerializer` must be ORed into the static
exclusion mask in `StagingEntityExtractor`.  This prevents any translator-handled
component from reaching the live world with stale handles.

The resulting functional regression must be clearly acknowledged:

- Discarding `PassengerBuffer` means any troops authored as embarked inside a
  carrier vehicle in the scenario editor will either spawn outside the vehicle or
  be permanently untracked — a severe authoring fidelity loss.
- Discarding `TargetMemory` means pre-assigned targeting is lost; all targeting
  systems start from a clean slate regardless of the authored scenario state.

This is accepted as a known limitation for this workstream's initial delivery.
The two architectural paths to resolution are:

1. **Schema Elevation:** Refactor `PassengerBuffer` and `TargetMemory` to store
   stable 64-bit `long` NetworkIdentity values instead of volatile `Entity` handles,
   aligning them with how `ActiveMissionPlan` behavior JSON parameters handle cross-
   entity references.  Once network-ID-based, these components follow the same
   `oldToNewMap` remapping pass defined in Decision 6.  The `IEntityScenarioTranslator`
   mechanism becomes unnecessary for them.
2. **Post-Genesis Reconciliation:** The scenario loader retains the raw translator
   component data (keyed by staging-repo NetworkIdentity) after extraction.  A
   deferred pass triggered after `NetworkSpawningSystem` confirms all entities are
   live uses the `oldToNewMap` to translate staging `Entity` handles to live handles
   and injects the components.  This requires a new reconciliation hook in the genesis
   pipeline.

Neither path is in scope for this workstream.  Both constitute non-trivial
architectural debt and must be formally tracked.

### Decision 11 — EntityCreationRequest DTO extensions for genesis-specific fields

The `EntityCreationRequest` DTO in `Hrot.Core.Network` receives two new optional
fields that support the staging pipeline without requiring any change to existing
callers:

- **`PreAllocatedNetworkId` (`long`, default `0`):** the network ID pre-allocated
  during Pass 1 of `StagingEntityExtractor`.  When non-zero, `CreateEntityRequestSystem`
  uses this value as the entity's network ID and skips its internal
  `idAllocator.AllocateId()` call.  All existing callers (`NedEntityCreationRequestSource`,
  `BdcEntityCreationRequestSource`) produce requests with `PreAllocatedNetworkId = 0`,
  so the existing allocation behaviour is fully preserved.
- **`ChildComponentOverrides` (`IReadOnlyDictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>?`,
  keyed by `PartMetadata.InstanceId`):** authored component overrides and pre-allocated
  network IDs for the blueprint children auto-generated by `CreateEntityRequestSystem`.
  Blueprint children in the staging DOM carry a `NetworkIdentity`; their new IDs are
  allocated during Pass 1 of `StagingEntityExtractor` and written into the tuple's
  `PreAllocatedId` field alongside their extracted components.  `CreateEntityRequestSystem`
  reads this field when iterating `parentTemplate.ChildBlueprints`: if the entry for
  `childDef.InstanceId` is present, it uses the tuple's `PreAllocatedId` for the child's
  `SpawnEntityCommand.NetworkId` (bypassing `AllocateId()`) and merges `Components` into
  the child's `InitialComponents`.  This ensures that behavior JSON parameters patched
  with child network IDs in Pass 2 always match the IDs that `NetworkSpawningSystem`
  materialises.  `NetworkSpawningSystem` itself is unchanged.
  Children without a `NetworkIdentity` in the staging DOM are assigned
  `PreAllocatedId = 0`; `CreateEntityRequestSystem` falls through to `AllocateId()` as normal.

Both fields default to `0` / `null`, making the extension fully backward-compatible
with all existing `EntityCreationRequest` construction sites.

---

## Phase 1 — Entity Creation Source Infrastructure

**Goal:** Replace the single `NedEntityCreationRequestSource` wired into
`CreateEntityRequestSystem` with a multiplexed composite that can also drain an
in-memory queue.  No scenario-loading logic yet — this phase only extends the
request ingestion pathway.

### Task C001: ScenarioEntityCreationRequestSource

An in-memory, thread-safe `IEntityCreationRequestSource` backed by a
`ConcurrentQueue<EntityCreationRequest>`.

- Provided as a shared service to both the CGF load handlers and `CgfLogicPack`.
- `Enqueue(EntityCreationRequest)` — called from the orchestration/load-handler
  thread during scenario or episode commit.
- `ProcessRequests(Action<EntityCreationRequest> handler)` — called on the ECS
  tick thread by `CreateEntityRequestSystem`; drains the queue up to a
  configurable maximum per tick.

### Task C002: CompositeEntityCreationRequestSource

An `IEntityCreationRequestSource` that wraps an ordered list of inner sources
and drains all of them in `ProcessRequests`.

- `NedEntityCreationRequestSource` is the first inner source; it is always drained.
- `ScenarioEntityCreationRequestSource` is the second inner source.
- The composite calls each inner source's `ProcessRequests` in order.

### Task C003: Wire composite source into CgfLogicPack

`CgfLogicPack` currently constructs `CreateEntityRequestSystem` with a
`NedEntityCreationRequestSource`.  This task replaces that with the
`CompositeEntityCreationRequestSource`.

- `ScenarioEntityCreationRequestSource` is constructed once and shared between
  `CgfLogicPack` (hands it to the composite) and `CgfApplication` (hands it to
  the load handlers constructed in Phase 3 and 4).
- No behavior changes to the live NED path.

---

## Phase 2 — Staging Entity Extractor

**Goal:** Implement the reusable service that converts scenario/episode JSON into
a list of `EntityCreationRequest` objects by using a transient staging
`EntityRepository`.

### Task C004: StagingEntityExtractor

A stateless service in `Hrot.CGF` (or `Hrot.Core.Scenario`) with signature:

```
IReadOnlyList<EntityCreationRequest> Extract(
    ScenarioSerializer serializer,
    string scenarioJson,
    INetworkIdAllocator idAllocator,
    ScenarioBehaviorRemapper? behaviorRemapper = null,
    Guid? episodeId = null)
```

Responsibilities:

1. **Hydrate staging repo** — Instantiate a transient `EntityRepository` with
   the component tables required by the scenario serializer.  Call
   `serializer.Deserialize(stagingRepo, json)`.

2. **Build exclusion mask** — A static `BitMask256` constructed once at class
   initialization using `GlobalComponentIds` constants:
   - `LifecycleDescriptor` (5)
   - `DescriptorOwnership` (59) — must be freshly computed by `CreateEntityRequestSystem`
   - `NetworkIdentity` (50)
   - `NetworkAuthority` (51)
   - `TkbIdentity` (65) — freshly assigned by `NetworkSpawningSystem`
   - `GhostStateTracker` (66)
   - `NetworkOwnership` (140)
   - `PendingNetworkAck` (141)

   Additionally, OR the union of `IEntityScenarioTranslator.GetConsumedComponentsMask()`
   for every translator registered with the `ScenarioSerializer` into the same
   mask.  These translator-handled component types contain volatile `Entity`
   handles that become stale once the staging repo is disposed (see Decision 10).

3. **Pass 1 — ID allocation:** Query staging repo for **all** entities that have a
   `NetworkIdentity` component — this includes both root entities and their blueprint
   children (which also carry `NetworkIdentity` in the saved scenario DOM).  Call
   `idAllocator.AllocateId()` for each one.  Record the mapping in a local
   `Dictionary<long, long>` (`oldId → newId`).

4. **Pass 2 — Root entity extraction:**
   - Query all entities in the staging repo.  Skip any entity that has a
     `PartMetadata` component (ID 55) — these are TKB structural blueprint
     children (e.g., turret, door).  Do NOT use `EntityInfo.CommanderId` to
     identify children; that field tracks ORBAT command hierarchy, not structural
     blueprint parentage.
   - For each non-child entity, iterate `stagingRepo.GetRegisteredComponentTypes()`
     (returns `IReadOnlyDictionary<Type, IComponentTable>`).  For each table
     whose `ComponentTypeId` is NOT set in the exclusion mask AND the entity's
     `ComponentMask` IS set for that ID, call `table.GetRawObject(entity.Index)`
     and add to `initialComponents`.
   - If `episodeId.HasValue`, append `new EpisodeTag { EpisodeId = episodeId.Value }`
     to `initialComponents` (this component has `[DataPolicy.NoSave]` so it is
     never in the staging repo itself).
   - If `behaviorRemapper != null`, intercept any `ActiveMissionPlan` component
     in `initialComponents` and remap its task `BehaviorParams` JSON strings
     using `ScenarioBehaviorRemapper.RemapJson(behaviorId, json, oldToNewMap)`.
   - Read `TkbType` from staging entity's `TkbIdentity` (if present, else 0).
   - Read `DisType` from `stagingRepo.GetEntityHeader(entity.Index).DisType.Value`.
   - In the same entity loop, entities WITH `PartMetadata` are processed separately:
     extract their non-excluded components using the same exclusion mask, additionally
     excluding `PartMetadata` (ID 55) itself.  Read the child entity's
     `NetworkIdentity.Value` before applying the exclusion mask; look it up in
     `oldToNewMap` to obtain the child's pre-allocated network ID (`0` if absent).
     Buffer these overrides in a local `Dictionary` keyed by
     `(PartMetadata.ParentEntity, PartMetadata.InstanceId)` with value
     `(preAllocatedChildId, extractedComponents)`.
     `PartMetadata.ParentEntity` is a valid staging-repo ECS handle at this point.
   - After iterating all entities, associate each child override set with its parent
     root entity's `EntityCreationRequest` as `ChildComponentOverrides`, keyed by
     `PartMetadata.InstanceId`.
   - Read `preAllocatedId` from `oldToNewMap` using the entity's `NetworkIdentity.Value`
     (old staging ID) as the key; `0` if the entity has no `NetworkIdentity`.  The
     `NetworkIdentity` component itself remains excluded from `InitialComponents`.
   - Build `EntityCreationRequest { RequestId = Guid.NewGuid(), OwnerAppInstanceId = 0,
     TkbType, DisType, InitialComponents = initialComponents,
     PreAllocatedNetworkId = preAllocatedId,
     ChildComponentOverrides = harvested child overrides as
     Dictionary<int, (long, IReadOnlyList<object>)> (or null if none) }`.

5. **Dispose** the staging `EntityRepository`.

### Task C005: Behavior Param Remapping

Define the remapping infrastructure used by `StagingEntityExtractor` to patch
network IDs embedded in behavior JSON strings.

#### C005a — RemapNetworkIdAttribute

```csharp
// Location: Fdp.Toolkits/Behavior/Attributes/RemapNetworkIdAttribute.cs
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class RemapNetworkIdAttribute : Attribute { }
```

#### C005b — Behavior DTOs

Place in `Fdp.Toolkits/Behavior/Params/`:

```
FireAtTargetParamsJsonDto  { [RemapNetworkId] long TargetNetworkId; int MaxRounds; float CooldownSeconds; }
FollowRouteParamsJsonDto   { [RemapNetworkId] long RouteEntityId;   float Speed;   bool Loop; }
```

Note: `routeEntityId` is currently stored as `int` in the JSON (see
`MissionPanel.BuildFollowRouteParams`).  The DTO uses `long` for architectural
consistency with `targetNetworkId`.  The `[RemapNetworkId]` attribute must handle
both `int` and `long` properties in `BehaviorParamRemapperCompiler`.

#### C005c — BehaviorParamRemapperCompiler

Expression-tree compiler that produces a `ParamRemapDelegate`:

```csharp
public delegate string ParamRemapDelegate(string json, IReadOnlyDictionary<long, long> idMap);
```

Compile steps at startup (cold path):
1. Reflect DTO type for all properties/fields with `[RemapNetworkId]`.
2. Build an `Action<TDto, IReadOnlyDictionary<long, long>>` via
   `Expression.Lambda` that does `if (map.TryGetValue(field.Value, out newId)) field.Value = newId`
   for each `[RemapNetworkId]` member.
3. Wrap in a lambda that deserializes JSON → calls the mutator → reserializes.
4. Pre-compiled delegates have no heap allocations on the warm path beyond the
   JSON deserialize/serialize round-trip (acceptable: scenario load is an I/O
   boundary operation).

Both `int` and `long` fields decorated with `[RemapNetworkId]` must be handled
(safe widening from `long` → value is applied back as `int` after clamping).

#### C005d — ScenarioBehaviorRemapper

Registry mapping `string BehaviorId → ParamRemapDelegate`.

```csharp
public sealed class ScenarioBehaviorRemapper
{
    public void Register<TDto>(string behaviorId) where TDto : class;
    public string RemapJson(string behaviorId, string json, IReadOnlyDictionary<long, long> idMap);
}
```

`Register<TDto>` calls `BehaviorParamRemapperCompiler.Compile<TDto>()` and stores
the result.  Unrecognized `behaviorId` values pass through unchanged.

Registration at composition time (alongside `BehaviorRegistry.Register`):

```
remapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget")
remapper.Register<FollowRouteParamsJsonDto>("FollowRoute")
```

### Task C013: EntityCreationRequest Extension and CreateEntityRequestSystem Genesis Gateway

Modifies two existing production types to support the genesis-specific fields
introduced by `StagingEntityExtractor` (see Decision 11).

#### C013a — EntityCreationRequest DTO

Add two new `init`-only properties to `EntityCreationRequest` in
`Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs`:

- `long PreAllocatedNetworkId { get; init; }` — defaults to `0L`.
- `IReadOnlyDictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>? ChildComponentOverrides { get; init; }` — defaults to `null`.

No existing property is changed.  All existing construction sites remain valid.

#### C013b — CreateEntityRequestSystem: pre-allocation gate

In `ProcessIncomingRequest`, after TKB validation, replace the unconditional
`_idAllocator.AllocateId()` call:

```csharp
long newNetworkId = request.PreAllocatedNetworkId != 0
    ? request.PreAllocatedNetworkId
    : _idAllocator.AllocateId();
```

IDs are drawn from the same monotonically-increasing `INetworkIdAllocator`; a
pre-allocated ID will not collide with any ID allocated later by the same allocator.

#### C013c — CreateEntityRequestSystem: child override merge and ID gate

In `ProcessPendingRequest`, inside the `foreach (var childDef in
parentTemplate.ChildBlueprints)` loop, replace the unconditional
`long childNetworkId = _idAllocator.AllocateId()` with:

```csharp
long childNetworkId;
if (pending.Request.ChildComponentOverrides?.TryGetValue(childDef.InstanceId, out var entry) == true)
{
    childNetworkId = entry.PreAllocatedId != 0 ? entry.PreAllocatedId : _idAllocator.AllocateId();
    childComponents.AddRange(entry.Components);
}
else
{
    childNetworkId = _idAllocator.AllocateId();
}
```

This change ensures the child's `SpawnEntityCommand.NetworkId` matches the ID that
was patched into the parent's behavior JSON during Pass 2.  NED/BDC-originated
requests always have `ChildComponentOverrides = null`, so the existing
`AllocateId()` path is exercised as before.

`NetworkSpawningSystem` consumes the enriched `SpawnEntityCommand.InitialComponents`
unchanged; no modification to that system is required.

---

## Phase 3 — CGF Scenario Load Handler

**Goal:** Replace the header-peek-only `ReferenceScenarioLoadHandler(world:null)`
on the CGF node with a handler that injects scenario entities through the genesis
pipeline.

### Task C006: CgfScenarioLoadHandler

Location: `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfScenarioLoadHandler.cs`

Implements `IClusterStateHandler`.  Claims `NodeOpType.PrepareLive`.

Constructor dependencies:
- `ScenarioSerializer scenarioSerializer`
- `IScenarioLoader scenarioLoader`
- `INetworkIdAllocator idAllocator`
- `ScenarioEntityCreationRequestSource requestQueue`
- `StagingEntityExtractor extractor`
- `ScenarioBehaviorRemapper? behaviorRemapper = null`

Lifecycle:
- **`PrepareAsync`** — call `scenarioLoader.TryLoadScenarioJson(scenarioId)`, stash
  the JSON string and transaction ID.
- **`Commit`** — call `extractor.Extract(serializer, pendingJson, idAllocator,
  behaviorRemapper)` and enqueue every resulting `EntityCreationRequest` into
  `requestQueue`.  The `CreateEntityRequestSystem` drains the queue during
  subsequent ECS ticks.
- **`Abort`** — clear the pending JSON.

Registration in `CgfApplication`: Replace
`new ReferenceScenarioLoadHandler(serializer, loader, world: null)`
with `new CgfScenarioLoadHandler(serializer, loader, idAllocator, requestQueue,
extractor, behaviorRemapper)` when a scenario serializer is configured.

---

## Phase 4 — CGF Episode Load Handler

**Goal:** Fix the architectural defects in episode loading (`ReferenceEpisodeLoadHandler`
directly deserializing into a live world) by replacing it on the CGF node with a
handler that uses the same staging pipeline.

### Task C007: CgfEpisodeLoadHandler

Location: `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/CgfEpisodeLoadHandler.cs`

Implements `IClusterStateHandler`.  Claims `NodeOpType.StartEpisode` and
`NodeOpType.StopEpisode`.

Constructor dependencies:
- `ScenarioSerializer scenarioSerializer`
- `IScenarioLoader scenarioLoader`
- `INetworkIdAllocator idAllocator`
- `ScenarioEntityCreationRequestSource requestQueue`
- `StagingEntityExtractor extractor`
- `EntityRepository world` — needed for `StopEpisode` (entity cleanup)
- `ScenarioBehaviorRemapper? behaviorRemapper = null`

`StartEpisode` path (mirrors `ReferenceEpisodeLoadHandler` but uses staging):
- `PrepareAsync`: load episode JSON, parse the `EpisodeHandlerPayload`, stash
  `(json, episodeId)`.
- `Commit`: call `extractor.Extract(..., episodeId: pendingEpisodeId)`.  This
  causes `EpisodeTag { EpisodeId = pendingEpisodeId }` to be appended to every
  entity's `InitialComponents`.  Enqueue requests into `requestQueue`.

`StopEpisode` path:
- Query all entities in `world` whose `EpisodeTag.EpisodeId` matches the
  active episode GUID.
- For each matching entity, read its `NetworkIdentity.Value` and publish a
  `DestroyEntityCommand` to the local event bus.
- The existing `NetworkSpawningSystem` / delete pipeline drives distributed
  teardown: each `DestroyEntityCommand` triggers the egress translators to
  broadcast a `DeleteEntityRequest` to the cluster, ensuring SimHost and IG
  nodes clean up their ghosts.  Direct `EntityRepository.DestroyEntity` calls
  must NOT be used; they bypass the event bus and cause permanent ghost leaks
  on remote nodes.

Registration in `CgfApplication`: Replace
`new ReferenceEpisodeLoadHandler(serializer, loader, world: null)`
with `new CgfEpisodeLoadHandler(serializer, loader, idAllocator, requestQueue,
extractor, world, behaviorRemapper)`.

**SimHost coordination:** When this handler is deployed, the SimHost node's
`ReferenceEpisodeLoadHandler` registration must be changed to `world: null`
(header-peek-only).  Episode entities will arrive at SimHost via the normal
DDS ghost-to-active promotion path originating from CGF's genesis pipeline.
Leaving the SimHost handler with a real world would cause every episode entity
to be instantiated twice — once via DDS and once via direct deserialization.

---

## Phase 5 — Generic Mission Editor UI

**Goal:** Replace the hardcoded `DrawFireAtTargetParams`, `DrawMoveToLocationParams`,
and `DrawFollowRouteParams` methods in `MissionPanel` with a generic,
DTO-attribute-driven rendering mechanism that works for any registered behavior.

This phase builds on Phase 2's DTOs and extends them with presentation metadata
attributes.

### Task C008: Presentation Attributes

Location: `Fdp.Toolkits/Behavior/Attributes/` (same assembly as `RemapNetworkIdAttribute`).

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class MapPickableWorldLocationAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class MapPickableEntityAttribute : Attribute
{
    public string[]? FilterPresets { get; }
    public MapPickableEntityAttribute(params string[] filterPresets) { ... }
}
```

Apply to the phase-2 DTOs:

```
FireAtTargetParamsJsonDto:
    [RemapNetworkId][MapPickableEntity]  long TargetNetworkId
    int MaxRounds
    float CooldownSeconds

FollowRouteParamsJsonDto:
    [RemapNetworkId][MapPickableEntity(PanelConstants.FilterPresetRoadGraphs)]  long RouteEntityId
    float Speed
    bool Loop

MoveToLocationParamsJsonDto:
    [MapPickableWorldLocation]  double TargetLat
    [MapPickableWorldLocation]  double TargetLon
    float Speed
    float ArrivalRadius
```

Note: `MoveToLocationParamsJsonDto` is a new DTO that does not require
`[RemapNetworkId]` and need not be registered in `ScenarioBehaviorRemapper`.
It is introduced here for completeness of the generic UI.

### Task C009: BehaviorUiCompiler

Location: `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorUiCompiler.cs`
(or `Hrot.UI.Common` if shared between IG frontend and ExCon frontend).

Compiles a `BehaviorUiDrawDelegate` per DTO type at application startup (cold path):

```csharp
public delegate string BehaviorUiDrawDelegate(
    string currentJson, int taskIndex, IPickInteractionContext context);
```

`IPickInteractionContext` is defined in the same project as `BehaviorUiCompiler`:

```csharp
public interface IPickInteractionContext
{
    bool IsPickPendingFor(int taskIndex, string propertyName);
    void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets);
    void RequestLocationPick(int taskIndex, string propertyName);
}
```

`MissionPanel` implements `IPickInteractionContext` and routes pick requests
through the existing `HandlePickEntity` / `HandlePickLocation` callbacks.
Because the context carries the `taskIndex` and `propertyName`, the panel can
track which specific field is awaiting a pick, eliminating the brittle
single-boolean state that breaks when multiple tasks have pickable fields.

Implementation:
1. Reflect DTO type at startup for all properties.
2. For each property, build a typed rendering action based on its type and
   attributes:
   - `[MapPickableEntity]`: render network ID label + "Pick" button; call
     `context.RequestEntityPick(taskIndex, propertyName, filterPresets)` on
     click; show `"[Picking...]"` when `context.IsPickPendingFor(taskIndex,
     propertyName)` is true.
   - `[MapPickableWorldLocation]`: render coordinate label + "Pick on Map" button;
     call `context.RequestLocationPick(taskIndex, propertyName)` on click.
   - `float/double`: `ImGui.InputFloat/InputDouble`.
   - `int/long`: `ImGui.InputInt` / `ImGui.InputText` (long requires text input).
   - `bool`: `ImGui.Checkbox`.
3. Build compiled `Expression`-tree getter/setter delegates (same pattern as
   `FdpAutoSerializer`) to avoid `PropertyInfo.GetValue/SetValue` reflection on
   the per-frame rendering path.
4. The delegate deserializes the JSON once per frame, renders controls, and
   returns the re-serialized JSON only if a change was made.

### Task C010: MissionPanel Integration

Refactor `MissionPanel`:
- Both copies (`Hrot.UI.Common/Panels/MissionPanel.cs` and
  `Hrot.Presentation/Panels/MissionPanel.cs`) must be reconciled — deduplicate
  into one canonical location.
- Remove `DrawFireAtTargetParams`, `DrawMoveToLocationParams`,
  `DrawFollowRouteParams` and all associated `TryParseXxx`/`BuildXxx` helpers.
- In `DrawContent`, replace `if (task.BehaviorId == "FireAtTarget") ...` chain with:
  ```csharp
  if (_uiRegistry.TryGet(task.BehaviorId, out var drawDelegate))
      newJson = drawDelegate(task.BehaviorParams ?? "", i, this /* IPickInteractionContext */);
  else
      DrawRawJsonEditor(i, ref paramsBuffer);
  ```
- Keep `DrawRawJsonEditor` as the fallback for unrecognized/future behavior types.

### Task C011: Composition Root Registration

In `CgfBehaviorSetup` (or the equivalent setup class), register DTO types for
both remapping and UI rendering:

```csharp
// Remapping (scenarios)
behaviorRemapper.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
behaviorRemapper.Register<FollowRouteParamsJsonDto>("FollowRoute");

// UI (editor)
uiRegistry.Register<FireAtTargetParamsJsonDto>("FireAtTarget");
uiRegistry.Register<FollowRouteParamsJsonDto>("FollowRoute");
uiRegistry.Register<MoveToLocationParamsJsonDto>("MoveToLocation");
```

---

## Phase 6 — SimHost Episode Handler Passive Demotion

**Goal:** Prevent the catastrophic split-brain failure that would occur if CGF's
new episode genesis pipeline is deployed while SimHost still directly deserializes
episode entities into its live `EntityRepository`.

### Task C012: Demote SimHost ReferenceEpisodeLoadHandler

Location: `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`
(or whichever method wires the cluster orchestration handlers on SimHost)

Change the `ReferenceEpisodeLoadHandler` registration from
`world: simRepo` to `world: null`.  This single argument change demotes SimHost
to a header-peek-only observer for episodes, exactly mirroring how it already
handles standard scenarios.

Episode entities reach SimHost exclusively via the DDS ghost-to-active promotion
path originating from CGF's `CgfEpisodeLoadHandler`.  The `StopEpisode` path is
also correctly passivated: with `world: null`, SimHost's handler is a no-op on
`StopEpisode`, and CGF's `CgfEpisodeLoadHandler` exclusively handles entity
destruction.

**This task MUST ship in the same release as TASK-C007.**  Deploying C007
without C012 creates a split-brain state where both CGF and SimHost materialize
the same entities simultaneously, producing duplicated ghosts, `NetworkIdentity`
collisions, and complete ELM handshake failure.

---

## Out of Scope

- **Checkpoint restore** — as confirmed in the design talk, checkpoint restoration
  is architecturally distinct from scenario/episode injection.  It requires all
  nodes to load their own node-specific binary snapshots in parallel (2PC), and the
  `NetworkEntityMap` must be rebuilt post-load by querying entities with
  `NetworkIdentity`.  This is a separate workstream.
- **Node/TypeIdMapper reconstruction** — these auto-populate via network discovery
  on the first post-load DDS exchange.  No explicit reconstruction is needed for
  scenario loading; the egress translators repopulate the caches natively.
- **SimHost scenario load changes** — SimHost's `HrotScenarioLoadHandler` (direct
  `ScenarioSerializer.Deserialize` to live world) is not modified.  Once the CGF
  drives entity genesis via the spawning pipeline, SimHost entities arrive as
  ghosts that are promoted to the active state via the existing DeferredTakeover
  handshake.

---

## Component ID Reference (Exclusion Mask)

| Component                | `GlobalComponentIds` constant | Value |
|--------------------------|-------------------------------|-------|
| `LifecycleDescriptor`    | `GlobalComponentIds.LifecycleDescriptor` | 5 |
| `DescriptorOwnership`    | `GlobalComponentIds.DescriptorOwnership` | 59 |
| `NetworkIdentity`        | `GlobalComponentIds.NetworkIdentity`     | 50 |
| `NetworkAuthority`       | `GlobalComponentIds.NetworkAuthority`    | 51 |
| `TkbIdentity`            | `GlobalComponentIds.TkbIdentity`         | 65 |
| `GhostStateTracker`      | `GlobalComponentIds.GhostStateTracker`   | 66 |
| `NetworkOwnership`       | `GlobalComponentIds.NetworkOwnership`    | 140 |
| `PendingNetworkAck`      | `GlobalComponentIds.PendingNetworkAck`   | 141 |

In addition, OR the union of `GetConsumedComponentsMask()` for every registered
`IEntityScenarioTranslator` (e.g., `TargetMemoryTranslator`, `PassengerBufferTranslator`)
into this mask at runtime when constructing `StagingEntityExtractor`.

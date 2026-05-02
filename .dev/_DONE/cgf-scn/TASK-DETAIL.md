# Task Detail

**Reference:** All tasks correspond to phases and sections in [DESIGN.md](./DESIGN.md).

---

## Phase 1 — Entity Creation Source Infrastructure

---

### TASK-C001 — ScenarioEntityCreationRequestSource

**Design Reference:** DESIGN.md § Phase 1 — Task C001

**Scope — IS included:**
- New class `ScenarioEntityCreationRequestSource` in `Hrot/Engine/Hrot.Core/Network/`
- Implements `IEntityCreationRequestSource` (same namespace, `Hrot.Core.Network`)
- Thread-safe enqueue from orchestration/load-handler thread
- Drain on ECS-tick thread via `ProcessRequests`

**Scope — NOT included:**
- Any scenario loading logic
- Modifications to existing `NedEntityCreationRequestSource`
- Integration with load handlers (Phases 3–4)

**Constraints:**
- `ProcessRequests` must be safe to call from the ECS tick thread while `Enqueue`
  is called from the orchestration thread.  Use `ConcurrentQueue<EntityCreationRequest>`.
- `ProcessRequests` must drain at most `CreateEntityRequestSystem.MaxRequestsPerTick`
  (500) items per call to prevent tick overrun.  Accept that parameter as a
  constructor argument with default 500.
- Do NOT allocate; reuse the queue's internal drain mechanism.
- Namespace: `Hrot.Core.Network`.  No references outside `FDP/Engine/Fdp.Core`
  and `Hrot/Engine/Hrot.Core`.

**Success Conditions:**

1. **Basic enqueue/drain:**
   Setup: create instance.
   Action: enqueue 3 requests on thread A; call `ProcessRequests` on thread B
   with a collecting handler.
   Assert: handler is called exactly 3 times with the correct requests in
   FIFO order.  Queue is empty after the call.

2. **Max-items-per-tick cap:**
   Setup: pre-enqueue 600 requests; cap = 500.
   Action: call `ProcessRequests` once.
   Assert: handler called exactly 500 times.
   Action: call `ProcessRequests` again.
   Assert: handler called exactly 100 times.  Queue empty.

3. **Empty queue is a no-op:**
   Action: call `ProcessRequests` on new empty instance.
   Assert: handler is never called; no exception.

4. **Concurrent safety:**
   Action: enqueue 1000 items from 4 concurrent tasks while a 5th task calls
   `ProcessRequests` in a loop.
   Assert: total items processed == 1000; no `InvalidOperationException`.

---

### TASK-C002 — CompositeEntityCreationRequestSource

**Design Reference:** DESIGN.md § Phase 1 — Task C002

**Scope — IS included:**
- New class `CompositeEntityCreationRequestSource` in `Hrot/Engine/Hrot.Core/Network/`
- Implements `IEntityCreationRequestSource`
- Wraps an ordered `IReadOnlyList<IEntityCreationRequestSource>`
- `ProcessRequests` calls each inner source's `ProcessRequests` in order

**Scope — NOT included:**
- Per-source max-item limits (handled by each inner source)
- Priority or interleaving semantics (simple sequential drain)

**Constraints:**
- Accepts the inner sources via constructor injection.  Minimum 1 inner source.
- If any inner source throws inside `ProcessRequests`, the exception propagates
  (do not swallow errors).
- Namespace: `Hrot.Core.Network`.

**Success Conditions:**

1. **Both sources drained:**
   Setup: inner source A enqueues request R1; inner source B enqueues R2 and R3.
   Action: `ProcessRequests(handler)` on composite.
   Assert: handler called with R1, R2, R3 (in that order).

2. **Empty sources are no-ops:**
   Setup: two empty sources.
   Action: `ProcessRequests`.
   Assert: handler never called; no exception.

3. **Single-source passthrough:**
   Setup: one inner source with 5 requests.
   Action: `ProcessRequests`.
   Assert: handler called 5 times.

4. **Constructor rejects empty list:**
   Action: construct with empty list.
   Assert: `ArgumentException` thrown.

---

### TASK-C003 — Wire Composite Source into CgfLogicPack

**Design Reference:** DESIGN.md § Phase 1 — Task C003

**Scope — IS included:**
- Modify `CgfLogicPack.cs` to construct `CompositeEntityCreationRequestSource`
  wrapping both `NedEntityCreationRequestSource` and the injected
  `ScenarioEntityCreationRequestSource`.
- `ScenarioEntityCreationRequestSource` instance is accepted as a constructor
  parameter of `CgfLogicPack`.
- `CgfApplication.cs` is modified to construct `ScenarioEntityCreationRequestSource`
  once and pass it to both `CgfLogicPack` and the scenario/episode load handlers
  (Phases 3 and 4).

**Scope — NOT included:**
- Changes to `CreateEntityRequestSystem` itself
- Changes to `NedEntityCreationRequestSource`
- Any scenario loading logic

**Constraints:**
- The live NED path must be completely unaffected when `ScenarioEntityCreationRequestSource`
  is empty (the typical operational state).
- `CgfLogicPack` must NOT construct `ScenarioEntityCreationRequestSource` itself;
  it must be supplied by `CgfApplication` so the same instance is shared with load handlers.
- Existing unit tests for `CgfLogicPack` must still pass; no public API change
  beyond the new constructor parameter.

**Success Conditions:**

1. **NED requests still processed:**
   Setup: use a `StubRequestSource` in place of NED source; enqueue one request.
   Action: tick the module.
   Assert: request reaches `SpawnEntityCommand`.

2. **Scenario requests processed during same tick:**
   Setup: NED source empty; scenario queue has one request.
   Action: tick the module.
   Assert: request reaches `SpawnEntityCommand`.

3. **Both sources processed in same tick:**
   Setup: NED stub has request Ra; scenario queue has request Rb.
   Action: tick.
   Assert: both Ra and Rb result in `SpawnEntityCommand` events.

4. **Null ScenarioEntityCreationRequestSource is rejected:**
   Action: construct `CgfLogicPack` with `scenarioSource: null`.
   Assert: `ArgumentNullException`.

---

## Phase 2 — Staging Entity Extractor

---

### TASK-C013 — EntityCreationRequest Extension and CreateEntityRequestSystem Genesis Gateway

**Design Reference:** DESIGN.md § Phase 2 — Task C013 (Decision 11)

**Scope — IS included:**
- Add `PreAllocatedNetworkId` (`long`) and `ChildComponentOverrides`
  (`IReadOnlyDictionary<int, (long PreAllocatedId, IReadOnlyList<object> Components)>?`)
  to `EntityCreationRequest` in
  `Hrot/Engine/Hrot.Core/Network/EntityLifecycleInterfaces.cs`
- Modify `CreateEntityRequestSystem.ProcessIncomingRequest` to use
  `PreAllocatedNetworkId` when non-zero (skip `AllocateId()`)
- Modify `CreateEntityRequestSystem.ProcessPendingRequest` to use
  `ChildComponentOverrides` entries: use `entry.PreAllocatedId` (when non-zero)
  for the child's `SpawnEntityCommand.NetworkId` instead of calling
  `AllocateId()`, and merge `entry.Components` into the child's `InitialComponents`

**Scope — NOT included:**
- Any change to `NetworkSpawningSystem`
- Any change to `NedEntityCreationRequestSource` or other existing callers
- Any change to tests that construct `EntityCreationRequest` (new fields default
  to `0`/`null`, so all existing construction sites compile unchanged)

**Constraints:**
- Both new properties must be `init`-only to preserve the DTO's immutable style.
- `PreAllocatedNetworkId = 0` is indistinguishable from a normal request; the
  gate condition is `!= 0`, not `> 0`, so ID `0` (which `INetworkIdAllocator`
  never returns) remains the sentinel.
- The pre-allocated ID is drawn from the same `INetworkIdAllocator` as normal
  requests.  Both paths share the same allocator state, so a pre-allocated ID
  will not collide with any subsequently allocated ID.
- `ChildComponentOverrides` arriving on the same tick as a large scenario load
  (hundreds of entities each with multiple children) must not cause unbounded
  allocations.  The `AddRange` call path uses the existing `childComponents`
  `List<object>`, which is already allocated per spawn loop iteration.
- When `ChildComponentOverrides` has an entry for a child but its `PreAllocatedId
  == 0`, the system falls through to `_idAllocator.AllocateId()`.  This handles
  the edge case of a child entity that had no `NetworkIdentity` in the staging
  DOM.

**Success Conditions:**

1. **Normal request unchanged — AllocateId() still called:**
   Setup: `EntityCreationRequest` with `PreAllocatedNetworkId = 0`.
   Action: tick `CreateEntityRequestSystem`.
   Assert: allocator `AllocateId()` called once; returned ID used for spawn.

2. **Pre-allocated ID bypasses AllocateId():**
   Setup: `EntityCreationRequest` with `PreAllocatedNetworkId = 5555L`;
   allocator would return a different value if called.
   Action: tick.
   Assert: `SpawnEntityCommand.NetworkId == 5555L`;
   Assert: allocator `AllocateId()` NOT called (count unchanged).

3. **Child uses pre-allocated ID and overrides merged:**
   Setup: TKB template with one child blueprint (`InstanceId = 2`).
   `EntityCreationRequest.ChildComponentOverrides = { 2: (PreAllocatedId: 9001L, Components: [WeaponStateOverride]) }`.
   Action: tick.
   Assert: child `SpawnEntityCommand.NetworkId == 9001L`.
   Assert: child `SpawnEntityCommand.InitialComponents` contains `WeaponStateOverride`.
   Assert: allocator `AllocateId()` NOT called for the child.

4. **PreAllocatedId = 0 in entry falls through to AllocateId():**
   Setup: TKB template with one child blueprint (`InstanceId = 2`).
   `ChildComponentOverrides = { 2: (PreAllocatedId: 0L, Components: []) }`.
   Action: tick.
   Assert: allocator `AllocateId()` called once for the child.

5. **Null ChildComponentOverrides — AllocateId() called for each child:**
   Setup: `EntityCreationRequest.ChildComponentOverrides = null`.
   TKB template has 2 child blueprints.
   Action: tick.
   Assert: no exception; allocator called twice (once per child).

6. **ChildComponentOverrides key not present for a child — AllocateId() called:**
   Setup: TKB template with children `InstanceId` 1 and 2.
   `ChildComponentOverrides = { 99: (9001L, []) }` (no key 1 or 2).
   Action: tick.
   Assert: both children spawned via `AllocateId()`; no exception.

---

### TASK-C004 — StagingEntityExtractor

**Design Reference:** DESIGN.md § Phase 2 — Task C004

**Scope — IS included:**
- New class `StagingEntityExtractor` in `Hrot/Subsystems/Hrot.CGF/Orchestration/`
  (or `Hrot/Engine/Hrot.Core/Scenario/` if reuse outside CGF is anticipated)
- Two-pass extraction: Pass 1 allocates new IDs; Pass 2 extracts/filters/patches
- Exclusion mask (8+ component type IDs) built as a static field, extended at
  construction time with translator-consumed masks
- Root-entity filtering by absence of `PartMetadata` component (ID 55)
- Pre-allocated network ID carry-through: each built `EntityCreationRequest` sets
  `PreAllocatedNetworkId` from the Pass 1 `oldToNewMap`
- Child component override harvesting: entities WITH `PartMetadata` have their
  non-excluded components and pre-allocated network IDs packed into the parent's
  `EntityCreationRequest.ChildComponentOverrides` dictionary as
  `(PreAllocatedId, Components)` tuples, keyed by `PartMetadata.InstanceId`
- Episode tagging via optional `Guid? episodeId` parameter
- Behavior param JSON remapping via optional `ScenarioBehaviorRemapper`
- Staging `EntityRepository` is disposed after extraction

**Scope — NOT included:**
- Cross-entity reference remapping via `IGuidResolver` for translator-handled
  components (these are excluded from extraction entirely — see Decision 10)
- Checkpoint restoration logic
- The `INetworkIdAllocator` is consumed but not owned; the caller provides it

**Constraints:**
- Must NOT retain any reference to the staging `EntityRepository` after the
  method returns.
- The static exclusion mask MUST be built using `GlobalComponentIds` named
  constants, not raw integer literals, to survive future constant renumbering.
  Baseline static entries: `LifecycleDescriptor` (5), `NetworkIdentity` (50),
  `NetworkAuthority` (51), `DescriptorOwnership` (59), `TkbIdentity` (65),
  `GhostStateTracker` (66), `NetworkOwnership` (140), `PendingNetworkAck` (141).
- **Translator component exclusion:** At construction time, OR the union of
  `IEntityScenarioTranslator.GetConsumedComponentsMask()` for every translator
  registered with the `ScenarioSerializer` into the exclusion mask instance
  (not the static field).  Translator-handled components contain volatile ECS
  `Entity` handles that become dangling once the staging repo is disposed.
  See Decision 10 in DESIGN.md.
- **Root-entity check:** an entity is a TKB structural child if and only if it
  has a `PartMetadata` component (`GlobalComponentIds.PartMetadata` = 55).  SKIP
  such entities.  Entities without `PartMetadata` are treated as root entities and
  are extracted.  Do NOT use `EntityInfo.CommanderId` for this check; that field
  tracks the ORBAT chain of command, not blueprint structure.
- `TkbType` is read from `TkbIdentity.TkbType`; if the entity has no
  `TkbIdentity`, `TkbType = 0` (entities without a TKB type will fail template
  lookup downstream — this is an acceptable fail-loud scenario).
- `DisType` is read from `stagingRepo.GetEntityHeader(entity.Index).DisType.Value`.
- Extraction order for `InitialComponents`: standard components first (from
  table iteration), `EpisodeTag` last (if `episodeId` is set).
- `ActiveMissionPlan` is a managed class (reference-type) component.  The
  reference returned by `GetRawObject` points directly into the staging
  `EntityRepository`.  **In-place mutation of `BehaviorParams` strings on this
  object is an intentional architectural exception**, permitted here because the
  staging world is transient and is disposed immediately after extraction; no
  other consumer can observe the mutation.  A deep clone of the entire DTO tree
  must NOT be performed; it is unnecessary overhead.
- **`PreAllocatedNetworkId` lookup:** Read the entity's `NetworkIdentity.Value`
  directly from the staging component table before the exclusion mask removes it
  from `InitialComponents`.  Look it up in `oldToNewMap`; the returned new ID is
  stored in `EntityCreationRequest.PreAllocatedNetworkId`.  For entities without a
  `NetworkIdentity` component, set `PreAllocatedNetworkId = 0`.
- **Child override harvesting:** In the same traversal, entities WITH `PartMetadata`
  are NOT added to the root extraction list.  Instead, their non-excluded components
  are extracted using the same exclusion mask, additionally excluding `PartMetadata`
  (ID 55) itself (its `ParentEntity` field is a volatile ECS handle valid only in
  the staging repo).  Also read the child entity's staging `NetworkIdentity.Value`
  and look it up in `oldToNewMap` to obtain its pre-allocated network ID (`0` if
  the child has no `NetworkIdentity`).  Overrides are buffered keyed by
  `(PartMetadata.ParentEntity, PartMetadata.InstanceId)` with value
  `(preAllocatedChildId, extractedComponents)`.
  After the full entity pass, the buffer is converted to an
  `IReadOnlyDictionary<int, (long, IReadOnlyList<object>)>` and attached to the
  matching parent root entity's `EntityCreationRequest` as `ChildComponentOverrides`.

**Success Conditions:**

1. **Basic extraction — single root entity:**
   Setup: build a `ScenarioSerializer`-compatible staging repo with one root
   entity (no `PartMetadata`) carrying `SimTransform`, `EntityInfo`, `TkbIdentity`.
   Action: call `Extract(serializer, json, stubAllocator)`.
   Assert: exactly one `EntityCreationRequest` returned with the correct `TkbType`;
   `InitialComponents` contains `SimTransform`; does NOT contain `NetworkIdentity`,
   `NetworkAuthority`, `TkbIdentity`, `LifecycleDescriptor`, `GhostStateTracker`,
   `NetworkOwnership`, `PendingNetworkAck`, or `DescriptorOwnership`.

2. **TKB structural child entities are filtered out:**
   Setup: staging repo with parent entity (no `PartMetadata`) and child entity
   that HAS a `PartMetadata` component.
   Action: `Extract`.
   Assert: exactly one request returned (parent only).

3. **ORBAT subordinates are NOT filtered out:**
   Setup: staging repo with two entities, both without `PartMetadata`; one has
   `EntityInfo.CommanderId = 0` and the other has a non-zero `CommanderId`.
   Action: `Extract`.
   Assert: two requests returned (both entities extracted regardless of CommanderId).

3. **Episode tag appended:**
   Setup: single root entity; `episodeId` = new Guid.
   Action: `Extract(..., episodeId: theGuid)`.
   Assert: `InitialComponents` contains `EpisodeTag { EpisodeId = theGuid }`.

4. **Network ID remapping — FireAtTarget:**
   Setup: root entity with `ActiveMissionPlan` containing a `FireAtTarget` task
   with `BehaviorParams = {"targetNetworkId":1001,"maxRounds":5,"cooldownSeconds":1.0}`;
   staging repo also has entity with `NetworkIdentity { Value = 1001 }`.
   Allocate new ID 2001 for old ID 1001.
   Register `FireAtTargetParamsJsonDto` in `ScenarioBehaviorRemapper`.
   Action: `Extract(..., behaviorRemapper: remapper)`.
   Assert: extracted `ActiveMissionPlan` task `BehaviorParams` contains
   `"targetNetworkId":2001`, not `1001`.

5. **Entities without NetworkIdentity are extracted without Pass 1 entry:**
   Setup: entity has no `NetworkIdentity`; entity has `SimTransform`, no `PartMetadata`.
   Action: `Extract`.
   Assert: one request returned; `InitialComponents` has `SimTransform`.
   No exception in Pass 1 (entity simply has nothing to remap).

6. **Translator-handled components are excluded:**
   Setup: register a `ScenarioSerializer` with a translator that marks component
   type ID T as consumed (`GetConsumedComponentsMask()` has bit T set).  Staging
   entity has component T set.
   Action: `Extract`.
   Assert: `InitialComponents` does NOT contain a component with type ID T.

7. **Disposal of staging repo:**
   Setup: use a counting `EntityRepository` wrapper to detect Dispose calls.
   Action: `Extract`.
   Assert: `Dispose()` was called exactly once on the staging repo.

8. **PreAllocatedNetworkId is set from Pass 1 allocation:**
   Setup: staging entity with `NetworkIdentity { Value = 1001L }`.
   Stub allocator returns `2001L` for that entity.
   Action: `Extract(serializer, json, stubAllocator)`.
   Assert: returned request has `PreAllocatedNetworkId == 2001L`.
   Assert: `InitialComponents` does NOT contain a `NetworkIdentity` component.

9. **Entity without NetworkIdentity has PreAllocatedNetworkId = 0:**
   Setup: staging entity with `SimTransform` and no `NetworkIdentity`.
   Action: `Extract`.
   Assert: `PreAllocatedNetworkId == 0L`.

10. **ChildComponentOverrides populated from PartMetadata children:**
    Setup: staging repo with root entity (no `PartMetadata`) carrying
    `NetworkIdentity { Value = 1000L }` and child entity with
    `PartMetadata { ParentEntity = rootHandle, InstanceId = 3 }`,
    `NetworkIdentity { Value = 1001L }`, and a `WeaponState` component.
    Stub allocator maps 1000 -> 2000, 1001 -> 2001.
    Action: `Extract`.
    Assert: exactly one `EntityCreationRequest` returned (root only).
    Assert: `request.ChildComponentOverrides` is not null.
    Assert: `request.ChildComponentOverrides[3].PreAllocatedId == 2001L`.
    Assert: `request.ChildComponentOverrides[3].Components` contains `WeaponState`.
    Assert: `request.ChildComponentOverrides[3].Components` does NOT contain `PartMetadata`.

11. **ChildComponentOverrides is null when root has no PartMetadata children:**
    Setup: staging repo with only a root entity (no `PartMetadata` children).
    Action: `Extract`.
    Assert: `request.ChildComponentOverrides == null`.

12. **Child entity ID is carried through to ChildComponentOverrides.PreAllocatedId:**
    Setup: staging repo with root entity (`NetworkIdentity = 1000`) and child
    (`NetworkIdentity = 1001`, `PartMetadata.InstanceId = 3`).  Allocator maps
    1000 -> 2000, 1001 -> 2001.
    Action: `Extract`.
    Assert: `request.ChildComponentOverrides[3].PreAllocatedId == 2001L`.

---

### TASK-C005 — Behavior Param Remapping Infrastructure

**Design Reference:** DESIGN.md § Phase 2 — Task C005a–C005d

#### C005a — RemapNetworkIdAttribute

**Scope:** New attribute class in `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/`.
No test required (verified in C005c tests).

#### C005b — Behavior DTOs

**Scope:** Two new DTO classes in `FDP/Toolkits/Fdp.Toolkits/Behavior/Params/`:
`FireAtTargetParamsJsonDto`, `FollowRouteParamsJsonDto`.  A third DTO
`MoveToLocationParamsJsonDto` is authored in this task but has no
`[RemapNetworkId]` members (used only for UI in Phase 5).

**Constraints:**
- Property names must match the JSON keys produced by `MissionPanel.BuildXxxParams`
  helpers (e.g., `TargetNetworkId` ↔ `"targetNetworkId"` via
  `JsonPropertyName` attribute or `camelCase` serializer option).
  Current JSON keys: `targetNetworkId` (long), `maxRounds` (int),
  `cooldownSeconds` (float), `routeEntityId` (int — widened to long in DTO),
  `targetLat`, `targetLon`, `speed`, `arrivalRadius`.
- Use `System.Text.Json` serialization compatible with `JsonSerializerOptions`
  with `PropertyNameCaseInsensitive = true`.

#### C005c — BehaviorParamRemapperCompiler

**Scope:** New static class `BehaviorParamRemapperCompiler` in
`FDP/Toolkits/Fdp.Toolkits/Behavior/`.

**Constraints:**
- Compile delegates once per DTO type at startup (not per remapper instance).
- Expression tree lambda must handle both `long` and `int` properties annotated
  with `[RemapNetworkId]`.  For `int` fields, the new `long` value is
  narrowing-casted to `int` after `TryGetValue`.
- If a DTO has no `[RemapNetworkId]` members, return an identity delegate
  `(json, map) => json` without building any lambda.

**Success Conditions:**

1. **FireAtTarget TargetNetworkId remapped:**
   Setup: register `FireAtTargetParamsJsonDto`; map `{1001L → 2001L}`.
   Input JSON: `{"targetNetworkId":1001,"maxRounds":5,"cooldownSeconds":1.0}`.
   Assert output: `"targetNetworkId":2001`; `maxRounds` and `cooldownSeconds`
   preserved unchanged.

2. **FollowRoute RouteEntityId remapped:**
   Setup: register `FollowRouteParamsJsonDto`; map `{999L → 888L}`.
   Input JSON: `{"routeEntityId":999}`.
   Assert output: `"routeEntityId":888`.

3. **ID not in map passes through unchanged:**
   Map: empty.
   Input JSON: `{"targetNetworkId":1001,"maxRounds":3,"cooldownSeconds":0.5}`.
   Assert output: `"targetNetworkId":1001` (unchanged).

4. **Empty/null JSON returns unchanged:**
   Input: `null` or `""`.
   Assert output: same as input; no exception.

5. **MoveToLocation has no remappable fields — identity delegate returned:**
   Register `MoveToLocationParamsJsonDto`; any map.
   Assert: output JSON is identical to input.

6. **Delegate is compiled only once per type (caching):**
   Call `Compile<FireAtTargetParamsJsonDto>()` three times.
   Assert: the returned delegate is the same reference each time (compare by
   object identity — verify via a single-call count on the expression tree
   builder via test-visible counter).

#### C005d — ScenarioBehaviorRemapper

**Success Conditions:**

1. **Registered behavior remapped:**
   Register `FireAtTargetParamsJsonDto` for `"FireAtTarget"`.
   Action: `RemapJson("FireAtTarget", json, map)`.
   Assert: delegate invoked; ID replaced correctly.

2. **Unregistered behavior passes through:**
   Action: `RemapJson("SomeUnknownBehavior", json, map)`.
   Assert: returns identical JSON string; no exception.

3. **Double-registration throws:**
   Register `"FireAtTarget"` twice.
   Assert: `InvalidOperationException` on second call.

---

## Phase 3 — CGF Scenario Load Handler

---

### TASK-C006 — CgfScenarioLoadHandler

**Design Reference:** DESIGN.md § Phase 3

**Scope — IS included:**
- New class `CgfScenarioLoadHandler` in
  `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/`
- Implements `IClusterStateHandler`; claims `NodeOpType.PrepareLive`
- `PrepareAsync`: load JSON from `IScenarioLoader`
- `Commit`: call `StagingEntityExtractor.Extract`, enqueue all requests
- Register in `CgfApplication` replacing `ReferenceScenarioLoadHandler(...world:null)`
  when a scenario serializer is configured

**Scope — NOT included:**
- The `StagingEntityExtractor` logic (TASK-C004)
- Changes to `NetworkSpawningSystem` or `CreateEntityRequestSystem`
- SimHost scenario loading changes

**Constraints:**
- If `scenarioLoader.TryLoadScenarioJson` returns `null`, `Commit` must be
  a no-op (scenario not found; do not enqueue empty requests).
- After `Abort`, `Commit` must not enqueue anything from the aborted transaction.
- Must handle `HrotScenarioEnvelopeDto` (the outer envelope format) correctly —
  pass the raw JSON to `StagingEntityExtractor` which passes it to
  `ScenarioSerializer.Deserialize`.  The handler does NOT parse envelope fields itself.
- Concurrency: `PrepareAsync` and `Commit` are called sequentially by the cluster
  orchestrator on a single thread; no locking needed beyond what `ScenarioEntityCreationRequestSource`
  provides.

**Success Conditions:**

1. **Happy path — requests enqueued:**
   Setup: `IScenarioLoader` stub returns a valid scenario JSON with 2 root entities;
   `StagingEntityExtractor` stub returns 2 `EntityCreationRequest` objects.
   Action: `PrepareAsync` then `Commit`.
   Assert: `ScenarioEntityCreationRequestSource` queue contains exactly 2 requests.

2. **Scenario not found — no requests:**
   `IScenarioLoader` returns `null`.
   Action: `PrepareAsync` then `Commit`.
   Assert: queue remains empty.

3. **Abort clears pending state:**
   Action: `PrepareAsync` (loader returns JSON), then `Abort`, then `Commit`.
   Assert: queue remains empty.

4. **CanHandle returns true only for PrepareLive:**
   Assert: `CanHandle(NodeOpType.PrepareLive) == true`;
   `CanHandle(NodeOpType.StartEpisode) == false`.

---

## Phase 4 — CGF Episode Load Handler

---

### TASK-C007 — CgfEpisodeLoadHandler

**Design Reference:** DESIGN.md § Phase 4

**Scope — IS included:**
- New class `CgfEpisodeLoadHandler` in
  `Hrot/Subsystems/Hrot.CGF/Orchestration/Handlers/`
- Implements `IClusterStateHandler`; claims `NodeOpType.StartEpisode` and
  `NodeOpType.StopEpisode`
- `StartEpisode.PrepareAsync`: parse `EpisodeHandlerPayload`, load episode JSON
- `StartEpisode.Commit`: call `StagingEntityExtractor.Extract` with `episodeId`;
  enqueue results
- `StopEpisode.Commit`: query all entities in `world` carrying a matching
  `EpisodeTag.EpisodeId`; read each entity's `NetworkIdentity.Value`; publish a
  `DestroyEntityCommand` to the local event bus for each.  Direct
  `EntityRepository.DestroyEntity` calls are explicitly forbidden on this path.
- Register in `CgfApplication` replacing
  `ReferenceEpisodeLoadHandler(serializer, loader, world: null)`

**Scope — NOT included:**
- Modification of `ReferenceEpisodeLoadHandler` in `Fdp.Toolkit`
- SimHost episode load changes
- `EpisodeTag` definition (already exists: `Fdp.Core.EpisodeTag` ID 84)

**Constraints:**
- **SimHost companion change:** When `CgfEpisodeLoadHandler` is deployed, the
  SimHost node's `ReferenceEpisodeLoadHandler` registration in
  `Hrot.SimHost/NodeBootstrapper.cs` must be updated to `world: null`
  (header-peek-only).  Episode entities reach SimHost via the DDS
  ghost-to-active path from CGF's genesis pipeline; omitting this change
  causes every episode entity to be double-instantiated.  This change is
  small (one constructor argument) but is a hard requirement for correctness.
- `StopEpisode` must use `EntityLifecycle.All` in its query to catch entities
  still in `Constructing` state when the episode is abruptly stopped.
- `StopEpisode` must destroy entities by publishing `DestroyEntityCommand` to the
  local event bus (one per matching entity), NOT by directly calling
  `EntityRepository.DestroyEntity`.  Direct calls bypass the event bus; the
  egress translators never fire; SimHost and IG ghost clean-up is never triggered,
  causing permanent ghost entity leaks on remote nodes.
- The `EpisodeHandlerPayload` parsing logic may be copied from
  `ReferenceEpisodeLoadHandler` (it correctly handles the DomainPayload variants
  as a `bool IsStart` + `Guid EpisodeId`).
- `episodeId` passed to `StagingEntityExtractor.Extract` must be the same GUID
  used in `StopEpisode` cleanup.  Store it across the `PrepareAsync`→`Commit`
  lifetime.

**Success Conditions:**

1. **StartEpisode enqueues requests with EpisodeTag:**
   Setup: loader returns JSON with 3 root entities; `episodeId` = G.
   Action: `PrepareAsync(StartEpisode intent)` then `Commit`.
   Assert: queue contains 3 requests; each has `EpisodeTag { EpisodeId = G }`
   in its `InitialComponents`.

2. **StopEpisode publishes DestroyEntityCommand per episode entity:**
   Setup: `world` contains 5 entities: 2 with `EpisodeTag { EpisodeId = G }`,
   3 without or with a different episode GUID.
   Action: `Commit(StopEpisode intent for G)`.
   Assert: 2 `DestroyEntityCommand` events published to the event bus;
   Assert: no `EntityRepository.DestroyEntity` call made directly;
   Assert: 3 non-episode entities are untouched.

3. **CanHandle returns true for StartEpisode and StopEpisode only:**
   Assert `StartEpisode` true, `StopEpisode` true, `PrepareLive` false.

4. **Abort before Commit leaves queue empty:**
   Action: `PrepareAsync(StartEpisode)`, then `Abort`, then `Commit`.
   Assert: queue empty.

5. **Missing episode JSON — no requests:**
   Loader returns `null`.
   Action: `PrepareAsync` then `Commit`.
   Assert: queue empty; no exception.

---

## Phase 5 — Generic Mission Editor UI

---

### TASK-C008 — Presentation Attributes

**Design Reference:** DESIGN.md § Phase 5 — Task C008

**Scope:** Two new attribute classes in `FDP/Toolkits/Fdp.Toolkits/Behavior/Attributes/`:
`MapPickableWorldLocationAttribute`, `MapPickableEntityAttribute`.  Apply both
to the DTOs from TASK-C005b.  Add `MoveToLocationParamsJsonDto` with the
geography attributes.

No runtime logic — attributes are metadata only.

**Constraints:**
- `MapPickableEntityAttribute` accepts optional string filter preset params
  (var-arg `string[]`).
- `MapPickableWorldLocationAttribute` has no properties (marker attribute).
- Both located in the same `Fdp.Toolkit.Behavior` assembly as `RemapNetworkIdAttribute`.
- `MoveToLocationParamsJsonDto` does NOT implement `[RemapNetworkId]` on any
  property (no network IDs in `MoveToLocation` params).

**Success Conditions:**

1. **Attributes are readable at runtime:**
   Assert: `typeof(FireAtTargetParamsJsonDto).GetProperty("TargetNetworkId")`
   has both `MapPickableEntityAttribute` and `RemapNetworkIdAttribute`.

2. **MoveToLocation has MapPickableWorldLocation on lat/lon, no RemapNetworkId:**
   Assert: `TargetLat` and `TargetLon` have `MapPickableWorldLocationAttribute`;
   no property has `RemapNetworkIdAttribute`.

3. **MapPickableEntityAttribute stores filter presets:**
   `new MapPickableEntityAttribute("roads", "graphs")`.
   Assert: `FilterPresets` array contains `["roads", "graphs"]`.

---

### TASK-C009 — BehaviorUiCompiler

**Design Reference:** DESIGN.md § Phase 5 — Task C009

**Scope — IS included:**
- New class `BehaviorUiCompiler` in `Hrot/Engine/Hrot.Presentation/Behavior/` or
  `Hrot/Engine/Hrot.UI.Common/Behavior/`
- `BehaviorUiRegistry` (dictionary `string → BehaviorUiDrawDelegate`) in the
  same file or a companion file
- Compiled property getter/setter delegates using `System.Linq.Expressions` (no
  `PropertyInfo.GetValue/SetValue` on the hot path)
- ImGui control selection based on property type and annotations:
  `[MapPickableEntity]` → network ID label + pick button
  `[MapPickableWorldLocation]` → coordinate label + map pick button
  `float` → `ImGui.InputFloat`
  `double` → `ImGui.InputDouble`
  `int`/`long` → text input (ImGui)
  `bool` → `ImGui.Checkbox`

**Scope — NOT included:**
- The `BehaviorUiDrawDelegate` type (defined in this task as a public delegate)
- Wiring into `CgfBehaviorSetup` (TASK-C011)

**Constraints:**
- All reflection (`GetProperties`, `GetCustomAttributes`) must happen at
  compile time inside `Compile<TDto>()`, NOT inside the returned delegate.
- The returned delegate must be compatible with the ImGui immediate-mode
  constraint: called every frame; must not allocate on stable execution paths
  (frame with no user input).  JSON deserialization per frame is acknowledged as
  an unavoidable allocation; everything else must be allocation-free.
- The delegate signature must match `BehaviorUiDrawDelegate` exactly:
  `(string currentJson, int taskIndex, IPickInteractionContext context) → string`
- `IPickInteractionContext` is defined alongside `BehaviorUiCompiler` (same
  assembly) and provides:
  - `bool IsPickPendingFor(int taskIndex, string propertyName)` — per-field
    pending flag; avoids brittle single-boolean tracking at panel level
  - `void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets)`
  - `void RequestLocationPick(int taskIndex, string propertyName)`
- Entity pick flow: the compiled delegate calls `context.IsPickPendingFor(taskIndex,
  propertyName)` to determine if its specific pick is in progress.  On click,
  it calls `context.RequestEntityPick(...)` or `context.RequestLocationPick(...)`.
  `MissionPanel` implements `IPickInteractionContext` and routes the request to
  the existing `HandlePickEntity` / `HandlePickLocation` callbacks.
- Location: must be in the Hrot.Presentation or Hrot.UI.Common project since it
  references ImGui.NET.

**Success Conditions:**

1. **Compile<T> produces non-null delegate:**
   `BehaviorUiCompiler.Compile<FireAtTargetParamsJsonDto>()` must return
   a non-null delegate.

2. **Compiled delegate does not use reflection at render time:**
   Annotate `BehaviorUiCompiler` internals with a test-visible call counter that
   tracks `PropertyInfo.GetValue` invocations.  Call the returned delegate.
   Assert: counter remains 0.

3. **JSON round-trip when value changes:**
   Simulate an `ImGui.InputFloat` returning `changed = true` with a new value.
   Assert: returned JSON reflects the updated value while all other fields are
   preserved.

4. **No change returns original JSON reference:**
   Simulate no user interaction (all ImGui calls return false).
   Assert: returned string is the same reference as `currentJson` (no
   unnecessary allocation).  Context receives no pick requests.

---

### TASK-C010 — MissionPanel Integration

**Design Reference:** DESIGN.md § Phase 5 — Task C010

**Scope — IS included:**
- Deduplicate `MissionPanel` — determine canonical location (likely
  `Hrot.Presentation`; remove/redirect from `Hrot.UI.Common` if it is a copy)
- Remove `DrawFireAtTargetParams`, `DrawMoveToLocationParams`,
  `DrawFollowRouteParams`
- Remove the corresponding `TryParseXxx` / `BuildXxx` static helpers that are
  now covered by the DTO + compiler
- Replace with `BehaviorUiRegistry` lookup + generic draw delegate call
- Keep `DrawRawJsonEditor` as fallback

**Scope — NOT included:**
- Changes to other panels or InspectorPanel
- Changes to MissionControlBehaviorParamsHelper (parse-at-runtime is separate
  from the editor-time UI DTO)

**Constraints:**
- The `MissionPanel` constructor signature must change minimally; `BehaviorUiRegistry`
  is injected via constructor.
- `MissionPanel` must implement `IPickInteractionContext`.  `RequestEntityPick`
  and `RequestLocationPick` route to the existing `HandlePickEntity` and
  `HandlePickLocation` callbacks, storing `(taskIndex, propertyName)` so that
  `IsPickPendingFor` can answer per-field queries.
- Any public/internal static helpers (`BuildFireAtTargetParams`,
  `TryParseFireAtTargetParams`) that are referenced outside `MissionPanel` must
  remain as pass-through compatibility shims until all callers are updated (verify
  with grep; if zero external callers, remove directly).
- The fallback `DrawRawJsonEditor` must be called for any `BehaviorId` not
  registered in `BehaviorUiRegistry`.

**Success Conditions:**

1. **Registry path renders without error:**
   Setup: `BehaviorUiRegistry` has `FireAtTarget` registered.
   Task with `BehaviorId = "FireAtTarget"` and valid JSON.
   `MissionPanel` stub implements `IPickInteractionContext` returning `false`
   from `IsPickPendingFor`.
   Action: render the panel (ImGui test renderer).
   Assert: no exception; rendering completes.

2. **Fallback DrawRawJsonEditor used for unknown behaviors:**
   Setup: `BehaviorUiRegistry` empty.
   Task with `BehaviorId = "CustomUnknown"`.
   Action: render.
   Assert: raw editor path was taken (verify by checking mock call or test output).

3. **No duplicate copies of MissionPanel:**
   File search: confirm `MissionPanel.cs` exists in exactly one project post-task.

---

### TASK-C011 — Composition Root Registration

**Design Reference:** DESIGN.md § Phase 5 — Task C011

**Scope — IS included:**
- Modify `CgfBehaviorSetup.cs` to construct and populate `ScenarioBehaviorRemapper`
  and `BehaviorUiRegistry`
- Register DTO types for `FireAtTarget`, `FollowRoute`, and `MoveToLocation`
- Pass `ScenarioBehaviorRemapper` through to `CgfScenarioLoadHandler` and
  `CgfEpisodeLoadHandler` constructors

**Scope — NOT included:**
- Registration in `EditorSubsystem`/`HrotEditor` composition root (a separate
  task for when the editor adopts the generic UI)

**Constraints:**
- Registration must happen before any scene is loaded but after the behavior
  registry is fully built.
- `BehaviorId` strings must match exactly what `BehaviorRegistry` uses
  (e.g., `"FireAtTarget"` — verified from `CgfBehaviorSetup`).
- `ScenarioBehaviorRemapper` instance is shared between `CgfScenarioLoadHandler`
  and `CgfEpisodeLoadHandler`.

**Success Conditions:**

1. **End-to-end remapping in integration test:**
   Setup: create a minimal scenario JSON with one entity carrying `ActiveMissionPlan`
   with a `FireAtTarget` task referencing old network ID 999.
   Wire up `CgfBehaviorSetup`, `ScenarioBehaviorRemapper`, `StagingEntityExtractor`,
   and `CgfScenarioLoadHandler` with a stub allocator ( 999 → 1999 ).
   Action: commit the handler.
   Assert: enqueued request's `ActiveMissionPlan.Plan.Tasks[0].BehaviorParams`
   contains `"targetNetworkId":1999`.

2. **All expected BehaviorIds are registered:**
   Assert: `ScenarioBehaviorRemapper` has delegates for `"FireAtTarget"` and
   `"FollowRoute"` after `CgfBehaviorSetup` runs.

3. **BehaviorUiRegistry has entries for all behavior types with non-trivial params:**
   Assert `"FireAtTarget"`, `"FollowRoute"`, `"MoveToLocation"` are registered
   in `BehaviorUiRegistry`.

---

### TASK-C012 — SimHost Episode Handler Passive Demotion

**Design Reference:** DESIGN.md § Phase 6

**Scope - IS included:**
- In the SimHost composition root (typically `Hrot/Subsystems/Hrot.SimHost/NodeBootstrapper.cs`
  or its equivalent orchestration setup file), change the `ReferenceEpisodeLoadHandler`
  registration from `world: <liveRepo>` to `world: null`
- Verify the change compiles and the SimHost episode handler still participates
  in the cluster orchestration handshake (ACK sent; Commit is a no-op)

**Scope - NOT included:**
- Modifying `ReferenceEpisodeLoadHandler` itself (it is toolkit code)
- Changing any other handler registration on SimHost
- Changing SimHost's scenario load handler (already world-bound, unchanged)

**Constraints:**
- MUST ship in the SAME release as TASK-C007 (`CgfEpisodeLoadHandler`).
  Deploying TASK-C007 without TASK-C012 creates a split-brain state: both CGF
  and SimHost materialize episode entities simultaneously, causing entity
  duplication, `NetworkIdentity` collisions, and ELM handshake failure.
- The change is exactly one argument: replace `world: <liveRepo>` with
  `world: null` at the point where `ReferenceEpisodeLoadHandler` is constructed
  for the episode role.
- `ReferenceEpisodeLoadHandler.CanHandle` returns `true` for `StartEpisode` and
  `StopEpisode` regardless of the `world` argument.  `PrepareAsync` will ACK.
  `CommitStartEpisode` with `world: null` is a no-op (correct: CGF owns genesis).
  `CommitStopEpisode` with `world: null` is a no-op (correct: CGF owns teardown).
- Verify there is no second registration of `ReferenceEpisodeLoadHandler` on the
  SimHost node that would still receive a non-null world.

**Success Conditions:**

1. **SimHost no longer loads episode entities directly:**
   Setup: integration cluster with CGF + SimHost.
   Action: issue a `StartEpisode` command with a scenario containing 2 entities.
   Assert: exactly 2 entities are created in total across the cluster, NOT 4.
   (Without this fix, both CGF genesis and SimHost direct-load would each create
   2 entities, producing 4.)

2. **SimHost participates in the cluster handshake:**
   Action: observe cluster orchestration logs during `StartEpisode`.
   Assert: SimHost's episode handler emits a `Prepare` ACK; `Commit` on SimHost
   produces no ECS state changes.

3. **StopEpisode destroys entities on CGF only:**
   Action: issue `StopEpisode` after a prior `StartEpisode`.
   Assert: episode-tagged entities are destroyed via `CgfEpisodeLoadHandler`;
   SimHost's handler completes `StopEpisode` without error (no-op).

4. **Build is clean:**
   Action: build `Hrot.SimHost.csproj`.
   Assert: zero compiler errors.

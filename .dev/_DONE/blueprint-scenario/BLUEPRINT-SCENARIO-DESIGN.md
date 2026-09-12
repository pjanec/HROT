# Blueprint ↔ Entity Assignment & Scenario Persistence — Design

**Scope:** how Instance Blueprints are assigned to entities — **statically** (authored into the scenario file,
multiple per entity) and **dynamically** (assigned/removed/replaced mid-simulation, from an FDP event or a blueprint
action node). AiPrimitive (behavior) assignment is a separate, existing path that we are **not** changing (§9).

**Status:** design of record, architect-aligned (`ARCHITECT-BRIEF-01.md` + responses) and verified against current
source. Implementation not yet started.

---

## 1. Background — dispatch kinds & state homes

- **Instance Blueprints** store state in the unmanaged `BlueprintBlackboard{1024,4096,16384}` components, managed by
  a **partition allocator** (`BlueprintBlackboardPartitions`, core `Fdp.Toolkits`). One entity can host **multiple**
  Instance Blueprints concurrently — each occupies a slot in the tier's dense slot table. `BlueprintTickSystem`
  ticks every populated slot; `BlueprintMaintenanceSystem` upgrades a tier (1024→4096→16384) when it overflows.
- **AiPrimitive Blueprints** (behaviors) project a single working state over `Blackboard1024`. **One per entity**
  (Slice-1 constraint). Assigned via the behavior/TKB path (§9), not this design.

Tier capacities (verified):

| Tier | MaxSlots | Payload bytes |
|---|---|---|
| `BlueprintBlackboard1024` | 4 | 928 |
| `BlueprintBlackboard4096` | 8 | 3936 |
| `BlueprintBlackboard16384` | 16 | 16096 |

---

## 2. Principle — persist the *intent*, never the runtime bytes

Scenario JSON is a **declarative authoring template**; live blackboard memory (latent cursors, tick counters,
mid-execution phase) is volatile runtime state that belongs in **checkpoints / Flight-Recorder**, not scenarios.

**Current bug:** `BlueprintBlackboard{1024,4096,16384}` carry `[ComponentId]` but **no `[DataPolicy]`**, so they
serialize into scenario JSON by default (the AiPrimitive blackboards are correctly `[DataPolicy(DataPolicy.NoSave)]`).

**Fix:** mark the three `BlueprintBlackboard*` components `[DataPolicy(DataPolicy.NoSave)]`, and persist a
**declarative assignment** (which blueprints, + optional variable overrides) via the engine's established
**intent-component + translator + genesis-materialization** pattern. Checkpoints/recorder continue to capture the
live bytes unchanged.

**Authoring invariant (safety — addresses the "don't depend on mutated blackboard" concern):** the *assignment
list* (slot-table `BlueprintId`s) is stable and **not** corrupted by execution, so extracting it on save is always
safe. The hazard is only for *initial variable overrides* (§6): diffing a blackboard that has **ticked** would
capture runtime drift as authored "overrides," reintroducing the exact anti-pattern this topic exists to kill.
**Rule:** the authoring/staging world's blackboards must remain at `InitDefault` — all execution (preview, run)
operates on a **snapshot / dry-run copy** (the engine already keeps a pre-tick snapshot repo + dry-run handlers).
`Extract` must therefore read a pristine (un-ticked) blackboard; when overrides land, authored values are taken
from that pristine state (or tracked explicitly), never from a ticked blackboard. A test must assert that *saving
after a preview does not bloat the assignment JSON with runtime state*. **Verified the invariant holds:** preview
enters via `ReferencePreviewHandler`, which snapshots a **separate** `EntityRepository`; the authoring repo is
shielded from `BlueprintTickSystem`, so `Extract` reads pristine authoring memory with **no manual reset** needed.

---

## 3. The unified attach/detach seam (core)

All assignment paths — editor, scenario genesis, and mid-runtime events — **funnel through one core seam**, so the
slot/`InitDefault`/idempotency logic exists once.

- **Location: `Fdp.Toolkits.Blueprints` (core).** Today `BlueprintAttachService` lives in
  `Hrot.Blueprints.Editor.Runtime` and takes an authoring `BlueprintAsset`; CGF/genesis must **not** depend on the
  editor assembly. Move the seam to core, keyed by the runtime **`BlueprintId`** (+ the `BlueprintRegistry`
  `def`) rather than the authoring asset. The low-level primitives it wraps (`BlueprintBlackboardPartitions.
  TryAttach`/`TryDetach`/`Initialize`/`GetSlotCount`/`GetSlot`) are already in core. The existing editor service
  becomes a thin forwarder (resolves `asset.AssetId` → id, calls the core seam).
- **Attach** (`AttachToEntity(world, registry, blueprintId, entity)`): require registered + `Kind == Instance`;
  idempotent (no-op if the id already occupies a slot on any tier); choose/ensure the tier component; `TryAttach`
  a slot; run `def.InitDefault(span)`. Returns a classified result.
- **Detach** (`DetachFromEntity(world, blueprintId, entity)`): `BlueprintBlackboardPartitions.TryDetach` (releases
  the slice, coalesces free space, **dense-compacts** the slot table by moving the last entry into the freed slot).
- **Replace** = detach-then-attach, synchronously, same tick.

---

## 4. Static assignment — scenario file

Uses the intent pattern (mirrors `InitialPassengersIntent` + `GenesisMaterializationSystem`):

- **`BlueprintAssignmentDto`** — `{ Guid AssetId; Dictionary<string,object>? Overrides }`. Stores the **stable
  `AssetId` GUID** (not the runtime hash); `Overrides` is **null/empty in the MVP** (§6).
- **`InitialBlueprintsIntent`** — `[DataPolicy(DataPolicy.Transient)]` managed component holding
  `List<BlueprintAssignmentDto>`. A transient boot instruction, removed after materialization.
- **`BlueprintStateTranslator : IEntityScenarioTranslator`**
  - **`Extract` (save):** for each `BlueprintBlackboard*` tier on the entity, resolve `byte* memory`, call
    `GetSlotCount(memory)`, loop `0..count-1` calling `GetSlot(memory, i)` and read `BlueprintSlotEntry.BlueprintId`;
    map each id back to its `AssetId` GUID (via the registry/reverse map) → emit `BlueprintAssignmentDto[]`. (Slots
    are dense — no gap scan, no managed enumerator → zero-alloc.)
  - **`Inject` (load):** parse the JSON array → attach an `InitialBlueprintsIntent` component to the entity.
  - **`GetOutputDomKeys` (REQUIRED — would crash load if omitted; verified vs `BrainBlackboardTranslator`):** must
    return **both** (a) the custom array key it writes (e.g. `"BlueprintAssignments"`) and (b) the legacy
    `"BlueprintBlackboard1024"`/`"4096"`/`"16384"` keys claimed as a **black hole** (no-op `Inject`).
    `ScenarioSerializer` (`:389`) routes only declared keys to translators; anything else falls through to
    `FdpAutoSerializer`, which throws `InvalidOperationException` on (a) the unmapped custom array key and (b) old
    scenarios still carrying the now-`NoSave` blackboard keys. This mirrors `BrainBlackboardTranslator`/
    `Blackboard1024Translator` (claim-key + no-op `Inject`).
- **Register the managed intent (REQUIRED):** add `RegisterManagedComponent<InitialBlueprintsIntent>()` to the
  genesis intent registry (`GenesisIntentRegistry.RegisterAll`, where `InitialPassengersIntent`/
  `InitialUnitSubordinateIntent` are registered). Managed components can't be injected unless registered, or
  `SetManagedComponent` throws at scenario load.
- **`BlueprintMaterializationSystem`** (CGF genesis, `Input` phase — CGF already registers
  `GenesisMaterializationSystem`, `CgfSubsystem.cs:354`): for each entity carrying `InitialBlueprintsIntent`:
  1. Resolve every `AssetId` → `def` via `BlueprintIdHash.Compute(assetId)` + registry; **skip + log** unresolved
     (deleted/uncompiled) assets — never fail the scenario (§7).
  2. **Pre-provision the tier** from aggregate need (§5): sum valid `def.StateSize` and count blueprints, pick the
     single fitting tier, add that `BlueprintBlackboard*` component once.
  3. Attach each blueprint via the core seam (§3); apply overrides if present (§6).
  4. Remove the `InitialBlueprintsIntent` component **via the `IEntityCommandBuffer`**
     (`cmd.RemoveManagedComponent<InitialBlueprintsIntent>(entity)`), **not** a direct repo removal — removing a
     component while iterating its `EntityQuery` invalidates the chunk iterator (mirrors `GenesisMaterializationSystem`).

### 4.1 Persist (Save) ↔ Load flow — end to end

**PERSIST (save scenario):** the live blackboard is the source of truth (§2); save *reads* it, never writes bytes.
1. **Author** (§12.2): user attaches blueprint(s) to the selected entity via the Entity Blueprints panel → the
   entity's `BlueprintBlackboard*` slot table now holds the `BlueprintId`(s). (Authoring world; blackboards at
   `InitDefault` per the §2 invariant.)
2. **Save** triggers `ScenarioSerializer` to walk each entity in the authoring `EntityRepository`.
3. For each entity, `BlueprintStateTranslator.Extract` scans every `BlueprintBlackboard*` tier
   (`GetSlotCount`/`GetSlot` → `BlueprintId` → `registry.TryGetById(id).AssetId`) and writes a
   `BlueprintAssignmentDto[]` under the `"BlueprintAssignments"` DOM key.
4. The blackboard components are `[DataPolicy(NoSave)]` → **not** byte-serialized; the translator also black-holes
   their keys. **Result:** clean declarative JSON (`BlueprintAssignments` = list of `AssetId`s), zero runtime bytes.

**LOAD (open scenario / test):** the reverse, funneling through the same core attach seam (§3).
5. `ScenarioSerializer` routes `"BlueprintAssignments"` → `BlueprintStateTranslator.Inject`, which sets an
   `InitialBlueprintsIntent` (transient managed component) on the materializing entity.
6. `BlueprintMaterializationSystem` (CGF genesis, `Input` phase) consumes the intent: resolve `AssetId`→`def`
   (skip+log unresolved), pre-provision the fitting tier (§5, ceiling-guarded), attach each via the seam
   (`InitDefault`), then remove the intent via the ECB.
7. `BlueprintTickSystem` ticks the attached blueprints on the next frame — the entity boots deterministically with
   no manual Compile/attach.

(Editor-time authoring needs no save: the panel attaches to live memory, and preview runs on a snapshot (§2). Save
is only for persistence; load is what makes a saved scenario a repeatable test fixture — §BSA-402.)

---

## 5. Tier pre-provisioning (avoid mid-tick upgrades)

"Largest-first" is insufficient (4 × 300-byte blueprints = 1200 > 928 → forces a 1024→4096 upgrade mid-load). The
materializer **pre-computes aggregate requirements** and provisions the correct tier up front:

- Sum `StateSize` over valid defs **and** count them; pick the smallest tier satisfying **both** the slot count and
  the payload-byte limit (table in §1).
- Add that single `BlueprintBlackboard*` component, then attach all blueprints into the existing block — the
  allocator carves sequential slots, materializing in **one frame** with zero `BlueprintMaintenanceSystem` churn.
- **Absolute-ceiling guard:** if the aggregate need exceeds the largest tier (**16 slots / 16096 bytes**),
  **log an error and clamp/truncate** the attachments — never proceed into a blind tier upgrade or throw a capacity
  exception mid-materialization. A scenario over-subscribing an entity must degrade gracefully, not crash the load.

---

## 6. Variable overrides (MVP: assignment-only; door left open)

The DTO's optional `Overrides` (`Dictionary<string,object>?`) is **empty in the MVP** — load always boots to
`InitDefault`. The format is forward-compatible so overrides drop in without a change. Future mechanism (uses
`BlueprintDefinition.StateFields` → `BlueprintFieldDescriptor { OffsetBytes, SizeBytes, ClrType }`, baked by the
compiler into the production registry — no `DebugMap` JSON needed):

- **Apply (load/attach):** after `InitDefault`, for each override key look up the descriptor and
  `Unsafe.WriteUnaligned` the marshaled value into the slot payload at `OffsetBytes`.
- **Diff (save/extract):** `stackalloc` a baseline, `def.InitDefault(baseline)`, then per field `SequenceEqual`
  the live slice vs baseline; record only differing fields (marshaled via `ClrType`) into `Overrides` → minimal
  scenario JSON.

**Deferred because** the authoring UX ("where is a per-instance override edited?") is unsettled. Not in MVP scope.

---

## 7. Dynamic assignment & mid-runtime mutation

Mid-runtime changes are **never** intent components (those are genesis-only, consumed and destroyed). They are
runtime commands:

- **Events:** `AttachInstanceBlueprintEvent` / `RemoveInstanceBlueprintEvent` / `ReplaceInstanceBlueprintEvent` —
  **unmanaged struct** events (zero-alloc) carrying `{ Entity; int BlueprintId }` (+ `NewBlueprintId` for replace).
  Consumed by a dedicated system in the **`Input` phase** (mirrors `BehaviorIngressSystem`), so the slot +
  `InitDefault` are ready before `Simulation`, and `BlueprintTickSystem` picks them up the same frame. The consuming
  system calls the §3 seam (replace = detach-then-attach). (The `Attach` event backs both the action node and the
  inspector's live-mode "add" — §12.)
- **Processing order within a frame's drain (required):** the system applies **all `Remove` events before any
  `Attach`**. So an in-place swap (remove a 300 B blueprint + add a 300 B blueprint) first `TryDetach`es and
  dense-compacts the slot, and the subsequent attach **reuses the freed capacity** instead of finding the tier full
  and triggering a spurious `BlueprintMaintenanceSystem` upgrade. (`Replace` is inherently detach-then-attach.)
- **Identity:** events carry the runtime `BlueprintId` (FNV-1a-32 of the asset GUID).
- **Resilience:** idempotent attach / safe detach; unknown ids are no-ops.

---

## 8. Blueprint action-node access (publish events from a graph)

There is no precedent for a visual node publishing to the `FdpEventBus`; we add it as a custom action node.

**Corrected mechanism (verified — the architect's "Library FunctionCall receives `ISimulationView`" is wrong).**
A plain `IrOp_LibraryCall` emits `{libClass}.{Method}({dataArgs})` — **only the node's data-pin arguments, no
engine context** (`StatementEmitter.cs:106`). A library `FunctionCall` therefore can **not** receive `view`/`self`.

The correct vehicle is a **`[SharedAiAction]` action node**, whose lowering emits
`static NodeStatus {Method}(ref {ParamsDto} dto, Entity self, EntityRepository world)`
(`InlineActionLowering.cs:33,112`). It receives **`self` and `EntityRepository world` directly** (no cast — `world`
is already an `EntityRepository`, and `EntityRepository.Bus` is a public `FdpEventBus`). So:

```csharp
// [SharedAiAction] method — appears as an action node in the palette
public static Fbt.NodeStatus ReplaceInstanceBlueprint(ref ReplaceParams dto, Entity self, EntityRepository world)
{
    world.Bus.Publish(new ReplaceInstanceBlueprintEvent {
        Entity = self, NewBlueprintId = dto.NewBlueprintId });
    return Fbt.NodeStatus.Success;
}
```

- Publishing an event is not a structural mutation, so it's safe mid-tick; the actual attach/detach happens in the
  consuming system (§7). The event is published during `Simulation` and consumed in the **next** frame's `Input`
  phase → a **one-frame latency** before the swap takes effect (acceptable for "replace my blueprint"; note it).
- Target defaults to `self`; a target pin allows acting on another entity.

---

## 9. AiPrimitive (behaviors) — unchanged, for contrast

Not modified by this design; documented so the two paths stay distinct:
- **Static:** TKB template `BehaviorProfileDto.DefaultBehaviorHash` → `BehaviorTkbTranslator` initializes
  `BehaviorState` at genesis.
- **Dynamic:** publish `AssignBehaviorEvent`/`AssignBehaviorHashEvent` (replace) or `ClearBehaviorEvent` (remove);
  `BehaviorIngressSystem` (sole `BehaviorState` mutator, `Input` phase) applies it and resets the BTree/HSM pointer.

No authoring-UX unification is needed: there is no TKB editing UI today (behaviors are authored as JSON), so the
"two static mechanisms confuse authors" concern does not arise.

---

## 10. Module placement

| Piece | Assembly |
|---|---|
| `BlueprintAssignmentDto`, `InitialBlueprintsIntent` (`[Transient]`) | core/common (shared) |
| Unified `AttachToEntity`/`DetachFromEntity` seam (by `BlueprintId`) | **`Fdp.Toolkits.Blueprints`** (core) |
| `Attach`/`Remove`/`ReplaceInstanceBlueprintEvent` + consuming system | **`Fdp.Toolkits.Blueprints`** (core, `Input` phase) |
| `BlueprintLifecycleLibrary` (action-node bridge) | core (compiled into the engine) |
| `BlueprintStateTranslator : IEntityScenarioTranslator` (declares `BlueprintAssignments` + black-holes legacy blackboard keys) | **CGF** scenario path |
| `RegisterManagedComponent<InitialBlueprintsIntent>()` | `GenesisIntentRegistry.RegisterAll` (genesis bootstrap) |
| `BlueprintMaterializationSystem` (intent → preprovision → attach → remove intent) | **CGF** genesis |
| `[DataPolicy(NoSave)]` on `BlueprintBlackboard{1024,4096,16384}` | `Fdp.Toolkits.Blueprints` (edit in place) |
| Editor `BlueprintAttachService` → thin forwarder to the core seam | `Hrot.Blueprints.Editor.Runtime` |
| Entity "Blueprints" authoring inspector (§12) | Editor (Details/Inspector; mirrors `ComponentEditDrawer`/`InspectorWindow`) |

---

## 11. Open items / verify-at-implementation

- **Library-call ABI (§8): RESOLVED — corrected.** A plain `IrOp_LibraryCall` passes only data-pin args
  (`StatementEmitter.cs:106`), so a Library `FunctionCall` can NOT receive `ISimulationView`. The action-node
  bridge instead uses the `[SharedAiAction]` path `(ref dto, Entity self, EntityRepository world)`
  (`InlineActionLowering.cs:112`) and publishes via `world.Bus`. (One-frame latency: publish in `Simulation` →
  consume in next `Input`.)
- **Id → AssetId reverse mapping (§4 Extract): property exists but is NOT populated — small fix required.**
  `BlueprintDefinition.AssetId` (`BlueprintDefinition.cs:28`, `init`) **exists** — so do **not** "add" it (and keep
  it `init`, not `required`, to avoid breaking other constructors). **But** the registrar emit does **not** set it:
  verified that `AssetId` appears in the compiler emit only in the DebugMap and a code comment, never in the
  generated `BlueprintDefinition` literal (`CSharpEmitter`), so at runtime `def.AssetId == Guid.Empty` for Instance
  blueprints → `Extract`'s `GetSlot → BlueprintId → registry.TryGetById → def.AssetId` would yield `Guid.Empty`.
  **Fix (prerequisite for the Extract task):** populate `AssetId = <asset guid>` in the emitted Instance
  `BlueprintDefinition` (and any in-memory registration path). One field in the emitter, not a schema change.
  (The architect flagged this as "property missing"; it's actually "population missing.")
- **Per-instance override authoring UX (§6):** deferred; revisit before building overrides.
- **`NoSave` migration: RESOLVED — it does NOT tolerate them by default.** Old scenarios carrying
  `BlueprintBlackboard*` keys would hit `FdpAutoSerializer` and throw. Fixed by `BlueprintStateTranslator.
  GetOutputDomKeys` claiming the three legacy blackboard keys as a black hole with a no-op `Inject` (§4).

---

## 12. Editor authoring UI ("Entity Blueprints")

This is the **authoring UI** that closes the topic — assign Instance blueprints to a selected map/scenario entity
so they bake into the scenario on save (and run in preview without saving). It is **not** the one-shot debug
`RunBlueprintOnEntityCommand` (kept as a shortcut). Consistent with §2, the live blackboard stays the source of
truth — the UI reads & mutates it; no competing persistent record.

**Two surfaces (do not wedge authoring into the Entity Inspector).** Instance blueprints span three tier
components and the authoring flow is a *staged, transactional* diff — both fight the Entity Inspector's
component-per-header, immediate-`IsDirty`-commit `ComponentEditDrawer`/`StructEdit` model. So split:

### 12.1 Entity Inspector — read-only monitoring
A custom **`IEntityAwareImGuiRenderer`** for each `BlueprintBlackboard{1024,4096,16384}` component (mirrors the
existing `BrainBlackboardRenderer` / `Blackboard1024Renderer`). On a blackboard component it reads the unmanaged
memory, `GetSlotCount`/`GetSlot`s the dense table, resolves each `BlueprintId` → name via the registry, and renders
a **read-only** live list (name, `InstanceVersion`, tick/latent-cursor status); `RenderValue` returns `true` to
replace the default byte-dump. Pure visibility — never tempts `StructEdit` to corrupt the partition bytes.
**These 3 renderers do NOT exist yet** — today the Entity Inspector shows a raw byte-dump of the partition memory.
Build one per tier, registered via `[ImGuiRenderer(typeof(BlueprintBlackboard1024))]` (etc.) in the
`ImGuiRendererRegistry` (infra + the `Brain`/`Blackboard1024Renderer` precedents are verified to exist). Each is a
*per-tier* summary; the unified cross-tier views are §12.4.

### 12.2 Dedicated "Entity Blueprints" panel — authoring (detached view-model)
A separate panel (`EntityBlueprintsPanel`) for the selected entity. **Reality** = a per-frame scan across all three
tiers (`GetSlotCount`/`GetSlot` → `BlueprintId` → name; `AssetId` for save). **Intent** = a local mutable
`List<BlueprintAssignmentDto>` (uncommitted UI staging, never persisted on its own). The UI renders the diff and
defers all structural mutation to **Apply**. Wireframe:

```text
┌ Entity Blueprints ──────────────────────────────────────────┐
│ Target: [42,v1] (OrcGuard)            Sim: [ RUNNING ]       │
│ Active Tier: BlueprintBlackboard1024                         │
│ Projected Usage: 3 / 4 Slots  |  650 / 928 Bytes            │
├──────────────────────────────────────────────────────────────┤
│ [ + Add Blueprint... ▾ ]                                     │
│  Blueprint          Status     Size   Action                 │
│  HealthRegen        Active     150 B  [ Remove ]             │
│  ~PatrolBehavior~   Removed    200 B  [ Restore ]           │
│  + SquadCombat      Added      300 B  [ Cancel ]            │
├──────────────────────────────────────────────────────────────┤
│ ⚠ Pending: 1 add, 1 remove        [ Apply ]  [ Revert All ] │
└──────────────────────────────────────────────────────────────┘
```
- **Projected Usage** = aggregate slots+bytes of (reality + adds − removes) vs. the current tier limits. Turns
  **yellow** when a tier upgrade will be needed on Apply; **red** + **Apply disabled** when it exceeds the absolute
  16384 ceiling (16 slots / 16096 bytes) — the UI front-stops the §5 ceiling guard.
- **+ Add Blueprint…** uses the existing `BlueprintPickerSources` filtered to `Instance` dispatch; picking appends
  to Intent (does not attach). **Revert All** clears Intent (snaps back to reality).

### 12.3 Commit — one seam, two timings (gate on the debug time controller)
On Apply, diff Intent vs. Reality → add/remove sets. Both timings funnel through the §3 core seam:
- **Paused (authoring):** apply **synchronously** at a frame-safe point (not inside `DrawUI`; mirror
  `ComponentEditDrawer`/`InspectorWindow` commit). **Must pre-provision the tier like genesis (§5), because
  `BlueprintMaintenanceSystem` runs in `BeforeSync` and does NOT run while paused — so `AttachToEntity` alone would
  return `NoSlotAvailable` on an overflow and the add would be silently lost.** Sequence: compute the post-Apply
  aggregate; if it needs a larger tier than the entity currently has, `repo.AddComponent` the larger
  `BlueprintBlackboard*`, `BlueprintBlackboardPartitions.CopyToLargerTier` to migrate existing slots (safe — ECS is
  quiescent while paused), **then `repo.RemoveComponent<OldTier>(entity)`** — this last step is required (mirrors
  `BlueprintMaintenanceSystem`); leaving both tiers attached makes `BlueprintTickSystem` tick the blueprints
  **twice** and `Extract` emit **duplicate** `BlueprintAssignmentDto`s. Only after the tier is correct,
  `DetachFromEntity` removals + `AttachToEntity` adds.
- **Running (live):** **publish** `AttachInstanceBlueprintEvent`/`RemoveInstanceBlueprintEvent` to `world.Bus` (§7);
  the `Input`-phase system applies them next frame, before `Simulation` — no race with `BlueprintTickSystem`. The
  §7 system applies **removals before additions**, so an in-place swap reuses freed capacity rather than forcing a
  tier upgrade. (A net add that genuinely overflows still triggers a legitimate `BlueprintMaintenanceSystem`
  upgrade next frame — fine at runtime.)

**Why safe mid-exercise:** the UI only *stages*; it mutates at a quiescent point (paused, with explicit
pre-provision) or defers to `Input` (running). Unmanaged memory is never written mid-tick. The Reality scan keeps
reflecting live state (incl. self-detach via §8), so the diff stays honest.

**Override editing (future, §6):** authored in this panel; deferred until the UX is designed. MVP = assignment
add/remove only.

### 12.4 The "complete view" across tiers (multi-tier fragmentation)

Because one entity's blueprints can be spread across `BlueprintBlackboard{1024,4096,16384}`, inspecting the raw
components separately never gives a whole picture. The design resolves this with **tier-abstracted surfaces** — the
designer never decodes or pieces together the raw components:

- **"What blueprints are assigned" (unified, all tiers):** the **Entity Blueprints panel** (§12.2) — its Reality
  scan already walks all three tiers into one list. (New, this design.)
- **"A blueprint's live variables / latent cursor" (unified, tier-abstracted):** the **`BlueprintRuntimeInspectorPane`**
  — **already exists** (Hrot.Blueprints.Editor/Inspector; sibling of the HSM/BTree panes, hosted in the shared
  `RuntimeInspectorWindow`). It calls `IBlueprintDebugSession.CaptureLiveState` each frame, which scans the
  partition tables across tiers, finds the active blueprint's slot, and projects the bytes into a clean field
  table. **No new work** — reuse it.
- **At-a-glance per-tier summary (in the Entity Inspector):** the §12.1 custom renderers (new) — secondary, replaces
  the byte-dump; not the primary "complete view."

(An "Instance Inspector" read-write mutation window was mentioned by the architect but **not found** in the tree —
treat as unverified; not relied upon by this design. Live variable *mutation* is out of scope here.)

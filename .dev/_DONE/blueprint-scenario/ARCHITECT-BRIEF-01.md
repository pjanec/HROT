# Blueprint ↔ Entity assignment & scenario persistence — early design brief

Aligning early so we design from a shared base. This captures the **decisions we've made**, the **code-reality
corrections** (verified against the current tree), and **focused questions** for you. Please push back where the
design intent differs, but note that where a code-level claim and the current source disagree, we go with the
source.

## Goal

Two ways to assign Instance Blueprints to entities, plus runtime mutation:
1. **Static** — authored in the scenario file (multiple blueprints per entity).
2. **Dynamic** — assigned/removed/replaced mid-simulation (from an FDP event or a blueprint action node).

AiPrimitive (behavior) assignment is a separate, already-working path (TKB `DefaultBehaviorHash` static;
`AssignBehaviorEvent`/`ClearBehaviorEvent` dynamic) — we are **not** changing it.

## Decisions made (please align to these)

1. **Do not serialize blackboard bytes into scenarios — confirmed a current bug.** `BlueprintBlackboard1024/4096/
   16384` carry `[ComponentId]` but **no `[DataPolicy]`** (the behavior blackboards *are* `NoSave`), so today they
   serialize latent cursors/tick counters into scenario JSON. We will mark them `[DataPolicy(DataPolicy.NoSave)]`
   and persist a **declarative assignment** instead. Checkpoints/Flight-Recorder still capture the live bytes.
2. **Declarative path = the engine's intent pattern.** A `[DataPolicy(DataPolicy.Transient)]` managed
   `InitialBlueprintsIntent` component holding a **list** of `BlueprintAssignmentDto`, written/read by a
   `BlueprintStateTranslator : IEntityScenarioTranslator` (`Extract` walks the slot table; `Inject` writes the
   intent), materialized by a system that funnels through the shared attach seam and then removes the intent.
   Mirrors `InitialPassengersIntent` + `GenesisMaterializationSystem`.
3. **MVP = assignment-only; format leaves the door open for per-instance variable overrides.**
   `BlueprintAssignmentDto` will carry an **optional** overrides map (empty in MVP); on load we always run
   `InitDefault`. We're deferring overrides because the *authoring UX* ("where do you edit a per-instance
   override?") isn't settled — but the DTO/format won't need to change to add them later.
4. **No UI-unification work.** There is no TKB editing UI today (behaviors live in JSON), so the "two static
   mechanisms confuse authors" concern doesn't exist. Keep the AiPrimitive (TKB) and Instance (intent) static
   paths separate at both backend and authoring.
5. **Mid-runtime remove/replace is in scope now** (not deferred): defined as events + a consuming system +
   blueprint-action-node access, funneling through detach/attach.

## Code-reality corrections (verified)

- **Host is CGF, not SimHost.** `CgfSubsystem` registers the `GenesisMaterializationSystem` (`CgfSubsystem.cs:354`)
  and `CgfScenarioLoadHandler` drives scenario load. So the blueprint **materialization system + translator live in
  CGF's genesis path** (the materialization *class* may sit in SimHost today, but CGF owns it).
- **The attach/detach seam must live in core (`Fdp.Toolkits.Blueprints`), NOT the editor.** `BlueprintAttachService`
  is currently in **`Hrot.Blueprints.Editor.Runtime`** and takes an authoring **`BlueprintAsset`**. CGF must not
  depend on the Blueprints *Editor* assembly. The low-level primitives it wraps (`BlueprintBlackboardPartitions.
  TryAttach`/`TryDetach`/`Initialize`, `BlueprintTickSystem`, the tier-upgrade `BlueprintMaintenanceSystem`) are
  **already in `Fdp.Toolkits` core**. So: add the unified attach seam to `Fdp.Toolkits` (keyed by **`BlueprintId`/
  `AssetId` GUID + registry `def`**, not the authoring asset); the editor's existing service becomes a thin
  forwarder. Both the editor and CGF/genesis then funnel through the same core seam.
- **Detach already exists** (`BlueprintBlackboardPartitions.TryDetach`, core) — good; the replace path is
  detach-then-attach on the same tick, as you described.

## Proposed module placement

| Piece | Lives in |
|---|---|
| `BlueprintAssignmentDto`, `InitialBlueprintsIntent` (`[Transient]`) | core/common (shared by translator + materializer) |
| Unified `AttachToEntity` / `DetachFromEntity` seam (by `BlueprintId`) | **`Fdp.Toolkits.Blueprints`** (core) |
| `BlueprintStateTranslator : IEntityScenarioTranslator` | CGF scenario path |
| `BlueprintMaterializationSystem` (intent → attach → remove intent) | CGF genesis |
| `Remove/ReplaceInstanceBlueprintEvent` + consuming system | core (so blueprint action nodes can publish) |

## Focused questions for you

1. **Seam placement:** agree the unified attach/detach seam belongs in `Fdp.Toolkits.Blueprints` (core), with the
   editor service reduced to a forwarder? Any reason it must instead live in CGF?
2. **Slot enumeration for `Extract`:** `BlueprintBlackboardPartitions` exposes `TryGetSlotOffset(id)` but I don't
   see an "enumerate all attached `BlueprintId`s on an entity" API. Is there one, or do we add a slot-table iterator?
3. **Future per-instance overrides:** what's the intended mechanism to (a) apply authored overrides after
   `InitDefault` and (b) on save, *diff* the live blackboard against `InitDefault` to capture only changed
   variables — presumably via the compiled state layout / `DebugMap`-style field map? We want the MVP DTO shaped so
   this slots in without a format change.
4. **Mid-runtime command surface:** confirm the event names/shape (`RemoveInstanceBlueprintEvent`,
   `ReplaceInstanceBlueprintEvent`) and the phase they're consumed in. Is there an existing precedent for a
   **blueprint action node publishing an FDP event** (so a running blueprint can attach/detach another), or is that
   new wiring?
5. **Identity & load resilience:** store the **`AssetId` GUID** in the DTO (resolved to the runtime `BlueprintId`
   via the registry at materialization). On load, if the blueprint isn't registered, we **skip + log** rather than
   fail the scenario — agree?
6. **Tier growth at load:** attaching N blueprints to one entity may cross tier boundaries (1024→4096→16384). The
   materializer should attach in an order/most-fitting tier to avoid needless `BlueprintMaintenanceSystem` upgrades
   — any guidance on ordering, or is "attach largest-state-first" sufficient?

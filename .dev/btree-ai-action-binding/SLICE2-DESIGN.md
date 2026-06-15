# Slice 2 — Design: multiple stateful AI primitives per entity

> **Status:** **architect-approved** (2026-06-15, incl. the three §10 mandated fixes). FUTURE / design-capture — implement **after** Slice 1 demos. Canonical version: `docs/blueprints/BTree_AiActionParameterBinding_Detailed_Design.md` §4.
> **One-line:** lift the Slice 1 constraint of "one stateful AiPrimitive working-state per entity" by moving working state into a **Blueprint-owned, partition-allocated** component, keyed by `BlueprintId` — without touching the engine's BTree/HSM kernels.

## 1. The constraint and why it exists (verified)
A stateful AiPrimitive's **WorkingState** (local variables, latent cursors) is projected **inline over the engine's `Blackboard1024`**, at `Memory + 8` (just past an 8-byte `StructureHash` header). The generated `BTreeTick`/`BTreeEvaluate` thunk does, each tick:
```
ulong storedHash = *(ulong*)memory;
if (storedHash != StructureHash) { zero(memory); *(ulong*)memory = StructureHash; InitDefaultWorkingState(memory + 8); }
ref var ws = ref Unsafe.AsRef<WorkingState>(memory + 8);
```
([AiPrimitiveEmitter.cs:156–193](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/AiPrimitiveEmitter.cs#L156)). With **two** stateful primitives on one entity, their `StructureHash`es differ → every tick the check mismatches → hard reset + zero → mutual thrash. Hence: **exactly one stateful working-state primitive per entity in Slice 1.** Stateless primitives (params only, no WorkingState) are unaffected and unlimited.

Codified in: `docs/blueprints/Blueprint_Subsystem_Architecture_v1.2.md` (Slice 1 constraint); `docs/blueprints/Blueprint_Subsystem_Slice2_Candidates.md` Theme C / item **C1** "AiPrimitive concurrent working-state per entity"; Roadmap v1.1 ranks the allocator as the **#1** Slice 2 task.

## 2. The plan
Move AiPrimitive working state **out of** the shared engine `Blackboard1024` into a **Blueprint-owned component managed by a partition allocator**. The architect explicitly **rejected** retrofitting a partition allocator onto the engine's `Blackboard1024` (it is used internally by the FastHSM/BTree kernels; altering its layout would ripple across the engine).

**Decision (user, 2026-06-15): Option β.** Merge AiPrimitive working-state allocations into the existing `BlueprintBlackboard{1024,4096,16384}` tiers — AiPrimitive thunks look up their slots exactly like Instance dispatch does today. **No dedicated `BlueprintAiWorking1024` component** (Option α rejected — avoids a parallel allocator/component and reuses the most proven machinery).

The change is contained **entirely within the Blueprint subsystem**; the BTree/HSM **kernels stay unchanged** (they still pass a `Blackboard1024*` to the thunk, which the thunk ignores for working state).

## 3. The thunk change (kernels untouched)
The kernel keeps passing `Blackboard1024*`; the generated thunk **ignores it for working state** and instead:
1. Uses the entity reference (`ctx.Self`) it already receives.
2. Fetches the new Blueprint-owned component.
3. Does a partition lookup `BlueprintBlackboardPartitions.TryGetSlotOffset(memory, slotKey, out int payloadOffset)`. **The slot key is NOT plain `BlueprintId`** — the same stateful blueprint used by multiple BTree nodes needs a distinct slot **per node instance**; see §6.2.
4. Projects its WorkingState over `memory + payloadOffset` (its isolated slice).

Cost: **+1 dictionary/component lookup per AiPrimitive tick** — deemed negligible on the hot path. Result: multiple distinct blueprint functions safely keep their own params, locals, and latent cursors on the same entity, no collisions.

## 4. Reusable infrastructure (already exists — Slice-1-proven)
- `BlueprintBlackboardPartitions.{Initialize, TryAttach, TryDetach, TryGetSlotOffset, GetSlotCount, GetSlot, CopyToLargerTier}` — `FDP/Toolkits/Fdp.Toolkits/Blueprints/Partitioning/BlueprintBlackboardPartitions.cs` (used by Instance dispatch, breakpoints, blueprint-scenario).
- `BlueprintBlackboard{1024,4096,16384}` tier components + tier-selection-by-aggregate-size.
- `BehaviorIngressSystem` provisioning pattern (mirror it to add/size the working-state component on assignment).
- `BlueprintAttachService.AttachToEntity` / re-pack flow (from blueprint-scenario).

So Slice 2 is mostly **wiring proven pieces** + the thunk emission change + provisioning, not new allocator R&D.

## 5. Slice 2 demo specifications
Goal: prove **multiple stateful** primitives coexist per entity with isolated state.
- **Demo S2-1 — two stateful counters.** A stateful "increment-with-internal-cursor" primitive instantiated **twice** on one entity (two nodes, two WorkingState slices). Each maintains its own cursor; assert no cross-contamination (one resetting/advancing does not touch the other).
- **Demo S2-2 — stateful "wait N ticks then succeed" reused with different N.** Same primitive, two nodes with different params + independent latent cursors; assert each completes on its own schedule.
- **Demo S2-3 — mixed stateless + multiple stateful.** Combine Slice 1 stateless actions with ≥2 stateful primitives; assert stateless bindings (baked offsets in `BrainBlackboard`) and stateful slices (partitioned working component) coexist.
- **Observation:** runtime proof tests (tick, assert per-slice WorkingState) + live inspector (extend a renderer to show the partitioned working component, or reuse `Blackboard1024Renderer` pattern for the new component).
- **Migration/tier check:** assert tier upgrade (e.g. 1024→4096) preserves existing slots when a 4th stateful primitive is added (exercise `CopyToLargerTier`).

## 6. Decisions & open questions (updated 2026-06-15 from user)
1. **α vs β — RESOLVED: β.** Merge into `BlueprintBlackboard*` tiers; no dedicated component.
2. **Slot identity — REFRAMED (the key design problem).** Correction: there is **no "BTree-authored action."** The BTree knows nothing about blueprints; blueprints are edited separately, and a BTree node simply *references* a blueprint action/condition. The real case is **the same stateful blueprint action used by multiple BTree nodes** → two independent instances on one entity. So keying the partition slot by `BlueprintId` alone is **insufficient** (both nodes map to one slot → the collision returns).
   **Leading proposal (verify with architect):** the BTree *composition layer* assigns a distinct **working-slot id per stateful node instance** and bakes it into that node's generated per-node adapter thunk — generalizing the Slice 1 baked-param-offset approach (the BTree already emits a distinct thunk per node binding, keyed by the param offset; it can bake a working-slot id too). The blueprint provides `TickCore(ref Params, ref WorkingState, self, world, time)`; the BTree-generated adapter projects Params at the bin-packed param offset **and** WorkingState at the node's partition slot, then calls `TickCore`. (The blueprint's own `BTreeTick`/`Memory+8` path stays the *standalone* blueprint-as-behavior hosting.) Slot key ≈ `(behavior/asset, stateful-node-id)`; the allocator is provisioned with one fixed-size slot per stateful node instance.
3. **Working-state sizing — RESOLVED.** Sizes are **statically known up front** from each blueprint's variable declarations (its `WorkingState` struct). The partition allocator places each instance's fixed-size slot; tier selection by aggregate size (reuse Instance-dispatch logic). Latent/await cursors live in that same per-instance slice.
4. **Shared working state → §7.** Per-instance working state is always isolated; *shared* mutable state across nodes/actions is a separate "shared blackboard" concept with its own design pass.
5. **Provisioning trigger** — when the working component is added/sized (assignment time; aggregate over all stateful node instances the behavior can run). Mirror `BehaviorIngressSystem` + blueprint-scenario aggregate pre-provisioning.
6. **Hot-reload** — slot table must survive / re-derive across ALC swaps (interacts with `BlueprintBlackboardPartitions` + `StructureHash`).

## 7. Shared working state — the "shared blackboard" concept (its own design pass)
Per-instance working state (§2–§6) is isolated by construction. But complex behaviors need **shared mutable state** — multiple actions/nodes (and sometimes multiple entities) coordinating through common variables (e.g. an early action computes a target a later action consumes; squad members reading/writing shared tactical state). The user flagged this as crucial; it deserves a dedicated design pass, not a bolt-on.

Distinctions to settle:
- **Shared params vs shared working state.** Shared *input* params already work via Slice 1 **aliasing** (multiple nodes → same variable → same baked offset → one slice). What's new here is shared *mutable scratch/working* state that is not any single node's private `WorkingState`.
- **Scope.** behavior-scope (shared across the tree's nodes) / squad-scope / global. Likely modeled as a named, typed shared region that actions bind to (read/write), distinct from private `WorkingState`.
- **Lifetime & ownership.** Who provisions/zeros it; when it resets; whether it persists across behavior changes.
- **Cross-entity.** Relates to the known "blueprints can write other entities mid-tick" theme (memory `project-blueprint-cross-entity-sync-mutation`) — a shared blackboard may span entities (squad), which has snapshot/determinism implications.
- **Mechanism candidates.** A dedicated shared `BlueprintBlackboard*` slot keyed by a *shared id* (not per-node); or a separate shared-state component; or elevating selected variables to "shared/behavior-scope" in the authoring model so the allocator gives them one slice that all bindings alias.

**RESOLVED (architect + user, 2026-06-15):**
- **First iteration = single-behavior, single-entity shared scratch** (scoped to the nodes of one executing tree on one entity), living in the same `BlueprintBlackboard*` tier → synchronous, zero-allocation inline mutation. A multi-entity shared blackboard would require routing writes through an `EntityCommandBuffer` (one-frame latency) and breaks synchronous determinism — so **not** for v1.
- **Squad-level sharing uses the virtual squad-leader entity's blackboard** (user, 2026-06-15). The "virtual leader" is an existing concept (hardcoded hill-attack commander; `Hrot.SquadCoordination`). Squad members **read** the leader entity's blackboard; the leader writes its own synchronously — sidestepping cross-entity mid-tick writes / `EntityCommandBuffer` latency. This is the intended squad-scope mechanism, distinct from behavior-scope scratch, and relates to memory `project-blueprint-cross-entity-sync-mutation` (read-via-snapshot, not synchronous cross-entity write).

## 8. Resume checklist (when starting Slice 2)
1. Read `docs/blueprints/Blueprint_Subsystem_Slice2_Candidates.md` Theme C/C1 + Roadmap v1.1.
2. Confirm the **β** decision (§2) and resolve the **slot-identity** proposal (§6.2: per-stateful-node-instance slot id baked into the BTree adapter thunk, calling the blueprint's `TickCore`) with the architect.
3. Spec the thunk change: BTree composition emits a per-node adapter (param offset + working-slot id) calling `TickCore`; provisioning adds/sizes the `BlueprintBlackboard*` tier and allocates one slot per stateful node instance. Kernels untouched.
4. **Apply the three §10 mandated fixes** before/with provisioning: synchronous `Input`-phase tier upgrade (Flaw 1); hot-reload via re-published `AssignBehaviorEvent` rather than inline `ResetSlot` (Flaw 2); cross-region validator hard-errors concurrent execution of the same stateful Subtree (Flaw 3). Then wire provisioning + tier selection (aggregate over **reachable** stateful node instances).
5. Build demos S2-1..S2-3 + proof tests.
6. (Separate) open the **shared-blackboard** design pass (§7) when a behavior needs shared mutable state.

## 9. Architect review (2026-06-15) — confirmations & resolutions
Reviewed by architect; reconciled against code (✓ = verified this session).
- **Slot identity — CONFIRMED (our reframing was right).** `BlueprintId` alone collides when the same stateful blueprint is used by multiple nodes. The allocator key is **strictly a 32-bit `int`** (no composite key — `BlueprintSlotEntry` packs to 16 B). **Synthesize a unique id per node instance = FNV-1a hash of `(BehaviorAssetId, NodeVisualId)`**, bake it into the per-node adapter thunk, use it as the slot key.
- **adapter-calls-`TickCore` — CONFIRMED for both params + working state.** Adapter projects `Params` (bin-packed offset over `BrainBlackboard`) + `WorkingState` (node partition slot over `BlueprintBlackboard*`), then calls the blueprint's shared `TickCore`.
- **Provisioning (Q1) — CONFIRMED: static worst-case at assignment.** No lazy mid-tick allocation (ECS structural changes are forbidden mid-`Simulation` without an `EntityCommandBuffer`; a mid-tick tier upgrade would drop ticks). `BehaviorIngressSystem` sums all **reachable** stateful nodes' state sizes, pre-provisions the correct tier component, and eager-allocates every slot.
- **Behavior change (Q2) — CONFIRMED.** On `AssignBehaviorEvent` (Input phase), `BehaviorIngressSystem` `TryDetach`es the old behavior's stateful slots before attaching the new (dense-compact + coalesce) — no slot leak.
- **Hot-reload (Q2) — CONFIRMED (✓ ResetSlot/InstanceVersion exist).** Slots survive ALC swaps; on `StructureHash` mismatch the thunk calls `BlueprintBlackboardPartitions.ResetSlot` (zero payload + bump `InstanceVersion`, keep the allocation). Cursor implicitly resets to `{ResumeAt=0, InstanceVersion=0}`.
- **Latent/await (Q5) — CONFIRMED.** A 16-byte `BlueprintLatentCursor` is emitted at **offset 0** of `WorkingState`; lives in the partition payload → survives ticks + soft reloads.
- **Mixing stateless + stateful (Q6) — CONFIRMED: no hazard.** Disjoint memory — `Params` over `BrainBlackboard` inline; `WorkingState` over the `BlueprintBlackboard*` partition slot. The adapter does both projections sequentially before `TickCore`.
- **Shared blackboard (Q4) — see §7 (resolved):** first iteration = single-behavior single-entity scratch in the `BlueprintBlackboard*` tier; squad-scope via the **virtual-leader entity's blackboard** (read by members).

> **Note:** the provisioning (Q1) and hot-reload (Q2) answers above are **refined/corrected by §10** (a later architect review found three hazards). §10 is authoritative where it conflicts.

## 10. Slice 2 hazards & mandated fixes (architect review, 2026-06-15) — MUST address before implementing
The architect greenlit Slice 1 but flagged three severe ECS-lifecycle/hot-reload hazards in the Slice 2 plan. These corrections are mandatory. (Referenced systems verified to exist: `BlueprintTickSystem`, the cross-region conflict validator `WouldCreateCrossRegionConflict`/`GetParallelRegionMap`, `BlueprintMaintenanceSystem`/`BeforeSync` — re-confirm exact phase wiring at kickoff.)

### Flaw 1 — Tier-upgrade race (provisioning latency). *Supersedes §6.5/§9 "provisioning".*
If the entity already has a smaller tier (e.g. `BlueprintBlackboard1024` from an existing Instance blueprint) and the new BTree's stateful nodes exceed it, the tier must upgrade. A **deferred ECB** upgrade runs in `BlueprintMaintenanceSystem` (`BeforeSync`), but `BehaviorIngressSystem` runs in `Input` and the BTree kernel ticks in the **same frame's** `Simulation` phase — so deferred upgrade ⇒ thunks run before the tier exists ⇒ missing slots ⇒ fatal crash.
**Fix:** `BehaviorIngressSystem` performs the tier upgrade **synchronously in the `Input` phase** — direct `repo.AddComponent` + `CopyToLargerTier` + `repo.RemoveComponent`. The `Input` phase is outside the parallel `Simulation` lock, so synchronous structural mutation is safe and guarantees the tier exists before the BTree ticks.

### Flaw 2 — Hot-reload ghost slot (memory corruption). *Supersedes §6.6/§9 "hot-reload".*
BTree synthetic slot keys (`FNV-1a(BehaviorAssetId, NodeVisualId)`) are **not** registered as standalone Instance blueprints, so `BlueprintTickSystem`'s reconcile (walk slot table → look up `BlueprintId` in `BlueprintRegistry` → compare `StructureHash`) **ignores them**. On a **Hard Reload** that *grows* a stateful `WorkingState`, the old slot keeps its smaller `PayloadSize`; the new thunk projects a larger struct over it → silently overwrites the adjacent slot. `ResetSlot` can't help (it zeroes, doesn't resize).
**Fix:** do **not** rely on inline `ResetSlot` for BTree working states. When `AiHotReloadCoordinator` detects a Hard Reload of a BTree asset, it must **re-publish `AssignBehaviorEvent`** for every entity running that behavior, forcing `BehaviorIngressSystem` to `TryDetach` the old ghost slots (dense-compact the tier) and re-provision correctly-sized slots from scratch.

### Flaw 3 — Synthetic-key concurrency collision (subtree hazard). *Refines §6.2.*
`FNV-1a(BehaviorAssetId, NodeVisualId)` is unique within one asset's execution — but if an HSM runs the **same Subtree** concurrently in two orthogonal parallel regions, the stateful nodes inside compute the **same** synthetic key for both → both project `WorkingState`/`BlueprintLatentCursor` over the same slot → race-write corruption.
**Fix:** extend the visual editor's **Cross-Region Conflict Validator** (the existing `WouldCreateCrossRegionConflict` path) to treat a stateful Subtree as a mutating writer of its synthetic keys, and **hard-error** on concurrent execution of the same stateful Subtree across parallel regions.

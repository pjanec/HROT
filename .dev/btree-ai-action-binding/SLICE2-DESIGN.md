# Slice 2 — Design: multiple stateful AI primitives per entity

> **Status:** FUTURE / design-capture only. Do **after** Slice 1 demos. Written 2026-06-15 from the architect's design talk + code verification, so we can resume without re-analysis.
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

Two options (decide at Slice 2 start):
- **Option α — new `BlueprintAiWorking1024` component** (mirrors the 1024-byte blackboard), using the **partition allocator already proven in Slice 1**.
- **Option β — merge** AiPrimitive working-state allocations into the existing `BlueprintBlackboard{1024,4096,16384}` tiers, so AiPrimitive thunks look up slots exactly like Instance dispatch does today.

Both contain the change **entirely within the Blueprint subsystem**; the BTree/HSM **kernels stay unchanged** (they still pass a `Blackboard1024*` to the thunk).

## 3. The thunk change (kernels untouched)
The kernel keeps passing `Blackboard1024*`; the generated thunk **ignores it for working state** and instead:
1. Uses the entity reference (`ctx.Self`) it already receives.
2. Fetches the new Blueprint-owned component.
3. Does a partition lookup `BlueprintBlackboardPartitions.TryGetSlotOffset(memory, BlueprintId, out int payloadOffset)` keyed by its own `BlueprintId`.
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

## 6. Open questions / decisions for Slice 2
1. **α vs β** — dedicated `BlueprintAiWorking1024` (clean isolation) vs merge into `BlueprintBlackboard*` (fewer components, shared allocator). Architect presented both; pick at kickoff.
2. **BlueprintId for authored (non-blueprint) stateful BTree nodes** — the partition lookup is keyed by `BlueprintId`. If a stateful action is authored directly in the BTree (not as a separate blueprint), what identity keys its slot? (Per-node id? Synthesised blueprint id?) Resolve before wiring.
3. **Latent cursors / `await`-style nodes** — confirm latent execution state lives in the same partitioned WorkingState slice and survives across ticks.
4. **Aliasing × working state** — Slice 1 aliasing is about *params* (shared input slice). Does any sharing apply to *working* state, or is working state always per-node-instance? (Likely always isolated.)
5. **Provisioning trigger** — when exactly is the working component added/sized (assignment time, aggregate over all stateful primitives the behavior may run)? Mirror `BehaviorIngressSystem` + the blueprint-scenario aggregate pre-provisioning.
6. **Hot-reload** — slot table must survive / re-derive across ALC swaps (interacts with `BlueprintBlackboardPartitions` + `StructureHash`).

## 7. Resume checklist (when starting Slice 2)
1. Read `docs/blueprints/Blueprint_Subsystem_Slice2_Candidates.md` Theme C/C1 + Roadmap v1.1.
2. Decide α vs β (§6.1) and the authored-node `BlueprintId` question (§6.2).
3. Spec the thunk emission change in `AiPrimitiveEmitter` (replace `Memory+8` with `TryGetSlotOffset(BlueprintId)` projection) — kernels untouched.
4. Wire provisioning of the working-state component + tier selection.
5. Build demos S2-1..S2-3 + proof tests.

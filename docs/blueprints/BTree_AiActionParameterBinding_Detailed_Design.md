# BTree AI Action/Condition Parameter Binding — Detailed Design

> **Status:** architect-approved (Slice 1 and Slice 2, 2026-06-15). Describes the **future state** after both slices are implemented.
> **Scope:** how authored (JSON, `Managed==true`) behavior trees bind multiple AI actions/conditions — each with its own parameter DTO — to editor-managed blackboard variables, with correct non-zero bin-packed offsets and (Slice 2) per-node working state.
> **Working drafts / demo specs:** `.dev/btree-ai-action-binding/` (SLICE1-DESIGN, SLICE2-DESIGN, ARCHITECT-REVIEW-BRIEF). This doc is the canonical aggregation.
> **Related canonical docs:** `Blackboard_Authoring_Detailed_Design.md` (Category-2 variables, bin-packing, cross-region validation), `Blackboard_Authoring_Addendum_v3_ActionParamAuthoring.md` (whole-DTO binding, node-owned variables), `Blueprint_Subsystem_Architecture_v1.2.md` (AiPrimitive, partition allocator, the Slice-1 working-state constraint this design lifts), `BTree_HSM_JSON_Persistence_Detailed_Design.md` (the `[BlueprintRegistrar]` masquerade registrar, D14), `Blueprint_Subsystem_Slice2_Candidates.md` §C1 (the candidate this realizes).

## 1. Goal & motivation
A real behavior composes many actions/conditions, each with a **different** parameter DTO (and the same action may be reused with **different** DTO instances). Binding every DTO to offset 0 of `BrainBlackboard` is useless. This design makes the authored/JSON path bind each node to its own **bin-packed, non-zero byte offset**, surfaced and edited in the visual blackboard authoring UI, with runnable demos. **Slice 1** covers multiple **stateless** actions/conditions; **Slice 2** lifts the "one stateful AiPrimitive working-state per entity" constraint.

## 2. Shared memory model (ground truth)
- `BrainBlackboard` (128 B): `BehaviorParameters = fixed byte[100]` at offset 0; interrupt/tail registers at 120–127. `MaxBehaviorParamByteSize=100` (master params must fit inline; oversize → `FDP_001` hard compiler error).
- A reusable action/condition is `static NodeStatus M(ref TDto p, ref BehaviorTreeState, ref BTreeContext)`; its DTO is projected via `Unsafe.As<…>(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters, (nint)offset))` at a **baked, bin-packed** byte offset (the legacy `paramIndex * sizeof(Params)` form is superseded; `paramIndex` is ignored on this path).
- `BlackboardBinPacker` computes sequential, C#-alignment-padded offsets (`MaxInlineBytes=100`, `MaxHeavyBytes=928`). The **authoritative** offset is the compiled struct layout (`Marshal.OffsetOf`); the editor bin-packer is advisory for the budget UI but must replicate C# sequential layout exactly (natural alignment capped at 8; padding before `fixed`/`[InlineArray]`).
- Overflow beyond 100 B inline → `Blackboard1024` heavy tier (`BehaviorDefinition.HeavyDtoType` + `[SharedAiHeavyAction]`).
- **Blueprints (AiPrimitives) are the primary authoring source** of actions/conditions; they compile to the **same** projection/memory model as hardcoded `[SharedAiAction]` methods (Params→`BrainBlackboard`, WorkingState→tiered component, registered into `BehaviorRegistry`), so blueprint-authored and hardcoded actions are interchangeable at the binding layer.

## 3. Slice 1 — stateless multi-action binding (architect-greenlit)

### 3.1 Authoring model
Editor-managed (Category-2) blackboard variables, each the **whole parameter DTO** of one action/condition (per `Blackboard_Authoring_Addendum_v3` §2.2 whole-DTO binding). A node binds its entire DTO param to exactly one variable via `ExpressionTargetField`. **"+ Promote to new variable"** creates an auto-managed (`IsAutoManaged`) variable for static-input-only actions. **Aliasing:** two nodes → same variable → same baked offset → zero-copy sharing; the editor must clearly distinguish *aliasing one shared DTO* from *distinct instances of the same DTO type* (the bin-packer reserves bytes uniquely for the latter). Hardcoded action DTOs are surfaced **read-only** (Category-1) via `ActionSchemaExporter` (first `ref` parameter type of the registered method).

### 3.2 Codegen
For each managed BTree asset the JSON generator emits:
1. a per-asset blackboard **struct** from the authored variables (reusing `BlackboardDtoEmitter`), `[StructLayout(Sequential)]`, bin-packed ≤100 B. **`bool` fields MUST be decorated `[MarshalAs(UnmanagedType.I1)]`** — `Marshal.OffsetOf` defaults `bool` to a 4-byte `BOOL` while the bin-packer/managed layout use 1 byte, so omitting it silently drifts offsets and corrupts Flight-Recorder/replay schemas;
2. the **topology over that struct**, so each binding compiles to a blob key `{Type}.{Method}@{offset}`;
3. a per-asset **`[BlueprintRegistrar]` masquerade registrar** (the D14 pattern from `BTree_HSM_JSON_Persistence_Detailed_Design.md`) of `ref BrainBlackboard` thunks keyed identically, each `Unsafe.As`/`AddByteOffset` projecting the DTO at its baked offset and calling the method.

**Composition model — "BTree owns layout, blueprint provides `TickCore`".** When the bound action is a blueprint AiPrimitive, the BTree generator **ignores** the blueprint's standalone `BTreeTick` (which uses `paramIndex*sizeof`) and emits a per-node **adapter** that projects `Params` at the BTree-controlled bin-packed offset (and, in Slice 2, `WorkingState` at the node's partition slot), then calls the blueprint's `TickCore(ref Params, ref WorkingState, self, world, time)`. The runtime stays `Interpreter<BrainBlackboard, BTreeContext>` (no generic-runtime change).

### 3.3 Validator
Unblock `ThreeParamReusable` when the method has the 3-param reusable shape and `ExpressionTargetField` resolves to an authored variable whose declared type equals param-0's DTO type (FQN string equality — no implicit subtyping; reference catalog keys `{DtoTypeFqn}::{FieldName}`). Otherwise emit a `BTREE0002` skip (never a build break). Defaults are baked into a generated `ParseParamsDelegate` (editor `DefaultValueJson` → static defaults, scenario JSON overlays at runtime, `Unsafe.Write` into the inline slot).

### 3.4 Constraints
Aggregate of all bound master DTOs must fit the 100 B inline region (else `FDP_001`). Exactly **one stateful** AiPrimitive per entity in Slice 1 (lifted by Slice 2, §4).

## 4. Slice 2 — multiple stateful primitives per entity (architect-approved)
Lifts the Slice-1 constraint (caused by inline working-state collision at `Blackboard1024 Memory+8` / single `StructureHash`).

### 4.1 Storage — Option β (no new component)
Move AiPrimitive **WorkingState** out of the engine `Blackboard1024` into the existing **`BlueprintBlackboard{1024,4096,16384}` tiers**, allocated by the Slice-1-proven `BlueprintBlackboardPartitions`. (Option α — a dedicated `BlueprintAiWorking1024` — was rejected.) Kernels are untouched: the generated thunk ignores the kernel's `Blackboard1024*`, fetches the Blueprint-owned tier component via `ctx.Self`, does `BlueprintBlackboardPartitions.TryGetSlotOffset(...)`, and projects WorkingState at its slot. Working-state sizes are statically known from each blueprint's `WorkingState` declarations; a 16-byte `BlueprintLatentCursor` sits at offset 0 of `WorkingState`.

### 4.2 Slot identity
The same stateful blueprint used by multiple nodes must get a distinct slot per node (so `BlueprintId` alone is insufficient). The allocator key is a 32-bit int, so the composition layer synthesizes a unique key = **`FNV-1a(BehaviorAssetId, NodeVisualId)`**, baked into the per-node adapter thunk. The adapter projects `Params` (bin-packed offset over `BrainBlackboard`) **and** `WorkingState` (partition slot over `BlueprintBlackboard*`), then calls `TickCore` — disjoint memory regions, no interference with Slice 1 stateless params.

### 4.3 Three mandated fixes (architect, must be implemented)
1. **Tier-upgrade race.** Provision/upgrade tiers **synchronously in the `Input` phase** inside `BehaviorIngressSystem` (`AddComponent`+`CopyToLargerTier`+`RemoveComponent`) — never deferred ECB, because the BTree ticks the same frame's `Simulation` phase before `BlueprintMaintenanceSystem` (`BeforeSync`) would run, and a missing slot would crash. Sum **reachable** stateful nodes and pre-provision worst-case at assignment.
2. **Hot-reload ghost slot.** BTree synthetic slot keys are not registered Instance blueprints, so `BlueprintTickSystem`'s reconcile ignores them; a Hard Reload that *grows* a `WorkingState` would overflow the old slot and corrupt the neighbor (`ResetSlot` can't resize). Fix: on Hard Reload of a BTree asset, `AiHotReloadCoordinator` **re-publishes `AssignBehaviorEvent`** for affected entities, forcing `BehaviorIngressSystem` to `TryDetach` the old slots and re-provision correctly-sized ones.
3. **Concurrent-subtree collision.** The same stateful Subtree run in two orthogonal parallel regions computes the same synthetic key → race corruption. Fix: extend the cross-region conflict validator (`WouldCreateCrossRegionConflict`) to treat a stateful Subtree as a mutating writer and **hard-error** on concurrent execution across parallel regions.

### 4.4 Shared blackboard (separate, scoped)
Per-instance working state is isolated. For **shared** mutable state: the first iteration targets **single-behavior, single-entity scratch** in the same `BlueprintBlackboard*` tier (synchronous, zero-alloc). **Squad-scope** sharing uses the **virtual squad-leader entity's blackboard** (an existing concept — hill-attack commander / `Hrot.SquadCoordination`): members read the leader's blackboard, avoiding synchronous cross-entity writes / `EntityCommandBuffer` latency. Multi-entity synchronous shared mutation is explicitly out of scope (breaks determinism).

## 5. Demos
Stateless multi-action (Slice 1): a managed blackboard with ≥2 distinct-DTO variables at distinct offsets, each bound to a different action/condition, plus a `Repeater`; counter climbs to threshold then a condition fails; observed via a runtime proof test (mirroring blueprint `CountingDemo_ProofTests`) and live in `BrainBlackboardRenderer`/`BTreeVisualizerRenderer`. Stateful multi-primitive (Slice 2): the same stateful primitive at two nodes maintains independent state; mixed stateless+stateful coexist. Full demo specs in `.dev/btree-ai-action-binding/SLICE1-DESIGN §5` and `SLICE2-DESIGN §5`.

## 6. Cross-references / supersedes
- **`Blueprint_Subsystem_Architecture_v1.2.md`** — the "one AiPrimitive working-state per entity" Slice-1 constraint (§ around the partition-allocator discussion) is **lifted by Slice 2 here** via Option β (not a `Blackboard1024` allocator / `BlueprintAiWorking1024`).
- **`Blueprint_Subsystem_Slice2_Candidates.md` §C1** — realized by §4 (Option β, FNV-1a slot key, three fixes).
- **`Blueprint_Subsystem_Hot_Reload_Detailed_Design.md`** — §4.3 Fix 2 adds synthetic-key (BTree-hosted stateful) reconciliation, which the current reconciler does not cover.
- **`Blackboard_Authoring_Detailed_Design.md`** — Category-2 variables, bin-packing, and the cross-region validator (§4.3 Fix 3 extends it); the `bool [MarshalAs(I1)]` rule applies to the generated struct.
- **`BTree_HSM_JSON_Persistence_Detailed_Design.md` D14** — the `[BlueprintRegistrar]` masquerade registrar reused by §3.2.

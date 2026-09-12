# Architect review brief — BTree AI action/condition parameter binding

> Paste this to the architect. Recommended: attach `SLICE1-DESIGN.md` and `SLICE2-DESIGN.md` as sources, then paste **Part 1 (Slice 1)** for the first pass and **Part 2 (Slice 2)** for the second. Part 0 is shared context.

---

## Part 0 — Context

We're designing how **JSON-authored behavior trees** bind **multiple** AI actions/conditions — each with its **own** parameter DTO at its **own** blackboard offset — via the visual editor, with runnable demos. This builds on your earlier guidance (the AiPrimitive params/working-state model, the bin-packer, `Blackboard1024`, `FDP_001`, and the Slice-1 "one stateful working-state primitive per entity" constraint). We've split the work: **Slice 1** = multiple *stateless* actions/conditions (buildable now); **Slice 2** = multiple *stateful* primitives per entity.

**Already verified in code (please don't re-litigate — just flag if any is wrong):**
- Baked-offset projection is already emitted: `Unsafe.AddByteOffset(ref bb.BehaviorParameters, (nint)entry.Offset)` (BTreeActionGenerator SharedAi path); the legacy AiPrimitive `paramIndex * sizeof(Params)` form is superseded and `paramIndex` is ignored in the baked-offset pipeline.
- Blueprint AiPrimitive actions/conditions compile to the **same** memory/projection model as hardcoded `[SharedAiAction]` (Params→BrainBlackboard, WorkingState→Blackboard1024, registered into BehaviorRegistry).
- The JSON BTree bridge now injects a registry populated from the assembly's `[FbtRegistrar]` (so bound actions actually execute; previously every bound action fell back to `Failure`), and the live `CgfSubsystem` no longer discarded JSON behavior definitions.
- `BlueprintBlackboardPartitions.{TryAttach,TryDetach,TryGetSlotOffset,CopyToLargerTier}` is Slice-1-proven (Instance dispatch).
- Stateful AiPrimitive working state collides at `Blackboard1024 Memory+8`/single `StructureHash` → one stateful primitive per entity in Slice 1.

**What we want from you:** poke holes — especially edge cases that would bite during codegen or at runtime, and confirm (or correct) the two design choices we made independently of you (flagged below).

---

## Part 1 — Slice 1 (stateless multi-action binding)

**Design in brief.** For each managed BTree asset, the BTree generator produces: (a) a per-asset blackboard **struct** from the authored variables (each variable = one whole action/condition DTO), `[StructLayout(Sequential)]`, bin-packed ≤100 B; (b) the **topology over that struct** so each binding compiles to a blob key `{Type}.{Method}@{offset}`; (c) a per-asset **registrar** of `ref BrainBlackboard` thunks keyed identically that do `Unsafe.As<…,TDto>(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters, (nint)offset))` and call the method — registered into the injected registry. Offsets come from the **compiled** struct (`Marshal.OffsetOf`), not editor-predicted. Whole-DTO binding: a node binds its whole DTO param to one variable via `ExpressionTargetField`; "promote to new variable" creates an auto-managed variable; aliasing = two nodes → same variable → same baked offset → zero-copy. Runtime stays `Interpreter<BrainBlackboard>` (no genericity change). Validator unblocks `ThreeParamReusable` when param-0 type == the bound variable's type.

**Questions / please review:**
1. **(Most important — cross-cutting) Blueprint-sourced action vs BTree-controlled offset.** A blueprint AiPrimitive emits its own `BTreeTick` that projects Params via `paramIndex * sizeof(Params)` — i.e. it assumes it owns offset placement. But in a multi-action BTree, the **BTree** must control each node's bin-packed offset. Should the BTree generate a **per-node adapter** that calls the blueprint's `TickCore(ref Params, …)` at the BTree-controlled offset (ignoring the blueprint's `BTreeTick`), or should the blueprint's `BTreeTick` be made offset-parameterized? Is "BTree owns layout, blueprint provides `TickCore`" the intended composition model? (This is the seam between "blueprints are the primary source" and "BTree bin-packs the blackboard.")
2. **Is the per-asset struct the right home?** We generate a per-asset struct + per-asset registrar in the BTree generator. Do you prefer that, or extending the existing `[SharedAiAction]`/`BTreeActionGenerator` mechanism? Any reason the per-asset registrar shouldn't register into the same injected `ActionRegistry`?
3. **Offset authority / alignment.** We rely on `Marshal.OffsetOf` of the compiled generated struct (not the editor bin-packer's advisory offsets). Any alignment edge cases (Vector3/Vector4/Quaternion, mixed-size fields, padding) where the generated struct's layout and the bin-packer's budget UI could diverge and confuse a designer?
4. **Defaults / ParseParams for authored variables.** How should default values for editor-authored variables be written into `BehaviorParameters` at assignment — a generated `ParseParams` for managed assets, or per-variable `DefaultValueJson`? Any init/zeroing edge cases?
5. **Validator type-match across ALC/assemblies.** Param-0 DTO type identity vs the variable's declared type — concerns with hot-reload ALC boundaries, nested DTOs, enums?
6. **Category-1 read-only reflection.** Reflecting a *hardcoded* action's DTO fields into the Variables panel read-only — how do we reliably know which DTO a node binds (the bound method's param-0 type), and any concern reflecting arbitrary `[BlackboardDtoStruct]`s?
7. **Anything that breaks the byte-identity gate** on existing `Managed==false` assets? (We guard all emit changes behind `Managed==true`.)

---

## Part 2 — Slice 2 (multiple stateful primitives per entity)

**Design in brief (your plan + our refinements).** Move AiPrimitive working state out of the engine `Blackboard1024` into the existing **`BlueprintBlackboard*` tiers** (**Option β** — user chose this; no dedicated `BlueprintAiWorking1024`). The generated thunk ignores the kernel's `Blackboard1024*`, fetches the Blueprint-owned component via `Self`, does `BlueprintBlackboardPartitions.TryGetSlotOffset(...)`, and projects WorkingState at its slot. Kernels untouched. Working-state sizes are statically known from each blueprint's `WorkingState` declarations; allocator places fixed-size slots; tier by aggregate size.

**Two things we decided/refined independently — please confirm or correct:**
- **Slot identity (our reframing).** There is **no "BTree-authored action"** — the BTree only *references* blueprint actions. The real multiplicity case is **the same stateful blueprint action used by multiple BTree nodes** → keying the slot by `BlueprintId` alone collides. Our proposal: the BTree composition layer assigns a **per-stateful-node-instance working-slot id**, baked into that node's adapter thunk (generalizing the Slice 1 baked-offset approach), slot key ≈ `(asset, stateful-node-id)`. **Is this the right identity model, or do you intend something else (e.g. allocate per (BlueprintId, occurrence))?**
- This makes the **adapter-calls-`TickCore`** model (Part 1 Q1) the shared composition mechanism for both params and working state. Confirm.

**Questions / please review:**
1. **Provisioning under conditional branches.** A BTree may have stateful nodes in branches that don't all run. Do we provision one slot per *static* stateful node instance (worst case), or lazily on first execution? Tier-size implications.
2. **Slot lifetime across behavior change / hot-reload.** When the active behavior changes or an ALC swaps, how do per-node slots get released/re-derived without leaking or aliasing stale state?
3. **`TryGetSlotOffset` key.** Today it's keyed by `BlueprintId` for Instance dispatch. For per-node stateful slots we need a finer key — does the allocator support a composite/extra key, or do we synthesize a unique id per (asset, node) and register it as a distinct "blueprint id"?
4. **Shared blackboard (separate concept).** Per-instance working state is isolated; complex behaviors also need **shared mutable state** across nodes/actions (and maybe across entities — squad). We've parked this as its own design pass. What **scope** should the first shared blackboard target — single-behavior shared scratch, or squad/multi-entity — and does cross-entity sharing change the snapshot/determinism story?
5. **Latent/await nodes.** Confirm multi-tick "Running" cursors live in the same per-instance working slot and survive across ticks under the partitioned model.
6. Any edge case in **mixing** Slice 1 stateless (baked offsets in `BrainBlackboard`) with Slice 2 stateful (partitioned working component) in one behavior?

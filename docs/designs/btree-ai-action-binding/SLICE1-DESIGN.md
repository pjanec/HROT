# Slice 1 — Detailed Design: BTree action/condition parameter binding (+ demos)

> **Status:** design approved-in-principle (user + architect, 2026-06-15); **not yet implemented** (this workstream's binding codegen). Foundations from DEC-05 already landed — see §6.
> **Scope:** authored JSON behavior trees bind **multiple stateless** actions/conditions, each with its own parameter DTO at its own bin-packed blackboard offset, via the visual editor; runnable demos. **Out of scope (Slice 2):** multiple *stateful* primitives per entity — see [SLICE2-DESIGN.md](SLICE2-DESIGN.md).

## 1. Goal & motivation
A real behavior composes many actions/conditions, each with a different parameter DTO (and sometimes the same action reused with different DTO instances). Binding all of them to **offset 0** is useless. Slice 1 makes the authored/JSON path support **distinct, non-zero, bin-packed offsets** per node binding — the realistic case — and surfaces it in the editor with runnable proof.

## 2. Verified architecture (ground truth)
All confirmed in code + by the architect (2026-06-15); updated to the current occurrence-slot
model — see [`DESIGN_Occurrence_Scoped_Storage.md`](../../blueprints/DESIGN_Occurrence_Scoped_Storage.md)
§28–§30 for the full record.

1. **Memory.** No per-entity blackboard component. Behavior input params + reusable-action DTOs live in the **root params occurrence slot**, located by `RootParamsAccess` / `OccurrenceSlotKey.ComputeRootParamsKey(BehaviorState.ActiveBehaviorHash)` — computed, never stored; interrupt/tail registers live separately in `BrainInterrupts`, untouched. There is no fixed inline region or overflow split: the bound is per-behaviour (`RootParamsBytes(def)`), enforced structurally by the partition allocator, with a structural ceiling of the tier payload (up to ~16 KB on `BlueprintBlackboard16384`).
2. **Assignment.** A generated/hand-written `ParseParamsDelegate(json, byte*)` runs once at behavior assignment, writing defaults into the root params slot ([BehaviorIngressSystem.cs:126-135](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BehaviorIngressSystem.cs#L126) — the parse runs into a shadow buffer sized by `RootParamsAccess.RootParamsBytes(def)` and is committed to the slot only on success).
3. **Projection (the key mechanism).** A reusable action/condition is `static NodeStatus M(ref TDto p, ref BehaviorTreeState, ref BTreeContext)`. The generator emits a thunk projecting the DTO at a **baked byte offset** from byte 0 of the resolved root params slot: `Unsafe.As<…>(ref Unsafe.AddByteOffset(ref RootParamsAccess.RootRef(ctx.World, ctx.Self), (nint)offset))`. **Already emitted today** at [BTreeActionGenerator.cs:693](../../FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeActionGenerator.cs#L693) for `[SharedAiAction]` entries; a standalone AiPrimitive/HSM thunk resolves its own occurrence slot the same way, never through a per-entity blackboard component. The legacy AiPrimitive `paramIndex * sizeof(Params)` form (homogeneous arrays) is superseded; **`paramIndex` is ignored** in the baked-offset pipeline.
4. **Registry / runtime.** The blob is type-erased: `MethodNames[]` hold string keys `{Type}.{Method}@{offset}` (or bare name for 4-param). `Interpreter<byte,BTreeContext>.BindActions` resolves each key in the injected `ActionRegistry`; a miss installs a `=>Failure` fallback. The JSON bridge injects a populated registry (PREREQ-A, `8eb45e0c`). The tick site is the BTree arm of `BrainTickSystem` — `def.BTreeInterpreter!.Tick(ref blackboard, ref btState, ref context)` at [BrainTickSystem.cs:283](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/BrainTickSystem.cs#L283). `BehaviorDefinition.BTreeInterpreter` is an `Interpreter<byte, BTreeContext>` ([BehaviorRegistry.cs:141](../../FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs#L141)), so the tree ticks against the root params slot's bytes handed in as a `ref byte` (a type-parameter swap, not a behaviour-shaped generic-runtime change).
5. **Bin-packer.** `BlackboardBinPacker` computes sequential, C#-alignment-padded offsets against `MaxInlineBytes = 16096` — the largest occurrence tier's payload, matching the `FDP_001` capacity bound. ⭐ `CE-314` removed the inline/heavy split, so there is one region and no overflow tier. Editor offsets are *advisory* for the budget UI; the **authoritative** offset is the compiled struct layout (`Marshal.OffsetOf`), and the real bound a build enforces is the tier ladder's structural ceiling.
6. **Blueprints = primary authoring source.** A blueprint **AiPrimitive** authors an action/condition as a function: typed **Parameters** → the **root params occurrence slot** (generated `struct Params`), **WorkingState** (locals) → **node working-state occurrence slots** in the tier ladder, **return** `NodeStatus`/`bool`, body = graph. It compiles to `BTreeTick`/`BTreeEvaluate` thunks registered into `BehaviorRegistry` — the **same** projection/memory model as hardcoded `[SharedAiAction]` ([AiPrimitiveEmitter.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Emit/AiPrimitiveEmitter.cs)). So authored-via-blueprint and hardcoded-C# actions are interchangeable at the binding layer.
7. **Aliasing.** Multiple nodes whose `ExpressionTargetField` points to the **same** variable → the bin-packer reserves the bytes once → the generator bakes the **same** offset into each node's thunk → zero-copy shared state. No per-node/orchestrator overhead.
8. **Sizing.** There is no overflow path and no heavy-DTO split. A master DTO's size is bound only by the tier ladder's structural ceiling — up to ~16 KB on the largest tier (`BlueprintBlackboard16384`) — enforced by the partition allocator against `RootParamsBytes(def)`.
9. **Slice 1 constraint (historical — lifted in Slice 2).** Exactly **one stateful** AiPrimitive working-state per entity, a limit of the single hash-guarded working-state block Slice 1 used. Stateless actions/conditions (params only) had **no** such limit. Slice 2 replaces the single block with **one working-state occurrence slot per node** in the tier ladder, so the limit no longer applies.

## 3. The Slice 1 design

### 3.1 Authoring model (whole-DTO binding)
- The asset has an **editor-managed (Category-2)** blackboard: a list of typed **variables**, each variable being the **whole parameter DTO** of some action/condition.
- A node binds its entire DTO param to exactly **one** variable via `ExpressionTargetField` (approved "whole-DTO binding", Addendum v3 §2.2).
- **"+ Promote to new variable"** creates an auto-managed (`IsAutoManaged`) variable of the action's DTO type for static-input-only actions (hidden from the main list; defaults baked into `ParseParams`).
- **Read-only reflection of hardcoded DTOs (Category-1):** when a node binds a hardcoded-C# action whose DTO is not an editor-managed variable, the Variables panel **reflects that DTO's fields read-only** (so the designer can see them). This is new (the panel today shows only editor-managed variables).

### 3.2 Codegen (the core new work)
For each managed BTree asset, `BTreeJsonGenerator` generates:
1. **Per-asset blackboard struct** from the authored variables (reuse `BlackboardDtoEmitter`), `[StructLayout(Sequential)]`, bin-packed to fit `BlackboardBinPacker.MaxInlineBytes` — the largest occurrence tier's payload, the same capacity bound `FDP_001` enforces; **offsets authoritative from the compiled struct** (`Marshal.OffsetOf`).
2. **Topology over that struct** — each binding compiles to a blob key `{Type}.{Method}@{offset}` (the builder's expression overload computes the offset from the generated struct).
3. **Per-asset registrar** — for each bound (method, variable), register a `ref byte` thunk keyed identically: `Unsafe.As<…,TDto>(ref Unsafe.AddByteOffset(ref rootSlotBytes, (nint)offset))` → call the method. Mirrors the existing `BTreeActionGenerator` emission. Registered into the registry the bridge already injects (PREREQ-A). A large variable is simply an ordinary slot on a bigger tier — there is no separate heavy-variable thunk or overflow path to defer.

Net: blob keys and registry keys both derive from the **same** generated struct's `Marshal.OffsetOf`, so they always match; the runtime stays `Interpreter<byte>`.

### 3.3 Validator
Unblock `ThreeParamReusable` at [BTreeMethodCompatibilityValidator.cs:149](../../Hrot/Subsystems/AI/Hrot.AiEditor.Generators/BTreeMethodCompatibilityValidator.cs#L149): accept it iff the method has the 3-param reusable shape (`ref TDto`, `ref BehaviorTreeState`, `ref TCtx`, returns `NodeStatus`) **and** `ExpressionTargetField` resolves to an authored variable whose declared type equals `TDto`. Otherwise emit a clear `BTREE0002` skip (never a build break). **Must land together with §3.2** — opening the gate without the per-asset struct would turn skips into build breaks.

### 3.4 Editor UX
- Node inspector **field-picker** (type-filtered `[BlackboardFieldPicker]`) sets `ExpressionTargetField` to a variable of the matching DTO type; "+ Promote to new variable".
- Variables panel: managed variables (editable) + read-only reflected hardcoded DTOs (§3.1).

## 4. Work breakdown (DO NOT implement yet)
| ID | Title | Notes |
|---|---|---|
| S1-1 | Variables-panel read-only reflection of hardcoded DTOs (Category-1) | Independent, additive; unblocks "see the DTO in the panel". |
| S1-2 | Per-asset struct + topology-over-struct codegen | `BTreeJsonGenerator` + `BlackboardDtoEmitter`; guarded `Managed==true`; byte-identity gate intact. |
| S1-3 | Per-asset baked-offset registrar codegen | Mirror `BTreeActionGenerator` emission; register into injected registry. |
| S1-4 | Validator unblock for `ThreeParamReusable` | Lands with S1-2/S1-3. |
| S1-5 | Node-inspector field-picker + promote-to-variable | Editor authoring. |
| S1-6 | Demo assets + runtime proof tests | §5. |

## 5. Slice 1 demo specifications
Demo nodes already exist: `DemoCounterNodes` (`33e09ec1`) — `DemoCounterParams { int Counter; int Threshold }`, `[BTreeCondition] Condition_CounterBelowThreshold`, `[BTreeAction] Action_IncrementCounter` (both **stateless**; `@0` bridges already generated).

- **Demo 1 — single action, single variable.** Managed blackboard with one `DemoCounterParams` variable; `Sequence[ Condition_CounterBelowThreshold, Action_IncrementCounter ]` bound to it; tick N frames; assert `Counter` climbs to `Threshold` then the condition fails. Proves the basic authored bind+run.
- **Demo 2 — multiple actions, multiple distinct DTOs, non-zero offsets (the headline).** Two (or more) variables of *different* DTO types at distinct bin-packed offsets, each bound to a different action/condition; include a `Repeater` decorator. Proves the real multi-action case the offset-0 model couldn't do. (Add a second tiny stateless DTO+action, e.g. `DemoFlagParams { bool Done } + Action_SetDone` / `Condition_IsDone`.)
- **Demo 3 (optional) — aliasing.** Two action nodes bind the *same* variable → assert they observe each other's writes (zero-copy shared state).
- **Observation (mirrors blueprint `CountingDemo`).** (a) Runtime proof test: attach behavior to an entity, tick, assert blackboard fields (pattern: `CountingDemo_ProofTests.cs`); (b) live: the tier renderers' **root-params arm** + `BTreeVisualizerRenderer` show the counter and active node in the entity inspector.
- **Authoring modes.** First demos may use **hardcoded** `DemoCounterNodes` with DTOs **reflected read-only** in the panel (S1-1). The same demo can later be re-expressed with **blueprint-authored AiPrimitives** — identical memory model, so no rework.

## 6. Already done (foundations, DEC-05)
- Load-flag round-trip fix + managed demo asset `T09` (`b2e09b29`); "Use editor-managed blackboard" toggle (`90c19d52`).
- **PREREQ-A** — JSON BTrees execute real bound actions/conditions; bridge injects a populated registry; live `CgfSubsystem` no longer discards JSON behavior defs (`8eb45e0c`).
- Demo nodes `DemoCounterNodes` (`33e09ec1`).
- FolderIcons test relaxed (`8ee40a33`).

## 7. Risks & verification discipline
- **Byte-identity gate** (`ByteIdenticalGateTests`, CombatShowcase/SampleScout): keep all emit changes behind `Managed==true`.
- **Incremental-generator caching:** `dotnet build-server shutdown` + `-t:Rebuild` for any codegen verification.
- **Validator/struct coupling:** S1-4 must not ship before S1-2/S1-3.
- Hard-verify every batch (diffs + real build + tests + a runtime tick proof); never trust agent reports; exclude editor autosave drift from commits.

## 8. Open decisions (for the user)
1. First demo authoring mode: hardcoded-DTO-reflected (faster) vs blueprint-AiPrimitive-authored (the end state). Recommended: hardcoded first, then re-author one as a blueprint.
2. ⛔ **Moot.** This asked whether Slice 1 should author large (>100 B) variables through the bin-packer or keep them hand-written (`[SharedAiHeavyAction]`). There is no longer a size-based split to decide between: any variable lands on whatever tier its size fits, authored or hand-written alike.

## 9. Architect review (2026-06-15) — confirmations & added requirements
Reviewed by architect; reconciled against code (✓ = verified this session).
- **Composition model (Q1) — CONFIRMED: "BTree owns layout, blueprint provides `TickCore`".** When a blueprint action is hosted in a multi-action BTree, the BTree generator **ignores** the blueprint's standalone `BTreeTick` (with its `paramIndex*sizeof` math) and emits a **per-node adapter** that projects `Params` at the bin-packed offset (and, in Slice 2, `WorkingState` at the node's partition slot), then calls `TickCore(ref Params, ref WorkingState, self, world, time)`.
- **Per-asset registrar (Q2) — CONFIRMED.** Roslyn generators can't see each other's emitted trees, so the JSON generator emits an isolated `[BlueprintRegistrar]` "masquerade" class that receives the injected `BehaviorRegistry` and statically calls `beh.RegisterAction`/`RegisterCondition` to wire the baked-offset thunks. **This is exactly the bridge PREREQ-A already established** — extend it.
- **⚠ CRITICAL — `bool` layout (Q3).** `Marshal.OffsetOf` (authoritative) defaults `bool` to a 4-byte Win32 `BOOL`, but the bin-packer (✓ `BlackboardBinPacker.cs:255`) and managed memory treat it as **1 byte**. The generated struct **MUST** decorate every `bool` field with `[MarshalAs(UnmanagedType.I1)]` or offsets drift → silent corruption (and broken Flight-Recorder/replay schemas). **Action:** ensure `BlackboardDtoEmitter` emits `[MarshalAs(I1)]` for bool (verify it does; today it emits bare `bool`). Also: the editor bin-packer must replicate C# sequential layout exactly — natural alignment capped at 8, padding before `fixed`/`[InlineArray]` fields.
- **Defaults / ParseParams (Q4) — CONFIRMED.** Editor `DefaultValueJson` is baked into a generated `ParseParamsDelegate`; at assignment `BehaviorIngressSystem` runs it once — apply static defaults, **overlay scenario JSON (runtime wins)**, `Unsafe.Write` into the inline slot. Heavy-tier variables also need an inline `StructureHash` init check (mirror `InitDefaultWorkingState`).
- **Validator type-match (Q5) — CONFIRMED.** Use **FQN string equality** (no subtype/implicit matching) — exact byte-layout match required. Reference catalog keys bindings as `{DtoTypeFqn}::{FieldName}` (cross-ALC-safe, no deep reflection).
- **Category-1 reflection (Q6) — CONFIRMED (✓ exists).** `ActionSchemaExporter` reflects the loaded assembly on startup, takes the method's **first `ref` parameter** type as `ActionSchemaEntry.DtoType`. The variable-type picker is restricted to primitives/vectors/enums, `[BlackboardDtoStruct]`-marked structs, and auto-detected action DTOs.
- **Byte-identity (Q7) — CONFIRMED safe.** Filter `*.btree.json` + emit only when `Managed==true`; Category-1 / `HROT_EDITOR_GENERATED`-absent files are never written. Legacy assets untouched.
- **Added hazard — aggregate ceiling.** Multiple distinct param DTOs rapidly consume a tier's payload. The bin-packer must pad for alignment and enforce the ceiling; if the **aggregate** of all bound master DTOs exceeds what any tier could seat, `FDP_001` refuses it at build time (the structural ceiling is the largest tier's whole payload, up to 16 096 B, and the allocator promotes automatically).
- **Added hazard — alias vs distinct instance.** The editor must clearly distinguish *aliasing* one shared DTO across nodes (same offset, intended sharing) from *distinct* instances of the same DTO type (the bin-packer must reserve bytes **uniquely** for each). A wrong call here silently shares or wastes memory.
